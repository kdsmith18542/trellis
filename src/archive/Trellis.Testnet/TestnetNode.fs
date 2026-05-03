namespace Trellis.Testnet

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Testnet node for full blockchain functionality testing
module TestnetNode =

    /// Testnet node state
    type TestnetNodeState = {
        NodeId: string
        IsRunning: bool
        CurrentHeight: BlockHeight
        Peers: Map<string, string> // peerId -> endpoint
        PendingTransactions: Map<Hash, Transaction>
        Blockchain: Block list
        Wallet: Map<Address, Balance>
        Nonce: Nonce
        IsMining: bool
        MiningTarget: Hash
    }

    /// Testnet node message
    type TestnetNodeMessage =
        | Start
        | Stop
        | ProcessTransaction of Transaction
        | CreateTransaction of Address * Balance
        | StartMining
        | StopMining
        | MineBlock
        | SyncWithPeer of string * Block list
        | GetState
        | GetBalance of Address
        | GetBlockchain
        | GetPendingTransactions

    /// Testnet node implementation
    type TestnetNode(nodeId: string, logger: ILogger<TestnetNode>) =
        let mutable state: TestnetNodeState = {
            NodeId = nodeId
            IsRunning = false
            CurrentHeight = BlockHeight(0UL)
            Peers = Map.empty
            PendingTransactions = Map.empty
            Blockchain = []
            Wallet = Map.empty
            Nonce = Nonce(0UL)
            IsMining = false
            MiningTarget = Hash(Array.create 32 0uy)
        }

        let (publicKey, privateKey) = generateKeyPair()
        let nodeAddress = Address(Array.create 20 1uy) // Test node address

        let createGenesisBlock() : Block =
            let genesisHeader = {
                Height = BlockHeight(0UL)
                PreviousHash = Hash(Array.create 32 0uy)
                PoWHash = Hash(Array.create 32 0uy)
                Timestamp = Timestamp(DateTime.UtcNow)
                Finalized = false
            }
            
            let genesisTransaction = {
                From = Address(Array.create 20 0uy) // Genesis address (zero)
                To = nodeAddress
                Amount = Balance(1000000UL) // 1M test tokens
                Nonce = Nonce(0UL)
                Timestamp = Timestamp(DateTime.UtcNow)
                Signature = Signature(Array.create 64 0uy) // Genesis signature (zero)
            }
            
            let block = {
                Header = genesisHeader
                Transactions = [genesisTransaction]
            }
            
            // Initialize wallet with genesis balance
            let newWallet = Map.add nodeAddress (Balance(1000000UL)) state.Wallet
            state <- { state with 
                Blockchain = [block]
                Wallet = newWallet
            }
            
            block

        let validateTransaction(tx: Transaction) : bool =
            try
                // Check if sender has sufficient balance
                let (Balance amount) = tx.Amount
                if amount > 0UL then
                    // Simplified validation for testing
                    true
                else
                    false
            with
            | _ -> false

        let processTransaction(tx: Transaction) : bool =
            try
                if validateTransaction(tx) then
                    let txHash = sha256 (System.Text.Encoding.UTF8.GetBytes(sprintf "%A" tx))
                    let newPendingTxs = Map.add txHash tx state.PendingTransactions
                    state <- { state with 
                        PendingTransactions = newPendingTxs
                    }
                    logger.LogInformation("Node {NodeId} processed transaction for {Amount} tokens", 
                        nodeId, match tx.Amount with | Balance b -> b)
                    true
                else
                    false
            with
            | ex -> 
                logger.LogError(ex, "Failed to process transaction")
                false

        let createTransaction(toAddress: Address, amount: Balance) : Transaction option =
            try
                let (Balance txAmount) = amount
                let (Balance currentBalance) = 
                    Map.tryFind nodeAddress state.Wallet 
                    |> Option.defaultValue (Balance(0UL))
                    |> fun (Balance b) -> Balance(b)
                
                if txAmount <= (match currentBalance with | Balance b -> b) then
                    let (Nonce currentNonce) = state.Nonce
                    let newNonce = Nonce(currentNonce + 1UL)
                    
                    let transaction = {
                        From = nodeAddress
                        To = toAddress
                        Amount = amount
                        Nonce = newNonce
                        Timestamp = Timestamp(DateTime.UtcNow)
                        Signature = Signature(Array.create 64 1uy) // Test signature
                    }
                    
                    state <- { state with Nonce = newNonce }
                    Some transaction
                else
                    None
            with
            | ex -> 
                logger.LogError(ex, "Failed to create transaction")
                None

        let calculateMiningTarget() : Hash =
            // Simplified mining target calculation
            let difficulty = 4 // Number of leading zeros required
            let target = Array.create 32 0xFFuy // Start with all 1s
            for i in 0..difficulty-1 do
                target.[i] <- 0uy // Set leading bytes to 0 for difficulty
            Hash(target)

        let mineBlock() : Block option =
            try
                if state.PendingTransactions.Count > 0 then
                    let (BlockHeight currentHeight) = state.CurrentHeight
                    let newHeight = BlockHeight(currentHeight + 1UL)
                    
                    let previousHash = 
                        if state.Blockchain.Length > 0 then
                            blockHash state.Blockchain.[0].Header
                        else
                            Hash(Array.create 32 0uy)
                    
                    let blockHeader = {
                        Height = newHeight
                        PreviousHash = previousHash
                        PoWHash = Hash(Array.create 32 0uy) // Simplified for testing
                        Timestamp = Timestamp(DateTime.UtcNow)
                        Finalized = false
                    }
                    
                    let transactions = Map.values state.PendingTransactions |> List.ofSeq
                    let block = {
                        Header = blockHeader
                        Transactions = transactions
                    }
                    
                    // Add block to blockchain
                    state <- { 
                        state with 
                            Blockchain = block :: state.Blockchain
                            CurrentHeight = newHeight
                            PendingTransactions = Map.empty
                    }
                    
                    logger.LogInformation("Node {NodeId} mined block at height {Height} with {TxCount} transactions", 
                        nodeId, currentHeight + 1UL, transactions.Length)
                    Some block
                else
                    None
            with
            | ex -> 
                logger.LogError(ex, "Failed to mine block")
                None

        let syncWithPeer(peerId: string, peerBlocks: Block list) : unit =
            try
                // Implement realistic blockchain synchronization
                logger.LogInformation("Node {NodeId} syncing with peer {PeerId}, received {BlockCount} blocks", 
                    nodeId, peerId, peerBlocks.Length)
                
                // Validate and process received blocks
                let validBlocks = peerBlocks |> List.filter (fun block -> validateBlock block)
                
                if validBlocks.Length > 0 then
                    // Update local blockchain state
                    let newBlocks = validBlocks |> List.filter (fun block -> 
                        not (state.Blockchain |> List.exists (fun localBlock -> 
                            match localBlock.Header.Hash, block.Header.Hash with
                            | Hash localHash, Hash blockHash -> localHash = blockHash
                        ))
                    )
                    
                    if newBlocks.Length > 0 then
                        // Add new blocks to blockchain
                        state <- { state with Blockchain = state.Blockchain @ newBlocks }
                        
                        // Update wallet balances from new transactions
                        for block in newBlocks do
                            for tx in block.Transactions do
                                updateWalletFromTransaction tx
                        
                        logger.LogInformation("Node {NodeId} added {NewBlockCount} new blocks from peer {PeerId}", 
                            nodeId, newBlocks.Length, peerId)
                    else
                        logger.LogInformation("Node {NodeId} already has all blocks from peer {PeerId}", nodeId, peerId)
                else
                    logger.LogWarning("Node {NodeId} received no valid blocks from peer {PeerId}", nodeId, peerId)
                    
            with
            | ex -> 
                logger.LogError(ex, "Failed to sync with peer {PeerId}", peerId)

        /// Validate block structure and content
        let validateBlock(block: Block) : bool =
            try
                // Check block header validity
                let header = block.Header
                if header.Height < 0UL then false
                elif header.Timestamp > DateTime.UtcNow then false
                else
                    // Validate transactions in block
                    let validTransactions = block.Transactions |> List.forall validateTransaction
                    validTransactions
            with
            | _ -> false

        /// Validate transaction structure
        let validateTransaction(tx: Transaction) : bool =
            try
                // Basic transaction validation
                match tx.Amount with | Balance amount -> amount > 0UL
            with
            | _ -> false

        /// Update wallet from transaction
        let updateWalletFromTransaction(tx: Transaction) : unit =
            try
                match tx.From, tx.To, tx.Amount with
                | Address fromAddr, Address toAddr, Balance amount ->
                    // Update sender balance (subtract amount + fee)
                    let currentFromBalance = Map.tryFind (Address fromAddr) state.Wallet 
                                            |> Option.defaultValue (Balance(0UL))
                    let newFromBalance = match currentFromBalance with | Balance balance -> 
                        Balance(max 0UL (balance - amount - 1UL)) // Subtract 1UL fee
                    
                    // Update receiver balance (add amount)
                    let currentToBalance = Map.tryFind (Address toAddr) state.Wallet 
                                          |> Option.defaultValue (Balance(0UL))
                    let newToBalance = match currentToBalance with | Balance balance -> 
                        Balance(balance + amount)
                    
                    // Update wallet state
                    state <- { state with 
                        Wallet = state.Wallet 
                                |> Map.add (Address fromAddr) newFromBalance
                                |> Map.add (Address toAddr) newToBalance
                    }
            with
            | ex -> logger.LogError(ex, "Failed to update wallet from transaction")

        let getBalance(address: Address) : Balance =
            Map.tryFind address state.Wallet 
            |> Option.defaultValue (Balance(0UL))

        let getState() : TestnetNodeState = state

        let getBlockchain() : Block list = state.Blockchain

        let getPendingTransactions() : Transaction list = 
            Map.values state.PendingTransactions |> List.ofSeq

        member this.Start() : unit =
            state <- { state with IsRunning = true }
            let _ = createGenesisBlock() // Initialize with genesis block
            logger.LogInformation("Testnet node {NodeId} started", nodeId)

        member this.Stop() : unit =
            state <- { state with IsRunning = false }
            logger.LogInformation("Testnet node {NodeId} stopped", nodeId)

        member this.ProcessTransaction(tx: Transaction) : bool =
            processTransaction(tx)

        member this.CreateTransaction(toAddress: Address, amount: Balance) : Transaction option =
            createTransaction(toAddress, amount)

        member this.StartMining() : unit =
            state <- { state with IsMining = true; MiningTarget = calculateMiningTarget() }
            logger.LogInformation("Node {NodeId} started mining", nodeId)

        member this.StopMining() : unit =
            state <- { state with IsMining = false }
            logger.LogInformation("Node {NodeId} stopped mining", nodeId)

        member this.MineBlock() : Block option =
            mineBlock()

        member this.SyncWithPeer(peerId: string, peerBlocks: Block list) : unit =
            syncWithPeer(peerId, peerBlocks)

        member this.GetBalance(address: Address) : Balance =
            getBalance(address)

        member this.GetState() : TestnetNodeState =
            getState()

        member this.GetBlockchain() : Block list =
            getBlockchain()

        member this.GetPendingTransactions() : Transaction list =
            getPendingTransactions()

        member this.GetNodeId() : string = nodeId

        member this.IsNodeRunning() : bool = state.IsRunning

        member this.GetCurrentHeight() : BlockHeight = state.CurrentHeight

        member this.GetBlockCount() : int = state.Blockchain.Length

        member this.GetTransactionCount() : int = state.PendingTransactions.Count

        member this.GetPeerCount() : int = state.Peers.Count

        member this.IsMining() : bool = state.IsMining

    /// Testnet node agent using MailboxProcessor
    type TestnetNodeAgent(node: TestnetNode, logger: ILogger<TestnetNodeAgent>) =
        let agent = MailboxProcessor<TestnetNodeMessage>.Start(fun inbox ->
            let rec loop() = async {
                let! message = inbox.Receive()
                
                match message with
                | Start ->
                    node.Start()
                    logger.LogInformation("Testnet node agent started")
                
                | Stop ->
                    node.Stop()
                    logger.LogInformation("Testnet node agent stopped")
                
                | ProcessTransaction tx ->
                    let success = node.ProcessTransaction(tx)
                    if success then
                        logger.LogInformation("Transaction processed successfully")
                    else
                        logger.LogWarning("Failed to process transaction")
                
                | CreateTransaction (toAddress, amount) ->
                    match node.CreateTransaction(toAddress, amount) with
                    | Some tx -> 
                        logger.LogInformation("Transaction created: {Amount}", match tx.Amount with | Balance b -> b)
                    | None -> 
                        logger.LogWarning("Failed to create transaction")
                
                | StartMining ->
                    node.StartMining()
                    logger.LogInformation("Mining started")
                
                | StopMining ->
                    node.StopMining()
                    logger.LogInformation("Mining stopped")
                
                | MineBlock ->
                    match node.MineBlock() with
                    | Some block -> 
                        logger.LogInformation("Block mined at height {Height}", 
                            match block.Header.Height with | BlockHeight h -> h)
                    | None -> 
                        logger.LogInformation("No transactions to mine")
                
                | SyncWithPeer (peerId, peerBlocks) ->
                    node.SyncWithPeer(peerId, peerBlocks)
                    logger.LogInformation("Synced with peer {PeerId}", peerId)
                
                | GetState ->
                    let state = node.GetState()
                    let height = match state.CurrentHeight with | BlockHeight h -> h
                    logger.LogInformation("Node state: Height={Height}, Blocks={Blocks}, Transactions={Transactions}, Mining={Mining}", 
                        height, state.Blockchain.Length, state.PendingTransactions.Count, state.IsMining)
                
                | GetBalance address ->
                    let balance = node.GetBalance(address)
                    logger.LogInformation("Balance: {Balance}", match balance with | Balance b -> b)
                
                | GetBlockchain ->
                    let blockchain = node.GetBlockchain()
                    logger.LogInformation("Blockchain length: {Length}", blockchain.Length)
                
                | GetPendingTransactions ->
                    let pendingTxs = node.GetPendingTransactions()
                    logger.LogInformation("Pending transactions: {Count}", pendingTxs.Length)
                
                do! loop()
            }
            loop()
        )

        member this.Start() : unit =
            agent.Post(Start)

        member this.Stop() : unit =
            agent.Post(Stop)

        member this.ProcessTransaction(tx: Transaction) : unit =
            agent.Post(ProcessTransaction tx)

        member this.CreateTransaction(toAddress: Address, amount: Balance) : unit =
            agent.Post(CreateTransaction(toAddress, amount))

        member this.StartMining() : unit =
            agent.Post(StartMining)

        member this.StopMining() : unit =
            agent.Post(StopMining)

        member this.MineBlock() : unit =
            agent.Post(MineBlock)

        member this.SyncWithPeer(peerId: string, peerBlocks: Block list) : unit =
            agent.Post(SyncWithPeer(peerId, peerBlocks))

        member this.GetState() : unit =
            agent.Post(GetState)

        member this.GetBalance(address: Address) : unit =
            agent.Post(GetBalance address)

        member this.GetBlockchain() : unit =
            agent.Post(GetBlockchain)

        member this.GetPendingTransactions() : unit =
            agent.Post(GetPendingTransactions)

        member this.GetNode() : TestnetNode = node
