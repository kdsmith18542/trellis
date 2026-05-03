namespace Trellis.Node

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleDPoS
open DPoSGovernance
open DPoSTokenomics
open TreasurySystem

/// Treasury service for handling treasury operations
module TreasuryService =

    /// Treasury service implementation
    type TreasuryService(logger: ILogger<TreasuryService>) =
        
        let defaultPolicy = {
            MaxSpendingPerProposal = Balance 1000000000000000000UL
            MaxSpendingPerBlock = Balance 10000000000000000000UL
            EmergencyThreshold = Balance 10000000000000000000UL
            ReserveAllocationRate = 0.30m
            DevelopmentAllocationRate = 0.25m
            GovernanceAllocationRate = 0.25m
            EmergencyAllocationRate = 0.20m
        }
        let mutable treasuryState = {
            Treasury = {
                Balance = Balance 0UL
                FundingSources = []
                SpendingHistory = []
                GovernanceReserve = Balance 0UL
                DevelopmentReserve = Balance 0UL
                EmergencyReserve = Balance 0UL
            }
            Budget = {
                TotalBudget = Balance 0UL
                AllocatedBudget = Balance 0UL
                AvailableBudget = Balance 0UL
                Categories = Map.empty
                BudgetPeriod = 10080UL
                LastReset = BlockHeight 0UL
                NextReset = BlockHeight 10080UL
            }
            Policy = defaultPolicy
            Operations = []
            LastUpdateBlock = BlockHeight 0UL
            AuditTrail = []
        }

        /// Fund treasury
        member this.FundTreasury (amount: Balance) (source: string) : Task<TreasuryResult> =
            task {
                try
                    let (result, updatedState) = fundTreasury treasuryState amount source
                    treasuryState <- updatedState
                    
                    logger.LogInformation("Funded treasury: {Amount} from {Source}", amount, source)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error funding treasury")
                    return Failure (sprintf "Failed to fund treasury: %s" ex.Message)
            }

        /// Spend from treasury
        member this.SpendFromTreasury (amount: Balance) (recipient: Address) (purpose: string) (proposalId: Hash) : Task<TreasuryResult> =
            task {
                try
                    let (result, updatedState) = spendFromTreasury treasuryState amount recipient purpose proposalId
                    treasuryState <- updatedState
                    
                    logger.LogInformation("Spent from treasury: {Amount} to {Recipient} for {Purpose}", amount, recipient, purpose)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error spending from treasury")
                    return Failure (sprintf "Failed to spend from treasury: %s" ex.Message)
            }

        /// Allocate reserve
        member this.AllocateReserve (amount: Balance) (reserveType: string) (purpose: string) : Task<TreasuryResult> =
            task {
                try
                    let (result, updatedState) = allocateReserve treasuryState amount reserveType purpose
                    treasuryState <- updatedState
                    
                    logger.LogInformation("Allocated reserve: {Amount} for {ReserveType} - {Purpose}", amount, reserveType, purpose)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error allocating reserve")
                    return Failure (sprintf "Failed to allocate reserve: %s" ex.Message)
            }

        /// Emergency withdrawal
        member this.EmergencyWithdrawal (amount: Balance) (recipient: Address) (reason: string) : Task<TreasuryResult> =
            task {
                try
                    let (result, updatedState) = emergencyWithdrawal treasuryState amount recipient reason
                    treasuryState <- updatedState
                    
                    logger.LogInformation("Emergency withdrawal: {Amount} to {Recipient} - {Reason}", amount, recipient, reason)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error emergency withdrawal")
                    return Failure (sprintf "Failed emergency withdrawal: %s" ex.Message)
            }

        /// Get treasury statistics
        member this.GetTreasuryStats () : Map<string, obj> =
            getTreasuryStats treasuryState

        /// Get treasury state
        member this.GetTreasuryState () : TreasuryState =
            treasuryState

        /// Get treasury balance
        member this.GetTreasuryBalance () : Balance =
            treasuryState.Treasury.Balance

        /// Get treasury reserves
        member this.GetTreasuryReserves () : (Balance * Balance * Balance) =
            (treasuryState.Treasury.GovernanceReserve, treasuryState.Treasury.DevelopmentReserve, treasuryState.Treasury.EmergencyReserve)

        /// Process treasury for a block
        member this.ProcessTreasuryBlock (blockHeight: BlockHeight) (tokenomicsState: TokenomicsState) : unit =
            try
                treasuryState <- processTreasuryBlock treasuryState blockHeight tokenomicsState
                logger.LogInformation("Processed treasury for block {BlockHeight}", blockHeight)
            with
            | ex -> logger.LogError(ex, "Error processing treasury block")

        /// Execute governance treasury spending
        member this.ExecuteGovernanceSpending (proposal: GovernanceProposal) : Task<TreasuryResult> =
            task {
                try
                    match proposal.ProposalType with
                    | TreasurySpending ->
                        // Extract spending parameters from proposal
                        let amount = proposal.Parameters |> Map.tryFind "amount" |> Option.map (fun v -> Balance (uint64 v)) |> Option.defaultValue (Balance 0UL)
                        let recipient = proposal.Parameters |> Map.tryFind "recipient" |> Option.map (fun v -> Address (System.Text.Encoding.UTF8.GetBytes(v))) |> Option.defaultValue (Address [||])
                        let purpose = proposal.Parameters |> Map.tryFind "purpose" |> Option.defaultValue "Governance spending"
                        
                        let! result = this.SpendFromTreasury amount recipient purpose proposal.Id
                        return result
                    | _ ->
                        return Failure "Not a treasury spending proposal"
                with
                | ex ->
                    logger.LogError(ex, "Error executing governance treasury spending")
                    return Failure (sprintf "Failed to execute governance spending: %s" ex.Message)
            }

        /// Auto-fund treasury from tokenomics
        member this.AutoFundTreasury (tokenomicsState: TokenomicsState) : unit =
            try
                let treasuryBalance = tokenomicsState.Treasury.Balance
                if treasuryBalance > Balance 0UL then
                    let (result, updatedState) = fundTreasury treasuryState treasuryBalance "Tokenomics funding"
                    treasuryState <- updatedState
                    logger.LogInformation("Auto-funded treasury from tokenomics: {Amount}", treasuryBalance)
            with
            | ex -> logger.LogError(ex, "Error auto-funding treasury")

    /// Interface for treasury operations
    type ITreasuryService =
        abstract member FundTreasury: Balance -> string -> Task<TreasuryResult>
        abstract member SpendFromTreasury: Balance -> Address -> string -> Hash -> Task<TreasuryResult>
        abstract member AllocateReserve: Balance -> string -> string -> Task<TreasuryResult>
        abstract member EmergencyWithdrawal: Balance -> Address -> string -> Task<TreasuryResult>
        abstract member GetTreasuryStats: unit -> Map<string, obj>
        abstract member GetTreasuryState: unit -> TreasuryState
        abstract member GetTreasuryBalance: unit -> Balance
        abstract member GetTreasuryReserves: unit -> (Balance * Balance * Balance)
        abstract member ProcessTreasuryBlock: BlockHeight -> TokenomicsState -> unit
        abstract member ExecuteGovernanceSpending: GovernanceProposal -> Task<TreasuryResult>
        abstract member AutoFundTreasury: TokenomicsState -> unit
