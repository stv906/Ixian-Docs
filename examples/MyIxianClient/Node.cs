using IXICore;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Network.Messages;
using IXICore.RegNames;
using IXICore.Storage;
using IXICore.Streaming;
using IXICore.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static IXICore.Transaction;

namespace IxianClient
{
    public class Node : IxianNode
    {
        private bool running = false;
        private Thread? mainLoopThread = null;

        private TransactionInclusion? tiv = null;

        private long lastSectorUpdate = 0;

        private NetworkClientManagerStatic? networkClientManagerStatic = null;

        public Node()
        {
            Init();
        }

        private void Init()
        {
            // Initialize IxianHandler with your app version
            IxianHandler.init("MyClient v1.0.0", this, NetworkType.main);

            // Initialize wallet
            if (!InitWallet())
            {
                throw new Exception("Failed to initialize wallet");
            }

            // Initialize peer storage
            PeerStorage.init("");

            // Setup network client manager (queries blockchain via S2)
            networkClientManagerStatic = new NetworkClientManagerStatic(10);
            NetworkClientManager.init(networkClientManagerStatic);

            // Init TIV
            tiv = new TransactionInclusion(new ICTransactionInclusionCallbacks(), false);

            // Initialize presence list with keepalive
            PresenceList.init("", 0, 'C', CoreConfig.clientKeepAliveInterval);

            IxianHandler.localStorage = new LocalStorage("", new ICLocalStorageCallbacks());

            InventoryCache.init(new InventoryCacheClient(tiv));

            RelaySectors.init(CoreConfig.relaySectorLevels, null);

            Console.WriteLine($"Node initialized. Wallet: {IxianHandler.getWalletStorage().getPrimaryAddress()}");
        }


        private bool InitWallet()
        {
            string walletPath = "wallet.ixi";
            WalletStorage walletStorage = new WalletStorage(walletPath);

            Logging.flush();

            if (!walletStorage.walletExists())
            {
                ConsoleHelpers.displayBackupText();

                // Request a password
                string password = "";
                while (password.Length < 10)
                {
                    Logging.flush();
                    password = ConsoleHelpers.requestNewPassword("Enter a password for your new wallet: ");
                    if (IxianHandler.forceShutdown)
                    {
                        return false;
                    }
                }
                walletStorage.generateWallet(password);
            }
            else
            {
                ConsoleHelpers.displayBackupText();

                bool success = false;
                while (!success)
                {
                    string password = "";
                    if (password.Length < 10)
                    {
                        Logging.flush();
                        Console.Write("Enter wallet password: ");
                        password = ConsoleHelpers.getPasswordInput();
                    }
                    if (IxianHandler.forceShutdown)
                    {
                        return false;
                    }
                    if (walletStorage.readWallet(password))
                    {
                        success = true;
                    }
                }
            }


            if (walletStorage.getPrimaryPublicKey() == null)
            {
                return false;
            }

            // Wait for any pending log messages to be written
            Logging.flush();

            Console.WriteLine();
            Console.WriteLine("Your IXIAN addresses are: ");
            Console.ForegroundColor = ConsoleColor.Green;
            foreach (var entry in walletStorage.getMyAddressesBase58())
            {
                Console.WriteLine(entry);
            }
            Console.ResetColor();
            Console.WriteLine();

            Logging.info("Public Node Address: {0}", walletStorage.getPrimaryAddress().ToString());


            if (walletStorage.viewingWallet)
            {
                Logging.error("Viewing-only wallet {0} cannot be used as the primary wallet.", walletStorage.getPrimaryAddress().ToString());
                return false;
            }

            IxianHandler.addWallet(walletStorage);

            // Prepare the balances list
            List<Address> address_list = IxianHandler.getWalletStorage().getMyAddresses();
            foreach (Address addr in address_list)
            {
                IxianHandler.balances.Add(new Balance(addr, 0));
            }

            return true;
        }

        public void Start()
        {
            if (running) return;

            running = true;

            // Start TIV
            tiv?.start("headers", 0, null, true);

            // Start presence keepalive (announces our presence to network)
            PresenceList.startKeepAlive();

            // Start the network queue
            NetworkQueue.start();

            // Connect to your sector of S2 nodes
            NetworkClientManager.start(1);

            // Connect to S2 streaming nodes (for presence and messaging)
            StreamClientManager.start(6, true);

            // Start main loop for periodic tasks
            mainLoopThread = new Thread(MainLoop);
            mainLoopThread.Start();

            Console.WriteLine("Node started and connecting to network...");
        }

        public void Stop()
        {
            running = false;
            mainLoopThread?.Join();

            PeerStorage.savePeersFile(true);

            tiv?.stop();
            
            PresenceList.stopKeepAlive();
            NetworkQueue.stop();
            NetworkClientManager.stop();
            StreamClientManager.stop();

            Console.WriteLine("Node stopped.");
        }

