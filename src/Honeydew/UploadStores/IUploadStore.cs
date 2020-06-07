using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Models;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Honeydew.UploadStores
{
    public interface IUploadStore : IDisposable
    {
        public Task WriteAllBytesAsync(string uploadId, Stream stream, CancellationToken cancellationToken);
        public Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken);

        public Task<long> AppendToUploadAsync(
            string uploadId,
            Stream stream,
            CancellationToken cancellationToken);

        public Task<long> AppendToUploadAsync(
            Upload upload,
            Stream stream,
            CancellationToken cancellationToken);

        public Task<DownloadResult> DownloadAsync(string uploadId, RangeHeaderValue range, CancellationToken cancellationToken);
        public Task<DownloadResult> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken);

        public Task DeleteAsync(string uploadId, CancellationToken cancellationToken);
        public Task DeleteAsync(Upload upload, CancellationToken cancellationToken);
    }
}
