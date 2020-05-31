using tusdotnet.Interfaces;

namespace Honeydew.UploadStores
{
    interface IHoneydewTusStore : ITusStore, ITusCreationStore, ITusReadableStore, ITusTerminationStore
    {
    }
}
