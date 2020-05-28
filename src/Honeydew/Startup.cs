using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.EntityFrameworkCore;
using Honeydew.Data;
using Honeydew.Exceptions;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tusdotnet;
using tusdotnet.Constants;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Models.Concatenation;
using System.Net.Mime;
using Microsoft.AspNetCore.StaticFiles;
#if DEBUG
using Westwind.AspNetCore.LiveReload;
#endif

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
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(
                    Configuration.GetConnectionString("DefaultConnection")));

            services.AddDefaultIdentity<User>(
                    options => options.SignIn.RequireConfirmedAccount = true)
                .AddEntityFrameworkStores<ApplicationDbContext>();

            services.AddTransient<SlugGenerator>();

            CreateUploadHandlers(services);

            services.Configure<SlugOptions>(Configuration.GetSection("SlugGeneration"));

            var builder = services.AddRazorPages();

#if DEBUG
            if (Environment.IsDevelopment())
            {
                //services.AddLiveReload();
                builder.AddRazorRuntimeCompilation();
            }
#endif
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
                app.UseStatusCodePages();

#if DEBUG
                //app.UseLiveReload();
#endif
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

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
                    services.Configure<StreamDiskStoreOptions>(Configuration.GetSection("Storage:Disk"));
                    services.AddSingleton<IStreamStore, StreamDiskStore>();
                    break;
                case StorageType.AzureBlob:
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

                ITusStore store = storageType switch
                {
                    StorageType.Disk => new TusDiskStore(serviceProvider),
                    StorageType.AzureBlob => throw new NotImplementedException(),
                    _ => throw new ArgumentOutOfRangeException()
                };

                return new DefaultTusConfiguration
                {
                    Store = store,
                    UrlPath = "/upload",
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
