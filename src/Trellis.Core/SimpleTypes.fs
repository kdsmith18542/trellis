namespace Trellis.Core

open System

module SimpleTypes =

    type Hash = Hash of byte[]
        with member this.ToByteArray() = let (Hash bytes) = this in bytes
    type Address = Address of byte[]
        with member this.ToByteArray() = let (Address bytes) = this in bytes
    type PublicKey = PublicKey of byte[]
        with member this.ToByteArray() = let (PublicKey bytes) = this in bytes
    type PrivateKey = PrivateKey of byte[]
        with member this.ToByteArray() = let (PrivateKey bytes) = this in bytes
    type Signature = Signature of byte[]
        with member this.ToByteArray() = let (Signature bytes) = this in bytes
    type Balance = Balance of uint64
    type Nonce = Nonce of uint64
    type BlockHeight = BlockHeight of uint64
    type Timestamp = Timestamp of DateTime

    type Transaction = {
        From: Address
        To: Address
        Amount: Balance
        Fee: Balance
        Nonce: Nonce
        Signature: Signature
        Hash: Hash
        GasLimit: uint64
        GasPrice: uint64
        Data: byte[]
        Metadata: Map<string, string>
        // EIP-1559 fields (optional for backward compatibility)
        MaxFeePerGas: uint64 option
        MaxPriorityFeePerGas: uint64 option
        TransactionType: uint8 // 0 = Legacy, 2 = EIP-1559
    }

    type BlockHeader = {
        Height: BlockHeight
        PreviousHash: Hash
        Timestamp: Timestamp
        Nonce: uint64
        Difficulty: uint64
        PoWHash: Hash
        Finalized: bool
        Hash: Hash
        MerkleRoot: Hash
        Validator: Address
        BaseFeePerGas: uint64
        GasUsed: uint64
        GasLimit: uint64
    }

    type Block = {
        Header: BlockHeader
        Transactions: Transaction list
    }

    /// Log entry for smart contract events
    type LogEntry = {
        Address: Address
        Topics: Hash list
        Data: byte[]
    }

    /// Execution receipt for transaction processing
    type ExecutionReceipt = {
        TransactionHash: Hash
        Status: bool  // true = success, false = failure
        GasUsed: uint64
        Logs: LogEntry list
        BlockHash: Hash
        BlockHeight: BlockHeight
        TransactionIndex: uint32
    }

    type Account = {
        Address: Address
        Balance: Balance
        Nonce: Nonce
    }

    type ChainState = {
        Accounts: Map<Address, Account>
        Blocks: Block list
        CurrentHeight: BlockHeight
        LastBlockHash: Hash
        Validators: Address list
        TotalSupply: Balance
        BaseFeePerGas: uint64
        GasUsed: uint64
        GasLimit: uint64
    }

    /// Multi-signature status
    type MultiSigStatus =
        | Pending
        | PartiallySigned
        | FullySigned
        | Executed
        | Rejected
        | Expired

    /// Multi-signature configuration
    type MultiSigConfig = {
        RequiredSignatures: uint32
        TotalSignatures: uint32
        Signers: Address list
        IsActive: bool
        CreatedAt: Timestamp
        UpdatedAt: Timestamp option
    }

    /// Multi-signature transaction
    type MultiSigTransaction = {
        TransactionId: Hash
        From: Address
        To: Address
        Amount: Balance
        Fee: Balance
        Timestamp: Timestamp
        Signatures: Map<Address, Signature>
        Status: MultiSigStatus
        Metadata: Map<string, string> option
    }

    /// Multi-signature wallet
    type MultiSigWallet = {
        Address: Address
        Config: MultiSigConfig
        Balance: Balance
        PendingTransactions: MultiSigTransaction list
        ExecutedTransactions: MultiSigTransaction list
    }

    /// Time-lock types
    type TimeLockType =
        | Absolute of Timestamp
        | Relative of TimeSpan
        | AtHeight of BlockHeight
        | Conditional of string

    /// Time-lock status
    type TimeLockStatus =
        | Pending
        | Locked
        | Unlocked
        | Executed
        | Expired
        | Cancelled

    /// Time-locked transaction
    type TimeLockedTransaction = {
        TransactionId: Hash
        From: Address
        ToAddress: Address
        Amount: Balance
        Fee: Balance
        TimeLock: TimeLockType
        CreatedAt: Timestamp
        Status: TimeLockStatus
        Metadata: Map<string, string> option
    }

    /// Time-lock wallet
    type TimeLockWallet = {
        Address: Address
        LockedTransactions: TimeLockedTransaction list
        ExecutedTransactions: TimeLockedTransaction list
        Balance: Balance
    }

    /// Enterprise wallet types
    type EnterpriseWalletType =
        | Corporate
        | Institutional
        | Treasury
        | Escrow
        | Compliance

    /// Enterprise wallet configuration
    type EnterpriseWalletConfig = {
        WalletType: EnterpriseWalletType
        CompanyName: string
        ComplianceLevel: uint32
        MaxTransactionAmount: Balance
        DailyLimit: Balance
        RequiredApprovals: uint32
        KYCRequired: bool
        AuditTrailEnabled: bool
        CreatedAt: Timestamp
        UpdatedAt: Timestamp option
    }

    /// Enterprise wallet
    type EnterpriseWallet = {
        Address: Address
        Config: EnterpriseWalletConfig
        MultiSigWallet: MultiSigWallet option
        TimeLockWallet: TimeLockWallet option
        Balance: Balance
        TransactionHistory: (MultiSigTransaction list * TimeLockedTransaction list)
        ComplianceReports: Map<string, string>
        LastAudit: Timestamp option
    }

    /// Enterprise transaction request
    type EnterpriseTransactionRequest = {
        RequestId: Hash
        From: Address
        To: Address
        Amount: Balance
        Fee: Balance
        Purpose: string
        Requester: Address
        Approvers: Address list
        Priority: uint32
        ExpiresAt: Timestamp option
        Metadata: Map<string, string>
        CreatedAt: Timestamp
    }

    /// Enterprise transaction approval
    type EnterpriseTransactionApproval = {
        RequestId: Hash
        Approver: Address
        Approved: bool
        Comments: string option
        Timestamp: Timestamp
        Signature: Signature
    }

    /// Enterprise wallet statistics
    type EnterpriseWalletStatistics = {
        Balance: uint64
        TotalTransactions: int
        PendingTransactions: int
        WalletType: EnterpriseWalletType
        ComplianceLevel: uint32
        LastAudit: Timestamp option
    }

    /// Compliance audit types
    type ComplianceAuditType =
        | TransactionAudit
        | WalletAudit
        | RegulatoryAudit
        | SecurityAudit
        | OperationalAudit

    /// Compliance audit record
    type ComplianceAuditRecord = {
        AuditId: Hash
        AuditType: ComplianceAuditType
        TargetAddress: Address
        Auditor: Address
        Findings: Map<string, string>
        ComplianceScore: uint32
        Recommendations: string list
        Timestamp: Timestamp
        Status: string
    }

    /// Compliance report
    type ComplianceReport = {
        ReportId: Hash
        WalletAddress: Address
        ReportType: string
        GeneratedAt: Timestamp
        Data: Map<string, string>
        ComplianceLevel: uint32
        Violations: string list
        Recommendations: string list
    }

    /// Regulatory compliance check
    type RegulatoryComplianceCheck = {
        CheckId: Hash
        Regulation: string
        CheckType: string
        Required: bool
        Passed: bool
        Details: string
        Timestamp: Timestamp
    }

    /// Staking information
    type StakingInfo = {
        Address: Address
        StakedAmount: Balance
        StakingStartTime: Timestamp
        UnlockTime: Timestamp
        RewardsEarned: Balance
        IsActive: bool
        PublicKey: PublicKey
        Stake: Balance
        Commission: uint64
        LastVoteHeight: BlockHeight
        SlashCount: uint64
    }

    /// Staking rewards calculation
    type StakingRewards = {
        StakerAddress: Address
        RewardAmount: Balance
        StakingDuration: TimeSpan
        AnnualRate: decimal
        CalculatedAt: Timestamp
    }

    /// Fee burning information
    type FeeBurning = {
        TransactionId: Hash
        BurnedAmount: Balance
        BurnRate: decimal
        BurnedAt: Timestamp
    }

    /// Token economics parameters
    type TokenEconomics = {
        MaxSupply: Balance
        InitialSupply: Balance
        BlockReward: Balance
        HalvingInterval: uint32
        StakingRewardRate: decimal
        BurnRate: decimal
    }

    /// gRPC Request/Response types
    type GetBalanceRequest = {
        Address: Address
    }

    type GetBalanceResponse = {
        Balance: Balance
        Success: bool
        ErrorMessage: string option
    }

    type GetAccountRequest = {
        Address: Address
    }

    type GetAccountResponse = {
        Account: Account option
        Found: bool
        ErrorMessage: string option
    }

    type SendTransactionRequest = {
        From: Address
        To: Address
        Amount: Balance
        Fee: Balance
        Nonce: Nonce
        Signature: Signature
    }

    type SendTransactionResponse = {
        Success: bool
        TransactionHash: Hash option
        ErrorMessage: string option
    }

    type GetBlockRequest = {
        BlockHash: Hash option
        BlockHeight: BlockHeight option
    }

    type GetBlockResponse = {
        Block: Block option
        Found: bool
        ErrorMessage: string option
    }

    type SubmitBlockRequest = {
        Block: Block
    }

    type SubmitBlockResponse = {
        Accepted: bool
        ErrorMessage: string option
    }

    type GetValidatorsRequest = {
        IncludeInactive: bool
    }

    type GetValidatorsResponse = {
        Validators: Address list
        TotalCount: uint32
        ErrorMessage: string option
    }

    type GetChainStateRequest = {
        IncludeStatistics: bool
    }

    type GetChainStateResponse = {
        CurrentHeight: BlockHeight
        LastBlockHash: Hash
        ValidatorCount: uint32
        TotalSupply: Balance
        ErrorMessage: string option
    }

    type BlockUpdate = {
        Block: Block
        UpdateType: string
        Timestamp: Timestamp
        BlockHeight: BlockHeight
        IsNew: bool
    }

    type TransactionUpdate = {
        Transaction: Transaction
        UpdateType: string
        Timestamp: Timestamp
        BlockHeight: BlockHeight
        IsNew: bool
    }