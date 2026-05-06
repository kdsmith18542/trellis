namespace Trellis.Core

open System
open SimpleTypes

/// Simple DPoS implementation using existing types
module SimpleDPoS =

    /// DPoS validator using existing types
    type DPoSValidator = {
        Address: Address
        PublicKey: PublicKey
        Stake: Balance
        IsActive: bool
        Commission: decimal
        PerformanceScore: float
    }

    /// DPoS consensus state
    type DPoSConsensus = {
        Validators: DPoSValidator list
        ActiveValidators: DPoSValidator list
        CurrentRound: uint64
        BlockProducer: Address option
    }

    /// DPoS staking operation
    type DPoSStakingOperation =
        | Stake of Address * Balance
        | Unstake of Address * Balance
        | Delegate of Address * Address * Balance
        | Undelegate of Address * Address * Balance

    /// DPoS staking result
    type DPoSStakingResult =
        | Success of string
        | Failure of string

    /// Create DPoS consensus
    let createDPoSConsensus (validators: DPoSValidator list) : DPoSConsensus =
        {
            Validators = validators
            ActiveValidators = validators |> List.filter (fun v -> v.IsActive)
            CurrentRound = 0UL
            BlockProducer = None
        }

    /// Add validator to consensus
    let addValidator (consensus: DPoSConsensus) (validator: DPoSValidator) : DPoSConsensus =
        { consensus with
            Validators = validator :: consensus.Validators
            ActiveValidators = if validator.IsActive then validator :: consensus.ActiveValidators else consensus.ActiveValidators
        }

    /// Remove validator from consensus
    let removeValidator (consensus: DPoSConsensus) (address: Address) : DPoSConsensus =
        { consensus with
            Validators = consensus.Validators |> List.filter (fun v -> v.Address <> address)
            ActiveValidators = consensus.ActiveValidators |> List.filter (fun v -> v.Address <> address)
        }

    /// Update validator stake
    let updateValidatorStake (consensus: DPoSConsensus) (address: Address) (newStake: Balance) : DPoSConsensus =
        { consensus with
            Validators = consensus.Validators |> List.map (fun v ->
                if v.Address = address then
                    { v with Stake = newStake }
                else
                    v
            )
            ActiveValidators = consensus.ActiveValidators |> List.map (fun v ->
                if v.Address = address then
                    { v with Stake = newStake }
                else
                    v
            )
        }

    /// Select block producer based on stake
    let selectBlockProducer (consensus: DPoSConsensus) : DPoSConsensus =
        let sortedValidators = 
            consensus.ActiveValidators
            |> List.sortByDescending (fun v -> v.Stake)
        
        let selectedProducer = 
            sortedValidators |> List.tryHead
        
        { consensus with
            BlockProducer = selectedProducer |> Option.map (fun v -> v.Address)
            CurrentRound = consensus.CurrentRound + 1UL
        }

    /// Validate block using DPoS
    let validateBlockWithDPoS (block: Block) (consensus: DPoSConsensus) : bool =
        match consensus.BlockProducer with
        | Some producer ->
            block.Header.Validator = producer
        | None -> false

    /// Process staking operation
    let processStakingOperation (consensus: DPoSConsensus) (operation: DPoSStakingOperation) : DPoSStakingResult * DPoSConsensus =
        try
            match operation with
            | Stake (address, amount) ->
                let validator = consensus.Validators |> List.tryFind (fun v -> v.Address = address)
                match validator with
                | Some v ->
                    let newStake = Balance (match v.Stake with | Balance s -> s + match amount with | Balance a -> a)
                    let updatedConsensus = updateValidatorStake consensus address newStake
                    (Success "Stake added successfully", updatedConsensus)
                | None ->
                    (Failure "Validator not found", consensus)
            
            | Unstake (address, amount) ->
                let validator = consensus.Validators |> List.tryFind (fun v -> v.Address = address)
                match validator with
                | Some v ->
                    let currentStake = match v.Stake with | Balance s -> s
                    let unstakeAmount = match amount with | Balance a -> a
                    if currentStake >= unstakeAmount then
                        let newStake = Balance (currentStake - unstakeAmount)
                        let updatedConsensus = updateValidatorStake consensus address newStake
                        (Success "Stake removed successfully", updatedConsensus)
                    else
                        (Failure "Insufficient stake", consensus)
                | None ->
                    (Failure "Validator not found", consensus)
            
            | Delegate (delegator, validatorAddress, amount) ->
                // Simple delegation - just update validator stake
                let validator = consensus.Validators |> List.tryFind (fun v -> v.Address = validatorAddress)
                match validator with
                | Some v ->
                    let newStake = Balance (match v.Stake with | Balance s -> s + match amount with | Balance a -> a)
                    let updatedConsensus = updateValidatorStake consensus validatorAddress newStake
                    (Success "Delegation successful", updatedConsensus)
                | None ->
                    (Failure "Validator not found", consensus)
            
            | Undelegate (delegator, validatorAddress, amount) ->
                // Simple undelegation - just update validator stake
                let validator = consensus.Validators |> List.tryFind (fun v -> v.Address = validatorAddress)
                match validator with
                | Some v ->
                    let currentStake = match v.Stake with | Balance s -> s
                    let undelegateAmount = match amount with | Balance a -> a
                    if currentStake >= undelegateAmount then
                        let newStake = Balance (currentStake - undelegateAmount)
                        let updatedConsensus = updateValidatorStake consensus validatorAddress newStake
                        (Success "Undelegation successful", updatedConsensus)
                    else
                        (Failure "Insufficient stake", consensus)
                | None ->
                    (Failure "Validator not found", consensus)
        with
        | ex -> (Failure (sprintf "Staking operation failed: %s" ex.Message), consensus)

    /// Calculate validator rewards
    let calculateValidatorRewards (validator: DPoSValidator) (block: Block) : Balance =
        let cfg = TrellisConfig.current
        let baseReward = Balance cfg.BlockReward
        let stakeMultiplier = 
            let stakeAmount = match validator.Stake with | Balance s -> s
            if stakeAmount > 0UL then
                float stakeAmount / 1000000000000000000.0 // Normalize to TRL
            else
                0.0
        
        let performanceMultiplier = validator.PerformanceScore / 100.0
        
        let totalMultiplier = stakeMultiplier * performanceMultiplier
        let rewardAmount = match baseReward with | Balance r -> r
        let finalReward = uint64 (float rewardAmount * totalMultiplier)
        
        Balance finalReward

    /// Calculate staking rewards
    let calculateStakingRewards (stake: Balance) (block: Block) : Balance =
        let cfg = TrellisConfig.current
        let baseReward = Balance (cfg.BlockReward / 2UL)
        let stakeAmount = match stake with | Balance s -> s
        let rewardAmount = match baseReward with | Balance r -> r
        
        let stakeMultiplier = 
            if stakeAmount > 0UL then
                float stakeAmount / 1000000000000000000.0 // Normalize to TRL
            else
                0.0
        
        let finalReward = uint64 (float rewardAmount * stakeMultiplier)
        Balance finalReward

    /// Get validator by address
    let getValidatorByAddress (consensus: DPoSConsensus) (address: Address) : DPoSValidator option =
        consensus.Validators |> List.tryFind (fun v -> v.Address = address)

    /// Get active validators
    let getActiveValidators (consensus: DPoSConsensus) : DPoSValidator list =
        consensus.ActiveValidators

    /// Get validator count
    let getValidatorCount (consensus: DPoSConsensus) : int =
        consensus.Validators.Length

    /// Get active validator count
    let getActiveValidatorCount (consensus: DPoSConsensus) : int =
        consensus.ActiveValidators.Length

    /// Check if validator is active
    let isValidatorActive (consensus: DPoSConsensus) (address: Address) : bool =
        consensus.ActiveValidators |> List.exists (fun v -> v.Address = address)

    /// Get total stake
    let getTotalStake (consensus: DPoSConsensus) : Balance =
        let totalStake = consensus.Validators |> List.sumBy (fun v -> match v.Stake with | Balance s -> s)
        Balance totalStake

    /// Get validator stake
    let getValidatorStake (consensus: DPoSConsensus) (address: Address) : Balance =
        match getValidatorByAddress consensus address with
        | Some validator -> validator.Stake
        | None -> Balance 0UL
