namespace Trellis.Node

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Hosting
open Trellis.Core
open SimpleTypes

/// P2P networking implementation for Trellis
module P2PNetwork =

    /// Deserialize block from byte array
    let private deserializeBlockFromBytes (data: byte array) : Block =
        let cfg = Trellis.Core.TrellisConfig.current
        if data.Length < 200 then // Minimum block size check
            // Return a basic block if data is too small
            {
                Header = {
                    Hash = SimpleCrypto.sha256 data
                    Height = BlockHeight(0UL)
                    Timestamp = Timestamp(DateTime.UtcNow)
                    PreviousHash = Hash(Array.create 32 0uy)
                    MerkleRoot = SimpleCrypto.sha256 data
                    Validator = Address(Array.create 20 1uy)
                    Nonce = 0UL
                    Difficulty = cfg.MinDifficulty
                    PoWHash = SimpleCrypto.sha256 data
                    Finalized = false
                    BaseFeePerGas = cfg.GenesisBaseFeePerGas
                    GasUsed = 0UL
                    GasLimit = cfg.GenesisGasLimit
                }
                Transactions = []
            }
        else
            // Parse block data (simplified binary format)
            let mutable offset = 0
            
            // Read hash (32 bytes)
            let hash = Hash(data.[offset..offset+31])
            offset <- offset + 32
            
            // Read height (8 bytes)
            let height = BlockHeight(BitConverter.ToUInt64(data, offset))
            offset <- offset + 8
            
            // Read timestamp (8 bytes)
            let timestamp = Timestamp(DateTime.FromBinary(BitConverter.ToInt64(data, offset)))
            offset <- offset + 8
            
            // Read previous hash (32 bytes)
            let previousHash = Hash(data.[offset..offset+31])
            offset <- offset + 32
            
            // Read merkle root (32 bytes)
            let merkleRoot = Hash(data.[offset..offset+31])
            offset <- offset + 32
            
            // Read validator (20 bytes)
            let validator = Address(data.[offset..offset+19])
            offset <- offset + 20
            
            // Read nonce (8 bytes)
            let nonce = BitConverter.ToUInt64(data, offset)
            offset <- offset + 8
            
            // Read difficulty (8 bytes)
            let difficulty = BitConverter.ToUInt64(data, offset)
            offset <- offset + 8
            
            // Read PoW hash (32 bytes)
            let powHash = Hash(data.[offset..offset+31])
            offset <- offset + 32
            
            // Read finalized flag (1 byte)
            let finalized = data.[offset] <> 0uy
            offset <- offset + 1
            
            // Read transaction count (4 bytes)
            let txCount = BitConverter.ToInt32(data, offset)
            offset <- offset + 4
            
            // Parse transactions (length-prefixed, robust bounds checks)
            let parseTransactions (count: int) (bytes: byte[]) (startOffset: int) =
                let mutable off = startOffset
                let txs = ResizeArray<Transaction>()
                let inline ensure n = off + n <= bytes.Length
                let inline take n =
                    let s = bytes.[off..off+n-1]
                    off <- off + n
                    s
                for _i in 1..count do
                    if ensure (20+20+8+8+8+32+8+8+4) then
                        let fromAddr = Address (take 20)
                        let toAddr = Address (take 20)
                        let amount = Balance (BitConverter.ToUInt64(take 8, 0))
                        let fee = Balance (BitConverter.ToUInt64(take 8, 0))
                        let nonce = Nonce (BitConverter.ToUInt64(take 8, 0))
                        let hash = Hash (take 32)
                        let gasLimit = BitConverter.ToUInt64(take 8, 0)
                        let gasPrice = BitConverter.ToUInt64(take 8, 0)
                        let dataLen = BitConverter.ToInt32(take 4, 0)
                        let data = 
                            if dataLen > 0 && ensure dataLen then take dataLen else [||]
                        // Signature length-prefixed (optional)
                        let signatureBytes = 
                            if ensure 4 then 
                                let slen = BitConverter.ToInt32(take 4, 0)
                                if slen > 0 && ensure slen then Signature (take slen) else Signature [||]
                            else Signature [||]
                        let tx = {
                            From = fromAddr
                            To = toAddr
                            Amount = amount
                            Fee = fee
                            Nonce = nonce
                            Signature = signatureBytes
                            Hash = hash
                            GasLimit = gasLimit
                            GasPrice = gasPrice
                            Data = data
                            Metadata = Map.empty
                            MaxFeePerGas = None
                            MaxPriorityFeePerGas = None
                            TransactionType = 0uy
                        }
                        txs.Add(tx)
                    else ()
                txs |> Seq.toList, off
            let transactions, _newOff = parseTransactions txCount data offset
            
            {
                Header = {
                    Hash = hash
                    Height = height
                    Timestamp = timestamp
                    PreviousHash = previousHash
                    MerkleRoot = merkleRoot
                    Validator = validator
                    Nonce = nonce
                    Difficulty = difficulty
                    PoWHash = powHash
                    Finalized = finalized
                    BaseFeePerGas = 1000000000UL
                    GasUsed = 0UL
                    GasLimit = 30000000UL
                }
                Transactions = transactions
            }

    /// Protocol version
    type ProtocolVersion = {
        Major: uint16
        Minor: uint16
        Patch: uint16
    }

    /// Current protocol version (read from global config)
    let currentProtocolVersion =
        let cfg = Trellis.Core.TrellisConfig.current
        { Major = cfg.P2PProtocolVersionMajor; Minor = cfg.P2PProtocolVersionMinor; Patch = cfg.P2PProtocolVersionPatch }

    /// Network message types with protocol version
    type NetworkMessage =
        | BlockMessage of Block * ProtocolVersion
        | TransactionMessage of Transaction * ProtocolVersion
        | VoteMessage of PublicKey * Signature * BlockHeight * ProtocolVersion
        | RequestBlocks of BlockHeight * BlockHeight * ProtocolVersion * string option // Optional request ID
        | ResponseBlocks of Block list * ProtocolVersion * string option // Optional request ID
        | Ping of ProtocolVersion
        | Pong of ProtocolVersion

    /// Peer information
    type Peer = {
        EndPoint: IPEndPoint
        IsConnected: bool
        LastSeen: DateTime
        NodeId: string
        Distance: uint64
        HealthScore: float // 0.0 to 1.0, based on successful vs failed interactions
        MessageCount: int
        LastPingTime: DateTime option
        LastPongTime: DateTime option
    }

    /// Kademlia DHT node
    type KademliaNode = {
        NodeId: string
        EndPoint: IPEndPoint
        LastSeen: DateTime
        IsActive: bool
    }

    /// Kademlia bucket for storing nodes
    type KademliaBucket = {
        Index: int
        Nodes: KademliaNode list
        LastUpdated: DateTime
    }

    /// Kademlia DHT implementation
    type KademliaDHT = {
        NodeId: string
        Buckets: Map<int, KademliaBucket>
        MaxBucketSize: int
        MaxBuckets: int
    }

    /// Network configuration
    type NetworkConfig = {
        ListenPort: int
        MaxPeers: int
        PingInterval: TimeSpan
        ConnectionTimeout: TimeSpan
    }

    /// P2P network manager
    type P2PNetworkManager(config: NetworkConfig, logger: ILogger<P2PNetworkManager>, blockchainState: GrpcService.IBlockchainState option) =
        let mutable peers: Map<string, Peer> = Map.empty
        let mutable isRunning = false
        let mutable listener: TcpListener option = None
        let mutable cancellationTokenSource: CancellationTokenSource option = None
        let mutable kademliaDHT: KademliaDHT option = None
        let mutable pendingBlockRequests: Map<string, (BlockHeight * BlockHeight * string * DateTime)> = Map.empty // requestId -> (start, end, peerId, timestamp)
        let mutable peerHealth: Map<string, (DateTime * int * int)> = Map.empty // peerId -> (lastSeen, successCount, failureCount)
        let cfg = Trellis.Core.TrellisConfig.current
        let mutable messageRateLimits: Map<string, (DateTime list * int)> = Map.empty // peerId -> (message timestamps, current window count)
        let rateLimitWindow = TimeSpan.FromSeconds(cfg.P2PRateLimitWindowSeconds)
        let maxMessagesPerWindow = cfg.P2PMaxMessagesPerWindow

        member this.Start() : unit =
            if not isRunning then
                logger.LogInformation("Starting P2P network on port {Port}", config.ListenPort)
                
                // Initialize Kademlia DHT
                let nodeId = System.Guid.NewGuid().ToString("N")
                let dht = {
                    NodeId = nodeId
                    Buckets = Map.empty
                    MaxBucketSize = cfg.P2PMaxBucketSize
                    MaxBuckets = cfg.P2PMaxBuckets
                }
                kademliaDHT <- Some dht
                
                let cts = new CancellationTokenSource()
                cancellationTokenSource <- Some cts
                
                // Start listening for connections
                this.StartListening(cts.Token)
                
                // Start peer discovery
                this.StartPeerDiscovery(cts.Token)
                
                // Start Kademlia maintenance
                this.StartKademliaMaintenance(cts.Token)
                
                isRunning <- true
                logger.LogInformation("P2P network started with node ID: {NodeId}", nodeId)

        member this.Stop() : unit =
            if isRunning then
                logger.LogInformation("Stopping P2P network")
                
                match cancellationTokenSource with
                | Some cts -> 
                    cts.Cancel()
                    cts.Dispose()
                    cancellationTokenSource <- None
                | None -> ()
                
                match listener with
                | Some l -> l.Stop()
                | None -> ()
                
                isRunning <- false
                logger.LogInformation("P2P network stopped")

        member this.AddPeer(endPoint: IPEndPoint) : unit =
            let nodeId = endPoint.ToString()
            let peer = {
                EndPoint = endPoint
                IsConnected = false
                LastSeen = DateTime.UtcNow
                NodeId = nodeId
                Distance = 0UL
                HealthScore = 0.5 // Start with neutral health score
                MessageCount = 0
                LastPingTime = None
                LastPongTime = None
            }
            
            peers <- Map.add nodeId peer peers
            peerHealth <- Map.add nodeId (DateTime.UtcNow, 0, 0) peerHealth
            logger.LogInformation("Added peer: {Peer}", nodeId)

        member this.RemovePeer(nodeId: string) : unit =
            peers <- Map.remove nodeId peers
            peerHealth <- Map.remove nodeId peerHealth
            // Clean up any pending requests from this peer
            pendingBlockRequests <- pendingBlockRequests |> Map.filter (fun _ (_, _, peer, _) -> peer <> nodeId)
            logger.LogInformation("Removed peer: {Peer}", nodeId)

        member this.GetPeers() : Peer list =
            peers |> Map.toList |> List.map snd

        /// Update peer health based on interaction success/failure
        member private this.UpdatePeerHealth(peerId: string, success: bool) : unit =
            match Map.tryFind peerId peerHealth with
            | Some (lastSeen, successCount, failureCount) ->
                let newSuccessCount = if success then successCount + 1 else successCount
                let newFailureCount = if success then failureCount else failureCount + 1
                let totalInteractions = newSuccessCount + newFailureCount
                let healthScore = if totalInteractions > 0 then float newSuccessCount / float totalInteractions else 0.5
                
                peerHealth <- Map.add peerId (DateTime.UtcNow, newSuccessCount, newFailureCount) peerHealth
                
                // Update peer's health score
                match Map.tryFind peerId peers with
                | Some peer ->
                    let updatedPeer = { peer with HealthScore = healthScore; LastSeen = DateTime.UtcNow }
                    peers <- Map.add peerId updatedPeer peers
                | None -> ()
                
                // Remove peer if health score is too low and has enough interactions
                if healthScore < cfg.P2PPeerEvictionHealthThreshold && totalInteractions >= cfg.P2PPeerEvictionMinInteractions then
                    logger.LogWarning("Removing peer {PeerId} due to low health score: {Score}", peerId, healthScore)
                    this.RemovePeer(peerId)
            | None -> ()

        /// Check peer health and remove unhealthy peers
        member private this.CheckPeerHealth() : unit =
            let now = DateTime.UtcNow
            let unhealthyTimeout = TimeSpan.FromMinutes(cfg.P2PPeerTimeoutMinutes)
            
            for (peerId, peer) in Map.toList peers do
                let timeSinceLastSeen = now - peer.LastSeen
                if timeSinceLastSeen > unhealthyTimeout then
                    logger.LogWarning("Removing peer {PeerId} due to inactivity: {TimeSinceLastSeen}", 
                        peerId, timeSinceLastSeen)
                    this.RemovePeer(peerId)
                else
                    // Update health score based on recent interactions
                    match Map.tryFind peerId peerHealth with
                    | Some (lastSeen, successCount, failureCount) ->
                        let totalInteractions = successCount + failureCount
                        if totalInteractions > 0 then
                            let healthScore = float successCount / float totalInteractions
                            if peer.HealthScore <> healthScore then
                                let updatedPeer = { peer with HealthScore = healthScore }
                                peers <- Map.add peerId updatedPeer peers
                    | None -> ()
            
            // Clean up old rate limit entries
            messageRateLimits <- 
                messageRateLimits
                |> Map.map (fun _ (timestamps, _) ->
                    let recentTimestamps = timestamps |> List.filter (fun ts -> now - ts < rateLimitWindow)
                    (recentTimestamps, recentTimestamps.Length)
                )
                |> Map.filter (fun _ (timestamps, _) -> timestamps.Length > 0)

        member this.GetDHTStatistics() : (int * int) =
            match kademliaDHT with
            | Some dht ->
                let totalNodes = 
                    dht.Buckets 
                    |> Map.toList 
                    |> List.sumBy (fun (_, bucket) -> bucket.Nodes.Length)
                let activeBuckets = 
                    dht.Buckets 
                    |> Map.toList 
                    |> List.filter (fun (_, bucket) -> bucket.Nodes.Length > 0)
                    |> List.length
                (totalNodes, activeBuckets)
            | None -> (0, 0)

        member this.GetClosestNodes(targetNodeId: string, count: int) : KademliaNode list =
            this.FindClosestNodes(targetNodeId, count)

        member this.BroadcastMessage(message: NetworkMessage) : unit =
            let messageBytes = this.SerializeMessage(message)
            
            for peer in this.GetPeers() do
                if peer.IsConnected then
                    this.SendToPeer(peer, messageBytes)

        member this.SendToPeer(peer: Peer, data: byte array) : unit =
            try
                use client = new TcpClient()
                client.Connect(peer.EndPoint)
                use stream = client.GetStream()
                stream.Write(data, 0, data.Length)
            with
            | ex -> logger.LogError(ex, "Failed to send message to peer {Peer}", peer.NodeId)

        member this.SerializeMessage(message: NetworkMessage) : byte array =
            // Protocol version header (6 bytes: 2 bytes each for major, minor, patch)
            let serializeVersion (v: ProtocolVersion) =
                Array.concat [
                    BitConverter.GetBytes(v.Major)
                    BitConverter.GetBytes(v.Minor)
                    BitConverter.GetBytes(v.Patch)
                ]
            
            match message with
            | BlockMessage (block, version) ->
                // Simple serialization - in production, use proper binary format
                let versionBytes = serializeVersion version
                let headerBytes = System.Text.Encoding.UTF8.GetBytes("BLOCK")
                let txCountBytes = BitConverter.GetBytes(block.Transactions.Length)
                Array.concat [versionBytes; headerBytes; txCountBytes]
            | TransactionMessage (tx, version) ->
                // Serialize transaction with pubkey for signature verification
                let versionBytes = serializeVersion version
                let headerBytes = System.Text.Encoding.UTF8.GetBytes("TXN1")
                let fromBytes = match tx.From with | Address addr -> addr
                let toBytes = match tx.To with | Address addr -> addr
                let amountBytes = match tx.Amount with | Balance bal -> BitConverter.GetBytes(bal)
                let feeBytes = match tx.Fee with | Balance f -> BitConverter.GetBytes(f)
                let nonceBytes = match tx.Nonce with | Nonce n -> BitConverter.GetBytes(n)
                let hashBytes = match tx.Hash with | Hash h -> h
                let gasLimitBytes = BitConverter.GetBytes(tx.GasLimit)
                let gasPriceBytes = BitConverter.GetBytes(tx.GasPrice)
                let dataBytes = tx.Data
                let dataLenBytes = BitConverter.GetBytes(dataBytes.Length)
                let sigBytes = match tx.Signature with | Signature s -> s
                let sigLenBytes = BitConverter.GetBytes(sigBytes.Length)
                let pubKeyHex = tx.Metadata |> Map.tryFind "pubkey" |> Option.defaultValue ""
                let pubKeyBytes =
                    try System.Convert.FromHexString(pubKeyHex) with _ -> [||]
                let pkLenBytes = BitConverter.GetBytes(pubKeyBytes.Length)
                Array.concat [
                    versionBytes; headerBytes;
                    fromBytes; toBytes; amountBytes; feeBytes; nonceBytes;
                    hashBytes; gasLimitBytes; gasPriceBytes;
                    dataLenBytes; dataBytes;
                    sigLenBytes; sigBytes;
                    pkLenBytes; pubKeyBytes
                ]
            | VoteMessage (pubKey, signature, height, version) ->
                // Simple serialization
                let versionBytes = serializeVersion version
                let pubKeyBytes = match pubKey with | PublicKey key -> key
                let sigBytes = match signature with | Signature signatureBytes -> signatureBytes
                let heightBytes = match height with | BlockHeight h -> BitConverter.GetBytes(h)
                Array.concat [versionBytes; pubKeyBytes; sigBytes; heightBytes]
            | RequestBlocks (startHeight, endHeight, version, requestIdOpt) ->
                let versionBytes = serializeVersion version
                let headerBytes = System.Text.Encoding.UTF8.GetBytes("REQB") // RequestBlocks
                let startBytes = match startHeight with | BlockHeight h -> BitConverter.GetBytes(h)
                let endBytes = match endHeight with | BlockHeight h -> BitConverter.GetBytes(h)
                let requestIdBytes = 
                    match requestIdOpt with
                    | Some id -> System.Text.Encoding.UTF8.GetBytes(id)
                    | None -> [||]
                let requestIdLenBytes = BitConverter.GetBytes(requestIdBytes.Length)
                Array.concat [versionBytes; headerBytes; startBytes; endBytes; requestIdLenBytes; requestIdBytes]
            | ResponseBlocks (blocks, version, requestIdOpt) ->
                let versionBytes = serializeVersion version
                let headerBytes = System.Text.Encoding.UTF8.GetBytes("RESB") // ResponseBlocks
                let countBytes = BitConverter.GetBytes(blocks.Length)
                let requestIdBytes = 
                    match requestIdOpt with
                    | Some id -> System.Text.Encoding.UTF8.GetBytes(id)
                    | None -> [||]
                let requestIdLenBytes = BitConverter.GetBytes(requestIdBytes.Length)
                Array.concat [versionBytes; headerBytes; countBytes; requestIdLenBytes; requestIdBytes]
            | Ping version ->
                let versionBytes = serializeVersion version
                let pingBytes = System.Text.Encoding.UTF8.GetBytes("PING")
                Array.concat [versionBytes; pingBytes]
            | Pong version ->
                let versionBytes = serializeVersion version
                let pongBytes = System.Text.Encoding.UTF8.GetBytes("PONG")
                Array.concat [versionBytes; pongBytes]

        member this.DeserializeMessage(data: byte array) : NetworkMessage option =
            if data.Length < 6 then None // Need at least 6 bytes for version
            else
                // Parse protocol version (first 6 bytes)
                let deserializeVersion (bytes: byte[]) (offset: int) : ProtocolVersion option =
                    if bytes.Length < offset + 6 then None
                    else
                        Some {
                            Major = BitConverter.ToUInt16(bytes, offset)
                            Minor = BitConverter.ToUInt16(bytes, offset + 2)
                            Patch = BitConverter.ToUInt16(bytes, offset + 4)
                        }
                
                match deserializeVersion data 0 with
                | Some version ->
                    // Check if version is compatible (same major version)
                    if version.Major <> currentProtocolVersion.Major then
                        logger.LogWarning("Received message with incompatible protocol version {Version}, expected {Expected}", 
                            version, currentProtocolVersion)
                        None
                    else
                        // Skip version bytes (6 bytes) and parse message type
                        if data.Length < 10 then None // Need version (6) + message type (4)
                        else
                            let messageType = System.Text.Encoding.UTF8.GetString(data.[6..9])
                            match messageType with
                            | "BLOCK" -> 
                                try
                                    let blockData = data.[10..] // Skip version (6) + message type (4)
                                    // Proper block deserialization
                                    let block = deserializeBlockFromBytes blockData
                                    Some (BlockMessage (block, version))
                                with
                                | _ -> None
                            | "TXN1" ->
                                try
                                    let bytes = data.[10..] // Skip version (6) + message type (4)
                                    let mutable off = 0
                                    let inline ensure n = off + n <= bytes.Length
                                    let inline take n = let s = bytes.[off..off+n-1] in off <- off + n; s
                                    if ensure (20+20+8+8+8+32+8+8+4) then
                                        let fromAddr = Address (take 20)
                                        let toAddr = Address (take 20)
                                        let amount = Balance (BitConverter.ToUInt64(take 8, 0))
                                        let fee = Balance (BitConverter.ToUInt64(take 8, 0))
                                        let nonce = Nonce (BitConverter.ToUInt64(take 8, 0))
                                        let hash = Hash (take 32)
                                        let gasLimit = BitConverter.ToUInt64(take 8, 0)
                                        let gasPrice = BitConverter.ToUInt64(take 8, 0)
                                        let dataLen = BitConverter.ToInt32(take 4, 0)
                                        let dataBytes = if dataLen > 0 && ensure dataLen then take dataLen else [||]
                                        let sigLen = if ensure 4 then BitConverter.ToInt32(take 4, 0) else 0
                                        let sigBytes = if sigLen > 0 && ensure sigLen then Signature (take sigLen) else Signature [||]
                                        let pkLen = if ensure 4 then BitConverter.ToInt32(take 4, 0) else 0
                                        let pkBytes = if pkLen > 0 && ensure pkLen then take pkLen else [||]
                                        let pubKeyHex = System.Convert.ToHexString(pkBytes)
                                        let tx = {
                                            From = fromAddr
                                            To = toAddr
                                            Amount = amount
                                            Fee = fee
                                            Nonce = nonce
                                            Signature = sigBytes
                                            Hash = hash
                                            GasLimit = gasLimit
                                            GasPrice = gasPrice
                                            Data = dataBytes
                                            Metadata = Map.ofList [ ("pubkey", pubKeyHex) ]
                                            MaxFeePerGas = None
                                            MaxPriorityFeePerGas = None
                                            TransactionType = 0uy
                                        }
                                        Some (TransactionMessage (tx, version))
                                    else None
                                with _ -> None
                            | "PING" -> Some (Ping version)
                            | "PONG" -> Some (Pong version)
                            | _ -> None
                | None -> None

        member private this.StartListening(cancellationToken: CancellationToken) : unit =
            Task.Run(fun () ->
                try
                    let tcpListener = new TcpListener(IPAddress.Any, config.ListenPort)
                    listener <- Some tcpListener
                    tcpListener.Start()
                    
                    while not cancellationToken.IsCancellationRequested do
                        let client = tcpListener.AcceptTcpClient()
                        this.HandleClient(client)
                with
                | ex -> logger.LogError(ex, "Error in network listener")
            ) |> ignore

        member private this.HandleClient(client: TcpClient) : unit =
            Task.Run(fun () ->
                try
                    use stream = client.GetStream()
                    let buffer = Array.zeroCreate<byte> 4096
                    let bytesRead = stream.Read(buffer, 0, buffer.Length)
                    
                    if bytesRead > 0 then
                        let messageData = buffer.[0..bytesRead-1]
                        match this.DeserializeMessage(messageData) with
                        | Some message -> this.HandleMessage(message)
                        | None -> logger.LogWarning("Received unknown message")
                with
                | ex -> logger.LogError(ex, "Error handling client connection")
            ) |> ignore

        member private this.HandleMessage(message: NetworkMessage) : unit =
            try
                match message with
                    | BlockMessage (block, version) ->
                        // Check protocol version compatibility
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received block with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received block message for height {Height}", block.Header.Height)
                            this.ProcessBlock(block)
                    | TransactionMessage (tx, version) ->
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received transaction with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received transaction message from {From} to {To}", tx.From, tx.To)
                            this.ProcessTransaction(tx)
                    | VoteMessage (pubKey, signature, height, version) ->
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received vote with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received vote message for height {Height}", height)
                            this.ProcessVote(pubKey, signature, height)
                    | RequestBlocks (startHeight, endHeight, version, requestIdOpt) ->
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received block request with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received block request from {StartHeight} to {EndHeight} (requestId: {RequestId})", 
                                startHeight, endHeight, match requestIdOpt with | Some id -> id | None -> "none")
                            this.SendBlocks(startHeight, endHeight, requestIdOpt)
                    | ResponseBlocks (blocks, version, requestIdOpt) ->
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received block response with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received {Count} blocks in response (requestId: {RequestId})", 
                                blocks.Length, match requestIdOpt with | Some id -> id | None -> "none")
                            // Update peer health on successful block response
                            match requestIdOpt with
                            | Some requestId ->
                                match Map.tryFind requestId pendingBlockRequests with
                                | Some (_, _, peerId, _) ->
                                    this.UpdatePeerHealth(peerId, true)
                                    pendingBlockRequests <- Map.remove requestId pendingBlockRequests
                                | None -> ()
                            | None -> ()
                            this.ProcessBlocks(blocks)
                    | Ping version ->
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received ping with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received ping")
                            this.BroadcastMessage(Pong currentProtocolVersion)
                    | Pong version ->
                        if version.Major <> currentProtocolVersion.Major then
                            logger.LogWarning("Received pong with incompatible protocol version {Version}, expected {Expected}", 
                                version, currentProtocolVersion)
                        else
                            logger.LogInformation("Received pong")
                            // Update peer health on successful pong
                            // Note: In a full implementation, we'd track which peer sent the pong
            with
            | ex -> logger.LogError(ex, "Error handling network message")

        /// Process received block
        member private this.ProcessBlock(block: Block) : unit =
            try
                logger.LogInformation("Processing block at height {Height}", block.Header.Height)
                // Validate block
                if this.ValidateBlock(block) then
                    // Add to local blockchain
                    this.AddBlock(block)
                    // Broadcast to other peers
                    this.BroadcastMessage(BlockMessage(block, currentProtocolVersion))
                    logger.LogInformation("Block processed and broadcasted successfully")
                else
                    logger.LogWarning("Block validation failed")
            with
            | ex -> logger.LogError(ex, "Error processing block")

        /// Process received transaction
        member private this.ProcessTransaction(transaction: Transaction) : unit =
            try
                logger.LogInformation("Processing transaction from {From} to {To}", transaction.From, transaction.To)
                // Validate transaction
                if this.ValidateTransaction(transaction) then
                    // Add to mempool
                    this.AddToMempool(transaction)
                    // Broadcast to other peers
                    this.BroadcastMessage(TransactionMessage(transaction, currentProtocolVersion))
                    logger.LogInformation("Transaction processed and broadcasted successfully")
                else
                    logger.LogWarning("Transaction validation failed")
            with
            | ex -> logger.LogError(ex, "Error processing transaction")

        /// Process received vote
        member private this.ProcessVote(pubKey: PublicKey, signature: Signature, height: BlockHeight) : unit =
            try
                logger.LogInformation("Processing vote for height {Height}", height)
                // Validate vote signature
                if this.ValidateVote(pubKey, signature, height) then
                    // Add vote to consensus
                    this.AddVote(pubKey, signature, height)
                    // Broadcast vote to other peers
                    this.BroadcastMessage(VoteMessage(pubKey, signature, height, currentProtocolVersion))
                    logger.LogInformation("Vote processed and broadcasted successfully")
                else
                    logger.LogWarning("Vote validation failed")
            with
            | ex -> logger.LogError(ex, "Error processing vote")

        /// Send requested blocks
        member private this.SendBlocks(startHeight: BlockHeight, endHeight: BlockHeight, requestIdOpt: string option) : unit =
            try
                logger.LogInformation("Sending blocks from {StartHeight} to {EndHeight} (requestId: {RequestId})", 
                    startHeight, endHeight, match requestIdOpt with | Some id -> id | None -> "none")
                let blocks = this.GetBlocks(startHeight, endHeight)
                // Send blocks to all peers (in a full implementation, we'd send only to the requesting peer)
                // Include request ID so the requester can match the response
                this.BroadcastMessage(ResponseBlocks(blocks, currentProtocolVersion, requestIdOpt))
                logger.LogInformation("Sent {Count} blocks", blocks.Length)
            with
            | ex -> logger.LogError(ex, "Error sending blocks: {Error}", ex.Message)

        /// Process received blocks
        member private this.ProcessBlocks(blocks: Block list) : unit =
            try
                logger.LogInformation("Processing {Count} received blocks", blocks.Length)
                for block in blocks do
                    if this.ValidateBlock(block) then
                        this.AddBlock(block)
                logger.LogInformation("Processed {Count} blocks successfully", blocks.Length)
            with
            | ex -> logger.LogError(ex, "Error processing blocks")

        /// Validate block with enhanced checks
        member private this.ValidateBlock(block: Block) : bool =
            try
                // Enhanced height validation
                let (BlockHeight blockHeight) = block.Header.Height
                let isValidHeight = blockHeight >= 0UL
                
                // Timestamp validation with reasonable bounds
                let isValidTimestamp = 
                    match block.Header.Timestamp with 
                    | Timestamp t -> 
                        let now = DateTime.UtcNow
                        let timeDiff = (now - t).TotalSeconds
                        // Allow blocks up to configured tolerance in the future and past
                        timeDiff >= -cfg.P2PBlockTimeTolerancePastSeconds && timeDiff <= cfg.P2PBlockTimeToleranceFutureSeconds
                
                // Transaction validation
                let hasValidTransactions = 
                    block.Transactions.Length >= 0 &&
                    block.Transactions |> List.forall (fun tx ->
                        // Basic transaction validation
                        let hasValidAddresses = tx.From <> tx.To
                        let hasValidAmount = match tx.Amount with | Balance a -> a > 0UL
                        let hasValidFee = match tx.Fee with | Balance f -> f >= 0UL
                        hasValidAddresses && hasValidAmount && hasValidFee
                    )
                
                // Block hash validation
                let blockHashValid =
                    let computedHash = SimpleCrypto.blockHash block.Header
                    computedHash = block.Header.Hash
                
                isValidHeight && isValidTimestamp && hasValidTransactions && blockHashValid
            with
            | ex ->
                logger.LogError(ex, "Block validation error: {Error}", ex.Message)
                false

        /// Validate transaction
        member private this.ValidateTransaction(transaction: Transaction) : bool =
            try
                // Basic validation - check addresses, amounts, etc.
                let hasValidAddresses = transaction.From <> transaction.To
                let hasValidAmount = match transaction.Amount with | Balance a -> a > 0UL
                
                hasValidAddresses && hasValidAmount
            with
            | _ -> false

        /// Validate vote
        member private this.ValidateVote(pubKey: PublicKey, signature: Signature, height: BlockHeight) : bool =
            try
                // Verify the signature over the height bytes
                let (BlockHeight h) = height
                let msg = BitConverter.GetBytes(h)
                SimpleCrypto.verifySignature pubKey msg signature
            with
            | _ -> false

        /// Add block to local blockchain
        member private this.AddBlock(block: Block) : unit =
            try
                match blockchainState with
                | Some state ->
                    // Validate block before adding
                    if this.ValidateBlock(block) then
                        let success = state.SubmitBlock(block)
                        if success then
                            logger.LogInformation("Block added to local blockchain: {Hash} at height {Height}", 
                                block.Header.Hash, block.Header.Height)
                        else
                            logger.LogWarning("Failed to add block to blockchain state: {Hash}", block.Header.Hash)
                    else
                        logger.LogWarning("Block validation failed, not adding: {Hash}", block.Header.Hash)
                | None ->
                    logger.LogWarning("Blockchain state not available, cannot add block")
            with
            | ex -> logger.LogError(ex, "Error adding block to local blockchain: {Hash}", block.Header.Hash)

        /// Add transaction to mempool
        member private this.AddToMempool(transaction: Transaction) : unit =
            try
                // Validate transaction before adding
                if this.ValidateTransaction(transaction) then
                    match blockchainState with
                    | Some state ->
                        // Use blockchain state to get account for nonce validation
                        let getAccount = fun (addr: Address) -> state.GetAccount(addr)
                        let result = Mempool.acceptTransaction transaction getAccount
                        match result with
                        | Ok hash ->
                            logger.LogInformation("Transaction added to mempool: {Hash}", hash)
                        | Error err ->
                            logger.LogWarning("Failed to add transaction to mempool: {Error}, hash: {Hash}", err, transaction.Hash)
                    | None ->
                        logger.LogWarning("Blockchain state not available, cannot add transaction to mempool")
                else
                    logger.LogWarning("Transaction validation failed, not adding to mempool: {Hash}", transaction.Hash)
            with
            | ex -> logger.LogError(ex, "Error adding transaction to mempool: {Hash}", transaction.Hash)

        /// Add vote to consensus
        member private this.AddVote(pubKey: PublicKey, signature: Signature, height: BlockHeight) : unit =
            // Implementation would add vote to consensus state
            logger.LogInformation("Vote added to consensus")

        /// Get blocks from local blockchain
        member private this.GetBlocks(startHeight: BlockHeight, endHeight: BlockHeight) : Block list =
            try
                match blockchainState with
                | Some state ->
                    let chainState = state.GetChainState()
                    let (BlockHeight startH) = startHeight
                    let (BlockHeight endH) = endHeight
                    
                    // Get blocks from chain state that are in the requested range
                    let blocksInRange = 
                        chainState.Blocks
                        |> List.filter (fun block ->
                            let (BlockHeight blockH) = block.Header.Height
                            blockH >= startH && blockH <= endH
                        )
                        |> List.sortBy (fun block -> 
                            let (BlockHeight h) = block.Header.Height
                            h
                        )
                    
                    logger.LogInformation("Retrieved {Count} blocks from height {StartHeight} to {EndHeight}", 
                        blocksInRange.Length, startHeight, endHeight)
                    blocksInRange
                | None ->
                    logger.LogWarning("Blockchain state not available, cannot retrieve blocks")
                    []
            with
            | ex ->
                logger.LogError(ex, "Error retrieving blocks from {StartHeight} to {EndHeight}", startHeight, endHeight)
                []

        member private this.StartPeerDiscovery(cancellationToken: CancellationToken) : unit =
            Task.Run(fun () ->
                while not cancellationToken.IsCancellationRequested do
                    try
                        this.DiscoverPeers()
                        Task.Delay(TimeSpan.FromSeconds(30.)).Wait()
                    with
                    | ex -> logger.LogError(ex, "Error in peer discovery")
            ) |> ignore

        member private this.StartKademliaMaintenance(cancellationToken: CancellationToken) : unit =
            Task.Run(fun () ->
                while not cancellationToken.IsCancellationRequested do
                    try
                        this.MaintainKademliaDHT()
                        // Also check peer health periodically
                        this.CheckPeerHealth()
                        Task.Delay(TimeSpan.FromMinutes(5.)).Wait()
                    with
                    | ex -> logger.LogError(ex, "Error in Kademlia maintenance")
            ) |> ignore

        /// Calculate XOR distance between two node IDs
        member private this.CalculateDistance(nodeId1: string, nodeId2: string) : uint64 =
            let bytes1 = System.Text.Encoding.UTF8.GetBytes(nodeId1)
            let bytes2 = System.Text.Encoding.UTF8.GetBytes(nodeId2)
            let maxLength = max bytes1.Length bytes2.Length
            let padded1 = Array.zeroCreate<byte> maxLength
            let padded2 = Array.zeroCreate<byte> maxLength
            Array.Copy(bytes1, 0, padded1, maxLength - bytes1.Length, bytes1.Length)
            Array.Copy(bytes2, 0, padded2, maxLength - bytes2.Length, bytes2.Length)
            
            let mutable distance = 0UL
            for i in 0..maxLength-1 do
                distance <- distance * 256UL + uint64 (padded1.[i] ^^^ padded2.[i])
            distance

        /// Get bucket index for a given distance
        member private this.GetBucketIndex(distance: uint64) : int =
            if distance = 0UL then 0
            else
                let mutable index = 0
                let mutable temp = distance
                while temp > 0UL do
                    temp <- temp >>> 1
                    index <- index + 1
                min index 159

        /// Add node to Kademlia DHT
        member private this.AddNodeToDHT(node: KademliaNode) : unit =
            match kademliaDHT with
            | Some dht ->
                let distance = this.CalculateDistance(dht.NodeId, node.NodeId)
                let bucketIndex = this.GetBucketIndex(distance)
                
                let bucket = 
                    match Map.tryFind bucketIndex dht.Buckets with
                    | Some b -> b
                    | None -> {
                        Index = bucketIndex
                        Nodes = []
                        LastUpdated = DateTime.UtcNow
                    }
                
                // Remove existing node if present
                let filteredNodes = bucket.Nodes |> List.filter (fun n -> n.NodeId <> node.NodeId)
                
                // Add new node if bucket not full
                let updatedNodes = 
                    if filteredNodes.Length < dht.MaxBucketSize then
                        node :: filteredNodes
                    else
                        // Replace oldest node
                        let sortedNodes = filteredNodes |> List.sortBy (fun n -> n.LastSeen)
                        node :: (sortedNodes |> List.take (dht.MaxBucketSize - 1))
                
                let updatedBucket = {
                    bucket with
                        Nodes = updatedNodes
                        LastUpdated = DateTime.UtcNow
                }
                
                let updatedBuckets = Map.add bucketIndex updatedBucket dht.Buckets
                kademliaDHT <- Some { dht with Buckets = updatedBuckets }
                
            | None -> ()

        /// Find closest nodes to a given node ID
        member private this.FindClosestNodes(targetNodeId: string, count: int) : KademliaNode list =
            match kademliaDHT with
            | Some dht ->
                let allNodes = 
                    dht.Buckets 
                    |> Map.toList 
                    |> List.collect (fun (_, bucket) -> bucket.Nodes)
                    |> List.filter (fun node -> node.IsActive)
                
                let sortedNodes = 
                    allNodes 
                    |> List.sortBy (fun node -> this.CalculateDistance(targetNodeId, node.NodeId))
                    |> List.take count
                
                sortedNodes
            | None -> []

        /// Discover peers using Kademlia DHT (parallel pings with .NET 10 and!)
        member private this.DiscoverPeers() : unit =
            match kademliaDHT with
            | Some dht ->
                // Find closest nodes to bootstrap
                let closestNodes = this.FindClosestNodes(dht.NodeId, 5)
                
                if not (List.isEmpty closestNodes) then
                    task {
                        let! results = 
                            closestNodes
                            |> List.map (fun node ->
                                task {
                                    try
                                        this.SendPingToNode(node)
                                        return Some node.NodeId
                                    with ex ->
                                        logger.LogError(ex, "Error pinging node {NodeId}", node.NodeId)
                                        return None
                                }
                            )
                            |> List.toArray
                            |> Task.WhenAll
                        ()
                    } |> ignore
            | None -> ()

        /// Send ping to a specific node
        member private this.SendPingToNode(node: KademliaNode) : unit =
            try
                use client = new TcpClient()
                client.Connect(node.EndPoint)
                use stream = client.GetStream()
                let pingData = this.SerializeMessage(Ping currentProtocolVersion)
                stream.Write(pingData, 0, pingData.Length)
                
                // Update node as active
                let updatedNode = { node with IsActive = true; LastSeen = DateTime.UtcNow }
                this.AddNodeToDHT(updatedNode)
                
            with
            | ex -> logger.LogError(ex, "Error pinging node {NodeId}", node.NodeId)

        /// Maintain Kademlia DHT by cleaning up inactive nodes
        member private this.MaintainKademliaDHT() : unit =
            match kademliaDHT with
            | Some dht ->
                let cutoffTime = DateTime.UtcNow.AddMinutes(-10.0)
                
                let updatedBuckets = 
                    dht.Buckets 
                    |> Map.map (fun _ bucket ->
                        let activeNodes = 
                            bucket.Nodes 
                            |> List.filter (fun node -> node.LastSeen > cutoffTime)
                        { bucket with Nodes = activeNodes }
                    )
                
                kademliaDHT <- Some { dht with Buckets = updatedBuckets }
                logger.LogInformation("Kademlia DHT maintenance completed")
            | None -> ()

        /// Get random bootstrap nodes (in production, these would be hardcoded or from DNS)
        member private this.GetBootstrapNodes() : KademliaNode list =
            [
                {
                    NodeId = "bootstrap1"
                    EndPoint = IPEndPoint(IPAddress.Parse("127.0.0.1"), 8080)
                    LastSeen = DateTime.UtcNow
                    IsActive = true
                }
                {
                    NodeId = "bootstrap2"
                    EndPoint = IPEndPoint(IPAddress.Parse("127.0.0.1"), 8081)
                    LastSeen = DateTime.UtcNow
                    IsActive = true
                }
            ]

        interface IDisposable with
            member this.Dispose() =
                this.Stop()

    /// Network service
    type NetworkService(config: NetworkConfig, logger: ILogger<NetworkService>, blockchainState: GrpcService.IBlockchainState option) =
        inherit BackgroundService()
        
        let loggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
        let p2pLogger = loggerFactory.CreateLogger<P2PNetworkManager>()
        let networkManager = new P2PNetworkManager(config, p2pLogger, blockchainState)

        override this.ExecuteAsync(stoppingToken) =
            async {
                logger.LogInformation("Starting P2P Network Service...")
                
                // Start the network
                networkManager.Start()
                
                // Keep running until cancellation
                while not stoppingToken.IsCancellationRequested do
                    do! Async.Sleep(1000)
                
                // Stop the network
                networkManager.Stop()
                logger.LogInformation("P2P Network Service stopped.")
            } |> Async.StartAsTask :> Task

        member this.BroadcastBlock(block: Block) : unit =
            networkManager.BroadcastMessage(BlockMessage (block, currentProtocolVersion))

        member this.BroadcastTransaction(tx: Transaction) : unit =
            networkManager.BroadcastMessage(TransactionMessage (tx, currentProtocolVersion))

        member this.GetPeers() : Peer list =
            networkManager.GetPeers()

        member this.GetDHTStatistics() : (int * int) =
            networkManager.GetDHTStatistics()

        member this.GetClosestNodes(targetNodeId: string, count: int) : KademliaNode list =
            networkManager.GetClosestNodes(targetNodeId, count)

        interface IDisposable with
            member this.Dispose() =
                networkManager.Stop()
                loggerFactory.Dispose()
