using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tusdotnet.Models;

namespace Honeydew.Tasks
{
    public class DeletionCleanupTask : IHostedService, IDisposable
    {
        private readonly ILogger<DeletionCleanupTask> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<DeletionOptions> _deletionOptions;
        private Timer _timer;

        public DeletionCleanupTask(
            ILogger<DeletionCleanupTask> logger,
            IServiceProvider serviceProvider,
            IOptionsMonitor<DeletionOptions> deletionOptions)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _deletionOptions = deletionOptions;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Deletion cleanup task running.");

            Schedule();

            return Task.CompletedTask;
        }

        private void Schedule()
        {
            if (_deletionOptions.CurrentValue.ScheduleAndMarkUploadsForDeletion)
            {
                _logger.LogDebug(
                    "Running next deletion cleanup task at {dateTimeOffset} (UTC)",
                    DateTimeOffset.UtcNow.AddSeconds(_deletionOptions.CurrentValue.RunCleanupEveryXSeconds));

                _timer = new Timer(DoWork, null, _deletionOptions.CurrentValue.RunCleanupEveryXSeconds * 1000, Timeout.Infinite);
            }
            else
            {
                _logger.LogInformation(
                    "Deletion scheduler not marked to run as {settingName} was set to false", 
                    nameof(DeletionOptions.ScheduleAndMarkUploadsForDeletion));
            }
        }

        private async void DoWork(object state)
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();
            var tusConfiguration = scope.ServiceProvider.GetService<DefaultTusConfiguration>();

            var now = DateTimeOffset.UtcNow;

            var toDelete = await context.Uploads.Where(x => x.PendingForDeletionAt <= now).ToArrayAsync();
            var deletedCount = 0;

            foreach (var upload in toDelete)
            {
                try
                {
                    if (_deletionOptions.CurrentValue.AlsoDeleteFileFromStorage)
                    {
                        await (tusConfiguration.Store as IHoneydewTusStore).DeleteFileAsync(upload.Id, CancellationToken.None);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Skipped file store deletion of {id} as {settingName} was marked as false",
                            upload.Id,
                            nameof(DeletionOptions.AlsoDeleteFileFromStorage));
                    }

                    context.Uploads.Remove(upload);

                    await context.SaveChangesAsync();

                    deletedCount++;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to delete upload {upload}", upload.Id);
                    throw;
                }
            }

            _logger.LogInformation(
                "Deletion cleanup task ran. Found: {Count} items. Deleted {deletedCount} items.", toDelete.Length, deletedCount);

            Schedule();
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
