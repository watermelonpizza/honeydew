using tusdotnet.Interfaces;

namespace Honeydew.TusStores
{
    interface IHoneydewTusStore : ITusStore, ITusTerminationStore, ITusCreationStore
    {
    }
}
