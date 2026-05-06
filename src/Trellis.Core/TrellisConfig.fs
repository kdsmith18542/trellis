namespace Trellis.Core

open System

/// Centralized configuration for a Trellis blockchain node.
/// All network-specific parameters live here so developers can customize
/// their chain via appsettings.json instead of recompiling.
type TrellisConfig = {
    // Chain identity
    ChainId: uint32
    NetworkName: string

    // Block timing & difficulty
    TargetBlockTime: uint64
    RetargetInterval: uint64
    MinDifficulty: uint64
    MaxDifficulty: uint64
    GenesisDifficultyBase: uint64
    GenesisDifficultyNetworkFactor: uint64

    // Gas & fees
    GenesisBaseFeePerGas: uint64
    GenesisGasLimit: uint64
    BaseFeeIncreaseMultiplier: float
    BaseFeeDecreaseMultiplier: float

    // Genesis identity strings
    GenesisBlockData: string
    GenesisValidatorSeed: string
    GenesisPreviousHashSeed: string
    GenesisMerkleRootSeed: string

    // Supply & rewards
    InitialTotalSupply: uint64
    MaxSupply: uint64
    BlockReward: uint64
    HalvingInterval: uint64
    BurnRate: decimal
    StakingRewardRate: decimal

    // Governance
    MinProposalDeposit: uint64
    VotingPeriod: uint64
    ExecutionDelay: uint64
    QuorumThreshold: decimal
    PassThreshold: decimal
    MaxProposalsPerBlock: uint32
    ProposalLifetime: uint64

    // P2P
    P2PProtocolVersionMajor: uint16
    P2PProtocolVersionMinor: uint16
    P2PProtocolVersionPatch: uint16
    P2PMaxBucketSize: int
    P2PMaxBuckets: int
    P2PRateLimitWindowSeconds: float
    P2PMaxMessagesPerWindow: int
    P2PPeerTimeoutMinutes: float
    P2PPeerEvictionHealthThreshold: float
    P2PPeerEvictionMinInteractions: int
    P2PBlockTimeTolerancePastSeconds: float
    P2PBlockTimeToleranceFutureSeconds: float

    // API
    ApiListenUrl: string

    // Mempool
    MempoolMaxSize: int
    MaxTransactionsPerSender: int

    // Cutover
    CutoverMinHeight: uint64
    CutoverMinMedianTimeDays: float
    CutoverMinTotalDifficulty: uint64 option
}

module TrellisConfig =
    /// Default configuration matching the original hardcoded values.
    let defaultConfig : TrellisConfig = {
        ChainId = 1337u
        NetworkName = "trellis-dev"
        TargetBlockTime = 600UL
        RetargetInterval = 2016UL
        MinDifficulty = 1UL
        MaxDifficulty = 0x1d00ffffUL
        GenesisDifficultyBase = 1000UL
        GenesisDifficultyNetworkFactor = 1UL
        GenesisBaseFeePerGas = 1_000_000_000UL
        GenesisGasLimit = 30_000_000UL
        BaseFeeIncreaseMultiplier = 1.125
        BaseFeeDecreaseMultiplier = 0.875
        GenesisBlockData = "Trellis Genesis Block - Enterprise Blockchain"
        GenesisValidatorSeed = "Trellis Genesis Validator"
        GenesisPreviousHashSeed = "GENESIS_NULL_HASH"
        GenesisMerkleRootSeed = "GENESIS_TRANSACTION_ROOT"
        InitialTotalSupply = 10_000_000_000_000_000_000UL
        MaxSupply = 18_000_000_000_000_000_000UL
        BlockReward = 2_000_000_000_000_000_000UL
        HalvingInterval = 210_000UL
        BurnRate = 0.01m
        StakingRewardRate = 0.05m
        MinProposalDeposit = 1_000_000_000_000_000_000UL
        VotingPeriod = 10080UL
        ExecutionDelay = 1440UL
        QuorumThreshold = 0.2m
        PassThreshold = 0.5m
        MaxProposalsPerBlock = 10u
        ProposalLifetime = 20160UL
        P2PProtocolVersionMajor = 1us
        P2PProtocolVersionMinor = 0us
        P2PProtocolVersionPatch = 0us
        P2PMaxBucketSize = 20
        P2PMaxBuckets = 160
        P2PRateLimitWindowSeconds = 60.0
        P2PMaxMessagesPerWindow = 100
        P2PPeerTimeoutMinutes = 10.0
        P2PPeerEvictionHealthThreshold = 0.2
        P2PPeerEvictionMinInteractions = 10
        P2PBlockTimeTolerancePastSeconds = 1800.0
        P2PBlockTimeToleranceFutureSeconds = 3600.0
        ApiListenUrl = "http://localhost:5000"
        MempoolMaxSize = 10000
        MaxTransactionsPerSender = 100
        CutoverMinHeight = 1_500_000UL
        CutoverMinMedianTimeDays = 180.0
        CutoverMinTotalDifficulty = None
    }

    /// Mutable global config. Set once at startup by ConfigLoader.
    let mutable current = defaultConfig