        private void MainLoop()
        {
            while (running)
            {
                try
                {
                    // Fetch sector nodes
                    RequestSectorUpdate();

                    // Request balance update periodically
                    RequestBalanceUpdate();

                    // Check for balance changes
                    CheckBalanceChanges();

                    // Process pending transactions (resend if needed, check status)
                    processPendingTransactions();

                    // Cleanup old presence entries
                    PresenceList.performCleanup();

                    // Save peer data
                    PeerStorage.savePeersFile();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error in main loop: {e.Message}");
                }

                Thread.Sleep(5000);
            }
        }

        private void RequestSectorUpdate()
        {
            if (lastSectorUpdate + 300 < Clock.getTimestamp())
            {
                lastSectorUpdate = Clock.getTimestamp();
                CoreProtocolMessage.fetchSectorNodes(IxianHandler.primaryWalletAddress, CoreConfig.maxRelaySectorNodesToRequest);
            }
        }

        private void RequestBalanceUpdate()
        {
            var balance = IxianHandler.balances.FirstOrDefault();
            if (balance == null || balance.lastUpdate + 300 < Clock.getTimestamp())
            {
                var primaryAddress = IxianHandler.getWalletStorage().getPrimaryAddress();

                // Create balance request message
                byte[] getBalanceBytes;
                using (var ms = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(ms))
                    {
                        writer.WriteIxiVarInt(primaryAddress.addressNoChecksum.Length);
                        writer.Write(primaryAddress.addressNoChecksum);
                    }
                    getBalanceBytes = ms.ToArray();
                }

                // Broadcast balance request to network
                CoreProtocolMessage.broadcastProtocolMessage(
                    new char[] { 'M', 'H', 'R' },
                    ProtocolMessageCode.getBalance2,
                    getBalanceBytes,
                    null
                );
            }
        }

        // ===== IxianNode Abstract Method Implementations =====

        public override ulong getHighestKnownNetworkBlockHeight()
        {
            ulong bh = getLastBlockHeight();
            ulong netBlockNum = CoreProtocolMessage.determineHighestNetworkBlockNum();
            if (bh < netBlockNum)
            {
                bh = netBlockNum;
            }

            return bh;
        }

        public override Block getBlockHeader(ulong blockNum)
        {
            return BlockHeaderStorage.getBlockHeader(blockNum);
        }

        public override byte[] getBlockHash(ulong blockNum)
        {
            var block = getBlockHeader(blockNum);
            return block?.blockChecksum ?? null;
        }

        public override Block getLastBlock()
        {
            return tiv?.getLastBlockHeader() ?? null;
        }

        public override ulong getLastBlockHeight()
        {
            if (tiv?.getLastBlockHeader() == null)
            {
                return 0;
            }
            return tiv.getLastBlockHeader().blockNum;
        }

        public override int getLastBlockVersion()
        {
            return Block.maxVersion;
        }

        public override bool addTransaction(Transaction tx, List<Address> relayNodeAddresses, bool force_broadcast)
        {
            foreach (var address in relayNodeAddresses)
            {
                NetworkClientManager.sendToClient(address, ProtocolMessageCode.transactionData2, tx.getBytes(true, true), null);
            }
            PendingTransactions.addPendingLocalTransaction(tx, relayNodeAddresses);
            return true;
        }

        public override bool isAcceptingConnections()
        {
            return false; // Clients don't accept incoming connections
        }

        public override Wallet getWallet(Address id)
        {
            foreach (Balance balance in IxianHandler.balances)
            {
                if (id.addressNoChecksum.SequenceEqual(balance.address.addressNoChecksum))
                    return new Wallet(id, balance.balance);
            }
            return new Wallet(id, 0);
        }

        public override IxiNumber getWalletBalance(Address id)
        {
            var balance = IxianHandler.balances.FirstOrDefault(b => b.address.SequenceEqual(id));
            return balance?.balance ?? new IxiNumber(0);
        }

