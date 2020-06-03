namespace Honeydew.UploadStores
{
    public class AzureBlobsStoreOptions : IStoreOptions
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
    }
}