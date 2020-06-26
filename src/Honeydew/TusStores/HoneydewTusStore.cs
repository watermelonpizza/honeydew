using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;
using tusdotnet.Stores;

namespace Honeydew.TusStores
{
    public class HoneydewTusStore : IHoneydewTusStore
    {
        private readonly IServiceProvider _provider;

        public HoneydewTusStore(IServiceProvider provider)
        {
            _provider = provider;
        }

        public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            using var store = scope.ServiceProvider.GetService<IUploadStore>();

            var bytesWrittenThisRequest = await store.AppendToUploadAsync(
                fileId,
                stream,
                cancellationToken);

            return bytesWrittenThisRequest;
        }

        public async Task<string> CreateFileAsync(long uploadLength, string metadata, CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();
            var slug = scope.ServiceProvider.GetService<SlugGenerator>();

            return await slug.GenerateSlugAsync(cancellationToken);
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
            var store = scope.ServiceProvider.GetService<IUploadStore>();

            await store.DeleteAsync(fileId, cancellationToken);
        }
    }
}
