using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Honeydew.UploadStores
{
    public class AzureBlobTusStore : ITusStore, ITusCreationStore, ITusReadableStore, ITusExpirationStore
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
            _logger.LogDebug("Creating a file. {metadata}", metadata);

            var uploadId = Guid.NewGuid().ToString("N");
            var parsedMetadata = Metadata.Parse(metadata);

            int gameId = int.Parse(parsedMetadata.First(x => x.Key == "gameId").Value.GetString(Encoding.UTF8));
            string originalFileName = parsedMetadata.First(x => x.Key == "name").Value.GetString(Encoding.UTF8);
            string contentType = parsedMetadata.First(x => x.Key == "contentType").Value.GetString(Encoding.UTF8);
            bool masterVideo = bool.TryParse(parsedMetadata.First(x => x.Key == "masterVideo").Value.GetString(Encoding.UTF8), out bool result) && result;

            using (var scope = _provider.CreateScope())
            using (var context = scope.ServiceProvider.GetService<ApplicationDbContext>())
            using (var mediaServicesManager = scope.ServiceProvider.GetService<IMediaServicesManager>())
            {
                // The asset name.
                string assetName = $"game-{gameId.ToString("0000000")}-{uploadId}";

                var expiryTime = DateTime.UtcNow.AddHours(5).ToUniversalTime();
                var uploadUri = await mediaServicesManager.CreateAssetContainerAsync(assetName, expiryTime, cancellationToken);

                context.VideoUploadMetadata.Add(
                    new VideoUploadMetadata
                    {
                        UploadId = uploadId,
                        AssetName = assetName,
                        AssetContainerUploadUri = uploadUri.ToString(),
                        ExpirationTimeUtc = expiryTime,
                        Length = uploadLength,
                        UploadedLength = 0,
                        ContentType = contentType,
                        MasterVideo = masterVideo,
                        Metadata = metadata,
                        BlockIds = string.Empty,
                        BlockNumber = 0,
                        OriginalFileName = originalFileName,
                        GameId = gameId
                    });

                await context.SaveChangesAsync(cancellationToken);
            }

            _logger.LogDebug("Created file. Upload Id: {uploadId}", uploadId);
            return uploadId;
        }

        public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Appending to '{fileId}'", fileId);

            try
            {
                int blockSize = 1_048_576; // 1 MiB

                using (var scope = _provider.CreateScope())
                using (var context = scope.ServiceProvider.GetService<ApplicationDbContext>())
                {
                    var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);

                    _logger.LogDebug("Found upload metadata for file: '{fileId}'", fileId);

                    var container = new CloudBlobContainer(new Uri(file.AssetContainerUploadUri));
                    var blob = container.GetBlockBlobReference(file.OriginalFileName);

                    _logger.LogDebug("Uploading file '{fileId}' to '{fileUrl}'", fileId, blob.Uri);

                    if (file.Length == file.UploadedLength)
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
                                    string.Format("BlockId{0}", file.BlockNumber.ToString("0000000"))));

                        // calculate the MD5 hash of the byte array
                        string blockHash = Convert.ToBase64String(MD5.Create().ComputeHash(buffer, 0, bytesRead));

                        // upload the block, provide the hash so Azure can verify it
                        await blob.PutBlockAsync(blockId, new MemoryStream(buffer, 0, bytesRead), blockHash, new AccessCondition(), new BlobRequestOptions(), new OperationContext(), cancellationToken);

                        bytesWritten += bytesRead;

                        file.BlockIds += $"{blockId} ";
                        file.BlockNumber++;
                        file.UploadedLength += bytesRead;

                        _logger.LogDebug("Read bytes {bytesRead}, written {bytesWritten}, block number {blockNumber} on file {fileId}", bytesRead, bytesWritten, file.BlockNumber, fileId);

                        //note: cancellation token *not* supplied as this must finish because we have sucessfully uploaded the latest block to azure.
                        await context.SaveChangesAsync();
                    } while (bytesRead != 0);

                    if (file.Length == file.UploadedLength)
                    {
                        await blob.PutBlockListAsync(file.BlockIds.Split(' ', StringSplitOptions.RemoveEmptyEntries));

                        blob.Properties.ContentType = file.ContentType;
                        await blob.SetPropertiesAsync();

                        using (var mediaServicesManager = scope.ServiceProvider.GetService<IMediaServicesManager>())
                        {
                            file.DateUploadedUtc = DateTime.UtcNow;
                            await mediaServicesManager.SubmitJobAsync(file.AssetName, cancellationToken);

                            await context.SaveChangesAsync();
                        }
                    }

                    return bytesWritten;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to append data");
                throw;
            }
        }

        public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);

                if (file == default)
                {
                    return false;
                }

                try
                {
                    var container = new CloudBlobContainer(new Uri(file.AssetContainerUploadUri));
                    var blob = container.GetBlockBlobReference(file.OriginalFileName);

                    return await blob.ExistsAsync();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to create container or blob reference to file {@file}", file);
                    return false;
                }
            }
        }

        public async Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                return file.ExpirationTimeUtc;
            }
        }

        public async Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.Where(x => x.ExpirationTimeUtc < DateTime.UtcNow).ToArrayAsync(cancellationToken);
                return file.Select(x => x.UploadId);
            }
        }

        public async Task<ITusFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                return new AzureBlobTusFile(fileId, file.Metadata);
            }
        }

        public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                return file.Length;
            }
        }

        public async Task<string> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                return file?.Metadata;
            }
        }

        public async Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                return file.UploadedLength;
            }
        }

        public Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public async Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                file.ExpirationTimeUtc = expires.UtcDateTime;

                await context.SaveChangesAsync(cancellationToken);
            }
        }

        private ApplicationDbContext GetContextInstance()
        {
            return _provider.CreateScope().ServiceProvider.GetService<ApplicationDbContext>();
        }
    }
}
