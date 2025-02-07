using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tusdotnet;
using tusdotnet.Constants;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Models.Concatenation;
using Honeydew.Tasks;
using Honeydew.TusStores;
using Honeydew.AuthenticationHandlers;
using Honeydew.Helpers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Honeydew
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public IHostEnvironment Environment { get; }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddApplicationInsightsTelemetry();

            var databaseTypeString = Configuration["Database:Type"];

            if (!Enum.TryParse(databaseTypeString, out DatabaseType type))
            {
                throw new Exception($"The database type `{databaseTypeString}` is not a valid database type. Valid options are: {string.Join(", ", Enum.GetNames(typeof(DatabaseType)))}");
            }

            var connectionString = Configuration.GetConnectionString("DefaultConnection");

            switch (type)
            {
                case DatabaseType.Sqlite:
                    services.AddDbContext<ApplicationDbContext, SqliteApplicationDbContext>(options =>
                        options.UseSqlite(connectionString));
                    break;
                case DatabaseType.SqlServer:
                    services.AddDbContext<ApplicationDbContext, SqlServerApplicationDbContext>(options =>
                        options.UseSqlServer(connectionString));
                    break;
                default:
                    throw new Exception($"The database type `{databaseTypeString}` is not a valid database type. Valid options are: {string.Join(", ", Enum.GetNames(typeof(DatabaseType)))}");
            }

            services.AddDefaultIdentity<User>(
                    options => options.SignIn.RequireConfirmedAccount = true)
                .AddEntityFrameworkStores<ApplicationDbContext>();

            services.AddTransient<SlugGenerator>();

            CreateUploadHandlers(services);

            services.Configure<Models.IdentityOptions>(Configuration.GetSection("Identity"));
            services.Configure<SlugOptions>(Configuration.GetSection("SlugGeneration"));
            services.Configure<DeletionOptions>(Configuration.GetSection("Deletion"));

            services.AddHostedService<DeletionCleanupTask>();

            services.AddAuthentication()
                .AddScheme<TokenAuthenticationHandlerOptions, TokenAuthenticationHandler>(
                    TokenAuthenticationHandler.TokenAuthenticationSchemeName,
                    options => { });

            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = new PathString("/identity/account/login");
            });

            var builder = services.AddRazorPages(options =>
            {
                options.Conventions.Add(new PageRouteTransformerConvention(new SlugifyParameterTransformer()));
            });

#if DEBUG
            if (Environment.IsDevelopment())
            {
                builder.AddRazorRuntimeCompilation();
            }
