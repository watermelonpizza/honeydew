using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Honeydew.UploadStores
{
    public class CloudProviderTusFile : ITusFile
    {
        private readonly string _metadata;

        public string Id { get; }

        public CloudProviderTusFile(string id, string metadata)
        {
            Id = id;
            _metadata = metadata;
        }

        public Task<Stream> GetContentAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Metadata.Parse(_metadata));
        }
    }
}
