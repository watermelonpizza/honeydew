﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using tusdotnet.Stores;

namespace Honeydew.UploadStores
{
    public class DiskStore : UploadStore<DiskStoreOptions>
    {
        private readonly ILogger _logger;
        private readonly ApplicationDbContext _context;

        private string _cachePath;
        private string _storagePath;

        private readonly int _maxReadBufferSize;
        private readonly int _maxWriteBufferSize;

        // Use our own array pool to not leak data to other parts of the running app.
        private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Create();

        public DiskStore(
            ILogger<DiskStore> logger,
            IOptionsMonitor<DiskStoreOptions> options,
            ApplicationDbContext context)
            : base(options, context)
        {
            _logger = logger;
            _context = context;

            _maxWriteBufferSize = TusDiskBufferSize.Default.WriteBufferSizeInBytes;
            _maxReadBufferSize = TusDiskBufferSize.Default.ReadBufferSizeInBytes;
        }

        public override void SetupOptions(DiskStoreOptions options)
        {
            _cachePath = options.CacheDirectory;
            _storagePath = options.StorageDirectory;

            try
            {
                if (!Directory.Exists(_cachePath))
                {
                    Directory.CreateDirectory(_cachePath);
                }

            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Cache path `{cachePath}` didn't exist, and attempt was made to create it and failed.", _cachePath);
                throw;
            }

            try
            {
                if (!Directory.Exists(_storagePath))
                {
                    Directory.CreateDirectory(_storagePath);
                }
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Storage path `{cachePath}` didn't exist, and attempt was made to create it and failed.", _storagePath);
                throw;
            }
        }

        public override Task DeleteAsync(Upload upload, CancellationToken cancellationToken)
        {
            File.Delete(Path.Combine(_storagePath, upload.Id + upload.Extension));

            return Task.CompletedTask;
        }

        public override Task<DownloadResult> DownloadAsync(Upload upload, RangeHeaderValue range, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new DownloadResult
                {
                    Stream = File.OpenRead(Path.Combine(_storagePath, upload.Id + upload.Extension))
                });
        }

        public override async Task<long> AppendToUploadAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            var (bytesWrittenThisRequest, clientDisconnectedDuringRead) = await AppendToUploadInternalAsync(
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
                   Path.Combine(_cachePath, upload.Id + upload.Extension),
                   Path.Combine(_storagePath, upload.Id + upload.Extension),
                   true);
            }

            // Don't want the user to be able to cancel
            await _context.SaveChangesAsync();

            return bytesWrittenThisRequest;
        }

        public override async Task WriteAllBytesAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
        {
            var cacheFilePath = Path.Combine(_cachePath, upload.Id + upload.Extension);
            var targetFilePath = Path.Combine(_storagePath, upload.Id + upload.Extension);

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

        private static async Task FlushFileToDisk(
            byte[] fileWriteBuffer,
            FileStream fileStream,
            int writeBufferNextFreeIndex)
        {
            await fileStream.WriteAsync(fileWriteBuffer, 0, writeBufferNextFreeIndex);
            await fileStream.FlushAsync();
        }

        private async Task<(long bytesRead, bool clientDisconnectedDuringRead)> AppendToUploadInternalAsync(Upload upload, Stream stream, CancellationToken cancellationToken)
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
                        throw new Exception($"Stream contains more data than the file's upload length. Stream data: {totalDiskFileLength}, upload length: {upload.Length}.");
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
    }
}
