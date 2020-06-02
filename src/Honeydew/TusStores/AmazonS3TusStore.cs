using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Honeydew.TusStores
{
    public class AmazonS3TusStore : ITusStore, ITusCreationStore, ITusReadableStore
    {
        private readonly ILogger<AmazonS3TusStore> _logger;
        private readonly IServiceProvider _provider;
        private readonly IAmazonS3 _s3;
        private readonly IAmazonMediaConvert _mediaConvert;
        private readonly AmazonMediaServicesConfig _config;

        public AmazonS3TusStore(
            ILogger<AmazonS3TusStore> logger,
            IServiceProvider provider,
            IAmazonS3 amazonS3,
            IAmazonMediaConvert mediaConvert,
            AmazonMediaServicesConfig config)
        {
            _logger = logger;
            _provider = provider;
            _s3 = amazonS3;
            _mediaConvert = mediaConvert;
            _config = config;
        }

        public async Task<string> CreateFileAsync(long uploadLength, string metadata, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creating a file. {metadata}", metadata);

            var fileId = Guid.NewGuid().ToString("N");
            var parsedMetadata = Metadata.Parse(metadata);

            int gameId = int.Parse(parsedMetadata.First(x => x.Key == "gameId").Value.GetString(Encoding.UTF8));
            string originalFileName = parsedMetadata.First(x => x.Key == "name").Value.GetString(Encoding.UTF8);
            string contentType = parsedMetadata.First(x => x.Key == "contentType").Value.GetString(Encoding.UTF8);
            bool masterVideo = bool.TryParse(parsedMetadata.First(x => x.Key == "masterVideo").Value.GetString(Encoding.UTF8), out bool result) && result;

            originalFileName = Path.GetFileName(originalFileName);

            using (var scope = _provider.CreateScope())
            using (var context = scope.ServiceProvider.GetService<ApplicationDbContext>())
            using (var userManager = scope.ServiceProvider.GetService<UserManager<ApplicationUser>>())
            {
                var file = new VideoUploadMetadata
                {
                    UploadId = fileId,
                    Length = uploadLength,
                    UploadedLength = 0,
                    ContentType = contentType,
                    MasterVideo = masterVideo,
                    Metadata = metadata,
                    BlockIds = string.Empty,
                    BlockNumber = 0,
                    OriginalFileName = originalFileName,
                    GameId = gameId
                };

                // Setup information required to initiate the multipart upload.
                var initiateRequest =
                    new InitiateMultipartUploadRequest
                    {
                        BucketName = _config.BucketName,
                        Key = Key(file),
                    };

                // Initiate the upload.
                InitiateMultipartUploadResponse initResponse = await _s3.InitiateMultipartUploadAsync(initiateRequest, cancellationToken);
                file.ProviderUploadId = initResponse.UploadId;

                context.VideoUploadMetadata.Add(file);

                await context.SaveChangesAsync(cancellationToken);
            }

            _logger.LogDebug("Created file. Upload Id: {uploadId}", fileId);

            return fileId;
        }

        public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Appending to '{fileId}'", fileId);

            try
            {
                int blockSize = 5 * 1024 * 1024; // 5 MB (required smallest block upload size for S3

                using (var scope = _provider.CreateScope())
                using (var context = scope.ServiceProvider.GetService<ApplicationDbContext>())
                {
                    var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);

                    _logger.LogDebug("Found upload metadata for file: '{fileId}'", fileId);

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
                                BucketName = _config.BucketName,
                                Key = Key(file),
                                UploadId = file.ProviderUploadId,
                                PartNumber = file.BlockNumber + 1, // Amazon S3 part uploads start at one.
                                PartSize = bytesRead,
                                InputStream = memoryBufferStream
                            };

                            var result = await _s3.UploadPartAsync(uploadPartRequest, cancellationToken);

                            PartETag partETag = new PartETag(result.PartNumber, result.ETag);
                            string serialised = JsonConvert.SerializeObject(partETag);
                            string eTag = Convert.ToBase64String(Encoding.UTF8.GetBytes(serialised));

                            file.BlockIds += $"{eTag} ";
                        }

                        bytesWritten += bytesRead;

                        file.BlockNumber++;
                        file.UploadedLength += bytesRead;

                        _logger.LogDebug("Read bytes {bytesRead}, written {bytesWritten}, block number {blockNumber} on file {fileId}", bytesRead, bytesWritten, file.BlockNumber, fileId);

                        //note: cancellation token *not* supplied as this must finish because we have sucessfully uploaded the latest block to AWS.
                        await context.SaveChangesAsync();
                    } while (bytesRead != 0);

                    if (file.Length == file.UploadedLength)
                    {
                        var blockIds = file.BlockIds.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        var completeRequest = new CompleteMultipartUploadRequest
                        {
                            BucketName = _config.BucketName,
                            Key = Key(file),
                            UploadId = file.ProviderUploadId,
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

                        file.DateUploadedUtc = DateTime.UtcNow;

                        if (!_config.DontSubmitJobOnUpload)
                        {
                            file.ProviderTranscodeJobId = await SubmitJobAsync(file);
                        }

                        var gameVideo = new GameVideo
                        {
                            Name = file.OriginalFileName,
                            Asset = new Asset
                            {
                                AssetKey = KeyWithoutExtension(file),
                                AssetType = AssetType.GameVideo,
                                CreatedAt = DateTime.UtcNow
                                // ContentType isn't applicable here
                            },
                            UploadId = file.UploadId,
                            GameId = file.GameId,
                            UserId = file.UserId,
                        };

                        var game = await context.Games.FindAsync(file.GameId);

                        if (game.GameState == GameState.Unknown ||
                            game.GameState == GameState.Unplayed ||
                            game.GameState == GameState.Played)
                        {
                            game.GameState = GameState.AwaitingStatistician;
                        }

                        if (file.MasterVideo)
                        {
                            game.MasterVideo = gameVideo;
                        }

                        context.GameVideos.Add(gameVideo);

                        await context.SaveChangesAsync();
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
                return file != null;
            }
        }

        public async Task<ITusFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
        {
            using (var context = GetContextInstance())
            {
                var file = await context.VideoUploadMetadata.FindAsync(new[] { fileId }, cancellationToken);
                return new CloudProviderTusFile(fileId, file.Metadata);
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

        private ApplicationDbContext GetContextInstance()
        {
            return _provider.CreateScope().ServiceProvider.GetService<ApplicationDbContext>();
        }

        private string Key(VideoUploadMetadata file) => $"{file.UploadId}/{file.OriginalFileName}";

        private string KeyWithoutExtension(VideoUploadMetadata file)
        {
            string originalFileNameWithoutExtension = Path.GetFileNameWithoutExtension(file.OriginalFileName);

            return $"{file.UploadId}/{originalFileNameWithoutExtension}";
        }

        private async Task<string> SubmitJobAsync(VideoUploadMetadata file, CancellationToken cancellationToken = default)
        {
            var inputs = new List<Input>
            {
                new Input
                {
                    FileInput = $"s3://{_config.BucketName}/{Key(file)}",
                    VideoSelector = new VideoSelector
                    {
                        ColorSpace = ColorSpace.FOLLOW
                    },
                    AudioSelectors = new Dictionary<string, AudioSelector>
                    {
                        { "Audio Selector 1", new AudioSelector() }
                    }
                }
            };

            var outputGroups = new List<OutputGroup>
            {
                new OutputGroup
                {
                    OutputGroupSettings = new OutputGroupSettings
                    {
                        CmafGroupSettings = new CmafGroupSettings
                        {
                            Destination = $"s3://{_config.BucketName}/{file.UploadId}/",
                        },
                    }
                }
            };

            var jobSettings = new JobSettings
            {
                TimecodeConfig = new TimecodeConfig
                {
                    Source = TimecodeSource.EMBEDDED
                },
                Inputs = inputs,
                OutputGroups = outputGroups,
            };

            var jobRequest = new CreateJobRequest
            {
                Role = _config.MediaConvertRole,
                JobTemplate = _config.JobTemplateName,
                Settings = jobSettings
            };

            jobRequest.UserMetadata.Add("GameId", file.GameId.ToString());
            jobRequest.UserMetadata.Add("UserId", file.UserId);

            CreateJobResponse createJobResponse = await _mediaConvert.CreateJobAsync(jobRequest);

            _logger.LogInformation("Created aws media services job for upload {key}, job id {jobId}", Key(file), createJobResponse.Job.Id);

            return createJobResponse.Job.Id;
        }
    }
}