        public override void parseProtocolMessage(ProtocolMessageCode code, byte[] data, RemoteEndpoint endpoint)
        {

            if (endpoint == null)
            {
                Logging.error("Endpoint was null. parseProtocolMessage");
                return;
            }
            try
            {
                switch (code)
                {
                    case ProtocolMessageCode.hello:
                        {
                            using (MemoryStream m = new MemoryStream(data))
                            {
                                using (BinaryReader reader = new BinaryReader(m))
                                {
                                    CoreProtocolMessage.processHelloMessageV6(endpoint, reader);

                                    Friend friend = FriendList.getFriend(endpoint.presence.wallet);
                                    if (friend != null)
                                    {
                                        friend.updatedStreamingNodes = Clock.getNetworkTimestamp();
                                        friend.relayNode = new Peer(endpoint.getFullAddress(true), endpoint.presence.wallet, Clock.getTimestamp(), Clock.getTimestamp(), Clock.getTimestamp(), 0);
                                        friend.online = true;
                                    }
                                }
                            }

                        }
                        break;


                    case ProtocolMessageCode.helloData:
                        using (MemoryStream m = new MemoryStream(data))
                        {
                            using (BinaryReader reader = new BinaryReader(m))
                            {
                                if (!CoreProtocolMessage.processHelloMessageV6(endpoint, reader))
                                {
                                    return;
                                }

                                char node_type = endpoint.presenceAddress.type;

                                ulong last_block_num = reader.ReadIxiVarUInt();
                                int bcLen = (int)reader.ReadIxiVarUInt();
                                byte[] block_checksum = reader.ReadBytes(bcLen);

                                endpoint.blockHeight = last_block_num;

                                int block_version = (int)reader.ReadIxiVarUInt();

                                if (node_type != 'C' && node_type != 'R')
                                {
                                    ulong highest_block_height = IxianHandler.getHighestKnownNetworkBlockHeight();
                                    if (last_block_num + 10 < highest_block_height)
                                    {
                                        CoreProtocolMessage.sendBye(endpoint, ProtocolByeCode.tooFarBehind, string.Format("Your node is too far behind, your block height is {0}, highest network block height is {1}.", last_block_num, highest_block_height), highest_block_height.ToString(), true);
                                        return;
                                    }
                                }

                                // Process the hello data
                                endpoint.helloReceived = true;
                                NetworkClientManager.recalculateLocalTimeDifference();

                                if (node_type == 'R')
                                {
                                    string[] connected_servers = StreamClientManager.getConnectedClients(true);
                                    if (connected_servers.Count() > 0
                                        && !connected_servers.Contains(StreamClientManager.primaryS2Address))
                                    {
                                        // Update local presence information
                                        StreamClientManager.primaryS2Address = endpoint.getFullAddress(true);
                                        IxianHandler.publicPort = endpoint.incomingPort;
                                        IxianHandler.publicIP = endpoint.address;
                                        PresenceList.forceSendKeepAlive = true;
                                        Logging.info("Forcing KA from networkprotocol");
                                        RequestSectorUpdate();
                                    }
                                    else
                                    {
                                        // Announce local presence
                                        var myPresence = PresenceList.curNodePresence;
                                        if (myPresence != null)
                                        {
                                            foreach (var pa in myPresence.addresses)
                                            {
                                                byte[] hash = CryptoManager.lib.sha3_512sqTrunc(pa.getBytes());
                                                var iika = new InventoryItemKeepAlive(hash, pa.lastSeenTime, myPresence.wallet, pa.device);
                                                endpoint.addInventoryItem(iika);
                                            }
                                        }
                                    }

                                    // Fetch friends presences if outgoing stream capabilities are enabled
                                    if ((CoreStreamProcessor.streamCapabilities & StreamCapabilities.Outgoing) != 0)
                                    {
                                        CoreStreamProcessor.fetchAllFriendsPresencesInSector(endpoint.presence.wallet);
                                    }
                                }

                                if (node_type == 'M'
                                    || node_type == 'H'
                                    || node_type == 'R')
                                {
                                    SubscribeToEvents(endpoint);
                                }

                                Friend friend = FriendList.getFriend(endpoint.presence.wallet);
                                if (friend != null)
                                {
                                    friend.updatedStreamingNodes = Clock.getNetworkTimestamp();
                                    friend.relayNode = new Peer(endpoint.getFullAddress(true), endpoint.presence.wallet, Clock.getTimestamp(), Clock.getTimestamp(), Clock.getTimestamp(), 0);
                                    friend.online = true;
                                    if (node_type == 'C')
                                    {
                                        if (friend.bot)
                                        {
                                            CoreStreamProcessor.sendGetBotInfo(friend);
                                        }
                                    }
                                }
                            }
                        }
                        break;

                    case ProtocolMessageCode.s2data:
                        {
                        }
                        break;

                    case ProtocolMessageCode.getPresence2:
                        {
                            using (MemoryStream m = new MemoryStream(data))
                            {
                                using (BinaryReader reader = new BinaryReader(m))
                                {
                                    int walletLen = (int)reader.ReadIxiVarUInt();
                                    Address wallet = new Address(reader.ReadBytes(walletLen));

                                    Presence p = PresenceList.getPresenceByAddress(wallet);
                                    if (p != null)
                                    {
                                        lock (p)
                                        {
                                            byte[][] presence_chunks = p.getByteChunks();
                                            foreach (byte[] presence_chunk in presence_chunks)
                                            {
                                                endpoint.sendData(ProtocolMessageCode.updatePresence, presence_chunk, null);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Logging.warn("Node has requested presence information about {0} that is not in our PL.", wallet.ToString());
                                    }
                                }
                            }
                        }
                        break;

                    case ProtocolMessageCode.balance2:
                        {
                            using (MemoryStream m = new MemoryStream(data))
                            {
                                using (BinaryReader reader = new BinaryReader(m))
                                {
                                    int address_length = (int)reader.ReadIxiVarUInt();
                                    Address address = new Address(reader.ReadBytes(address_length));

                                    int balance_bytes_len = (int)reader.ReadIxiVarUInt();
                                    byte[] balance_bytes = reader.ReadBytes(balance_bytes_len);

                                    // Retrieve the latest balance
                                    IxiNumber ixi_balance = new IxiNumber(balance_bytes);

                                    // Retrieve the blockheight for the balance
                                    ulong block_height = reader.ReadIxiVarUInt();
                                    byte[] block_checksum = reader.ReadBytes((int)reader.ReadIxiVarUInt());

                                    foreach (Balance balance in IxianHandler.balances)
                                    {
                                        if (address.addressNoChecksum.SequenceEqual(balance.address.addressNoChecksum))
                                        {
                                            if (block_height > balance.blockHeight && (balance.balance != ixi_balance || balance.blockHeight == 0))
                                            {
                                                balance.address = address;
                                                balance.balance = ixi_balance;
                                                balance.blockHeight = block_height;
                                                balance.blockChecksum = block_checksum;
                                                balance.verified = false;
                                            }

                                            balance.lastUpdate = Clock.getTimestamp();
                                        }
                                    }
                                }
                            }
                        }
                        break;


                    case ProtocolMessageCode.updatePresence:
                        HandleUpdatePresence(data, endpoint);
                        break;

                    case ProtocolMessageCode.keepAlivePresence:
                        HandleKeepAlivePresence(data, endpoint);
                        break;

                    case ProtocolMessageCode.blockHeaders4:
                        {
                            using (MemoryStream m = new MemoryStream(data))
                            {
                                using (BinaryReader reader = new BinaryReader(m))
                                {
                                    ulong from = reader.ReadIxiVarUInt();
                                    ulong totalCount = reader.ReadIxiVarUInt();

                                    int filterLen = (int)reader.ReadIxiVarUInt();
                                    byte[] filterBytes = reader.ReadBytes(filterLen);

                                    byte[] headersBytes = new byte[reader.BaseStream.Length - reader.BaseStream.Position];
                                    Array.Copy(data, reader.BaseStream.Position, headersBytes, 0, headersBytes.Length);

                                    tiv?.receivedBlockHeaders3(headersBytes, endpoint);
                                }
                            }
                        }
                        break;

                    case ProtocolMessageCode.blockHeaders3:
                        {
                            // Forward the block headers to the TIV handler
                            tiv?.receivedBlockHeaders3(data, endpoint);
                        }
                        break;

                    case ProtocolMessageCode.pitData2:
                        {
                            tiv?.receivedPIT2(data, endpoint);
                        }
                        break;

                    case ProtocolMessageCode.transactionData2:
                        HandleTransactionData(data, endpoint);
                        break;

                    case ProtocolMessageCode.bye:
                        CoreProtocolMessage.processBye(data, endpoint);
                        break;

                    case ProtocolMessageCode.sectorNodes:
                        HandleSectorNodes(data, endpoint);
                        break;

                    case ProtocolMessageCode.keepAlivesChunk:
                        HandleKeepAlivesChunk(data, endpoint);
                        break;

                    case ProtocolMessageCode.getKeepAlives:
                        CoreProtocolMessage.processGetKeepAlives(data, endpoint);
                        break;

                    case ProtocolMessageCode.inventory2:
                        break;

                    case ProtocolMessageCode.rejected:
                        HandleRejected(data, endpoint);
                        break;

                    default:
                        Logging.warn("Unknown protocol message: {0}, from {1} ({2})", code, endpoint.getFullAddress(), endpoint.serverWalletAddress);
                        break;

                }
            }
            catch (Exception e)
            {
                Logging.error("Error parsing network message. Details: {0}", e.ToString());
            }
        }

        public override void shutdown()
        {
            Stop();
        }

        public override IxiNumber getMinSignerPowDifficulty(ulong blockNum, int curBlockVersion, long curBlockTimestamp)
        {
            return ConsensusConfig.minBlockSignerPowDifficulty;
        }

        public override RegisteredNameRecord getRegName(byte[] name, bool useAbsoluteId)
        {
            throw new NotImplementedException();
        }

        public (Transaction transaction, List<Address> relayNodeAddresses) prepareTransactionFrom(Address fromAddress, Address toAddress, IxiNumber amount)
        {
            IxiNumber fee = ConsensusConfig.forceTransactionPrice;
            Dictionary<Address, ToEntry> to_list = new(new AddressComparer());
            Address pubKey = new(IxianHandler.getWalletStorage().getPrimaryPublicKey());

            if (!IxianHandler.getWalletStorage().isMyAddress(fromAddress))
            {
                Logging.info("From address is not my address.");
                return (null, null);
            }

            Dictionary<byte[], IxiNumber> from_list = new(new ByteArrayComparer())
            {
                { IxianHandler.getWalletStorage().getAddress(fromAddress).nonce, amount }
            };

            to_list.AddOrReplace(toAddress, new ToEntry(Transaction.maxVersion, amount));

            List<Address> relayNodeAddresses = NetworkClientManager.getRandomConnectedClientAddresses(2);
            IxiNumber relayFee = 0;
            foreach (Address relayNodeAddress in relayNodeAddresses)
            {
                var tmpFee = fee > ConsensusConfig.transactionDustLimit ? fee : ConsensusConfig.transactionDustLimit;
                to_list.AddOrReplace(relayNodeAddress, new ToEntry(Transaction.maxVersion, tmpFee));
                relayFee += tmpFee;
            }

            // Prepare transaction to calculate fee
            Transaction transaction = new((int)Transaction.Type.Normal, fee, to_list, from_list, pubKey, IxianHandler.getHighestKnownNetworkBlockHeight());

            relayFee = 0;
            foreach (Address relayNodeAddress in relayNodeAddresses)
            {
                var tmpFee = transaction.fee > ConsensusConfig.transactionDustLimit ? transaction.fee : ConsensusConfig.transactionDustLimit;
                to_list[relayNodeAddress].amount = tmpFee;
                relayFee += tmpFee;
            }

            byte[] first_address = from_list.Keys.First();
            from_list[first_address] = from_list[first_address] + relayFee + transaction.fee;
            IxiNumber wal_bal = IxianHandler.getWalletBalance(new Address(transaction.pubKey.addressNoChecksum, first_address));
            if (from_list[first_address] > wal_bal)
            {
                IxiNumber maxAmount = wal_bal - transaction.fee;

                if (maxAmount < 0)
                    maxAmount = 0;

                Console.WriteLine($"Insufficient funds to cover amount and transaction fee.\nMaximum amount you can send is {maxAmount} IXI.\n");
                return (null, null);
            }
            // Prepare transaction with updated "from" amount to cover fee
            transaction = new((int)Transaction.Type.Normal, fee, to_list, from_list, pubKey, IxianHandler.getHighestKnownNetworkBlockHeight());
            return (transaction, relayNodeAddresses);
        }

        public Transaction sendTransactionFrom(Address fromAddress, Address toAddress, IxiNumber amount)
        {
            var prepTx = prepareTransactionFrom(fromAddress, toAddress, amount);
            var transaction = prepTx.transaction;
            var relayNodeAddresses = prepTx.relayNodeAddresses;
            
            if (transaction == null || relayNodeAddresses == null)
            {
                return null;
            }
            
            // Send the transaction
            if (IxianHandler.addTransaction(transaction, relayNodeAddresses, true))
            {
                Console.WriteLine($"Sending transaction, txid: {transaction.getTxIdString()}");
                return transaction;
            }
            else
            {
                Console.WriteLine($"Could not send transaction, txid: {transaction.getTxIdString()}");
            }
            return null;
        }

        public bool SendPayment(string toAddress, string amount)
        {
            try
            {
                var recipient = new Address(toAddress);
                var txAmount = new IxiNumber(amount);
                var myAddress = IxianHandler.getWalletStorage().getPrimaryAddress();

                var tx = sendTransactionFrom(myAddress, recipient, txAmount);
                return tx != null;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending payment: {e.Message}");
                return false;
            }
        }

        private IxiNumber lastKnownBalance = new IxiNumber(0);

        private void CheckBalanceChanges()
        {
            var myAddress = IxianHandler.getWalletStorage().getPrimaryAddress();
            var currentBalance = IxianHandler.getWalletBalance(myAddress);

            if (currentBalance != lastKnownBalance)
            {
                Console.WriteLine($"\n*** Balance changed: {lastKnownBalance} -> {currentBalance} IXI ***\n");
                lastKnownBalance = currentBalance;
            }
        }

        // Query your own balance
        public IxiNumber GetMyBalance()
        {
            var myAddress = IxianHandler.getWalletStorage().getPrimaryAddress();
            return IxianHandler.getWalletBalance(myAddress);
        }

        // Query any address balance (returns cached value, use RequestBalanceUpdate to refresh)
        public IxiNumber GetBalance(string address)
        {
            var addr = new Address(address);
            return IxianHandler.getWalletBalance(addr);
        }

        // Request fresh balance from network for specific address
        public void RequestBalanceUpdate(Address address)
        {
            byte[] getBalanceBytes;
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.WriteIxiVarInt(address.addressNoChecksum.Length);
                    writer.Write(address.addressNoChecksum);
                }
                getBalanceBytes = ms.ToArray();
            }

            CoreProtocolMessage.broadcastProtocolMessage(
                new char[] { 'M', 'H', 'R' },
                ProtocolMessageCode.getBalance2,
                getBalanceBytes,
                null
            );
        }
        // Check if an address is online (from local cache)
        public bool IsAddressOnline(string address)
        {
            var addr = new Address(address);
            var presence = PresenceList.getPresenceByAddress(addr);
            return presence != null;
        }

