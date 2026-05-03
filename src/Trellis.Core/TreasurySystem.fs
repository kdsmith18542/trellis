namespace Trellis.Core

open System
open SimpleTypes
open SimpleDPoS
open DPoSGovernance
open DPoSTokenomics

/// Treasury System for Trellis DPoS
module TreasurySystem =

    /// Treasury operation types
    type TreasuryOperation =
        | FundTreasury of Balance * string // Amount, source description
        | SpendFromTreasury of Balance * Address * string * Hash // Amount, recipient, purpose, proposal ID
        | AllocateReserve of Balance * string * string // Amount, reserve type, purpose
        | EmergencyWithdrawal of Balance * Address * string // Amount, recipient, reason

    /// Treasury operation status
    type TreasuryOperationStatus =
        | Pending
        | Approved
        | Executed
        | Rejected
        | Failed

    /// Treasury audit record
    type TreasuryAuditRecord = {
        Id: Hash
        Operation: TreasuryOperation
        PreviousBalance: Balance
        NewBalance: Balance
        Executor: Address
        Timestamp: Timestamp
        BlockHeight: BlockHeight
        Status: TreasuryOperationStatus
    }

    /// Treasury allocation categories
    type TreasuryAllocation = {
        Category: string
        AllocatedAmount: Balance
        SpentAmount: Balance
        RemainingAmount: Balance
        LastAllocation: Timestamp
        LastSpending: Timestamp option
    }

    /// Treasury budget
    type TreasuryBudget = {
        TotalBudget: Balance
        AllocatedBudget: Balance
        AvailableBudget: Balance
        Categories: Map<string, TreasuryAllocation>
        BudgetPeriod: uint64 // Blocks
        LastReset: BlockHeight
        NextReset: BlockHeight
    }

    /// Treasury policy
    type TreasuryPolicy = {
        MaxSpendingPerProposal: Balance
        MaxSpendingPerBlock: Balance
        EmergencyThreshold: Balance
        ReserveAllocationRate: decimal // Percentage of treasury for reserves
        DevelopmentAllocationRate: decimal // Percentage for development
        GovernanceAllocationRate: decimal // Percentage for governance
        EmergencyAllocationRate: decimal // Percentage for emergency
    }

    /// Treasury state
    type TreasuryState = {
        Treasury: Treasury
        Budget: TreasuryBudget
        Policy: TreasuryPolicy
        Operations: TreasuryOperation list
        LastUpdateBlock: BlockHeight
        AuditTrail: TreasuryAuditRecord list
    }

    /// Treasury result
    type TreasuryResult =
        | Success of string
        | Failure of string

    /// Default treasury policy
    let defaultTreasuryPolicy : TreasuryPolicy = {
        MaxSpendingPerProposal = Balance 1000000000000000000UL // 1 TRL
        MaxSpendingPerBlock = Balance 10000000000000000000UL // 10 TRL
        EmergencyThreshold = Balance 10000000000000000000UL // 10 TRL
        ReserveAllocationRate = 0.30m // 30% for reserves
        DevelopmentAllocationRate = 0.25m // 25% for development
        GovernanceAllocationRate = 0.25m // 25% for governance
        EmergencyAllocationRate = 0.20m // 20% for emergency
    }

    /// Initialize treasury state
    let initTreasuryState (treasury: Treasury) (policy: TreasuryPolicy) : TreasuryState =
        {
            Treasury = treasury
            Budget = {
                TotalBudget = treasury.Balance
                AllocatedBudget = Balance 0UL
                AvailableBudget = treasury.Balance
                Categories = Map.empty
                BudgetPeriod = 10080UL // ~1 week
                LastReset = BlockHeight 0UL
                NextReset = BlockHeight 10080UL
            }
            Policy = policy
            Operations = []
            LastUpdateBlock = BlockHeight 0UL
            AuditTrail = []
        }

    /// Fund treasury from tokenomics
    let fundTreasury (state: TreasuryState) (amount: Balance) (source: string) : TreasuryResult * TreasuryState =
        try
            let (Balance fundingAmount) = amount
            let (Balance currentBalance) = state.Treasury.Balance
            let newBalance = Balance (currentBalance + fundingAmount)
            
            let operation = FundTreasury (amount, source)
            let updatedOperations = operation :: state.Operations
            
            let updatedTreasury = {
                state.Treasury with
                    Balance = newBalance
            }
            
            let updatedBudget = {
                state.Budget with
                    TotalBudget = newBalance
                    AvailableBudget = newBalance
            }
            
            let auditRecord = {
                Id = Hash (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source + DateTime.UtcNow.ToString())))
                Operation = operation
                PreviousBalance = state.Treasury.Balance
                NewBalance = newBalance
                Executor = Address [||] // System operation
                Timestamp = Timestamp DateTime.UtcNow
                BlockHeight = state.LastUpdateBlock
                Status = Executed
            }
            
            let updatedState = {
                state with
                    Treasury = updatedTreasury
                    Budget = updatedBudget
                    Operations = updatedOperations
                    AuditTrail = auditRecord :: state.AuditTrail
            }
            
            (Success "Treasury funded successfully", updatedState)
        with
        | ex -> (Failure (sprintf "Failed to fund treasury: %s" ex.Message), state)

    /// Spend from treasury (governance approved)
    let spendFromTreasury (state: TreasuryState) (amount: Balance) (recipient: Address) (purpose: string) (proposalId: Hash) : TreasuryResult * TreasuryState =
        try
            let (Balance spendingAmount) = amount
            let (Balance currentBalance) = state.Treasury.Balance
            
            // Check spending limits
            if spendingAmount > currentBalance then
                (Failure "Insufficient treasury funds", state)
            elif spendingAmount > (match state.Policy.MaxSpendingPerProposal with | Balance max -> max) then
                (Failure "Spending exceeds maximum per proposal", state)
            else
                let newBalance = Balance (currentBalance - spendingAmount)
                let operation = SpendFromTreasury (amount, recipient, purpose, proposalId)
                let updatedOperations = operation :: state.Operations
                
                let updatedTreasury = {
                    state.Treasury with
                        Balance = newBalance
                }
                
                let updatedBudget = {
                    state.Budget with
                        TotalBudget = newBalance
                        AvailableBudget = newBalance
                }
                
                let auditRecord = {
                    Id = Hash (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proposalId.ToString() + purpose + DateTime.UtcNow.ToString())))
                    Operation = operation
                    PreviousBalance = state.Treasury.Balance
                    NewBalance = newBalance
                    Executor = Address [||] // Governance execution
                    Timestamp = Timestamp DateTime.UtcNow
                    BlockHeight = state.LastUpdateBlock
                    Status = Executed
                }
                
                let updatedState = {
                    state with
                        Treasury = updatedTreasury
                        Budget = updatedBudget
                        Operations = updatedOperations
                        AuditTrail = auditRecord :: state.AuditTrail
                }
                
                (Success "Treasury spending executed", updatedState)
        with
        | ex -> (Failure (sprintf "Failed to spend from treasury: %s" ex.Message), state)

    /// Allocate treasury reserves
    let allocateReserve (state: TreasuryState) (amount: Balance) (reserveType: string) (purpose: string) : TreasuryResult * TreasuryState =
        try
            let (Balance allocationAmount) = amount
            let (Balance currentBalance) = state.Treasury.Balance
            
            if allocationAmount > currentBalance then
                (Failure "Insufficient treasury funds for allocation", state)
            else
                let operation = AllocateReserve (amount, reserveType, purpose)
                let updatedOperations = operation :: state.Operations
                
                // Update specific reserve
                let updatedReserve = 
                    match reserveType.ToLower() with
                    | "governance" -> { state.Treasury with GovernanceReserve = Balance (match state.Treasury.GovernanceReserve with | Balance gr -> gr + allocationAmount) }
                    | "development" -> { state.Treasury with DevelopmentReserve = Balance (match state.Treasury.DevelopmentReserve with | Balance dr -> dr + allocationAmount) }
                    | "emergency" -> { state.Treasury with EmergencyReserve = Balance (match state.Treasury.EmergencyReserve with | Balance er -> er + allocationAmount) }
                    | _ -> state.Treasury
                
                let newBalance = Balance (currentBalance - allocationAmount)
                let updatedTreasury = { updatedReserve with Balance = newBalance }
                
                let auditRecord = {
                    Id = Hash (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reserveType + purpose + DateTime.UtcNow.ToString())))
                    Operation = operation
                    PreviousBalance = state.Treasury.Balance
                    NewBalance = newBalance
                    Executor = Address [||] // System operation
                    Timestamp = Timestamp DateTime.UtcNow
                    BlockHeight = state.LastUpdateBlock
                    Status = Executed
                }
                
                let updatedState = {
                    state with
                        Treasury = updatedTreasury
                        Operations = updatedOperations
                        AuditTrail = auditRecord :: state.AuditTrail
                }
                
                (Success "Reserve allocated successfully", updatedState)
        with
        | ex -> (Failure (sprintf "Failed to allocate reserve: %s" ex.Message), state)

    /// Emergency withdrawal
    let emergencyWithdrawal (state: TreasuryState) (amount: Balance) (recipient: Address) (reason: string) : TreasuryResult * TreasuryState =
        try
            let (Balance withdrawalAmount) = amount
            let (Balance currentBalance) = state.Treasury.Balance
            
            // Check emergency threshold
            if withdrawalAmount > (match state.Policy.EmergencyThreshold with | Balance threshold -> threshold) then
                (Failure "Withdrawal exceeds emergency threshold", state)
            elif withdrawalAmount > currentBalance then
                (Failure "Insufficient treasury funds", state)
            else
                let newBalance = Balance (currentBalance - withdrawalAmount)
                let operation = EmergencyWithdrawal (amount, recipient, reason)
                let updatedOperations = operation :: state.Operations
                
                let updatedTreasury = {
                    state.Treasury with
                        Balance = newBalance
                }
                
                let auditRecord = {
                    Id = Hash (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reason + DateTime.UtcNow.ToString())))
                    Operation = operation
                    PreviousBalance = state.Treasury.Balance
                    NewBalance = newBalance
                    Executor = Address [||] // Emergency system
                    Timestamp = Timestamp DateTime.UtcNow
                    BlockHeight = state.LastUpdateBlock
                    Status = Executed
                }
                
                let updatedState = {
                    state with
                        Treasury = updatedTreasury
                        Operations = updatedOperations
                        AuditTrail = auditRecord :: state.AuditTrail
                }
                
                (Success "Emergency withdrawal executed", updatedState)
        with
        | ex -> (Failure (sprintf "Failed emergency withdrawal: %s" ex.Message), state)

    /// Process treasury operations for a block
    let processTreasuryBlock (state: TreasuryState) (blockHeight: BlockHeight) (tokenomicsState: TokenomicsState) : TreasuryState =
        try
            // Check if budget reset is needed
            let (BlockHeight height) = blockHeight
            let (BlockHeight nextReset) = state.Budget.NextReset
            
            let updatedState = 
                if height >= nextReset then
                    // Reset budget
                    let newLastReset = state.Budget.NextReset
                    let (Balance budgetPeriod) = Balance state.Budget.BudgetPeriod
                    let newNextReset = BlockHeight (height + budgetPeriod)
                    let newBudget = {
                        state.Budget with
                            LastReset = newLastReset
                            NextReset = newNextReset
                            TotalBudget = state.Treasury.Balance
                            AvailableBudget = state.Treasury.Balance
                            AllocatedBudget = Balance 0UL
                    }
                    { state with Budget = newBudget; LastUpdateBlock = blockHeight }
                else
                    { state with LastUpdateBlock = blockHeight }
            
            // Auto-allocate reserves based on policy
            let (Balance currentBalance) = updatedState.Treasury.Balance
            let reserveAmount = Balance (uint64 (decimal currentBalance * updatedState.Policy.ReserveAllocationRate))
            
            if reserveAmount > Balance 0UL then
                let (_, finalState) = allocateReserve updatedState reserveAmount "governance" "Automatic reserve allocation"
                finalState
            else
                updatedState
        with
        | ex -> state

    /// Get treasury statistics
    let getTreasuryStats (state: TreasuryState) : Map<string, obj> =
        let totalBalance = state.Treasury.Balance
        let governanceReserve = state.Treasury.GovernanceReserve
        let developmentReserve = state.Treasury.DevelopmentReserve
        let emergencyReserve = state.Treasury.EmergencyReserve
        let totalOperations = state.Operations.Length
        let totalAuditRecords = state.AuditTrail.Length
        
        Map.empty
            .Add("TotalBalance", totalBalance :> obj)
            .Add("GovernanceReserve", governanceReserve :> obj)
            .Add("DevelopmentReserve", developmentReserve :> obj)
            .Add("EmergencyReserve", emergencyReserve :> obj)
            .Add("TotalOperations", totalOperations :> obj)
            .Add("TotalAuditRecords", totalAuditRecords :> obj)
            .Add("AvailableBudget", state.Budget.AvailableBudget :> obj)
            .Add("AllocatedBudget", state.Budget.AllocatedBudget :> obj)