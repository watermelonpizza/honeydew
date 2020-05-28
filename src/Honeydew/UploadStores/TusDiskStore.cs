using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;
using tusdotnet.Stores;

namespace Honeydew.UploadStores
{
    public class TusDiskStore : ITusStore, ITusCreationStore, ITusReadableStore
    {
        private readonly IServiceProvider _provider;
        private readonly string _cachePath;
        private readonly string _storagePath;

        private readonly int _maxReadBufferSize;
        private readonly int _maxWriteBufferSize;

        // Use our own array pool to not leak data to other parts of the running app.
        private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Create();

        public TusDiskStore(
            IServiceProvider provider,
            TusDiskBufferSize bufferSize = null)
        {
            _provider = provider;

            var options = _provider.GetService<IOptionsMonitor<StreamDiskStoreOptions>>().CurrentValue;

            _cachePath = options.CacheDirectory;
            _storagePath = options.StorageDirectory;

            if (!Directory.Exists(_cachePath))
            {
                Directory.CreateDirectory(_cachePath);
            }

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }

            bufferSize ??= TusDiskBufferSize.Default;

            _maxWriteBufferSize = bufferSize.WriteBufferSizeInBytes;
            _maxReadBufferSize = bufferSize.ReadBufferSizeInBytes;
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

            var slug = await slugGenerator.GenerateSlug(cancellationToken);

            await File.Create(Path.Combine(_cachePath, slug + extension)).DisposeAsync();

            return slug;
        }

        public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            var upload = await context.Uploads.FindAsync(new[] { fileId }, cancellationToken);

            var (bytesWrittenThisRequest, clientDisconnectedDuringRead) = await AppendToFile(
                upload,
                stream,
                cancellationToken);

            if (clientDisconnectedDuringRead)
            {
                return bytesWrittenThisRequest;
            }

            upload.UploadedLength += bytesWrittenThisRequest;

            if (upload.Length == upload.UploadedLength)
            {
                File.Move(
                    Path.Combine(_cachePath, fileId + upload.Extension),
                    Path.Combine(_storagePath, fileId + upload.Extension),
                    true);
            }

            // Don't want the user to be able to cancel
            await context.SaveChangesAsync();

            return bytesWrittenThisRequest;
        }

        public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            return await context.Uploads.FindAsync(new[] { fileId }, cancellationToken) != null;
        }

        public async Task<ITusFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            await using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

            var upload = await context.Uploads.FindAsync(new[] { fileId }, cancellationToken);

            return upload == null
                ? null
                : new TusDiskFile(fileId, Path.Combine(_storagePath, fileId + upload.Extension), upload.Metadata);
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

        private async Task<(long bytesRead, bool clientDisconnectedDuringRead)> AppendToFile(
            Upload upload,
            Stream stream,
            CancellationToken cancellationToken)
        {
            // Most logic from tusdotnet.Stores.TusDiskStore.AppendDataAsync
            var httpReadBuffer = BufferPool.Rent(_maxReadBufferSize);
            var fileWriteBuffer = BufferPool.Rent(Math.Max(_maxWriteBufferSize, _maxReadBufferSize));

            try
            {
                await using var diskFileStream =
                    new FileStream(
                        Path.Combine(_cachePath, upload.Id + upload.Extension),
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        true);

                var totalDiskFileLength = diskFileStream.Length;

                if (upload.Length == totalDiskFileLength)
                {
                    return (0, false);
                }

                int numberOfBytesReadFromClient;
                var bytesWrittenThisRequest = 0L;
                var writeBufferNextFreeIndex = 0;
                var clientDisconnectedDuringRead = false;

                do
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    numberOfBytesReadFromClient = await stream.ReadAsync(httpReadBuffer, 0, _maxReadBufferSize, cancellationToken);

                    clientDisconnectedDuringRead = cancellationToken.IsCancellationRequested;

                    totalDiskFileLength += numberOfBytesReadFromClient;

                    if (totalDiskFileLength > upload.Length)
                    {
                        throw new TusStoreException($"Stream contains more data than the file's upload length. Stream data: {totalDiskFileLength}, upload length: {upload.Length}.");
                    }

                    // Can we fit the read data into the write buffer? If not flush it now.
                    if (writeBufferNextFreeIndex + numberOfBytesReadFromClient > _maxWriteBufferSize)
                    {
                        await FlushFileToDisk(fileWriteBuffer, diskFileStream, writeBufferNextFreeIndex);
                        writeBufferNextFreeIndex = 0;
                    }

                    Array.Copy(
                        sourceArray: httpReadBuffer,
                        sourceIndex: 0,
                        destinationArray: fileWriteBuffer,
                        destinationIndex: writeBufferNextFreeIndex,
                        length: numberOfBytesReadFromClient);

                    writeBufferNextFreeIndex += numberOfBytesReadFromClient;
                    bytesWrittenThisRequest += numberOfBytesReadFromClient;

                } while (numberOfBytesReadFromClient != 0);

                // Flush the remaining buffer to disk.
                if (writeBufferNextFreeIndex != 0)
                    await FlushFileToDisk(fileWriteBuffer, diskFileStream, writeBufferNextFreeIndex);

                return (bytesWrittenThisRequest, clientDisconnectedDuringRead);
            }
            finally
            {
                BufferPool.Return(httpReadBuffer);
                BufferPool.Return(fileWriteBuffer);
            }
        }

        private static async Task FlushFileToDisk(
            byte[] fileWriteBuffer,
            FileStream fileStream,
            int writeBufferNextFreeIndex)
        {
            await fileStream.WriteAsync(fileWriteBuffer, 0, writeBufferNextFreeIndex);
            await fileStream.FlushAsync();
        }
    }
}