        // Get presence details for an address (from local cache)
        public Presence? GetPresence(string address)
        {
            var addr = new Address(address);
            return PresenceList.getPresenceByAddress(addr);
        }

        // Request sector nodes from the network that handle a specific address's sector
        // The sector is determined by the first 10 bytes of the address's sector prefix
        public void RequestSector(string address)
        {
            var addr = new Address(address);
            Console.WriteLine($"Requesting sectors for {address}...");
            
            // Fetch relay nodes (S2 nodes) that are responsible for this address's sector
            CoreProtocolMessage.fetchSectorNodes(addr, CoreConfig.maxRelaySectorNodesToRequest);
            
            // The network will respond with sectorNodes message containing relay node presences
            // This is handled by HandleSectorNodes() which updates RelaySectors and PeerStorage
        }

        // Request presence information for a specific address from the network
        // Uses the sector-based routing system to query the appropriate relay nodes
        public void RequestPresence(string address)
        {
            var addr = new Address(address);
            Console.WriteLine($"Requesting presence for {address}...");

            // Create a temporary friend object to use the sector-based presence fetching mechanism
            var friend = new Friend(FriendState.Approved, addr, null, "", null, null, 0, true);
            
            // Get relay nodes responsible for this address's sector from local cache
            List<Peer> peers = new();
            var relays = RelaySectors.Instance.getSectorNodes(addr.sectorPrefix, CoreConfig.maxRelaySectorNodesToRequest);
            foreach (var relay in relays)
            {
                var p = PresenceList.getPresenceByAddress(relay);
                if (p == null)
                {
                    continue;
                }
                var pa = p.addresses.First();
                peers.Add(new(pa.address, relay, pa.lastSeenTime, 0, 0, 0));
            }
            
            // Assign sector nodes to the friend and request presence via streaming protocol
            friend.sectorNodes = peers;
            friend.updatedSectorNodes = Clock.getTimestamp();
            CoreStreamProcessor.fetchFriendsPresence(friend);

            // The network will respond with updatePresence messages
        }

