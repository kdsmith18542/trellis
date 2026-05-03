namespace Trellis.Beta

open System
open System.Collections.Generic
open SimpleTypes
open SimpleCrypto

/// Incentive system for institutional beta testnet
module IncentiveSystem =

    /// Incentive reward types
    type IncentiveReward =
        | BlockProductionReward of Balance
        | ConsensusParticipationReward of Balance
        | ComplianceReward of Balance
        | ReputationReward of Balance
        | StakingReward of Balance

    /// Incentive calculation parameters
    type IncentiveParameters = {
        BaseBlockReward: Balance
        ConsensusParticipationMultiplier: float
        ComplianceMultiplier: float
        ReputationMultiplier: float
        StakingMultiplier: float
        SlashingPenalty: Balance
        InactivityPenalty: Balance
    }

    /// Incentive system state
    type IncentiveSystemState = {
        TotalRewardsDistributed: Balance
        TotalPenaltiesApplied: Balance
        ActiveValidators: int
        RewardPool: Balance
        PenaltyPool: Balance
        LastDistribution: DateTime
    }

    /// Incentive system for corporate validators
    type CorporateIncentiveSystem(parameters: IncentiveParameters) =
        let mutable state: IncentiveSystemState = {
            TotalRewardsDistributed = Balance(0UL)
            TotalPenaltiesApplied = Balance(0UL)
            ActiveValidators = 0
            RewardPool = Balance(1000000UL) // 1M tokens initial pool
            PenaltyPool = Balance(0UL)
            LastDistribution = DateTime.UtcNow
        }

        member this.CalculateBlockProductionReward(validatorStake: Balance, participationLevel: string) : Balance =
            let (Balance stake) = validatorStake
            let baseReward = match participationLevel with
                            | "LeadValidator" -> 2000UL
                            | "ActiveValidator" -> 1500UL
                            | "Participant" -> 1000UL
                            | _ -> 500UL
            
            let stakeMultiplier = min 2.0 (float stake / 100000.0) // Max 2x for 100k+ stake
            let reward = uint64 (float baseReward * stakeMultiplier)
            Balance(reward)

        member this.CalculateConsensusReward(participationScore: float, reputation: float) : Balance =
            let baseReward = 500UL
            let participationMultiplier = min 2.0 participationScore
            let reputationMultiplier = min 2.0 reputation
            let reward = uint64 (float baseReward * participationMultiplier * reputationMultiplier)
            Balance(reward)

        member this.CalculateComplianceReward(complianceScore: float, kycStatus: string) : Balance =
            if kycStatus = "Verified" && complianceScore >= 0.8 then
                let baseReward = 1000UL
                let complianceMultiplier = complianceScore
                let reward = uint64 (float baseReward * complianceMultiplier)
                Balance(reward)
            else
                Balance(0UL)

        member this.CalculateReputationReward(reputation: float, slashingEvents: int) : Balance =
            if slashingEvents = 0 && reputation >= 0.9 then
                let baseReward = 2000UL
                let reputationMultiplier = reputation
                let reward = uint64 (float baseReward * reputationMultiplier)
                Balance(reward)
            else
                Balance(0UL)

        member this.CalculateStakingReward(stake: Balance, stakingDuration: TimeSpan) : Balance =
            let (Balance stakeAmount) = stake
            let baseReward = 100UL
            let durationMultiplier = min 3.0 (stakingDuration.TotalDays / 30.0) // Max 3x for 30+ days
            let stakeMultiplier = min 2.0 (float stakeAmount / 50000.0) // Max 2x for 50k+ stake
            let reward = uint64 (float baseReward * durationMultiplier * stakeMultiplier)
            Balance(reward)

        member this.ApplySlashingPenalty(validatorId: string, reason: string) : Balance =
            let penalty = match reason with
                         | "DoubleSign" -> Balance(10000UL)
                         | "Inactivity" -> Balance(5000UL)
                         | "ComplianceViolation" -> Balance(2000UL)
                         | _ -> Balance(1000UL)
            
            // Update penalty pool
            let (Balance currentPenalties) = state.PenaltyPool
            let (Balance penaltyAmount) = penalty
            state <- { state with PenaltyPool = Balance(currentPenalties + penaltyAmount) }
            
            penalty

        member this.DistributeRewards(validatorId: string, rewards: IncentiveReward list) : bool =
            try
                let totalReward = rewards |> List.fold (fun acc reward ->
                    match reward with
                    | BlockProductionReward b -> let (Balance accB) = acc; let (Balance bB) = b; Balance(accB + bB)
                    | ConsensusParticipationReward b -> let (Balance accB) = acc; let (Balance bB) = b; Balance(accB + bB)
                    | ComplianceReward b -> let (Balance accB) = acc; let (Balance bB) = b; Balance(accB + bB)
                    | ReputationReward b -> let (Balance accB) = acc; let (Balance bB) = b; Balance(accB + bB)
                    | StakingReward b -> let (Balance accB) = acc; let (Balance bB) = b; Balance(accB + bB)
                ) (Balance(0UL))
                
                // Update state
                let (Balance currentRewards) = state.TotalRewardsDistributed
                let (Balance totalRewardAmount) = totalReward
                state <- { state with TotalRewardsDistributed = Balance(currentRewards + totalRewardAmount) }
                
                printfn "Distributed rewards to validator %s: %d tokens" validatorId (match totalReward with | Balance b -> b)
                true
            with
            | ex -> 
                printfn "Failed to distribute rewards: %s" ex.Message
                false

        member this.GetIncentiveStats() : (Balance * Balance * int * Balance * Balance) =
            (state.TotalRewardsDistributed, state.TotalPenaltiesApplied, state.ActiveValidators, state.RewardPool, state.PenaltyPool)

        member this.GenerateIncentiveReport() : string =
            let (totalRewards, totalPenalties, activeValidators, rewardPool, penaltyPool) = this.GetIncentiveStats()
            
            sprintf """
            Incentive System Report
            ======================
            Total Rewards Distributed: %d tokens
            Total Penalties Applied: %d tokens
            Active Validators: %d
            Reward Pool: %d tokens
            Penalty Pool: %d tokens
            
            Average Reward per Validator: %.2f tokens
            """.Trim() 
                (match totalRewards with | Balance b -> b)
                (match totalPenalties with | Balance b -> b)
                activeValidators
                (match rewardPool with | Balance b -> b)
                (match penaltyPool with | Balance b -> b)
                (if activeValidators > 0 then 
                    float (match totalRewards with | Balance b -> b) / float activeValidators 
                 else 0.0)

    /// Testnet incentive coordinator
    type TestnetIncentiveCoordinator(incentiveSystem: CorporateIncentiveSystem) =
        
        member this.ProcessBlockRewards(validatorId: string, blockCount: int, stake: Balance, participationLevel: string) : bool =
            let blockReward = incentiveSystem.CalculateBlockProductionReward(stake, participationLevel)
            let consensusReward = incentiveSystem.CalculateConsensusReward(1.0, 1.0) // Full participation
            let rewards = [BlockProductionReward(blockReward); ConsensusParticipationReward(consensusReward)]
            incentiveSystem.DistributeRewards(validatorId, rewards)

        member this.ProcessComplianceRewards(validatorId: string, complianceScore: float, kycStatus: string) : bool =
            let complianceReward = incentiveSystem.CalculateComplianceReward(complianceScore, kycStatus)
            let reputationReward = incentiveSystem.CalculateReputationReward(1.0, 0) // High reputation, no slashing
            let rewards = [ComplianceReward(complianceReward); ReputationReward(reputationReward)]
            incentiveSystem.DistributeRewards(validatorId, rewards)

        member this.ProcessStakingRewards(validatorId: string, stake: Balance, stakingDuration: TimeSpan) : bool =
            let stakingReward = incentiveSystem.CalculateStakingReward(stake, stakingDuration)
            let rewards = [StakingReward(stakingReward)]
            incentiveSystem.DistributeRewards(validatorId, rewards)

        member this.ProcessSlashing(validatorId: string, reason: string) : Balance =
            incentiveSystem.ApplySlashingPenalty(validatorId, reason)

        member this.GenerateIncentiveReport() : string =
            incentiveSystem.GenerateIncentiveReport()

        member this.GetIncentiveStats() : (Balance * Balance * int * Balance * Balance) =
            incentiveSystem.GetIncentiveStats()
