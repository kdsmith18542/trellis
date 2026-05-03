namespace Trellis.Testnet

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Testnet miner for block creation and validation
module TestnetMiner =

    /// Mining configuration
    type MiningConfig = {
        Difficulty: int
        BlockTime: TimeSpan
        MaxTransactionsPerBlock: int
        MiningReward: Balance
    }

    /// Mining result
    type MiningResult = {
        Success: bool
        Block: Block option
        Hash: Hash option
        Nonce: uint64
        TimeElapsed: TimeSpan
    }

    /// Testnet miner implementation
    type TestnetMiner(config: MiningConfig, logger: ILogger<TestnetMiner>) =
        let mutable isMining = false
        let mutable currentNonce = 0UL

        let calculateTarget(difficulty: int) : Hash =
            let target = Array.create 32 0xFFuy // Start with all 1s
            let difficultyBytes = min difficulty 32
            for i in 0..difficultyBytes-1 do
                target.[i] <- 0uy // Set leading bytes to 0 for difficulty
            Hash(target)

        let validateBlock(block: Block) : bool =
            try
                // Validate block structure
                let (BlockHeight height) = block.Header.Height
                if height = 0UL then
                    true // Genesis block
                else
                    // Check if block has transactions
                    block.Transactions.Length > 0
            with
            | _ -> false

        let createMiningRewardTransaction(minerAddress: Address, height: BlockHeight) : Transaction =
            {
                From = Address(Array.create 20 0uy) // Coinbase transaction (zero address)
                To = minerAddress
                Amount = config.MiningReward
                Nonce = Nonce(0UL)
                Timestamp = Timestamp(DateTime.UtcNow)
                Signature = Signature(Array.create 64 0uy) // Coinbase signature (zero signature)
            }

        let mineBlock(transactions: Transaction list, previousHash: Hash, height: BlockHeight, minerAddress: Address) : MiningResult =
            let startTime = DateTime.UtcNow
            let target = calculateTarget(config.Difficulty)
            
            try
                // Create mining reward transaction
                let rewardTx = createMiningRewardTransaction(minerAddress, height)
                let allTransactions = rewardTx :: transactions
                
                // Create block header
                let blockHeader = {
                    Height = height
                    PreviousHash = previousHash
                    PoWHash = Hash(Array.create 32 0uy) // Will be calculated during mining
                    Timestamp = Timestamp(DateTime.UtcNow)
                    Finalized = false
                }
                
                // Create block
                let block = {
                    Header = blockHeader
                    Transactions = allTransactions
                }
                
                // Simplified mining - just find a hash that starts with zeros
                let mutable found = false
                let mutable nonce = 0UL
                let maxAttempts = 1000UL // Limit attempts for testing
                
                while not found && nonce < maxAttempts do
                    let testData = System.Text.Encoding.UTF8.GetBytes(sprintf "%A%u" block nonce)
                    let hash = sha256 testData
                    
                    // Check if hash meets difficulty target
                    let hashBytes = match hash with | Hash h -> h
                    let targetBytes = match target with | Hash t -> t
                    
                    let mutable meetsTarget = true
                    for i in 0..config.Difficulty-1 do
                        if i < hashBytes.Length && i < targetBytes.Length then
                            if hashBytes.[i] > targetBytes.[i] then
                                meetsTarget <- false
                    
                    if meetsTarget then
                        found <- true
                        currentNonce <- nonce
                    else
                        nonce <- nonce + 1UL
                
                let endTime = DateTime.UtcNow
                let timeElapsed = endTime - startTime
                
                if found then
                    let finalHash = sha256 (System.Text.Encoding.UTF8.GetBytes(sprintf "%A%u" block currentNonce))
                    logger.LogInformation("Block mined successfully! Nonce: {Nonce}, Time: {Time}ms", 
                        currentNonce, timeElapsed.TotalMilliseconds)
                    
                    {
                        Success = true
                        Block = Some block
                        Hash = Some finalHash
                        Nonce = currentNonce
                        TimeElapsed = timeElapsed
                    }
                else
                    logger.LogWarning("Mining failed - no valid nonce found in {Attempts} attempts", maxAttempts)
                    {
                        Success = false
                        Block = None
                        Hash = None
                        Nonce = currentNonce
                        TimeElapsed = timeElapsed
                    }
            with
            | ex -> 
                logger.LogError(ex, "Mining error occurred")
                {
                    Success = false
                    Block = None
                    Hash = None
                    Nonce = currentNonce
                    TimeElapsed = DateTime.UtcNow - startTime
                }

        let validateMinedBlock(block: Block, target: Hash) : bool =
            try
                // Validate the mined block meets the difficulty target
                let blockHash = blockHash block.Header
                let hashBytes = match blockHash with | Hash h -> h
                let targetBytes = match target with | Hash t -> t
                
                let mutable meetsTarget = true
                for i in 0..config.Difficulty-1 do
                    if i < hashBytes.Length && i < targetBytes.Length then
                        if hashBytes.[i] > targetBytes.[i] then
                            meetsTarget <- false
                
                meetsTarget
            with
            | _ -> false

        member this.StartMining() : unit =
            isMining <- true
            logger.LogInformation("Mining started with difficulty {Difficulty}", config.Difficulty)

        member this.StopMining() : unit =
            isMining <- false
            logger.LogInformation("Mining stopped")

        member this.IsMining() : bool = isMining

        member this.MineBlock(transactions: Transaction list, previousHash: Hash, height: BlockHeight, minerAddress: Address) : MiningResult =
            if not isMining then
                logger.LogWarning("Mining not started - cannot mine block")
                {
                    Success = false
                    Block = None
                    Hash = None
                    Nonce = 0UL
                    TimeElapsed = TimeSpan.Zero
                }
            else
                mineBlock(transactions, previousHash, height, minerAddress)

        member this.ValidateBlock(block: Block) : bool =
            validateBlock(block)

        member this.ValidateMinedBlock(block: Block) : bool =
            let target = calculateTarget(config.Difficulty)
            validateMinedBlock(block, target)

        member this.GetMiningConfig() : MiningConfig = config

        member this.GetCurrentNonce() : uint64 = currentNonce

        member this.ResetNonce() : unit =
            currentNonce <- 0UL
            logger.LogInformation("Mining nonce reset")

    /// Mining coordinator for managing multiple miners
    type MiningCoordinator(miners: TestnetMiner list, logger: ILogger<MiningCoordinator>) =
        let mutable activeMiners = miners

        member this.StartAllMining() : unit =
            for miner in activeMiners do
                miner.StartMining()
            logger.LogInformation("Started {Count} miners", activeMiners.Length)

        member this.StopAllMining() : unit =
            for miner in activeMiners do
                miner.StopMining()
            logger.LogInformation("Stopped all miners")

        member this.MineBlock(transactions: Transaction list, previousHash: Hash, height: BlockHeight, minerAddress: Address) : MiningResult option =
            try
                // Use the first available miner
                if activeMiners.Length > 0 then
                    let miner = activeMiners.[0]
                    let result = miner.MineBlock(transactions, previousHash, height, minerAddress)
                    
                    if result.Success then
                        logger.LogInformation("Block mined by coordinator: Height {Height}, Nonce {Nonce}", 
                            match height with | BlockHeight h -> h, result.Nonce)
                        Some result
                    else
                        logger.LogWarning("Mining failed for block at height {Height}", match height with | BlockHeight h -> h)
                        None
                else
                    logger.LogError("No active miners available")
                    None
            with
            | ex -> 
                logger.LogError(ex, "Mining coordinator error")
                None

        member this.ValidateBlock(block: Block) : bool =
            try
                if activeMiners.Length > 0 then
                    let miner = activeMiners.[0]
                    miner.ValidateBlock(block)
                else
                    false
            with
            | ex -> 
                logger.LogError(ex, "Block validation error")
                false

        member this.GetActiveMinerCount() : int = activeMiners.Length

        member this.GetMiningStats() : (string * bool * uint64) list =
            activeMiners
            |> List.mapi (fun i miner -> 
                (sprintf "miner-%d" i, miner.IsMining(), miner.GetCurrentNonce())
            )
