using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Honeydew.UploadStores
{
    public class TusDiskFile : ITusFile
    {
        private readonly string _filePath;
        private readonly string _metadata;

        public string Id { get; }

        public TusDiskFile(string fileId, string filePath, string metadata)
        {
            Id = fileId;
            _filePath = filePath;
            _metadata = metadata;
        }

        public Task<Stream> GetContentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(
                File.Open(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        public Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
        {
            var parsedMetadata = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, _metadata);
            return Task.FromResult(parsedMetadata.Metadata);
        }

    }
}
