using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.UploadStores
{
    public class DownloadResult
    {
        public Stream Stream { get; set; }
        public string ContentRange { get; set; }
    }
}
