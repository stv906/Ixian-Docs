using IXICore;
using IXICore.Storage;
using IXICore.Streaming;

namespace IxianClient
{
    internal class ICLocalStorageCallbacks : LocalStorageCallbacks
    {
        public bool receivedNewTransaction(Transaction transaction)
        {
            return false;
        }

        public void processMessage(FriendMessage friendMessage)
        {
        }
    }
}
