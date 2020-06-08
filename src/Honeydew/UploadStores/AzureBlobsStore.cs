using Amazon.Runtime;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Honeydew.Data;
using Honeydew.Exceptions;
using Honeydew.Models;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Honeydew.UploadStores
{
    public class AzureBlobsStore : UploadStore<AzureBlobsStoreOptions>
    {
        public const int blockSize = 1 * 1024 * 1024; // 1 MB

        private readonly ILogger _logger;

        private BlobContainerClient _blobContainerClient;
        private long _maximumAllowedDownloadRangeFromBlobStoreInBytes = 16 * 1024 * 1024; // 16MiB

        public AzureBlobsStore(
            ApplicationDbContext context,
            IOptionsMonitor<AzureBlobsStoreOptions> options,
            ILogger<AzureBlobsStore> logger)
            : base(options, context)
        {
            _logger = logger;
        }

        public override void SetupOptions(AzureBlobsStoreOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new StorageConfigException(
                    StorageType.AzureBlobs,
                    nameof(options.ConnectionString),
                    options.ConnectionString,
                    configValueExamples: new[] { "DefaultEndpointsProtocol=https;AccountName=<storage account name>;AccountKey=<your-access-key>;EndpointSuffix=core.windows.net", "UseDevelopmentStorage=true" });
            }

            if (string.IsNullOrWhiteSpace(options.ContainerName))
            {
                throw new StorageConfigException(
                    StorageType.AzureBlobs,
                    nameof(options.ContainerName),
                    options.ContainerName,
                    configValueExamples: new[] { "honeydew", "myazureblobcontainer" });
            }

            if (options.MaximumAllowedRangeLengthFromBlobStoreInBytes < 0)
            {
                throw new StorageConfigException(
                    StorageType.AzureBlobs,
                    nameof(options.MaximumAllowedRangeLengthFromBlobStoreInBytes),
                    options.MaximumAllowedRangeLengthFromBlobStoreInBytes.ToString(),
                    configValueExamples: new[] { "16777216", "0" });
            }

            _blobContainerClient =
                new BlobContainerClient(
                    options.ConnectionString,
                    options.ContainerName);
        }

        public override Task DeleteAsync(Upload upload, CancellationToken cancellationToken)
        {
            return _blobContainerClient.DeleteBlobIfExistsAsync(upload.Id + upload.Extension);
        }

        public override async Task<DownloadResult> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken)
        {
            var blob = _blobContainerClient.GetBlobClient(upload.Id + upload.Extension);

            if (range == null)
            {
                return new DownloadResult
                {
                    Stream = (await blob.DownloadAsync(cancellationToken)).Value.Content
                };
            }
            else
            {
                var offset = range.Ranges.FirstOrDefault()?.From.GetValueOrDefault() ?? 0;

                // Bound the to range to the maximum allowed download range so if the client requests an unbounded range (0-) 
                // then we don't grab the potentially giant file from the backing store which then has to be relayed to the client.
                long? length = range.Ranges.FirstOrDefault()?.To;

                if (_maximumAllowedDownloadRangeFromBlobStoreInBytes > 0)
                {
                    if (length.HasValue)
                    {
                        length = Math.Min(
                            length.GetValueOrDefault() - offset,
                            Math.Min(
                                _maximumAllowedDownloadRangeFromBlobStoreInBytes,
                                upload.Length - offset));
                    }
                    else
                    {
                        // Grab the minimum out of the client requested to val
                        length = Math.Min(
                            _maximumAllowedDownloadRangeFromBlobStoreInBytes,
                            upload.Length - offset);
                    }
                }

                var response = await blob.DownloadAsync(new HttpRange(offset, length), cancellationToken: cancellationToken);

                return new DownloadResult
                {
                    Stream = response.Value.Content,
                    ContentRange = response.GetRawResponse().Headers.FirstOrDefault(x => x.Name == "Content-Range").Value
                };
            }
        }

        public override async Task<long> AppendToUploadAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                if (!upload.BlockNumber.HasValue)
                {
                    upload.BlockNumber = 0;
                }

                var blobName = upload.Id + upload.Extension;

                var blob = _blobContainerClient.GetBlockBlobClient(blobName);

                _logger.LogDebug("Uploading file '{id}' to '{fileUrl}'", blobName, blob.Uri);

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
                        _logger.LogDebug("Request to append cancelled for file '{id}'", blobName);
                        break;
                    }

                    _logger.LogTrace("Reading bytes for file '{id}'", blobName);

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

                    _logger.LogDebug("Read bytes {bytesRead}, written {bytesWritten}, block number {blockNumber} on file {id}", bytesRead, bytesWritten, upload.BlockNumber, blobName);

                    // note: cancellation token *not* supplied as this must finish because we have sucessfully uploaded the latest block to azure.
                    await DbContext.SaveChangesAsync();
                } while (bytesRead != 0);

                if (upload.Length == upload.UploadedLength)
                {
                    await blob.CommitBlockListAsync(
                        upload.BlockIds.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                        new BlobHttpHeaders { ContentType = upload.MediaType },
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

        public override Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            var blob = _blobContainerClient.GetBlockBlobClient(upload.Id + upload.Extension);

            return blob.UploadAsync(
                stream,
                new BlobHttpHeaders { ContentType = upload.MediaType },
                cancellationToken: cancellationToken);
        }
    }
}
