using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Honeydew.Data;
using Honeydew.Exceptions;
using Honeydew.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Honeydew.UploadStores
{
    public class S3Store : UploadStore<S3StoreOptions>
    {
        public const int blockSize = 5 * 1024 * 1024; // 5 MB (required smallest block upload size for S3)

        private readonly ILogger _logger;
        private readonly ApplicationDbContext _context;

        private IAmazonS3 _s3;
        private string _bucket;
        private long _maximumAllowedDownloadRangeFromBucketInBytes = 16 * 1024 * 1024; // 16MiB

        public S3Store(ApplicationDbContext context, IOptionsMonitor<S3StoreOptions> options, ILogger<AzureBlobsStore> logger)
            : base(options, context)
        {
            _context = context;
            _logger = logger;
        }

        public override void SetupOptions(S3StoreOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Bucket))
            {
                throw new StorageConfigException(
                    StorageType.S3,
                    nameof(options.Bucket),
                    options.Bucket,
                    configValueExamples: new[] { "honeydew", "mybucketname" });
            }

            if (string.IsNullOrWhiteSpace(options.AccessKey))
            {
                throw new StorageConfigException(
                    StorageType.S3,
                    nameof(options.AccessKey),
                    options.AccessKey);
            }

            if (string.IsNullOrWhiteSpace(options.SecretAccessKey))
            {
                throw new StorageConfigException(
                    StorageType.S3,
                    nameof(options.SecretAccessKey),
                    options.SecretAccessKey);
            }

            if (string.IsNullOrWhiteSpace(options.Region))
            {
                throw new StorageConfigException(
                    StorageType.S3,
                    nameof(options.Region),
                    options.Region,
                    configValueExamples: "us-west-1");
            }

            if (options.MaximumAllowedRangeLengthFromBucketInBytes < 0)
            {
                throw new StorageConfigException(
                    StorageType.AzureBlobs,
                    nameof(options.MaximumAllowedRangeLengthFromBucketInBytes),
                    options.MaximumAllowedRangeLengthFromBucketInBytes.ToString(),
                    configValueExamples: new[] { "16777216", "0" });
            }

            _s3 = new AmazonS3Client(options.AccessKey, options.SecretAccessKey, RegionEndpoint.GetBySystemName(options.Region));
            _bucket = options.Bucket;
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

                if (upload.Length == upload.UploadedLength)
                {
                    return 0;
                }

                int bytesRead = 0;
                long bytesWritten = 0;

                if (upload.ProviderUploadId == null)
                {
                    // Setup information required to initiate the multipart upload.
                    var initiateRequest =
                        new InitiateMultipartUploadRequest
                        {
                            BucketName = _bucket,
                            Key = blobName,
                        };

                    // Initiate the upload.
                    InitiateMultipartUploadResponse initResponse = await _s3.InitiateMultipartUploadAsync(initiateRequest, cancellationToken);
                    upload.ProviderUploadId = initResponse.UploadId;
                }

                do
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Request to append cancelled for file '{id}'", blobName);
                        break;
                    }

                    var buffer = new byte[blockSize];

                    // Checking for last block (it will never reach 5MB exactly)
                    int lastBytesRead = 0;
                    bytesRead = 0;

                    do
                    {
                        lastBytesRead = await stream.ReadAsync(
                            buffer,
                            bytesRead,
                            // Ensure we don't overread the buffer
                            blockSize - bytesRead,
                            cancellationToken);
                        bytesRead += lastBytesRead;
                    } while (bytesRead < blockSize && lastBytesRead > 0);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    using (MemoryStream memoryBufferStream = new MemoryStream(buffer, 0, bytesRead))
                    {
                        var uploadPartRequest = new UploadPartRequest
                        {
                            BucketName = _bucket,
                            Key = blobName,
                            UploadId = upload.ProviderUploadId,
                            PartNumber = upload.BlockNumber.Value + 1, // Amazon S3 part uploads start at one.
                            PartSize = bytesRead,
                            InputStream = memoryBufferStream
                        };

                        var result = await _s3.UploadPartAsync(uploadPartRequest, cancellationToken);

                        PartETag partETag = new PartETag(result.PartNumber, result.ETag);
                        string serialised = JsonConvert.SerializeObject(partETag);
                        string eTag = Convert.ToBase64String(Encoding.UTF8.GetBytes(serialised));

                        upload.BlockIds += $"{eTag} ";
                    }

                    bytesWritten += bytesRead;

                    upload.BlockNumber++;
                    upload.UploadedLength += bytesRead;

                    _logger.LogDebug("Read bytes {bytesRead}, written {bytesWritten}, block number {blockNumber} on file {fileId}", bytesRead, bytesWritten, upload.BlockNumber, blobName);

                    // note: cancellation token *not* supplied as this must finish because we have sucessfully uploaded the latest block to S3.
                    await DbContext.SaveChangesAsync();
                } while (bytesRead != 0);

                if (upload.Length == upload.UploadedLength)
                {
                    var blockIds = upload.BlockIds.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    var completeRequest = new CompleteMultipartUploadRequest
                    {
                        BucketName = _bucket,
                        Key = blobName,
                        UploadId = upload.ProviderUploadId,
                    };

                    List<PartETag> partETags = new List<PartETag>();

                    foreach (var blockId in blockIds)
                    {
                        string serialised = Encoding.UTF8.GetString(Convert.FromBase64String(blockId));
                        PartETag partETag = JsonConvert.DeserializeObject<PartETag>(serialised);
                        partETags.Add(partETag);
                    }

                    completeRequest.AddPartETags(partETags);

                    await _s3.CompleteMultipartUploadAsync(completeRequest);

                    await DbContext.SaveChangesAsync();
                }

                return bytesWritten;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to append data");
                throw;
            }
        }

        public override async Task DeleteAsync(Upload upload, CancellationToken cancellationToken)
        {
            await _s3.DeleteObjectAsync(_bucket, upload.Id + upload.Extension, cancellationToken);
        }

        public override async Task<DownloadResult> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucket,
                Key = upload.Id + upload.Extension,
                // TODO: Fix this
                ByteRange = new ByteRange(range.Ranges.FirstOrDefault()?.From.GetValueOrDefault() ?? 0, range.Ranges.FirstOrDefault()?.To.GetValueOrDefault() ?? 0)
            };

            var stream = await _s3.GetObjectAsync(request);

            return new DownloadResult
            {
                Stream = stream.ResponseStream,
                ContentRange = stream.ContentRange
            };
        }

        public override Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            return _s3.UploadObjectFromStreamAsync(_bucket, upload.Id + upload.Extension, stream, null, cancellationToken);
        }
    }
}
