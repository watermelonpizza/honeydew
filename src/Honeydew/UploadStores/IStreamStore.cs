using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Honeydew.UploadStores
{
    public interface IStreamStore
    {
        public Task WriteAllBytesAsync(string name, Stream stream, CancellationToken cancellationToken);
    }
}
