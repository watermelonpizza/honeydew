using Honeydew.Data;
using Honeydew.Models;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Honeydew.UploadStores
{
    public abstract class UploadStore<TOptions> : IUploadStore
        where TOptions : IStoreOptions
    {
        private readonly IOptionsMonitor<TOptions> _options;
        private readonly IDisposable _onChangeCallback;

        protected ApplicationDbContext DbContext { get; }

        public UploadStore(IOptionsMonitor<TOptions> optionsMonitor, ApplicationDbContext context)
        {
            _options = optionsMonitor;
            DbContext = context;

            SetupOptions(_options.CurrentValue);

            _onChangeCallback = _options.OnChange(SetupOptions);
        }

        public async Task<long> AppendToUploadAsync(string uploadId, Stream stream, CancellationToken cancellationToken)
        {
            var upload = await DbContext.Uploads.FindAsync(new[] { uploadId }, cancellationToken);

            return await AppendToUploadAsync(upload, stream, cancellationToken);
        }

        public abstract Task<long> AppendToUploadAsync(Upload upload, Stream stream, CancellationToken cancellationToken);

        public async Task DeleteAsync(string uploadId, CancellationToken cancellationToken)
        {
            var upload = await DbContext.Uploads.FindAsync(new[] { uploadId }, cancellationToken);

            await DeleteAsync(upload, cancellationToken);
        }

        public abstract Task DeleteAsync(Upload upload, CancellationToken cancellationToken);

        public async Task<DownloadResult> DownloadAsync(string uploadId, RangeHeaderValue range, CancellationToken cancellationToken)
        {
            var upload = await DbContext.Uploads.FindAsync(new[] { uploadId }, cancellationToken);

            return await DownloadAsync(upload, range, cancellationToken);
        }

        public abstract Task<DownloadResult> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken);

        public async Task WriteAllBytesAsync(string uploadId, Stream stream, CancellationToken cancellationToken)
        {
            var upload = await DbContext.Uploads.FindAsync(new[] { uploadId }, cancellationToken);

            await WriteAllBytesAsync(upload, stream, cancellationToken);
        }

        public abstract Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken);

        public abstract void SetupOptions(TOptions options);

        public void Dispose()
        {
            _onChangeCallback.Dispose();
        }
    }
}
