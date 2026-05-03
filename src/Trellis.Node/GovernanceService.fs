namespace Trellis.Node

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleDPoS
open DPoSGovernance

/// Interface for blockchain state operations
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
    abstract member GetCurrentHeight: unit -> BlockHeight

/// Governance service for handling DPoS governance operations
module GovernanceService =

    /// Governance service implementation
    type GovernanceService(logger: ILogger<GovernanceService>, blockchainState: IBlockchainState) =
        
        let mutable governanceState = initGovernanceState defaultGovernanceParameters

        /// Create a new governance proposal
        member this.CreateProposal (title: string) (description: string) (proposalType: ProposalType) (proposer: Address) (parameters: Map<string, string>) : Task<GovernanceResult> =
            task {
                try
                    let proposalId = Hash (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(title + description + DateTime.UtcNow.ToString())))
                    let currentTime = Timestamp DateTime.UtcNow
                    let currentHeight = blockchainState.GetCurrentHeight()
                    let (BlockHeight height) = currentHeight
                    
                    let proposal = {
                        Id = proposalId
                        Title = title
                        Description = description
                        ProposalType = proposalType
                        Proposer = proposer
                        StartBlock = currentHeight
                        EndBlock = BlockHeight (height + defaultGovernanceParameters.VotingPeriod)
                        ExecutionBlock = Some (BlockHeight (height + defaultGovernanceParameters.VotingPeriod + defaultGovernanceParameters.ExecutionDelay))
                        Status = Pending
                        CreatedAt = currentTime
                        UpdatedAt = currentTime
                        Parameters = parameters
                        ExecutionData = None
                    }
                    
                    let (result, updatedState) = createProposal proposal governanceState
                    governanceState <- updatedState
                    
                    logger.LogInformation("Created governance proposal: {ProposalId} by {Proposer}", proposalId, proposer)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error creating governance proposal")
                    return Failure (sprintf "Failed to create proposal: %s" ex.Message)
            }

        /// Submit a vote on a proposal
        member this.SubmitVote (proposalId: Hash) (voter: Address) (vote: VoteOption) (signature: Signature) : Task<GovernanceResult> =
            task {
                try
                    let currentTime = Timestamp DateTime.UtcNow
                    let consensus = blockchainState.GetDPoSConsensus()
                    let votingPower = calculateVotingPower voter consensus
                    let (Balance powerAmount) = votingPower.TotalStake
                    
                    let proposalVote = {
                        ProposalId = proposalId
                        Voter = voter
                        Vote = vote
                        VotingPower = Balance powerAmount
                        Timestamp = currentTime
                        Signature = signature
                    }
                    
                    let (result, updatedState) = submitVote proposalVote governanceState
                    governanceState <- updatedState
                    
                    logger.LogInformation("Vote submitted on proposal {ProposalId} by {Voter}: {Vote}", proposalId, voter, vote)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error submitting vote")
                    return Failure (sprintf "Failed to submit vote: %s" ex.Message)
            }

        /// Execute a passed proposal
        member this.ExecuteProposal (proposalId: Hash) : Task<GovernanceResult> =
            task {
                try
                    let (result, updatedState) = executeProposal proposalId governanceState
                    governanceState <- updatedState
                    
                    logger.LogInformation("Executed governance proposal: {ProposalId}", proposalId)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error executing proposal")
                    return Failure (sprintf "Failed to execute proposal: %s" ex.Message)
            }

        /// Get proposal details
        member this.GetProposal (proposalId: Hash) : GovernanceProposal option =
            governanceState.Proposals.TryFind proposalId

        /// Get all proposals
        member this.GetAllProposals () : GovernanceProposal list =
            governanceState.Proposals |> Map.toList |> List.map snd

        /// Get active proposals
        member this.GetActiveProposals () : GovernanceProposal list =
            governanceState.ActiveProposals
            |> List.choose (fun id -> governanceState.Proposals.TryFind id)

        /// Get votes for a proposal
        member this.GetProposalVotes (proposalId: Hash) : ProposalVote list =
            governanceState.Votes |> Map.tryFind proposalId |> Option.defaultValue []

        /// Get governance statistics
        member this.GetGovernanceStats () : Map<string, obj> =
            getGovernanceStats governanceState

        /// Update proposal statuses (called on each block)
        member this.UpdateProposalStatuses () : unit =
            try
                let currentHeight = blockchainState.GetCurrentHeight()
                let (BlockHeight height) = currentHeight
                
                // Update status for all active proposals
                for proposalId in governanceState.ActiveProposals do
                    match governanceState.Proposals.TryFind proposalId with
                    | Some proposal ->
                        let (BlockHeight endHeight) = proposal.EndBlock
                        if height >= endHeight then
                            let (_, updatedState) = updateProposalStatus proposalId governanceState
                            governanceState <- updatedState
                    | None -> ()
            with
            | ex -> logger.LogError(ex, "Error updating proposal statuses")

        /// Process governance operations for a block
        member this.ProcessGovernanceBlock (block: Block) : unit =
            try
                // Update proposal statuses
                this.UpdateProposalStatuses()
                
                // Check for proposals ready for execution
                for proposalId in governanceState.ExecutedProposals do
                    match governanceState.Proposals.TryFind proposalId with
                    | Some proposal when proposal.Status = Passed ->
                        let (BlockHeight currentHeight) = blockchainState.GetCurrentHeight()
                        match proposal.ExecutionBlock with
                        | Some (BlockHeight execHeight) when currentHeight >= execHeight ->
                            let (result, updatedState) = executeProposal proposalId governanceState
                            governanceState <- updatedState
                            logger.LogInformation("Auto-executed proposal {ProposalId}: {Result}", proposalId, result)
                        | _ -> ()
                    | _ -> ()
            with
            | ex -> logger.LogError(ex, "Error processing governance block")

    /// Interface for governance operations
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