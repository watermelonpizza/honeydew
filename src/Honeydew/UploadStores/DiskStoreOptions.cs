namespace Honeydew.UploadStores
{
    public class DiskStoreOptions : IStoreOptions
    {
        public string CacheDirectory { get; set; }
        public string StorageDirectory { get; set; }
    }
}