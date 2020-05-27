using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Honeydew.UploadStores
{
    public class StreamDiskStore : IStreamStore
    {
        private readonly StreamDiskStoreOptions _options;

        public StreamDiskStore(IOptionsMonitor<StreamDiskStoreOptions> options)
        {
            _options = options.CurrentValue;
        }

        public async Task WriteAllBytesAsync(string name, Stream stream, CancellationToken cancellationToken)
        {
            var cacheFilePath = Path.Combine(_options.CacheDirectory, name);
            var targetFilePath = Path.Combine(_options.StorageDirectory, name);

            await using (var file = File.OpenWrite(cacheFilePath))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }

            // User cancelled the upload, kill the file.
            if (cancellationToken.IsCancellationRequested)
            {
                File.Delete(cacheFilePath);
            }
            else
            {
                File.Move(cacheFilePath, targetFilePath);
            }
        }
    }
}
