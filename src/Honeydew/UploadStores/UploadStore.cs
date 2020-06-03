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

        public UploadStore(IOptionsMonitor<TOptions> optionsMonitor)
        {
            _options = optionsMonitor;

            SetupOptions(_options.CurrentValue);

            _onChangeCallback = _options.OnChange(SetupOptions);
        }

        public abstract Task<long> AppendToUploadAsync(string uploadId, Stream stream, CancellationToken cancellationToken);
        public abstract Task<long> AppendToUploadAsync(Upload upload, Stream stream, CancellationToken cancellationToken);
        public abstract Task DeleteAsync(string uploadId, CancellationToken cancellationToken);
        public abstract Task DeleteAsync(Upload upload, CancellationToken cancellationToken);
        public abstract Task<Stream> DownloadAsync(string uploadId, RangeHeaderValue range, CancellationToken cancellationToken);
        public abstract Task<Stream> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken);
        public abstract Task WriteAllBytesAsync(string uploadId, Stream stream, CancellationToken cancellationToken);
        public abstract Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken);

        public abstract void SetupOptions(TOptions options);

        public void Dispose()
        {
            _onChangeCallback.Dispose();
        }
    }
}
