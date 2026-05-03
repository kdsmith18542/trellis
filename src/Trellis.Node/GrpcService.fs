namespace Trellis.Node

open System
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Trellis.Core
open SimpleTypes
open Trellis.Core.ChainParams
open SimpleDPoS
open DPoSGovernance
open DPoSTokenomics
open TreasurySystem
open FSharp.Control

/// gRPC service implementation for Trellis
module GrpcService =

    /// gRPC Response types (temporary until proto generation)
    type GetBalanceResponse = {
        Balance: Balance
        Success: bool
        ErrorMessage: string option
    }

    type GetAccountResponse = {
        Account: Account option
        Found: bool
        ErrorMessage: string option
    }

    type SendTransactionResponse = {
        Success: bool
        TransactionHash: Hash option
        ErrorMessage: string option
    }

    type GetBlockResponse = {
        Block: Block option
        Found: bool
        ErrorMessage: string option
    }

    type SubmitBlockResponse = {
        Accepted: bool
        ErrorMessage: string option
    }

    type GetValidatorsResponse = {
        Validators: (PublicKey * StakingInfo) list
        TotalCount: uint32
        ErrorMessage: string option
    }

    type GetChainStateResponse = {
        CurrentHeight: BlockHeight
        LastBlockHash: Hash
        ValidatorCount: uint32
        TotalSupply: Balance
        BaseFeePerGas: uint64
        ErrorMessage: string option
    }

    type GetDPoSConsensusRequest = {
        Dummy: unit
    }

    type GetDPoSConsensusResponse = {
        ValidatorCount: int
        ActiveValidatorCount: int
        CurrentRound: uint64
        BlockProducer: string option
        TotalStake: uint64
        ErrorMessage: string option
    }

    // Governance request/response types
    type CreateProposalRequest = {
        Title: string
        Description: string
        ProposalType: string
        Proposer: Address
        Parameters: Map<string, string>
    }

    type CreateProposalResponse = {
        ProposalId: string
        Success: bool
        ErrorMessage: string option
    }

    type SubmitVoteRequest = {
        ProposalId: string
        Voter: Address
        Vote: string
        Signature: Signature
    }

    type SubmitVoteResponse = {
        Success: bool
        ErrorMessage: string option
    }

    type GetProposalRequest = {
        ProposalId: string
    }

    type GetProposalResponse = {
        Proposal: GovernanceProposal option
        ErrorMessage: string option
    }

    type GetProposalsRequest = {
        Filter: string option // "active", "passed", "rejected", etc.
    }

    type GetProposalsResponse = {
        Proposals: GovernanceProposal list
        ErrorMessage: string option
    }

    // Tokenomics request/response types
    type GetTokenomicsStatsRequest = {
        Dummy: unit
    }

    type GetTokenomicsStatsResponse = {
        Stats: Map<string, obj>
        ErrorMessage: string option
    }

    type GetTokenSupplyRequest = {
        Dummy: unit
    }

    type GetTokenSupplyResponse = {
        TokenSupply: TokenSupply
        ErrorMessage: string option
    }

    type GetBalanceRequest = {
        Address: Address
    }

    type GetAccountRequest = {
        Address: Address
    }

    type SendTransactionRequest = {
        From: Address
        To: Address
        Amount: Balance
        Nonce: Nonce
        Fee: Balance
        PublicKey: PublicKey
        Signature: Signature
    }

    type GetBlockRequest = {
        BlockHash: Hash
    }

    type SubmitBlockRequest = {
        Block: Block
    }

    type GetValidatorsRequest = {
        Dummy: unit
    }

    type GetChainStateRequest = {
        Dummy: unit
    }

    /// Blockchain state interface for gRPC service
    type IBlockchainState =
        abstract member GetAccount: Address -> Account option
        abstract member GetBalance: Address -> Balance
        abstract member GetBlock: Hash -> Block option
        abstract member GetChainState: unit -> ChainState
        abstract member GetValidators: unit -> (PublicKey * StakingInfo) list
        abstract member SubmitTransaction: Transaction -> bool
        abstract member SubmitBlock: Block -> bool
        abstract member GetDPoSConsensus: unit -> DPoSConsensus
        abstract member GetBaseFeePerGas: unit -> uint64
        abstract member GetLastBlockBurn: unit -> uint64
        abstract member GetLastBlockTips: unit -> uint64

    /// Interface for governance service operations
    type IGovernanceService =
        abstract member CreateProposal: string -> string -> ProposalType -> Address -> Map<string, string> -> Task<GovernanceResult>
        abstract member SubmitVote: Hash -> Address -> VoteOption -> Signature -> Task<GovernanceResult>
        abstract member ExecuteProposal: Hash -> Task<GovernanceResult>
        abstract member GetProposal: Hash -> GovernanceProposal option
        abstract member GetAllProposals: unit -> GovernanceProposal list
        abstract member GetActiveProposals: unit -> GovernanceProposal list
        abstract member GetProposalVotes: Hash -> ProposalVote list
        abstract member GetGovernanceStats: unit -> Map<string, obj>
        abstract member ProcessGovernanceBlock: Block -> unit

    /// gRPC service for Trellis blockchain
    type TrellisGrpcService(logger: ILogger<TrellisGrpcService>, blockchainState: IBlockchainState, governanceService: IGovernanceService) =
        
        // Account operations
        member this.GetBalance(request: GetBalanceRequest) : Task<GetBalanceResponse> =
            task {
                try
                    // Input validation
                    let (Address addrBytes) = request.Address
                    if addrBytes = null || addrBytes.Length = 0 then
                        return {
                            Balance = Balance(0UL)
                            Success = false
                            ErrorMessage = Some "Invalid address: address cannot be null or empty"
                        }
                    else
                        if addrBytes.Length <> 20 then
                            return {
                                Balance = Balance(0UL)
                                Success = false
                                ErrorMessage = Some "Invalid address: address must be 20 bytes"
                            }
                        else
                            logger.LogInformation("GetBalance request for address: {Address}", request.Address)
                            
                            let balance = blockchainState.GetBalance(request.Address)
                            return {
                                Balance = balance
                                Success = true
                                ErrorMessage = None
                            }
                with
                | ex ->
                    logger.LogError(ex, "Error getting balance for address: {Address}", request.Address)
                    return {
                        Balance = Balance(0UL)
                        Success = false
                        ErrorMessage = Some ex.Message
                    }
            }
        
        member this.GetAccount(request: GetAccountRequest) : Task<GetAccountResponse> =
            task {
                try
                    // Input validation
                    let (Address addrBytes) = request.Address
                    if addrBytes = null || addrBytes.Length = 0 then
                        return {
                            Account = None
                            Found = false
                            ErrorMessage = Some "Invalid address: address cannot be null or empty"
                        }
                    else
                        if addrBytes.Length <> 20 then
                            return {
                                Account = None
                                Found = false
                                ErrorMessage = Some "Invalid address: address must be 20 bytes"
                            }
                        else
                            logger.LogInformation("GetAccount request for address: {Address}", request.Address)
                            
                            match blockchainState.GetAccount(request.Address) with
                            | Some account ->
                                return {
                                    Account = Some account
                                    Found = true
                                    ErrorMessage = None
                                }
                            | None ->
                                return {
                                    Account = None
                                    Found = false
                                    ErrorMessage = Some "Account not found"
                                }
                with
                | ex ->
                    logger.LogError(ex, "Error getting account for address: {Address}", request.Address)
                    return {
                        Account = None
                        Found = false
                        ErrorMessage = Some ex.Message
                    }
            }
        
        // Transaction operations
        member this.SendTransaction(request: SendTransactionRequest) : Task<SendTransactionResponse> =
            task {
                try
                    // Input validation
                    let (Address fromBytes) = request.From
                    let (Address toBytes) = request.To
                    let (Balance amount) = request.Amount
                    let (Balance fee) = request.Fee
                    let (PublicKey pubKeyBytes) = request.PublicKey
                    let (Signature sigBytes) = request.Signature
                    
                    // Validate addresses
                    if fromBytes = null || fromBytes.Length <> 20 then
                        return {
                            Success = false
                            TransactionHash = None
                            ErrorMessage = Some "Invalid from address: must be 20 bytes"
                        }
                    else
                        if toBytes = null || toBytes.Length <> 20 then
                            return {
                                Success = false
                                TransactionHash = None
                                ErrorMessage = Some "Invalid to address: must be 20 bytes"
                            }
                        else
                            // Validate amount and fee
                            if amount = 0UL then
                                return {
                                    Success = false
                                    TransactionHash = None
                                    ErrorMessage = Some "Invalid amount: cannot be zero"
                                }
                            else
                                if fee = 0UL then
                                    return {
                                        Success = false
                                        TransactionHash = None
                                        ErrorMessage = Some "Invalid fee: cannot be zero"
                                    }
                                else
                                    // Validate public key
                                    if pubKeyBytes = null || pubKeyBytes.Length = 0 then
                                        return {
                                            Success = false
                                            TransactionHash = None
                                            ErrorMessage = Some "Invalid public key: cannot be null or empty"
                                        }
                                    else
                                        // Validate signature
                                        if sigBytes = null || sigBytes.Length = 0 then
                                            return {
                                                Success = false
                                                TransactionHash = None
                                                ErrorMessage = Some "Invalid signature: cannot be null or empty"
                                            }
                                        else
                                            logger.LogInformation("SendTransaction request from {From} to {To} amount {Amount}", 
                                                request.From, request.To, request.Amount)
                                            
                                            // Create transaction from request (include sender pubkey in metadata)
                                            let pubKeyHex = System.Convert.ToHexString(pubKeyBytes)
                                            let baseTx = {
                                                From = request.From
                                                To = request.To
                                                Amount = request.Amount
                                                Nonce = request.Nonce
                                                Fee = request.Fee
                                                Signature = request.Signature
                                                Hash = Hash [||] // will set after hashing
                                                GasLimit = 21000UL
                                                GasPrice = 20UL
                                                Data = Array.empty
                                                Metadata = Map.ofList [ ("pubkey", pubKeyHex) ]
                                                MaxFeePerGas = None
                                                MaxPriorityFeePerGas = None
                                                TransactionType = 0uy
                                            }
                                            let txHash = SimpleCrypto.transactionHashSimple baseTx
                                            let transaction = { baseTx with Hash = txHash }

                                            // Route to mempool (minimal admission); fall back to state if mempool not available
                                            let mempoolResult = 
                                                try 
                                                    Trellis.Node.Mempool.acceptTransaction transaction (fun a -> blockchainState.GetAccount a)
                                                with _ -> Result.Error "mempool_unavailable"
                                            match mempoolResult with
                                            | Ok _ ->
                                                return {
                                                    Success = true
                                                    TransactionHash = Some transaction.Hash
                                                    ErrorMessage = None
                                                }
                                            | Error err ->
                                                // Attempt direct submission (legacy path)
                                                let success = blockchainState.SubmitTransaction(transaction)
                                                if success then
                                                    return {
                                                        Success = true
                                                        TransactionHash = Some transaction.Hash
                                                        ErrorMessage = None
                                                    }
                                                else
                                                    return {
                                                        Success = false
                                                        TransactionHash = None
                                                        ErrorMessage = Some (sprintf "Transaction validation failed (%s)" err)
                                                    }
                with
                | ex ->
                    logger.LogError(ex, "Error sending transaction")
                    return {
                        Success = false
                        TransactionHash = None
                        ErrorMessage = Some ex.Message
                    }
            }
        
        // Block operations
        member this.GetBlock(request: GetBlockRequest) : Task<GetBlockResponse> =
            task {
                try
                    // Input validation
                    let (Hash hashBytes) = request.BlockHash
                    if hashBytes = null || hashBytes.Length = 0 then
                        return {
                            Block = None
                            Found = false
                            ErrorMessage = Some "Invalid block hash: hash cannot be null or empty"
                        }
                    else
                        if hashBytes.Length <> 32 then
                            return {
                                Block = None
                                Found = false
                                ErrorMessage = Some "Invalid block hash: hash must be 32 bytes"
                            }
                        else
                            logger.LogInformation("GetBlock request for hash: {Hash}", request.BlockHash)
                            
                            match blockchainState.GetBlock(request.BlockHash) with
                            | Some block ->
                                return {
                                    Block = Some block
                                    Found = true
                                    ErrorMessage = None
                                }
                            | None ->
                                return {
                                    Block = None
                                    Found = false
                                    ErrorMessage = Some "Block not found"
                                }
                with
                | ex ->
                    logger.LogError(ex, "Error getting block: {Hash}", request.BlockHash)
                    return {
                        Block = None
                        Found = false
                        ErrorMessage = Some ex.Message
                    }
            }
        
        member this.SubmitBlock(request: SubmitBlockRequest) : Task<SubmitBlockResponse> =
            task {
                try
                    // Input validation
                    let block = request.Block
                    let (Hash hashBytes) = block.Header.Hash
                    let (BlockHeight height) = block.Header.Height
                    
                    // Validate block hash
                    if hashBytes = null || hashBytes.Length = 0 then
                        return {
                            Accepted = false
                            ErrorMessage = Some "Invalid block: hash cannot be null or empty"
                        }
                    else
                        if hashBytes.Length <> 32 then
                            return {
                                Accepted = false
                                ErrorMessage = Some "Invalid block: hash must be 32 bytes"
                            }
                        else
                            // Validate block height
                            if height = 0UL && block.Header.PreviousHash <> Hash(Array.zeroCreate 32) then
                                return {
                                    Accepted = false
                                    ErrorMessage = Some "Invalid block: genesis block must have zero previous hash"
                                }
                            else
                                // Validate transactions (F# lists are never null, but check if empty is acceptable)
                                // Transactions list is always valid in F#, so we skip null check
                                    // Validate merkle root matches transactions
                                    let calculatedRoot = SimpleCrypto.calculateMerkleRoot block.Transactions
                                    if calculatedRoot <> block.Header.MerkleRoot then
                                        return {
                                            Accepted = false
                                            ErrorMessage = Some "Invalid block: merkle root does not match transactions"
                                        }
                                    else
                                        logger.LogInformation("SubmitBlock request for block: {Hash} at height {Height}", 
                                            block.Header.Hash, height)
                                        
                                        let success = blockchainState.SubmitBlock(block)
                                        if success then
                                            return {
                                                Accepted = true
                                                ErrorMessage = None
                                            }
                                        else
                                            return {
                                                Accepted = false
                                                ErrorMessage = Some "Block validation failed"
                                            }
                with
                | ex ->
                    logger.LogError(ex, "Error submitting block: {Error}", ex.Message)
                    return {
                        Accepted = false
                        ErrorMessage = Some ex.Message
                    }
            }
        
        // Validator operations
        member this.GetValidators(request: GetValidatorsRequest) : Task<GetValidatorsResponse> =
            task {
                try
                    logger.LogInformation("GetValidators request")
                    
                    let validators = blockchainState.GetValidators()
                    
                    return {
                        Validators = validators
                        TotalCount = uint32 validators.Length
                        ErrorMessage = None
                    }
                with
                | ex ->
                    logger.LogError(ex, "Error getting validators")
                    return {
                        Validators = []
                        TotalCount = 0u
                        ErrorMessage = Some ex.Message
                    }
            }
        
        // Chain state operations
        member this.GetChainState(request: GetChainStateRequest) : Task<GetChainStateResponse> =
            task {
                try
                    logger.LogInformation("GetChainState request")
                    
                    let chainState = blockchainState.GetChainState()
                    return {
                        CurrentHeight = chainState.CurrentHeight
                        LastBlockHash = chainState.LastBlockHash
                        ValidatorCount = uint32 chainState.Validators.Length
                        TotalSupply = chainState.TotalSupply
                        BaseFeePerGas = blockchainState.GetBaseFeePerGas()
                        ErrorMessage = None
                    }
                with
                | ex ->
                    logger.LogError(ex, "Error getting chain state")
                    return {
                        CurrentHeight = BlockHeight(0UL)
                        LastBlockHash = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes("error_state"))
                        ValidatorCount = 0u
                        TotalSupply = Balance(0UL)
                        BaseFeePerGas = 0UL
                        ErrorMessage = Some ex.Message
                    }
            }

        member this.GetDPoSConsensus(request: GetDPoSConsensusRequest) : Task<GetDPoSConsensusResponse> =
            task {
                try
                    let consensus = blockchainState.GetDPoSConsensus()
                    let totalStake = getTotalStake consensus
                    let (Balance stakeAmount) = totalStake
                    return {
                        ValidatorCount = getValidatorCount consensus
                        ActiveValidatorCount = getActiveValidatorCount consensus
                        CurrentRound = consensus.CurrentRound
                        BlockProducer = consensus.BlockProducer |> Option.map (fun addr -> 
                            let (Address bytes) = addr
                            Convert.ToHexString(bytes)
                        )
                        TotalStake = stakeAmount
                        ErrorMessage = None
                    }
                with ex ->
                    logger.LogError(ex, "Error getting DPoS consensus")
                    return {
                        ValidatorCount = 0
                        ActiveValidatorCount = 0
                        CurrentRound = 0UL
                        BlockProducer = None
                        TotalStake = 0UL
                        ErrorMessage = Some ex.Message
                    }
            }

        // Governance operations
        member this.CreateProposal(request: CreateProposalRequest) : Task<CreateProposalResponse> =
            task {
                try
                    let proposalType =
                        match request.ProposalType.ToLower() with
                        | "parameterchange" -> ParameterChange
                        | "treasuryspending" -> TreasurySpending
                        | "validatorslashing" -> ValidatorSlashing
                        | "networkupgrade" -> NetworkUpgrade
                        | _ -> Custom

                    let parameters = request.Parameters |> Map.map (fun k v -> v.ToString())
                    // Generate unique proposal ID using SHA256 hash of title, description, and timestamp
                    let timestamp = DateTime.UtcNow.ToString()
                    let hashInput = request.Title + request.Description + timestamp
                    let hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hashInput))
                    let proposalIdBytes = hashBytes
                    let proposalIdString = System.Convert.ToHexString(proposalIdBytes)

                    let! result = governanceService.CreateProposal request.Title request.Description proposalType request.Proposer parameters

                    match result with
                    | DPoSGovernance.Success _ ->
                        return {
                            ProposalId = proposalIdString
                            Success = true
                            ErrorMessage = None
                        }
                    | DPoSGovernance.Failure msg ->
                        return {
                            ProposalId = ""
                            Success = false
                            ErrorMessage = Some msg
                        }
                with ex ->
                    logger.LogError(ex, "Error creating proposal")
                    return {
                        ProposalId = ""
                        Success = false
                        ErrorMessage = Some ex.Message
                    }
            }

        member this.SubmitVote(request: SubmitVoteRequest) : Task<SubmitVoteResponse> =
            task {
                try
                    // Parse vote option from string
                    let voteOption =
                        match request.Vote.ToLower() with
                        | "yes" -> Some Yes
                        | "no" -> Some No
                        | "abstain" -> Some Abstain
                        | _ -> None

                    match voteOption with
                    | None ->
                        return {
                            Success = false
                            ErrorMessage = Some "Invalid vote option. Must be 'yes', 'no', or 'abstain'"
                        }
                    | Some vote ->
                        // Convert proposal ID from string to Hash
                        let proposalId = Hash (System.Text.Encoding.UTF8.GetBytes(request.ProposalId))

                        // Submit vote through governance service
                        let! result = governanceService.SubmitVote proposalId request.Voter vote request.Signature

                        match result with
                        | DPoSGovernance.Success _ ->
                            logger.LogInformation("Vote submitted successfully for proposal: {ProposalId} by voter: {Voter}", request.ProposalId, request.Voter)
                            return {
                                Success = true
                                ErrorMessage = None
                            }
                        | DPoSGovernance.Failure msg ->
                            logger.LogWarning("Vote submission failed for proposal: {ProposalId} by voter: {Voter}. Reason: {Reason}", request.ProposalId, request.Voter, msg)
                            return {
                                Success = false
                                ErrorMessage = Some msg
                            }
                with ex ->
                    logger.LogError(ex, "Error submitting vote")
                    return {
                        Success = false
                        ErrorMessage = Some ex.Message
                    }
            }

        member this.GetProposal(request: GetProposalRequest) : Task<GetProposalResponse> =
            task {
                try
                    let proposalId = Hash (System.Text.Encoding.UTF8.GetBytes(request.ProposalId))
                    let proposal = governanceService.GetProposal proposalId
                    return {
                        Proposal = proposal
                        ErrorMessage = None
                    }
                with ex ->
                    logger.LogError(ex, "Error getting proposal")
                    return {
                        Proposal = None
                        ErrorMessage = Some ex.Message
                    }
            }

        member this.GetProposals(request: GetProposalsRequest) : Task<GetProposalsResponse> =
            task {
                try
                    let proposals = 
                        match request.Filter with
                        | Some "active" -> governanceService.GetActiveProposals()
                        | Some "passed" -> governanceService.GetAllProposals() |> List.filter (fun p -> p.Status = DPoSGovernance.Passed)
                        | Some "rejected" -> governanceService.GetAllProposals() |> List.filter (fun p -> p.Status = DPoSGovernance.Rejected)
                        | _ -> governanceService.GetAllProposals()
                    
                    return {
                        Proposals = proposals
                        ErrorMessage = None
                    }
                with ex ->
                    logger.LogError(ex, "Error getting proposals")
                    return {
                        Proposals = []
                        ErrorMessage = Some ex.Message
                    }
            }
        
        // Streaming operations
        member this.StreamBlocks(request: GetChainStateRequest) : AsyncSeq<BlockUpdate> =
            asyncSeq {
                try
                    logger.LogInformation("StreamBlocks request")
                    
                    // Stream new blocks as they arrive
                    let mutable lastHeight = 0UL
                    while true do
                        let chainState = blockchainState.GetChainState()
                        if chainState.CurrentHeight > BlockHeight(lastHeight) then
                            // New blocks available
                            let newBlocks = chainState.Blocks |> List.filter (fun b -> 
                                match b.Header.Height with | BlockHeight h -> h > lastHeight)
                            
                            for block in newBlocks do
                                yield {
                                    Block = block
                                    UpdateType = "NewBlock"
                                    Timestamp = block.Header.Timestamp
                                    BlockHeight = block.Header.Height
                                    IsNew = true
                                }
                            
                            lastHeight <- match chainState.CurrentHeight with | BlockHeight h -> h
                        
                        do! Async.Sleep(1000) // Check every second
                with
                | ex ->
                    logger.LogError(ex, "Error in block streaming")
                    yield {
                        Block = {
                            Header = {
                                Hash = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes("error_block"))
                                Height = BlockHeight(0UL)
                                Timestamp = Timestamp(DateTime.UtcNow)
                                PreviousHash = Hash(Array.create 32 0uy)
                                MerkleRoot = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes("error_merkle"))
                                Validator = Address(Array.create 20 0uy)
                                Nonce = 0UL
                                Difficulty = 0UL
                                PoWHash = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes("error_pow"))
                                Finalized = false
                                BaseFeePerGas = Trellis.Core.TrellisConfig.current.GenesisBaseFeePerGas
                                GasUsed = 0UL
                                GasLimit = Trellis.Core.TrellisConfig.current.GenesisGasLimit
                            }
                            Transactions = []
                        }
                        UpdateType = "Error"
                        Timestamp = Timestamp(DateTime.UtcNow)
                        BlockHeight = BlockHeight(0UL)
                        IsNew = false
                    }
            }
        
        member this.StreamTransactions(request: GetChainStateRequest) : AsyncSeq<TransactionUpdate> =
            asyncSeq {
                try
                    logger.LogInformation("StreamTransactions request")
                    
                    // Stream new transactions as they arrive
                    let mutable lastTxCount = 0
                    while true do
                        let chainState = blockchainState.GetChainState()
                        let currentTxCount = chainState.Blocks |> List.sumBy (fun b -> b.Transactions.Length)
                        
                        if currentTxCount > lastTxCount then
                            // New transactions available
                            let allTxs = chainState.Blocks |> List.collect (fun b -> b.Transactions)
                            let newTxs = allTxs |> List.skip lastTxCount
                            
                            for tx in newTxs do
                                yield {
                                    Transaction = tx
                                    UpdateType = "NewTransaction"
                                    Timestamp = Timestamp(DateTime.UtcNow)
                                    BlockHeight = chainState.CurrentHeight
                                    IsNew = true
                                }
                            
                            lastTxCount <- currentTxCount
                        
                        do! Async.Sleep(1000) // Check every second
                with
                | ex ->
                    logger.LogError(ex, "Error in transaction streaming")
                    yield {
                        Transaction = {
                            From = Address(Array.create 20 0uy)
                            To = Address(Array.create 20 0uy)
                            Amount = Balance(0UL)
                            Nonce = Nonce(0UL)
                            Fee = Balance(0UL)
                            Signature = Signature(Array.create 64 0uy)
                            Hash = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes("error_transaction"))
                            GasLimit = 0UL
                            GasPrice = 0UL
                            Data = Array.empty
                            Metadata = Map.empty
                            MaxFeePerGas = None
                            MaxPriorityFeePerGas = None
                            TransactionType = 0uy
                        }
                        UpdateType = "Error"
                        Timestamp = Timestamp(DateTime.UtcNow)
                        BlockHeight = BlockHeight(0UL)
                        IsNew = false
                    }
            }

    /// gRPC server configuration (temporarily disabled)
    module GrpcServerConfig =
        
        let configureGrpcServices (services: IServiceCollection) : unit =
            // services.AddGrpc() |> ignore
            // services.AddGrpcReflection() |> ignore
            ()
        
        let configureGrpcPipeline (app: IApplicationBuilder) : unit =
            app.UseRouting() |> ignore
            // app.UseEndpoints(fun endpoints ->
            //     endpoints.MapGrpcService<TrellisGrpcService>() |> ignore
            //     endpoints.MapGrpcReflectionService() |> ignore
            // ) |> ignore
            ()
