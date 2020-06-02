using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Honeydew.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Honeydew.TusStores
{
    public class AzureBlobTusStore : IHoneydewTusStore
    {
        private readonly ILogger<AzureBlobTusStore> _logger;
        private readonly IServiceProvider _provider;

        public AzureBlobTusStore(ILogger<AzureBlobTusStore> logger, IServiceProvider provider)
        {
            _logger = logger;
            _provider = provider;
        }

        public async Task<string> CreateFileAsync(long uploadLength, string metadata, CancellationToken cancellationToken)
        {
            var parsedMetadata =
                MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, metadata)
                    .Metadata;

            var name = parsedMetadata.FirstOrDefault(x => x.Key == "name").Value.GetString(Encoding.UTF8);

            var extension = Path.GetExtension(name);

            using var scope = _provider.CreateScope();
            var slugGenerator = scope.ServiceProvider.GetService<SlugGenerator>();
            var blobClient = scope.ServiceProvider.GetService<BlobContainerClient>();

            return await slugGenerator.GenerateSlugAsync(cancellationToken);
        }

        public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Appending to '{fileId}'", fileId);

            try
            {
                int blockSize = 1_048_576; // 1 MiB

                using var scope = _provider.CreateScope();
                await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();
                var blobContainer = scope.ServiceProvider.GetService<BlobContainerClient>();

                var upload = await context.Uploads.FindAsync(new[] { fileId }, cancellationToken);

                if (!upload.BlockNumber.HasValue)
                {
                    upload.BlockNumber = 0;
                }

                var blob = blobContainer.GetBlockBlobClient(fileId);

                _logger.LogDebug("Uploading file '{fileId}' to '{fileUrl}'", fileId, blob.Uri);

                if (upload.Length == upload.UploadedLength)
                {
                    return 0;
                }

                int bytesRead = 0;
                long bytesWritten = 0;

                do
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Request to append cancelled for file '{fileId}'", fileId);
                        break;
                    }

                    _logger.LogTrace("Reading bytes for file '{fileId}'", fileId);

                    var buffer = new byte[blockSize];
                    bytesRead = await stream.ReadAsync(buffer, 0, blockSize, cancellationToken);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    // create a blockID from the block number, add it to the block ID list
                    // the block ID is a base64 string
                    string blockId =
                        Convert.ToBase64String(
                            Encoding.ASCII.GetBytes(
                                string.Format("BlockId{0}", upload.BlockNumber.Value.ToString("0000000"))));

                    // calculate the MD5 hash of the byte array
                    byte[] blockHash = MD5.Create().ComputeHash(buffer, 0, bytesRead);

                    // upload the block, provide the hash so Azure can verify it
                    await blob.StageBlockAsync(blockId, new MemoryStream(buffer, 0, bytesRead), blockHash, cancellationToken: cancellationToken);

                    bytesWritten += bytesRead;

                    upload.BlockIds += $"{blockId} ";
                    upload.BlockNumber++;
                    upload.UploadedLength += bytesRead;

                    _logger.LogDebug("Read bytes {bytesRead}, written {bytesWritten}, block number {blockNumber} on file {fileId}", bytesRead, bytesWritten, upload.BlockNumber, fileId);

                    // note: cancellation token *not* supplied as this must finish because we have sucessfully uploaded the latest block to azure.
                    await context.SaveChangesAsync();
                } while (bytesRead != 0);

                if (upload.Length == upload.UploadedLength)
                {
                    await blob.CommitBlockListAsync(
                        upload.BlockIds.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                        metadata: new Dictionary<string, string> { { "Content-Type", upload.MediaType } },
                        cancellationToken: cancellationToken);
                }

                return bytesWritten;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to append data");
                throw;
            }
        }

        public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            return await context.Uploads.FindAsync(new[] { fileId }, cancellationToken) != null;
        }

        public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            return (await context.Uploads.FindAsync(new[] { fileId }, cancellationToken))?.Length;
        }

        public async Task<string> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            return (await context.Uploads.FindAsync(new[] { fileId }, cancellationToken))?.Metadata;
        }

        public async Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            return (await context.Uploads.FindAsync(new[] { fileId }, cancellationToken))?.UploadedLength ?? 0;
        }

        public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            var upload = await context.Uploads.FindAsync(new[] { fileId }, cancellationToken);

            File.Delete(Path.Combine(_storagePath, fileId + upload.Extension));
        }
    }
}
