namespace Trellis.Core

open System
open SimpleTypes
open SimpleDPoS
open DPoSGovernance

/// DPoS Tokenomics system for Trellis
module DPoSTokenomics =

    /// Token supply parameters
    type TokenSupply = {
        MaxSupply: Balance
        CurrentSupply: Balance
        CirculatingSupply: Balance
        StakedSupply: Balance
        TreasurySupply: Balance
        BurnedSupply: Balance
    }

    /// Treasury funding sources
    type TreasuryFundingSource =
        | BlockRewards of Balance
        | TransactionFees of Balance
        | GovernanceDeposits of Balance
        | PenaltyFees of Balance
        | ExternalDonations of Balance

    /// Treasury spending status
    type TreasurySpendingStatus =
        | Pending
        | Approved
        | Executed
        | Rejected

    /// Treasury spending records
    type TreasurySpending = {
        Id: Hash
        Amount: Balance
        Purpose: string
        Recipient: Address
        ApprovedBy: Hash // Proposal ID
        Timestamp: Timestamp
        Status: TreasurySpendingStatus
    }

    /// Treasury system
    type Treasury = {
        Balance: Balance
        FundingSources: TreasuryFundingSource list
        SpendingHistory: TreasurySpending list
        GovernanceReserve: Balance
        DevelopmentReserve: Balance
        EmergencyReserve: Balance
    }

    /// Token economics parameters
    type TokenEconomics = {
        InitialSupply: Balance
        MaxSupply: Balance
        InflationRate: decimal // Annual inflation rate
        StakingRewardRate: decimal // Annual staking reward rate
        ValidatorRewardRate: decimal // Annual validator reward rate
        TreasuryRate: decimal // Percentage of rewards to treasury
        BurnRate: decimal // Percentage of fees burned
        HalvingInterval: uint64 // Blocks between halvings
        LastHalving: BlockHeight
        NextHalving: BlockHeight
    }

    /// Staking rewards calculation
    type StakingRewards = {
        ValidatorRewards: Balance
        DelegatorRewards: Balance
        TotalRewards: Balance
        RewardRate: decimal
        LastRewardBlock: BlockHeight
    }

    /// Inflation record
    type InflationRecord = {
        BlockHeight: BlockHeight
        InflationRate: decimal
        NewTokens: Balance
        TotalSupply: Balance
        Timestamp: Timestamp
    }

    /// Tokenomics state
    type TokenomicsState = {
        TokenSupply: TokenSupply
        Economics: TokenEconomics
        Treasury: Treasury
        StakingRewards: StakingRewards
        LastUpdateBlock: BlockHeight
        InflationHistory: InflationRecord list
    }

    /// Tokenomics result
    type TokenomicsResult =
        | Success of string
        | Failure of string

    /// Default tokenomics parameters (read from global config)
    let defaultTokenomics : TokenEconomics =
        let cfg = TrellisConfig.current
        {
            InitialSupply = Balance cfg.InitialTotalSupply
            MaxSupply = Balance cfg.MaxSupply
            InflationRate = 0.05m // 5% annual inflation
            StakingRewardRate = cfg.StakingRewardRate
            ValidatorRewardRate = 0.12m // 12% annual validator rewards
            TreasuryRate = 0.10m // 10% of rewards to treasury
            BurnRate = cfg.BurnRate
            HalvingInterval = cfg.HalvingInterval
            LastHalving = BlockHeight 0UL
            NextHalving = BlockHeight cfg.HalvingInterval
        }

    /// Initialize tokenomics state
    let initTokenomicsState (economics: TokenEconomics) : TokenomicsState =
        {
            TokenSupply = {
                MaxSupply = economics.MaxSupply
                CurrentSupply = economics.InitialSupply
                CirculatingSupply = economics.InitialSupply
                StakedSupply = Balance 0UL
                TreasurySupply = Balance 0UL
                BurnedSupply = Balance 0UL
            }
            Economics = economics
            Treasury = {
                Balance = Balance 0UL
                FundingSources = []
                SpendingHistory = []
                GovernanceReserve = Balance 0UL
                DevelopmentReserve = Balance 0UL
                EmergencyReserve = Balance 0UL
            }
            StakingRewards = {
                ValidatorRewards = Balance 0UL
                DelegatorRewards = Balance 0UL
                TotalRewards = Balance 0UL
                RewardRate = economics.StakingRewardRate
                LastRewardBlock = BlockHeight 0UL
            }
            LastUpdateBlock = BlockHeight 0UL
            InflationHistory = []
        }

    /// Calculate block rewards for DPoS
    let calculateBlockRewards (state: TokenomicsState) (blockHeight: BlockHeight) : Balance =
        let (BlockHeight height) = blockHeight
        let (BlockHeight lastHalving) = state.Economics.LastHalving
        let (BlockHeight nextHalving) = state.Economics.NextHalving
        
        // Check if halving should occur
        if height >= nextHalving then
            // Halving occurred - reduce rewards by half
            let newRewardRate = state.Economics.InflationRate / 2.0m
            let (Balance initialAmount) = state.Economics.InitialSupply
            let baseReward = Balance (uint64 (decimal initialAmount * newRewardRate / 365.0m / 24.0m / 360.0m)) // Daily reward / blocks per day
            baseReward
        else
            // Normal reward calculation
            let (Balance initialAmount) = state.Economics.InitialSupply
            let baseReward = Balance (uint64 (decimal initialAmount * state.Economics.InflationRate / 365.0m / 24.0m / 360.0m))
            baseReward

    /// Distribute block rewards
    let distributeBlockRewards (state: TokenomicsState) (blockHeight: BlockHeight) (validator: Address) (delegators: Address list) : TokenomicsResult * TokenomicsState =
        try
            let blockReward = calculateBlockRewards state blockHeight
            let (Balance rewardAmount) = blockReward
            
            // Calculate distribution
            let treasuryAmount = Balance (uint64 (decimal rewardAmount * state.Economics.TreasuryRate))
            let validatorAmount = Balance (uint64 (decimal rewardAmount * state.Economics.ValidatorRewardRate))
            let delegatorAmount = Balance (rewardAmount - match treasuryAmount with | Balance ta -> ta - match validatorAmount with | Balance va -> va)
            
            // Update token supply
            let newCurrentSupply = Balance (match state.TokenSupply.CurrentSupply with | Balance cs -> cs + rewardAmount)
            let newCirculatingSupply = Balance (match state.TokenSupply.CirculatingSupply with | Balance cs -> cs + rewardAmount)
            
            // Update treasury
            let newTreasuryBalance = Balance (match state.Treasury.Balance with | Balance tb -> tb + match treasuryAmount with | Balance ta -> ta)
            let treasuryFunding = BlockRewards treasuryAmount
            let newFundingSources = treasuryFunding :: state.Treasury.FundingSources
            
            // Update staking rewards
            let newValidatorRewards = Balance (match state.StakingRewards.ValidatorRewards with | Balance vr -> vr + match validatorAmount with | Balance va -> va)
            let newDelegatorRewards = Balance (match state.StakingRewards.DelegatorRewards with | Balance dr -> dr + match delegatorAmount with | Balance da -> da)
            let newTotalRewards = Balance (match newValidatorRewards with | Balance vr -> vr + match newDelegatorRewards with | Balance dr -> dr)
            
            let updatedState = {
                state with
                    TokenSupply = {
                        state.TokenSupply with
                            CurrentSupply = newCurrentSupply
                            CirculatingSupply = newCirculatingSupply
                    }
                    Treasury = {
                        state.Treasury with
                            Balance = newTreasuryBalance
                            FundingSources = newFundingSources
                    }
                    StakingRewards = {
                        state.StakingRewards with
                            ValidatorRewards = newValidatorRewards
                            DelegatorRewards = newDelegatorRewards
                            TotalRewards = newTotalRewards
                            LastRewardBlock = blockHeight
                    }
                    LastUpdateBlock = blockHeight
            }
            
            (Success "Block rewards distributed", updatedState)
        with
        | ex -> (Failure (sprintf "Failed to distribute block rewards: %s" ex.Message), state)

    /// Process transaction fees
    let processTransactionFees (state: TokenomicsState) (fees: Balance) : TokenomicsResult * TokenomicsState =
        try
            let (Balance feeAmount) = fees
            
            // Calculate burn amount
            let burnAmount = Balance (uint64 (decimal feeAmount * state.Economics.BurnRate))
            let treasuryAmount = Balance (feeAmount - match burnAmount with | Balance ba -> ba)
            
            // Update supply
            let newCurrentSupply = Balance (match state.TokenSupply.CurrentSupply with | Balance cs -> cs - match burnAmount with | Balance ba -> ba)
            let newBurnedSupply = Balance (match state.TokenSupply.BurnedSupply with | Balance bs -> bs + match burnAmount with | Balance ba -> ba)
            
            // Update treasury
            let newTreasuryBalance = Balance (match state.Treasury.Balance with | Balance tb -> tb + match treasuryAmount with | Balance ta -> ta)
            let treasuryFunding = TransactionFees treasuryAmount
            let newFundingSources = treasuryFunding :: state.Treasury.FundingSources
            
            let updatedState = {
                state with
                    TokenSupply = {
                        state.TokenSupply with
                            CurrentSupply = newCurrentSupply
                            BurnedSupply = newBurnedSupply
                    }
                    Treasury = {
                        state.Treasury with
                            Balance = newTreasuryBalance
                            FundingSources = newFundingSources
                    }
            }
            
            (Success "Transaction fees processed", updatedState)
        with
        | ex -> (Failure (sprintf "Failed to process transaction fees: %s" ex.Message), state)

    /// Execute treasury spending
    let executeTreasurySpending (state: TokenomicsState) (amount: Balance) (recipient: Address) (purpose: string) (proposalId: Hash) : TokenomicsResult * TokenomicsState =
        try
            let (Balance spendingAmount) = amount
            let (Balance treasuryBalance) = state.Treasury.Balance
            
            if spendingAmount > treasuryBalance then
                (Failure "Insufficient treasury funds", state)
            else
                let spendingId = Hash (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proposalId.ToString() + purpose + DateTime.UtcNow.ToString())))
                
                let treasurySpending = {
                    Id = spendingId
                    Amount = amount
                    Purpose = purpose
                    Recipient = recipient
                    ApprovedBy = proposalId
                    Timestamp = Timestamp DateTime.UtcNow
                    Status = Executed
                }
                
                let newTreasuryBalance = Balance (treasuryBalance - spendingAmount)
                let newSpendingHistory = treasurySpending :: state.Treasury.SpendingHistory
                
                let updatedState = {
                    state with
                        Treasury = {
                            state.Treasury with
                                Balance = newTreasuryBalance
                                SpendingHistory = newSpendingHistory
                        }
                }
                
                (Success "Treasury spending executed", updatedState)
        with
        | ex -> (Failure (sprintf "Failed to execute treasury spending: %s" ex.Message), state)

    /// Update staking supply
    let updateStakingSupply (state: TokenomicsState) (stakedAmount: Balance) : TokenomicsState =
        let updatedSupply = {
            state.TokenSupply with
                StakedSupply = stakedAmount
        }
        { state with TokenSupply = updatedSupply }

    /// Check halving conditions
    let checkHalving (state: TokenomicsState) (blockHeight: BlockHeight) : TokenomicsState =
        let (BlockHeight height) = blockHeight
        let (BlockHeight nextHalving) = state.Economics.NextHalving
        
        if height >= nextHalving then
            // Halving occurred
            let newLastHalving = state.Economics.NextHalving
            let newNextHalving = BlockHeight (match newLastHalving with | BlockHeight lh -> lh + state.Economics.HalvingInterval)
            let newInflationRate = state.Economics.InflationRate / 2.0m
            
            let updatedEconomics = {
                state.Economics with
                    LastHalving = newLastHalving
                    NextHalving = newNextHalving
                    InflationRate = newInflationRate
            }
            
            let inflationRecord = {
                BlockHeight = blockHeight
                InflationRate = newInflationRate
                NewTokens = Balance 0UL // Would calculate actual new tokens
                TotalSupply = state.TokenSupply.CurrentSupply
                Timestamp = Timestamp DateTime.UtcNow
            }
            
            {
                state with
                    Economics = updatedEconomics
                    InflationHistory = inflationRecord :: state.InflationHistory
            }
        else
            state

    /// Get tokenomics statistics
    let getTokenomicsStats (state: TokenomicsState) : Map<string, obj> =
        let inflationRate = state.Economics.InflationRate
        let stakingRewardRate = state.Economics.StakingRewardRate
        let validatorRewardRate = state.Economics.ValidatorRewardRate
        let treasuryBalance = state.Treasury.Balance
        let totalRewards = state.StakingRewards.TotalRewards
        
        Map.empty
            .Add("InflationRate", inflationRate :> obj)
            .Add("StakingRewardRate", stakingRewardRate :> obj)
            .Add("ValidatorRewardRate", validatorRewardRate :> obj)
            .Add("TreasuryBalance", treasuryBalance :> obj)
            .Add("TotalRewards", totalRewards :> obj)
            .Add("CurrentSupply", state.TokenSupply.CurrentSupply :> obj)
            .Add("CirculatingSupply", state.TokenSupply.CirculatingSupply :> obj)
            .Add("StakedSupply", state.TokenSupply.StakedSupply :> obj)
            .Add("BurnedSupply", state.TokenSupply.BurnedSupply :> obj)