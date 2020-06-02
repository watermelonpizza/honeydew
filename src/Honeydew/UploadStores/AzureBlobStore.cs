using Azure;
using Azure.Storage.Blobs;
using Honeydew.Data;
using Honeydew.Models;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Honeydew.UploadStores
{
    public class AzureBlobStore : IUploadStore
    {
        private readonly BlobContainerClient _blobContainerClient;
        private readonly ApplicationDbContext _context;

        public AzureBlobStore(BlobContainerClient blobContainerClient, ApplicationDbContext context)
        {
            _blobContainerClient = blobContainerClient;
            _context = context;
        }

        public async Task DeleteAsync(string uploadId, CancellationToken cancellationToken)
        {
            var upload = await _context.Uploads.FindAsync(new[] { uploadId }, cancellationToken);

            await DeleteAsync(upload, cancellationToken);
        }

        public Task DeleteAsync(Upload upload, CancellationToken cancellationToken)
        {
            return _blobContainerClient.DeleteBlobIfExistsAsync(upload.Id + upload.Extension);
        }

        public async Task<Stream> DownloadAsync(string uploadId, RangeHeaderValue range, CancellationToken cancellationToken)
        {
            var upload = await _context.Uploads.FindAsync(new[] { uploadId }, cancellationToken);

            return await DownloadAsync(upload, range, cancellationToken);
        }

        public async Task<Stream> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken)
        {
            var blob = _blobContainerClient.GetBlobClient(upload.Id + upload.Extension);

            var response = await blob.DownloadAsync(new HttpRange(), cancellationToken: cancellationToken);

            return response.Value.Content;
        }

        public Task<long> AppendToUploadAsync(string uploadId, Stream stream, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<long> AppendToUploadAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task WriteAllBytesAsync(string uploadId, Stream stream, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