        // Display presence information for an address
        public void DisplayPresenceInfo(string address)
        {
            var addr = new Address(address);
            var presence = PresenceList.getPresenceByAddress(addr);

            if (presence == null)
            {
                Console.WriteLine($"  Status: Offline or not found in local cache");
                Console.WriteLine($"  Tip: The presence might not be cached yet. Wait a few seconds after RequestPresence().");
                return;
            }

            Console.WriteLine($"  Status: Online");
            Console.WriteLine($"  Wallet: {presence.wallet?.ToString() ?? "N/A"}");
            Console.WriteLine($"  Endpoints: {presence.addresses.Count}");

            foreach (var endpoint in presence.addresses)
            {
                char nodeType = endpoint.type;
                string typeDesc = nodeType switch
                {
                    'C' => "Client",
                    'M' => "Master (DLT)",
                    'H' => "Host (DLT)",
                    'R' => "Relay (S2)",
                    'W' => "Worker",
                    _ => "Unknown"
                };
                Console.WriteLine($"    - {endpoint.address} (type: {nodeType} - {typeDesc})");
            }
        }

        // Get transaction status
        public string GetTransactionStatus(byte[] txid)
        {
            // Check if transaction is confirmed
            Transaction confirmedTx = TransactionCache.getTransaction(txid);
            if (confirmedTx != null && confirmedTx.applied != 0)
            {
                return $"Confirmed in block {confirmedTx.applied}";
            }

            // Check if transaction is pending
            Transaction unconfirmedTx = TransactionCache.getUnconfirmedTransaction(txid);
            if (unconfirmedTx != null)
            {
                return "Pending (waiting for confirmation)";
            }

            // Check pending transactions list
            var pendingTx = PendingTransactions.getPendingTransaction(txid);
            if (pendingTx != null)
            {
                return $"Sent (confirmed by {pendingTx.confirmedNodeList.Count} nodes)";
            }

            return "Unknown (not found)";
        }

