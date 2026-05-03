namespace Trellis.Node

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleDPoS
open DPoSGovernance
open DPoSTokenomics

/// Tokenomics service for handling DPoS tokenomics operations
module TokenomicsService =

    /// Tokenomics service implementation
    type TokenomicsService(logger: ILogger<TokenomicsService>) =
        
        let mutable tokenomicsState = initTokenomicsState defaultTokenomics

        /// Process block rewards for a new block
        member this.ProcessBlockRewards (blockHeight: BlockHeight) (validator: Address) (delegators: Address list) : Task<TokenomicsResult> =
            task {
                try
                    let (result, updatedState) = distributeBlockRewards tokenomicsState blockHeight validator delegators
                    tokenomicsState <- updatedState
                    
                    // Check for halving
                    tokenomicsState <- checkHalving tokenomicsState blockHeight
                    
                    logger.LogInformation("Processed block rewards for block {BlockHeight}", blockHeight)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error processing block rewards")
                    return Failure (sprintf "Failed to process block rewards: %s" ex.Message)
            }

        /// Process transaction fees
        member this.ProcessTransactionFees (fees: Balance) : Task<TokenomicsResult> =
            task {
                try
                    let (result, updatedState) = processTransactionFees tokenomicsState fees
                    tokenomicsState <- updatedState
                    
                    logger.LogInformation("Processed transaction fees: {Fees}", fees)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error processing transaction fees")
                    return Failure (sprintf "Failed to process transaction fees: %s" ex.Message)
            }

        /// Execute treasury spending
        member this.ExecuteTreasurySpending (amount: Balance) (recipient: Address) (purpose: string) (proposalId: Hash) : Task<TokenomicsResult> =
            task {
                try
                    let (result, updatedState) = executeTreasurySpending tokenomicsState amount recipient purpose proposalId
                    tokenomicsState <- updatedState
                    
                    logger.LogInformation("Executed treasury spending: {Amount} to {Recipient} for {Purpose}", amount, recipient, purpose)
                    return result
                with
                | ex ->
                    logger.LogError(ex, "Error executing treasury spending")
                    return Failure (sprintf "Failed to execute treasury spending: %s" ex.Message)
            }

        /// Update staking supply
        member this.UpdateStakingSupply (stakedAmount: Balance) : unit =
            try
                tokenomicsState <- updateStakingSupply tokenomicsState stakedAmount
                logger.LogInformation("Updated staking supply: {StakedAmount}", stakedAmount)
            with
            | ex -> logger.LogError(ex, "Error updating staking supply")

        /// Get tokenomics statistics
        member this.GetTokenomicsStats () : Map<string, obj> =
            getTokenomicsStats tokenomicsState

        /// Get current token supply
        member this.GetTokenSupply () : TokenSupply =
            tokenomicsState.TokenSupply

        /// Get treasury information
        member this.GetTreasuryInfo () : Treasury =
            tokenomicsState.Treasury

        /// Get staking rewards information
        member this.GetStakingRewards () : StakingRewards =
            tokenomicsState.StakingRewards

        /// Get economics parameters
        member this.GetEconomicsParameters () : TokenEconomics =
            tokenomicsState.Economics

        /// Process tokenomics for a block
        member this.ProcessTokenomicsBlock (block: Block) (validator: Address) (delegators: Address list) (totalFees: Balance) : unit =
            try
                // Process block rewards
                let (_, updatedState) = distributeBlockRewards tokenomicsState block.Header.Height validator delegators
                tokenomicsState <- updatedState
                
                // Process transaction fees
                let (_, updatedState2) = processTransactionFees tokenomicsState totalFees
                tokenomicsState <- updatedState2
                
                // Check for halving
                tokenomicsState <- checkHalving tokenomicsState block.Header.Height
                
                logger.LogInformation("Processed tokenomics for block {BlockHeight}", block.Header.Height)
            with
            | ex -> logger.LogError(ex, "Error processing tokenomics block")

    /// Interface for tokenomics operations
    type ITokenomicsService =
        abstract member ProcessBlockRewards: BlockHeight -> Address -> Address list -> Task<TokenomicsResult>
        abstract member ProcessTransactionFees: Balance -> Task<TokenomicsResult>
        abstract member ExecuteTreasurySpending: Balance -> Address -> string -> Hash -> Task<TokenomicsResult>
        abstract member UpdateStakingSupply: Balance -> unit
        abstract member GetTokenomicsStats: unit -> Map<string, obj>
        abstract member GetTokenSupply: unit -> TokenSupply
        abstract member GetTreasuryInfo: unit -> Treasury
        abstract member GetStakingRewards: unit -> StakingRewards
        abstract member GetEconomicsParameters: unit -> TokenEconomics
        abstract member ProcessTokenomicsBlock: Block -> Address -> Address list -> Balance -> unit
