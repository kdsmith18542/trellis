namespace Trellis.Node

open System
open System.IO
open System.Collections.Generic
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open GrpcService
open Trellis.Core.ChainParams
open Trellis.Core.Serialization
open SimpleCrypto
open SimpleDPoS

/// Blockchain state implementation for gRPC service
module BlockchainState =

    /// In-memory blockchain state implementation
    type InMemoryBlockchainState(logger: ILogger<InMemoryBlockchainState>) =
        let mutable accounts = Map.empty<Address, Account>
        let mutable blocks = List.empty<Block>
        let mutable validators = List.empty<PublicKey * StakingInfo>
        let mutable currentHeight = BlockHeight(0UL)
        // EIP-1559 style state: track base fee per gas and total supply
        let cfg = Trellis.Core.TrellisConfig.current
        let mutable baseFeePerGas: uint64 = cfg.GenesisBaseFeePerGas
        let mutable totalSupply: Balance = Balance(cfg.InitialTotalSupply)
        let mutable lastBlockBurn: uint64 = 0UL
        let mutable lastBlockTips: uint64 = 0UL
        let lockObj = obj()
        // File-based persistence (JSON) for canonical chain state and blocks
        let dataDir = Path.Combine(AppContext.BaseDirectory, "data")
        let blocksDir = Path.Combine(dataDir, "blocks")
        do
            if not (Directory.Exists(dataDir)) then Directory.CreateDirectory(dataDir) |> ignore
            if not (Directory.Exists(blocksDir)) then Directory.CreateDirectory(blocksDir) |> ignore
        let toHex (b: byte[]) = System.Convert.ToHexString(b)

        /// Get block by height from storage or memory
        member private this.GetBlockByHeight(height: BlockHeight) : Block option =
            lock lockObj (fun () ->
                // First check in-memory blocks
                let inMemoryBlock = 
                    blocks 
                    |> List.tryFind (fun b -> b.Header.Height = height)
                
                match inMemoryBlock with
                | Some block -> Some block
                | None ->
                    // Try to load from storage
                    try
                        let (BlockHeight h) = height
                        let blockFile = Path.Combine(blocksDir, sprintf "block_%d.json" h)
                        if File.Exists(blockFile) then
                            let data = File.ReadAllBytes(blockFile)
                            Some (deserializeBlock data)
                        else
                            None
                    with
                    | _ -> None
            )

        /// Compute MedianTimePast of last N blocks (default 11) or now if no blocks
        member private this.GetMedianTimePast(?windowSize: int) : DateTime =
            let n = defaultArg windowSize 11
            lock lockObj (fun () ->
                let ts =
                    blocks
                    |> List.truncate n
                    |> List.map (fun b -> match b.Header.Timestamp with | Timestamp t -> t)
                match ts with
                | [] -> DateTime.UtcNow
                | xs ->
                    let sorted = xs |> List.sort
                    sorted.[sorted.Length/2]
            )

        /// Sum of declared difficulties (simple proxy for TD)
        member private this.GetTotalDifficulty() : uint64 =
            lock lockObj (fun () ->
                blocks |> List.sumBy (fun b -> b.Header.Difficulty)
            )

        /// DPoS consensus state
        member private this.GetDPoSConsensus() : DPoSConsensus =
            lock lockObj (fun () ->
                // Convert validators to DPoS validators
                let dposValidators = validators |> List.map (fun (publicKey, stakingInfo) ->
                    {
                        Address = stakingInfo.Address
                        PublicKey = publicKey
                        Stake = stakingInfo.Stake
                        IsActive = stakingInfo.IsActive
                        Commission = decimal stakingInfo.Commission / 100.0m // Convert to decimal
                        PerformanceScore = 100.0 // Default performance score
                    }
                )
                createDPoSConsensus dposValidators
            )

        interface IBlockchainState with
            member this.GetAccount(address: Address) : Account option =
                lock lockObj (fun () ->
                    accounts.TryFind address
                )

            member this.GetBalance(address: Address) : Balance =
                lock lockObj (fun () ->
                    match accounts.TryFind address with
                    | Some account -> account.Balance
                    | None -> Balance(0UL)
                )

            member this.GetBlock(hash: Hash) : Block option =
                lock lockObj (fun () ->
                    match blocks |> List.tryFind (fun b -> b.Header.Hash = hash) with
                    | Some b -> Some b
                    | None ->
                        // Try loading from file store
                        let (Hash hbytes) = hash
                        let path = Path.Combine(blocksDir, toHex hbytes + ".json")
                        if File.Exists(path) then
                            try
                                let bytes = File.ReadAllBytes(path)
                                Some (deserializeBlock bytes)
                            with _ -> None
                        else None
                )

            member this.GetChainState() : ChainState =
                lock lockObj (fun () ->
                    let lastHash = 
                        match blocks with
                        | [] -> Hash(Array.zeroCreate 32)
                        | b::_ -> b.Header.Hash
                    {
                        Accounts = accounts
                        Blocks = blocks
                        CurrentHeight = currentHeight
                        LastBlockHash = lastHash
                        Validators = validators |> List.map snd |> List.map (fun v -> v.Address)
                        TotalSupply = totalSupply
                        BaseFeePerGas = cfg.GenesisBaseFeePerGas
                        GasUsed = 0UL
                        GasLimit = cfg.GenesisGasLimit
                    }
                )

            member this.GetValidators() : (PublicKey * StakingInfo) list =
                lock lockObj (fun () ->
                    validators
                )

            member this.SubmitTransaction(transaction: Transaction) : bool =
                lock lockObj (fun () ->
                    try
                        logger.LogInformation("Submitting transaction: {Hash}", transaction.Hash)
                        
                        // Basic transaction validation
                        let isValid = this.ValidateTransaction(transaction)
                        if isValid then
                            // Update account balances
                            this.ProcessTransaction(transaction)
                            true
                        else
                            logger.LogWarning("Transaction validation failed: {Hash}", transaction.Hash)
                            false
                    with
                    | ex ->
                        logger.LogError(ex, "Error processing transaction: {Hash}", transaction.Hash)
                        false
                )

            member this.SubmitBlock(block: Block) : bool =
                lock lockObj (fun () ->
                    try
                        logger.LogInformation("Submitting block: {Hash} at height {Height}", 
                            block.Header.Hash, block.Header.Height)
                        
                        // Basic block validation
                        let isValid = this.ValidateBlock(block)
                        if isValid then
                            // Compute EIP-1559 style burn and proposer tips
                            let miner = block.Header.Validator
                            
                            // Calculate block reward with halving (Bitcoin-style)
                            let calculateBlockReward (blockHeight: BlockHeight) =
                                let (BlockHeight height) = blockHeight
                                let halvingInterval = cfg.HalvingInterval
                                let halvings = height / halvingInterval
                                let baseReward = cfg.BlockReward
                                let halvedReward = baseReward >>> int halvings
                                Balance(halvedReward)
                            
                            let baseReward = calculateBlockReward(block.Header.Height)
                            
                            // Check max supply constraint
                            let maxSupply = Balance(cfg.MaxSupply)
                            let (Balance currentSupply) = totalSupply
                            let (Balance rewardAmount) = baseReward
                            
                            // Only mint if we haven't reached max supply
                            let actualReward = 
                                if currentSupply + rewardAmount > (match maxSupply with | Balance ms -> ms) then
                                    Balance(0UL) // No more rewards if max supply reached
                                else
                                    baseReward

                            // Helper to compute actual fee, burn, and tip per transaction
                            let computeFees (tx: Transaction) =
                                let gasUsed = tx.GasLimit // simplistic assumption
                                let maxFeeFromTx =
                                    match tx.Fee with | Balance f -> f
                                let maxFeeByGas = tx.GasLimit * tx.GasPrice
                                let actualFee = min maxFeeFromTx maxFeeByGas
                                let baseBurnPerGas = baseFeePerGas
                                let baseBurn = Math.Min(actualFee, baseBurnPerGas * gasUsed)
                                let tip = actualFee - baseBurn
                                struct(actualFee, baseBurn, tip, gasUsed)

                            let feeComputations = block.Transactions |> List.map computeFees
                            let totalTip = feeComputations |> List.sumBy (fun (struct(_,_,t,_)) -> t)
                            let totalBurn = feeComputations |> List.sumBy (fun (struct(_,b,_,_)) -> b)
                            let gasUsedSum = feeComputations |> List.sumBy (fun (struct(_,_,_,g)) -> g)

                            // Update base fee based on block gas usage (very simplified EIP-1559 rule)
                            let targetGas = max 1UL (gasUsedSum / 2UL)
                            let newBaseFee =
                                if gasUsedSum > targetGas then
                                    // increase
                                    max 1UL (uint64 (float baseFeePerGas * cfg.BaseFeeIncreaseMultiplier))
                                else
                                    // decrease
                                    max 1UL (uint64 (float baseFeePerGas * cfg.BaseFeeDecreaseMultiplier))

                            baseFeePerGas <- newBaseFee
                            lastBlockBurn <- totalBurn
                            lastBlockTips <- totalTip

                            // Credit miner: actual reward + tips
                            let minerFees = Balance(totalTip)
                            // Update miner balance
                            let minerAcc = (this :> IBlockchainState).GetAccount(miner)
                            let currentBal = match minerAcc with | Some a -> a.Balance | None -> Balance 0UL
                            let (Balance cb) = currentBal
                            let (Balance ar) = actualReward
                            let (Balance mf) = minerFees
                            let newBal = Balance (cb + ar + mf)
                            let newMinerAcc = { Address = miner; Balance = newBal; Nonce = match minerAcc with | Some a -> a.Nonce | None -> Nonce 0UL }
                            accounts <- accounts.Add(miner, newMinerAcc)

                            // Update total supply: minted actual reward minus burned base fees
                            let (Balance ts) = totalSupply
                            totalSupply <- Balance (ts + ar - totalBurn)

                            // Add block to chain
                            blocks <- block :: blocks
                            currentHeight <- block.Header.Height
                            // Persist block and chain state
                            try
                                // Persist block and chain state
                                let (Hash hbytes) = block.Header.Hash
                                let bpath = Path.Combine(blocksDir, toHex hbytes + ".json")
                                File.WriteAllBytes(bpath, serializeBlock block)
                                let persistedState : ChainState =
                                    {
                                        Accounts = accounts
                                        Blocks = []
                                        CurrentHeight = currentHeight
                                        LastBlockHash = block.Header.Hash
                                        Validators = validators |> List.map snd |> List.map (fun v -> v.Address)
                                        TotalSupply = totalSupply
                                        BaseFeePerGas = cfg.GenesisBaseFeePerGas
                                        GasUsed = 0UL
                                        GasLimit = cfg.GenesisGasLimit
                                    }
                                File.WriteAllBytes(Path.Combine(dataDir, "chainstate.json"), serializeChainState persistedState)
                            with _ -> ()
                            
                            // Process transactions in block
                            for tx in block.Transactions do
                                this.ProcessTransaction(tx) |> ignore
                                // Remove from mempool if present
                                Trellis.Node.Mempool.remove tx.Hash
                            
                            logger.LogInformation("Block added successfully: {Hash}", block.Header.Hash)
                            true
                        else
                            logger.LogWarning("Block validation failed: {Hash}", block.Header.Hash)
                            false
                    with
                    | ex ->
                        logger.LogError(ex, "Error processing block: {Hash}", block.Header.Hash)
                        false
                )
            member this.GetDPoSConsensus() = this.GetDPoSConsensus()
            member this.GetBaseFeePerGas() = baseFeePerGas
            member this.GetLastBlockBurn() = lastBlockBurn
            member this.GetLastBlockTips() = lastBlockTips

        /// Validate transaction (comprehensive validation)
        member private this.ValidateTransaction(transaction: Transaction) : bool =
            try
                // 1. Basic structure validation
                if transaction.From = transaction.To then
                    logger.LogWarning("Transaction from and to addresses are the same")
                    false
                else
                    // 2. Amount validation
                    let (Balance amount) = transaction.Amount
                    if amount = 0UL then
                        logger.LogWarning("Transaction amount is zero")
                        false
                    else
                        // 3. Fee validation
                        let (Balance fee) = transaction.Fee
                        if fee = 0UL then
                            logger.LogWarning("Transaction fee is zero")
                            false
                        else
                            // 4. Gas validation
                            if transaction.GasLimit = 0UL then
                                logger.LogWarning("Transaction gas limit is zero")
                                false
                            else
                                if transaction.GasPrice = 0UL then
                                    logger.LogWarning("Transaction gas price is zero")
                                    false
                                else
                                    // 5. Signature validation
                                    if not (SimpleCrypto.verifyTransactionSimple transaction) then
                                        logger.LogWarning("Transaction signature validation failed")
                                        false
                                    else
                                        // 6. Nonce validation
                                        let senderAccount = (this :> IBlockchainState).GetAccount(transaction.From)
                                        let (Nonce txNonce) = transaction.Nonce
                                        let validNonce =
                                            match senderAccount with
                                            | Some acc ->
                                                let (Nonce accNonce) = acc.Nonce
                                                txNonce = accNonce + 1UL
                                            | None ->
                                                txNonce = 1UL
                                        
                                        if not validNonce then
                                            let expectedNonce = 
                                                match senderAccount with 
                                                | Some acc -> 
                                                    let (Nonce n) = acc.Nonce 
                                                    n + 1UL 
                                                | None -> 1UL
                                            logger.LogWarning("Transaction nonce is invalid: expected {Expected}, got {Got}",
                                                expectedNonce, txNonce)
                                            false
                                        else
                                            // 7. Balance validation
                                            let senderBalance = (this :> IBlockchainState).GetBalance(transaction.From)
                                            let totalRequired = amount + fee
                                            
                                            match senderBalance with
                                            | Balance balance ->
                                                if balance < totalRequired then
                                                    logger.LogWarning("Insufficient balance: required {Required}, available {Available}",
                                                        totalRequired, balance)
                                                    false
                                                else
                                                    // 8. Gas fee calculation validation
                                                    let maxFeeByGas = transaction.GasLimit * transaction.GasPrice
                                                    if fee > maxFeeByGas then
                                                        logger.LogWarning("Transaction fee exceeds gas limit * gas price")
                                                        false
                                                    else
                                                        // 9. EIP-1559 validation (if applicable)
                                                        match transaction.MaxFeePerGas, transaction.MaxPriorityFeePerGas with
                                                        | Some maxFee, Some maxPriority ->
                                                            if maxFee < maxPriority then
                                                                logger.LogWarning("MaxFeePerGas is less than MaxPriorityFeePerGas")
                                                                false
                                                            else
                                                                if maxFee < transaction.GasPrice then
                                                                    logger.LogWarning("MaxFeePerGas is less than GasPrice")
                                                                    false
                                                                else
                                                                    true
                                                        | None, None -> true // Legacy transaction
                                                        | _ ->
                                                            logger.LogWarning("Incomplete EIP-1559 fee fields")
                                                            false
            with
            | ex ->
                logger.LogError(ex, "Exception during transaction validation: {Error}", ex.Message)
                false

        /// Process transaction (update balances)
        member private this.ProcessTransaction(transaction: Transaction) : unit =
            // Update sender balance
            let senderAccount = (this :> IBlockchainState).GetAccount(transaction.From)
            let senderBalance = 
                match senderAccount with
                | Some acc -> acc.Balance
                | None -> Balance(0UL)
            
            // Compute actual fee charged to sender (min(maxFee, gasLimit*gasPrice))
            let actualFee =
                let maxFeeFromTx = match transaction.Fee with | Balance f -> f
                let maxFeeByGas = transaction.GasLimit * transaction.GasPrice
                min maxFeeFromTx maxFeeByGas

            let newSenderBalance =
                match senderBalance with
                | Balance b ->
                    match transaction.Amount with
                    | Balance a -> Balance(b - a - actualFee)
            
            let newSenderAccount = {
                Address = transaction.From
                Balance = newSenderBalance
                Nonce = match transaction.Nonce with | Nonce n -> Nonce(n + 1UL)
            }
            
            accounts <- accounts.Add(transaction.From, newSenderAccount)
            
            // Update receiver balance
            let receiverAccount = (this :> IBlockchainState).GetAccount(transaction.To)
            let receiverBalance = 
                match receiverAccount with
                | Some acc -> acc.Balance
                | None -> Balance(0UL)
            
            let newReceiverBalance = 
                match receiverBalance with
                | Balance b -> 
                    match transaction.Amount with
                    | Balance a -> Balance(b + a)
            
            let newReceiverAccount = {
                Address = transaction.To
                Balance = newReceiverBalance
                Nonce = match receiverAccount with | Some acc -> acc.Nonce | None -> Nonce(0UL)
            }
            
            accounts <- accounts.Add(transaction.To, newReceiverAccount)

        /// Validate block with enhanced PoW/PoS gating and height sequencing
        member private this.ValidateBlock(block: Block) : bool =
            try
                // Compute Merkle root from transactions (pairwise hash reduction)
                let computeMerkleRoot (txs: Transaction list) : Hash =
                    let txHashes =
                        txs
                        |> List.map (fun tx ->
                            match tx.Hash with
                            | Hash b when b.Length > 0 -> Hash b
                            | _ -> SimpleCrypto.transactionHashSimple tx)
                    let rec reduce (hs: Hash list) =
                        match hs with
                        | [] -> SimpleCrypto.sha256 Array.empty
                        | [h] -> h
                        | _ ->
                            let pairs =
                                hs
                                |> List.chunkBySize 2
                                |> List.map (function
                                    | [Hash a; Hash b] -> SimpleCrypto.sha256 (Array.append a b)
                                    | [h] -> h
                                    | _ -> SimpleCrypto.sha256 Array.empty)
                            reduce pairs
                    reduce txHashes

                // Enhanced height sequencing validation
                let (BlockHeight blockHeight) = block.Header.Height
                let (BlockHeight currentHeight) = currentHeight
                
                let isValidHeight = 
                    blockHeight >= 0UL && 
                    blockHeight <= currentHeight + 1UL // Allow next block or reorg
                
                // Height sequencing: block height must be exactly currentHeight + 1
                let heightSequencingOk = 
                    match blocks with
                    | [] -> blockHeight = 0UL // Genesis block
                    | last::_ -> 
                        let (BlockHeight lastHeight) = last.Header.Height
                        blockHeight = lastHeight + 1UL

                // Enhanced previous-hash continuity validation
                let prevHashOk =
                    match blocks with
                    | [] -> 
                        // Genesis block: previous hash should be all zeros
                        match block.Header.PreviousHash with
                        | Hash h -> h |> Array.forall (fun b -> b = 0uy)
                    | last::_ -> 
                        // Non-genesis: must match last block hash exactly
                        block.Header.PreviousHash = last.Header.Hash

                // Timestamp validation with reasonable bounds
                let isValidTimestamp = 
                    let cfg = Trellis.Core.TrellisConfig.current
                    match block.Header.Timestamp with 
                    | Timestamp t -> 
                        let now = DateTime.UtcNow
                        let timeDiff = (now - t).TotalSeconds
                        timeDiff >= -cfg.P2PBlockTimeTolerancePastSeconds && timeDiff <= cfg.P2PBlockTimeToleranceFutureSeconds

                // Enhanced Merkle root validation using SimpleCrypto
                let merkleOk =
                    let expected = SimpleCrypto.calculateMerkleRoot block.Transactions
                    expected = block.Header.MerkleRoot

                // Transaction validation
                let hasValidTransactions = 
                    block.Transactions.Length >= 0 &&
                    block.Transactions |> List.forall (fun tx -> this.ValidateTransaction(tx))

                // Difficulty validation (ensure difficulty matches retargeting rules)
                let expectedDifficulty = this.CalculateDifficultyRetarget(block)
                let difficultyValid = block.Header.Difficulty = expectedDifficulty

                // DPoS validation
                let dposConsensus = this.GetDPoSConsensus()
                let dposValid = validateBlockWithDPoS block dposConsensus

                // Block hash validation (ensure block hash matches header)
                let blockHashValid =
                    let computedHash = SimpleCrypto.blockHash block.Header
                    computedHash = block.Header.Hash

                // All validations must pass
                isValidHeight && 
                heightSequencingOk && 
                prevHashOk && 
                isValidTimestamp && 
                merkleOk && 
                hasValidTransactions && 
                difficultyValid &&
                dposValid && 
                blockHashValid
            with
            | ex ->
                logger.LogError(ex, "Block validation error: {Error}", ex.Message)
                false

        /// Initialize with genesis block
        member this.InitializeGenesis() : unit =
            lock lockObj (fun () ->
                // Create genesis block with proper initialization
                let genesisData = System.Text.Encoding.UTF8.GetBytes(cfg.GenesisBlockData)
                let genesisHash = SimpleCrypto.sha256 genesisData
                let genesisValidator = this.CreateGenesisValidator()
                
                let genesisBlock = {
                    Header = {
                        Hash = genesisHash
                        Height = BlockHeight(0UL)
                        Timestamp = Timestamp(DateTime.UtcNow)
                        PreviousHash = this.CreateGenesisPreviousHash() // Proper genesis previous hash
                        MerkleRoot = this.CreateGenesisMerkleRoot() // Proper genesis merkle root
                        Validator = genesisValidator
                        Nonce = 0UL
                        Difficulty = this.CalculateGenesisDifficulty() // Proper genesis difficulty
                        PoWHash = genesisHash
                        Finalized = true
                        BaseFeePerGas = cfg.GenesisBaseFeePerGas
                        GasUsed = 0UL
                        GasLimit = cfg.GenesisGasLimit
                    }
                    Transactions = []
                }
                
                // Attempt to restore from storage; otherwise initialize with genesis
                let csPath = Path.Combine(dataDir, "chainstate.json")
                if File.Exists(csPath) then
                    let data = File.ReadAllBytes(csPath)
                    let st = deserializeChainState data
                    accounts <- st.Accounts
                    currentHeight <- st.CurrentHeight
                    // keep blocks empty; load on demand from storage
                else
                    blocks <- [genesisBlock]
                    currentHeight <- BlockHeight(0UL)
                    try
                        let (Hash gh) = genesisHash
                        File.WriteAllBytes(Path.Combine(blocksDir, toHex gh + ".json"), serializeBlock genesisBlock)
                        let persistedState : ChainState =
                            {
                                Accounts = accounts
                                Blocks = []
                                CurrentHeight = currentHeight
                                LastBlockHash = genesisHash
                                Validators = []
                                TotalSupply = totalSupply
                                BaseFeePerGas = cfg.GenesisBaseFeePerGas
                                GasUsed = 0UL
                                GasLimit = cfg.GenesisGasLimit
                            }
                        File.WriteAllBytes(csPath, serializeChainState persistedState)
                    with _ -> ()
                // Initialize total supply and base fee
                baseFeePerGas <- cfg.GenesisBaseFeePerGas
                totalSupply <- Balance(cfg.InitialTotalSupply)
                lastBlockBurn <- 0UL
                lastBlockTips <- 0UL
                
                // Create initial accounts
                let initialAccounts = [
                    (Address([|1uy; 2uy; 3uy; 4uy; 5uy|]), {
                        Address = Address([|1uy; 2uy; 3uy; 4uy; 5uy|])
                        Balance = Balance(1000000UL * 1_000_000_000_000_000_000UL) // 1M TRL
                        Nonce = Nonce(0UL)
                    })
                    (Address([|6uy; 7uy; 8uy; 9uy; 10uy|]), {
                        Address = Address([|6uy; 7uy; 8uy; 9uy; 10uy|])
                        Balance = Balance(500000UL * 1_000_000_000_000_000_000UL) // 500K TRL
                        Nonce = Nonce(0UL)
                    })
                ]
                
                for (addr, account) in initialAccounts do
                    accounts <- accounts.Add(addr, account)
                
                logger.LogInformation("Genesis block initialized with {AccountCount} accounts", accounts.Count)
            )


        

        /// Create genesis validator with proper initialization
        member private this.CreateGenesisValidator() : Address =
            // Create a deterministic genesis validator address
            let genesisData = System.Text.Encoding.UTF8.GetBytes(cfg.GenesisValidatorSeed)
            let validatorHash = SimpleCrypto.sha256 genesisData
            let validatorBytes = match validatorHash with | Hash bytes -> Array.take 20 bytes
            Address validatorBytes

        /// Create proper genesis previous hash
        member private this.CreateGenesisPreviousHash() : Hash =
            // Genesis block has a special "null" previous hash
            let nullHashData = System.Text.Encoding.UTF8.GetBytes(cfg.GenesisPreviousHashSeed)
            SimpleCrypto.sha256 nullHashData

        /// Create proper genesis merkle root
        member private this.CreateGenesisMerkleRoot() : Hash =
            // Genesis merkle root is calculated from genesis transactions
            let genesisTxData = System.Text.Encoding.UTF8.GetBytes(cfg.GenesisMerkleRootSeed)
            SimpleCrypto.sha256 genesisTxData

        /// Calculate proper genesis difficulty
        member private this.CalculateGenesisDifficulty() : uint64 =
            // Genesis difficulty should be set to a reasonable starting value
            cfg.GenesisDifficultyBase * cfg.GenesisDifficultyNetworkFactor

        /// Calculate difficulty retargeting (Bitcoin-style algorithm)
        member private this.CalculateDifficultyRetarget(block: Block) : uint64 =
            let retargetInterval = cfg.RetargetInterval
            let targetBlockTime = cfg.TargetBlockTime
            let minDifficulty = cfg.MinDifficulty
            let maxDifficulty = cfg.MaxDifficulty
            
            let (BlockHeight blockHeight) = block.Header.Height
            
            // Don't retarget for genesis block or blocks before retarget interval
            if blockHeight = 0UL || blockHeight < retargetInterval then
                match blocks with
                | [] -> this.CalculateGenesisDifficulty()
                | last::_ -> last.Header.Difficulty
            else
                // Find the block at the retarget point using GetBlockByHeight
                let retargetHeight = BlockHeight (blockHeight - retargetInterval)
                let retargetBlock = this.GetBlockByHeight(retargetHeight)
                
                match retargetBlock with
                | Some retarget ->
                    // Calculate time difference between current and retarget blocks
                    let timeDiff = 
                        match (block.Header.Timestamp, retarget.Header.Timestamp) with
                        | (Timestamp current, Timestamp retargetTime) ->
                            (current - retargetTime).TotalSeconds |> uint64
                    
                    // Calculate new difficulty
                    let oldDifficulty = retarget.Header.Difficulty
                    let timeRatio = max 1UL (timeDiff / (retargetInterval * targetBlockTime))
                    
                    // Apply difficulty adjustment with bounds
                    let newDifficulty = 
                        if timeRatio > 4UL then oldDifficulty * 4UL // Max 4x increase
                        elif timeRatio < 1UL / 4UL then oldDifficulty / 4UL // Max 4x decrease
                        else oldDifficulty * timeRatio
                    
                    // Apply bounds
                    max minDifficulty (min maxDifficulty newDifficulty)
                | None ->
                    // Fallback to previous block's difficulty
                    match blocks with
                    | [] -> this.CalculateGenesisDifficulty()
                    | last::_ -> last.Header.Difficulty

    /// Create blockchain state instance
    let createBlockchainState (logger: ILogger<InMemoryBlockchainState>) : IBlockchainState =
        let state = new InMemoryBlockchainState(logger)
        state.InitializeGenesis()
        state :> IBlockchainState