        // Get transaction status by string txid
        public string GetTransactionStatus(string txidString)
        {
            try
            {
                byte[] txid = Transaction.txIdLegacyToV8(txidString);
                return GetTransactionStatus(txid);
            }
            catch
            {
                return "Invalid transaction ID";
            }
        }

        private void SubscribeToEvents(RemoteEndpoint endpoint)
        {
            CoreProtocolMessage.subscribeToEvents(endpoint);

            // Subscribe to friend presences if outgoing stream capabilities are enabled
            if ((CoreStreamProcessor.streamCapabilities & StreamCapabilities.Outgoing) != 0)
            {
                byte[] friend_matcher = FriendList.getFriendCuckooFilter();
                if (friend_matcher != null)
                {
                    byte[] event_data = NetworkEvents.prepareEventMessageData(NetworkEvents.Type.keepAlive, friend_matcher);
                    endpoint.sendData(ProtocolMessageCode.attachEvent, event_data);
                }
            }
        }

        private void HandleTransactionData(byte[] data, RemoteEndpoint endpoint)
        {
            Transaction tx = new Transaction(data, true, true);

            if (endpoint.presenceAddress.type == 'M'
                || endpoint.presenceAddress.type == 'H'
                || endpoint.presenceAddress.type == 'R')
            {
                PendingTransactions.increaseReceivedCount(tx.id, endpoint.presence.wallet);
            }

            TransactionCache.addUnconfirmedTransaction(tx);

            tiv?.receivedNewTransaction(tx);
        }


