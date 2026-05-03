namespace Trellis.Regnet

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Regnet node for testing Trellis blockchain
module RegnetNode =

    /// Regnet node state
    type RegnetNodeState = {
        NodeId: string
        IsRunning: bool
        CurrentHeight: BlockHeight
        Peers: string list
        Transactions: Transaction list
        Blocks: Block list
        Balance: Balance
        Nonce: Nonce
    }

    /// Regnet node message
    type RegnetNodeMessage =
        | Start
        | Stop
        | ProcessTransaction of Transaction
        | GetState
        | GetBalance of Address
        | CreateTransaction of Address * Balance
        | MineBlock
        | SyncWithPeer of string

    /// Regnet node implementation
    type RegnetNode(nodeId: string, logger: ILogger<RegnetNode>) =
        let mutable state: RegnetNodeState = {
            NodeId = nodeId
            IsRunning = false
            CurrentHeight = BlockHeight(0UL)
            Peers = []
            Transactions = []
            Blocks = []
            Balance = Balance(1000000UL) // 1M tokens for testing
            Nonce = Nonce(0UL)
        }

        let (publicKey, privateKey) = generateKeyPair()

        let createGenesisBlock() : Block =
            let genesisHeader = {
                Height = BlockHeight(0UL)
                PreviousHash = Hash(Array.create 32 0uy)
                PoWHash = Hash(Array.create 32 0uy)
                Timestamp = Timestamp(DateTime.UtcNow)
                Finalized = false
            }
            
            let genesisTransaction = {
                From = Address(Array.create 20 0uy) // Genesis address
                To = Address(Array.create 20 1uy) // First regnet address
                Amount = Balance(1000000UL) // 1M regnet tokens
                Nonce = Nonce(0UL)
                Timestamp = Timestamp(DateTime.UtcNow)
                Signature = Signature(Array.create 64 0uy) // Genesis signature
            }
            
            {
                Header = genesisHeader
                Transactions = [genesisTransaction]
            }

        let processTransaction(tx: Transaction) : bool =
            try
                // Validate transaction
                let (Balance amount) = tx.Amount
                if amount > 0UL then
                    // Add to pending transactions
                    state <- { state with Transactions = tx :: state.Transactions }
                    logger.LogInformation("Node {NodeId} processed transaction for {Amount} tokens", nodeId, amount)
                    true
                else
                    false
            with
            | ex -> 
                logger.LogError(ex, "Failed to process transaction")
                false

        let createTransaction(toAddress: Address, amount: Balance) : Transaction option =
            try
                let (Balance currentBalance) = state.Balance
                let (Balance txAmount) = amount
                
                if txAmount <= currentBalance then
                    let (Nonce currentNonce) = state.Nonce
                    let newNonce = Nonce(currentNonce + 1UL)
                    
                    let transaction = {
                        From = Address(Array.create 20 1uy) // Regnet test address
                        To = toAddress
                        Amount = amount
                        Nonce = newNonce
                        Timestamp = Timestamp(DateTime.UtcNow)
                        Signature = Signature(Array.create 64 1uy) // Regnet test signature
                    }
                    
                    state <- { state with Nonce = newNonce }
                    Some transaction
                else
                    None
            with
            | ex -> 
                logger.LogError(ex, "Failed to create transaction")
                None

        let mineBlock() : Block option =
            try
                if state.Transactions.Length > 0 then
                    let (BlockHeight currentHeight) = state.CurrentHeight
                    let newHeight = BlockHeight(currentHeight + 1UL)
                    
                    let previousHash = 
                        if state.Blocks.Length > 0 then
                            blockHash state.Blocks.[0].Header
                        else
                            Hash(Array.create 32 0uy)
                    
                    let blockHeader = {
                        Height = newHeight
                        PreviousHash = previousHash
                        PoWHash = Hash(Array.create 32 0uy) // Regnet PoW hash
                        Timestamp = Timestamp(DateTime.UtcNow)
                        Finalized = false
                    }
                    
                    let block = {
                        Header = blockHeader
                        Transactions = state.Transactions
                    }
                    
                    // Add block to chain
                    state <- { 
                        state with 
                            Blocks = block :: state.Blocks
                            CurrentHeight = newHeight
                            Transactions = []
                    }
                    
                    logger.LogInformation("Node {NodeId} mined block at height {Height}", nodeId, currentHeight + 1UL)
                    Some block
                else
                    None
            with
            | ex -> 
                logger.LogError(ex, "Failed to mine block")
                None

        let getBalance(address: Address) : Balance =
            // Simplified balance lookup for testing
            Balance(1000000UL)

        let getState() : RegnetNodeState = state

        member this.Start() : unit =
            state <- { state with IsRunning = true }
            logger.LogInformation("Regnet node {NodeId} started", nodeId)

        member this.Stop() : unit =
            state <- { state with IsRunning = false }
            logger.LogInformation("Regnet node {NodeId} stopped", nodeId)

        member this.ProcessTransaction(tx: Transaction) : bool =
            processTransaction(tx)

        member this.CreateTransaction(toAddress: Address, amount: Balance) : Transaction option =
            createTransaction(toAddress, amount)

        member this.MineBlock() : Block option =
            mineBlock()

        member this.GetBalance(address: Address) : Balance =
            getBalance(address)

        member this.GetState() : RegnetNodeState =
            getState()

        member this.GetNodeId() : string = nodeId

        member this.IsNodeRunning() : bool = state.IsRunning

        member this.GetCurrentHeight() : BlockHeight = state.CurrentHeight

        member this.GetBlockCount() : int = state.Blocks.Length

        member this.GetTransactionCount() : int = state.Transactions.Length

        member this.GetPeerCount() : int = state.Peers.Length

    /// Regnet node agent using MailboxProcessor
    type RegnetNodeAgent(node: RegnetNode, logger: ILogger<RegnetNodeAgent>) =
        let agent = MailboxProcessor<RegnetNodeMessage>.Start(fun inbox ->
            let rec loop() = async {
                let! message = inbox.Receive()
                
                match message with
                | Start ->
                    node.Start()
                    logger.LogInformation("Regnet node agent started")
                
                | Stop ->
                    node.Stop()
                    logger.LogInformation("Regnet node agent stopped")
                
                | ProcessTransaction tx ->
                    let success = node.ProcessTransaction(tx)
                    if success then
                        logger.LogInformation("Transaction processed successfully")
                    else
                        logger.LogWarning("Failed to process transaction")
                
                | GetState ->
                    let state = node.GetState()
                    logger.LogInformation("Node state: Height={Height}, Blocks={Blocks}, Transactions={Transactions}", 
                        match state.CurrentHeight with | BlockHeight h -> h,
                        state.Blocks.Length,
                        state.Transactions.Length)
                
                | GetBalance address ->
                    let balance = node.GetBalance(address)
                    logger.LogInformation("Balance: {Balance}", match balance with | Balance b -> b)
                
                | CreateTransaction (toAddress, amount) ->
                    match node.CreateTransaction(toAddress, amount) with
                    | Some tx -> 
                        logger.LogInformation("Transaction created: {Amount}", match tx.Amount with | Balance b -> b)
                    | None -> 
                        logger.LogWarning("Failed to create transaction")
                
                | MineBlock ->
                    match node.MineBlock() with
                    | Some block -> 
                        logger.LogInformation("Block mined at height {Height}", 
                            match block.Header.Height with | BlockHeight h -> h)
                    | None -> 
                        logger.LogInformation("No transactions to mine")
                
                | SyncWithPeer peerId ->
                    logger.LogInformation("Syncing with peer {PeerId}", peerId)
                
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

        member this.GetState() : unit =
            agent.Post(GetState)

        member this.GetBalance(address: Address) : unit =
            agent.Post(GetBalance address)

        member this.CreateTransaction(toAddress: Address, amount: Balance) : unit =
            agent.Post(CreateTransaction(toAddress, amount))

        member this.MineBlock() : unit =
            agent.Post(MineBlock)

        member this.SyncWithPeer(peerId: string) : unit =
            agent.Post(SyncWithPeer peerId)

        member this.GetNode() : RegnetNode = node
