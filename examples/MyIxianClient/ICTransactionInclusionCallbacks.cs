using IXICore;
using IXICore.Meta;
using IXICore.Storage;
using IXICore.Streaming;
using System.Linq;

namespace IxianClient
{
    internal class ICTransactionInclusionCallbacks : TransactionInclusionCallbacks
    {
        public void receivedTIVResponse(Transaction tx, bool verified)
        {
            if (!verified)
            {
                tx.applied = 0;
                return;
            }
            else
            {
                PendingTransactions.remove(tx.id);
            }

            TransactionCache.addTransaction(tx);
            Friend friend = FriendList.getFriend(tx.pubKey);
            if (friend == null)
            {
                foreach (var toEntry in tx.toList)
                {
                    friend = FriendList.getFriend(toEntry.Key);
                    if (friend != null)
                    {
                        break;
                    }
                }
            }

            IxianHandler.balances.First().lastUpdate = 0;
        }

        public void receivedBlockHeader(Block block_header, bool verified)
        {
            foreach (Balance balance in IxianHandler.balances)
            {
                if (balance.blockChecksum != null && balance.blockChecksum.SequenceEqual(block_header.blockChecksum))
                {
                    balance.verified = true;
                }
            }

            if (block_header.blockNum >= IxianHandler.getHighestKnownNetworkBlockHeight())
            {
                IxianHandler.status = NodeStatus.ready;
            }
        }
    }
}
