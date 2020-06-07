using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.UploadStores
{
    public class S3StoreOptions : IStoreOptions
    {
        public string Bucket { get; set; }
        public string AccessKey { get; set; }
        public string SecretAccessKey { get; set; }
        public string Region { get; set; }
        public long MaximumAllowedRangeLengthFromBucketInBytes { get; set; }
    }
}