        private void HandleSectorNodes(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;

            var prefixAndOffset = data.ReadIxiBytes(offset);
            offset += prefixAndOffset.bytesRead;
            byte[] prefix = prefixAndOffset.bytes;

            var nodeCountAndOffset = data.GetIxiVarUInt(offset);
            offset += nodeCountAndOffset.bytesRead;
            int nodeCount = (int)nodeCountAndOffset.num;

            for (int i = 0; i < nodeCount; i++)
            {
                var kaBytesAndOffset = data.ReadIxiBytes(offset);
                offset += kaBytesAndOffset.bytesRead;

                Presence p = PresenceList.updateFromBytes(kaBytesAndOffset.bytes, IxianHandler.getMinSignerPowDifficulty(IxianHandler.getLastBlockHeight() + 1, IxianHandler.getLastBlockVersion(), Clock.getNetworkTimestamp()));
                if (p != null)
                {
                    RelaySectors.Instance.addRelayNode(p.wallet);
                }
            }

            List<Peer> peers = new();
            var relays = RelaySectors.Instance.getSectorNodes(prefix, CoreConfig.maxRelaySectorNodesToRequest);
            foreach (var relay in relays)
            {
                var p = PresenceList.getPresenceByAddress(relay);
                if (p == null)
                {
                    continue;
                }
                var pa = p.addresses.First();
                peers.Add(new(pa.address, relay, pa.lastSeenTime, 0, 0, 0));

                PeerStorage.addPeerToPeerList(pa.address, p.wallet, pa.lastSeenTime, 0, 0, 0);
            }

            if (IxianHandler.primaryWalletAddress.sectorPrefix.SequenceEqual(prefix))
            {
                networkClientManagerStatic.setClientsToConnectTo(peers);
            }

            var friends = FriendList.getFriendsBySectorPrefix(prefix);
            foreach (var friend in friends)
            {
                friend.updatedSectorNodes = Clock.getNetworkTimestamp();
                friend.sectorNodes = peers;
            }
        }