#endif
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, ApplicationDbContext context)
        {
            if (bool.TryParse(Configuration["Database:AutoMigrateDatabase"], out bool migrateDatabase) && migrateDatabase)
            {
                context.Database.Migrate();
            }

            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/error", "?code={0}");

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseTus(httpContext => Task.FromResult(httpContext.RequestServices.GetService<DefaultTusConfiguration>()));

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
            });
        }

        public class SlugifyParameterTransformer : IOutboundParameterTransformer
        {
            public string TransformOutbound(object value)
            {
                // Slugify value
                return value == null ? null : Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1-$2").ToLower();
            }
        }

        private void CreateUploadHandlers(IServiceCollection services)
        {
            var storageTypeString = Configuration["Storage:Type"];

            if (!Enum.TryParse(storageTypeString, true, out StorageType storageType))
            {
                throw new Exception($"The storage type `{storageTypeString}` is not a valid storage type.");
            }

            switch (storageType)
            {
                case StorageType.Disk:
                    services.Configure<DiskStoreOptions>(Configuration.GetSection("Storage:Disk"));
                    services.AddTransient<IUploadStore, DiskStore>();
                    break;
                case StorageType.AzureBlobs:
                    services.Configure<AzureBlobsStoreOptions>(Configuration.GetSection("Storage:AzureBlobs"));
                    services.AddTransient<IUploadStore, AzureBlobsStore>();
                    break;
                case StorageType.S3:
                    services.Configure<S3StoreOptions>(Configuration.GetSection("Storage:S3"));
                    services.AddTransient<IUploadStore, S3Store>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            services.AddSingleton(CreateTusConfiguration(storageType));
        }

        private Func<IServiceProvider, DefaultTusConfiguration> CreateTusConfiguration(StorageType storageType)
            => (serviceProvider) =>
            {
                var logger = serviceProvider.GetService<ILoggerFactory>().CreateLogger<Startup>();

                return new DefaultTusConfiguration
                {
                    Store = new HoneydewTusStore(serviceProvider),
                    UrlPath = "/api/tusupload",
                    MetadataParsingStrategy = MetadataParsingStrategy.AllowEmptyValues,
                    Events = new Events
                    {
                        OnAuthorizeAsync = ctx =>
                        {
                            if (!ctx.HttpContext.User.Identity.IsAuthenticated)
                            {
                                ctx.FailRequest(HttpStatusCode.Unauthorized);
                            }

                            return Task.CompletedTask;
                        },
                        OnBeforeCreateAsync = ctx =>
                        {
                            // Partial files are not complete so we do not need to validate
                            // the metadata in our example.
                            if (ctx.FileConcatenation is FileConcatPartial)
                            {
                                return Task.CompletedTask;
                            }

                            if (!ctx.Metadata.ContainsKey("name") || ctx.Metadata["name"].HasEmptyValue || string.IsNullOrWhiteSpace(ctx.Metadata["name"].GetString(Encoding.UTF8)))
                            {
                                ctx.FailRequest("name metadata must be specified.");
                            }

                            return Task.CompletedTask;
                        },
                        OnCreateCompleteAsync = async ctx =>
                        {
                            logger.LogInformation($"Created file {ctx.FileId} using {ctx.Store.GetType().FullName}");

                            var name = ctx.Metadata["name"].GetString(Encoding.UTF8);
                            var mediaType = MediaTypeHelpers.GetMediaTypeFromMetadata(ctx.Metadata);

                            var filename = Path.GetFileNameWithoutExtension(name);
                            var extension = Path.GetExtension(name);

                            var scope = serviceProvider.CreateScope();

                            var userManager =
                                scope
                                    .ServiceProvider
                                    .GetService<UserManager<User>>();

                            await using var context =
                                scope
                                    .ServiceProvider
                                    .GetService<ApplicationDbContext>();

                            await context.Uploads.AddAsync(
                                new Upload
                                {
                                    Id = ctx.FileId,
                                    Name = filename,
                                    Extension = extension,
                                    OriginalFileNameWithExtension = Path.GetFileName(name),
                                    MediaType = mediaType,
                                    CodeLanguage = CodeLanguageHelpers.GetLanageFromExtension(extension),
                                    Length = ctx.UploadLength,
                                    Metadata = ctx.HttpContext.Request.Headers[HeaderConstants.UploadMetadata],
                                    UserId = userManager.GetUserId(ctx.HttpContext.User),
                                    CreatedBy = userManager.GetUserName(ctx.HttpContext.User)
                                }, ctx.CancellationToken);

                            await context.SaveChangesAsync(ctx.CancellationToken);
                        },
                        OnBeforeDeleteAsync = ctx =>
                        {
                            // Can the file be deleted? If not call ctx.FailRequest(<message>);
                            return Task.CompletedTask;
                        },
                        OnDeleteCompleteAsync = ctx =>
                        {
                            logger.LogInformation($"Deleted file {ctx.FileId} using {ctx.Store.GetType().FullName}");
                            return Task.CompletedTask;
                        },
                        OnFileCompleteAsync = async ctx =>
                        {
                            logger.LogInformation($"Upload of {ctx.FileId} completed using {ctx.Store.GetType().FullName}");

                            await using var context =
                                serviceProvider
                                    .CreateScope()
                                    .ServiceProvider
                                    .GetService<ApplicationDbContext>();

                            var upload = await context.Uploads.FindAsync(ctx.FileId);

                            upload.Status = UploadStatus.Complete;

                            // Don't want the user to be able to cancel
                            await context.SaveChangesAsync();
                        }
                    },
                    Expiration = new SlidingExpiration(TimeSpan.FromMinutes(5))
                };
            };
    }
}