        private void HandleKeepAlivesChunk(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    int ka_count = (int)reader.ReadIxiVarUInt();

                    int max_ka_per_chunk = CoreConfig.maximumKeepAlivesPerChunk;
                    if (ka_count > max_ka_per_chunk)
                    {
                        ka_count = max_ka_per_chunk;
                    }

                    for (int i = 0; i < ka_count; i++)
                    {
                        if (m.Position == m.Length)
                        {
                            break;
                        }

                        int ka_len = (int)reader.ReadIxiVarUInt();
                        byte[] ka_bytes = reader.ReadBytes(ka_len);

                        HandleKeepAlivePresence(ka_bytes, endpoint);
                    }
                }
            }
        }

        private void HandleRejected(byte[] data, RemoteEndpoint endpoint)
        {
            try
            {
                Rejected rej = new Rejected(data);
                switch (rej.code)
                {
                    case RejectedCode.TransactionInvalid:
                    case RejectedCode.TransactionInsufficientFee:
                    case RejectedCode.TransactionDust:
                        Logging.error("Transaction {0} was rejected with code: {1}", Crypto.hashToString(rej.data), rej.code);
                        PendingTransactions.remove(rej.data);
                        // TODO flag transaction as invalid
                        break;

                    case RejectedCode.TransactionDuplicate:
                        Logging.warn("Transaction {0} already sent.", Crypto.hashToString(rej.data), rej.code);
                        // All good
                        PendingTransactions.increaseReceivedCount(rej.data, endpoint.serverWalletAddress);
                        break;

                    default:
                        Logging.error("Received 'rejected' message with unknown code {0} {1}", rej.code, Crypto.hashToString(rej.data));
                        break;
                }
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Exception occured while processing 'rejected' message with code {0} {1}", data[0], Crypto.hashToString(data)), e);
            }
        }

        private void HandleUpdatePresence(byte[] data, RemoteEndpoint endpoint)
        {

            // Parse the data and update entries in the presence list
            Presence p = PresenceList.updateFromBytes(data, 0);
            if (p == null)
            {
                return;
            }

            Logging.info("Received presence update for " + p.wallet);
            Friend f = FriendList.getFriend(p.wallet);
            if (f != null)
            {
                var pa = p.addresses[0];
                f.relayNode = new Peer(pa.address, null, pa.lastSeenTime, 0, 0, 0);
                f.updatedStreamingNodes = pa.lastSeenTime;
            }
        }

        private void HandleKeepAlivePresence(byte[] data, RemoteEndpoint endpoint)
        {
            byte[] hash = CryptoManager.lib.sha3_512sqTrunc(data);

            InventoryCache.Instance.setProcessedFlag(InventoryItemTypes.keepAlive, hash);

            Address address;
            long last_seen;
            byte[] device_id;
            char node_type;
            bool updated = PresenceList.receiveKeepAlive(data, out address, out last_seen, out device_id, out node_type, endpoint);

            Logging.trace("Received keepalive update for " + address);
            Presence p = PresenceList.getPresenceByAddress(address);
            if (p == null)
                return;

            Friend f = FriendList.getFriend(p.wallet);
            if (f != null)
            {
                var pa = p.addresses[0];
                f.relayNode = new Peer(pa.address, null, pa.lastSeenTime, 0, 0, 0);
                f.updatedStreamingNodes = pa.lastSeenTime;
            }
        }

        public static void processPendingTransactions()
        {
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            lock (PendingTransactions.pendingTransactions)
            {
                long cur_time = Clock.getTimestamp();
                List<PendingTransaction> tmp_pending_transactions = new(PendingTransactions.pendingTransactions);
                foreach (var entry in tmp_pending_transactions)
                {
                    long tx_time = entry.addedTimestamp;

                    if (entry.transaction.blockHeight > last_block_height)
                    {
                        // not ready yet, syncing to the network
                        continue;
                    }

                    Transaction t = TransactionCache.getTransaction(entry.transaction.id);
                    if (t == null)
                    {
                        t = entry.transaction;
                    }
                    else
                    {
                        if (t.applied != 0)
                        {
                            PendingTransactions.pendingTransactions.RemoveAll(x => x.transaction.id.SequenceEqual(t.id));
                            continue;
                        }
                    }

                    // if transaction expired, remove it from pending transactions
                    if (last_block_height > ConsensusConfig.getRedactedWindowSize()
                        && t.blockHeight < last_block_height - ConsensusConfig.getRedactedWindowSize())
                    {
                        Logging.error("Error sending the transaction {0}, expired", t.getTxIdString());
                        PendingTransactions.pendingTransactions.RemoveAll(x => x.transaction.id.SequenceEqual(t.id));
                        continue;
                    }

                    if (entry.rejectedNodeList.Count() > 3
                        && entry.rejectedNodeList.Count() > entry.confirmedNodeList.Count())
                    {
                        Logging.error("Error sending the transaction {0}, rejected", t.getTxIdString());
                        PendingTransactions.pendingTransactions.RemoveAll(x => x.transaction.id.SequenceEqual(t.id));
                        continue;
                    }

                    if (cur_time - tx_time > 60) // if the transaction is pending for over 60 seconds, resend
                    {
                        Logging.warn("Transaction {0} pending for a while, resending", t.getTxIdString());
                        foreach (var address in entry.relayNodeAddresses)
                        {
                            NetworkClientManager.sendToClient(address, ProtocolMessageCode.transactionData2, t.getBytes(true, true), null);
                        }
                        CoreProtocolMessage.broadcastGetTransaction(t.id, 0);
                        entry.addedTimestamp = cur_time;
                        entry.confirmedNodeList.Clear();
                        entry.rejectedNodeList.Clear();
                    }
                }
            }
        }
    }
}
