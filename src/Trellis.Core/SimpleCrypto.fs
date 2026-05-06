namespace Trellis.Core

open System
open System.Text
open SimpleTypes
open Trellis.Core.ChainParams
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Crypto.Signers
open Org.BouncyCastle.Security

module SimpleCrypto =

    let sha256 (data: byte[]) : Hash =
        // Use BouncyCastle SHA-256 for consistency across runtimes
        let d = Org.BouncyCastle.Crypto.Digests.Sha256Digest()
        d.BlockUpdate(data, 0, data.Length)
        let outArr = Array.zeroCreate<byte> 32
        d.DoFinal(outArr, 0) |> ignore
        Hash outArr

    /// Generate Ed25519 keypair (32-byte private seed, 32-byte public key)
    let generateKeyPair () : (PublicKey * PrivateKey) =
        let rng = new SecureRandom()
        let seed = Array.zeroCreate<byte> 32
        rng.NextBytes(seed)
        let priv = Ed25519PrivateKeyParameters(seed, 0)
        let pub = priv.GeneratePublicKey()
        (PublicKey (pub.GetEncoded()), PrivateKey (priv.GetEncoded()))

    /// Derive public key from Ed25519 private key bytes
    let derivePublicKey (privateKey: PrivateKey) : PublicKey =
        let (PrivateKey sk) = privateKey
        let priv = Ed25519PrivateKeyParameters(sk, 0)
        let pub = priv.GeneratePublicKey()
        PublicKey (pub.GetEncoded())

    /// Sign message with Ed25519
    let sign (privateKey: PrivateKey) (message: byte[]) : Signature =
        let (PrivateKey sk) = privateKey
        let priv = Ed25519PrivateKeyParameters(sk, 0)
        let signer = new Ed25519Signer()
        signer.Init(true, priv)
        signer.BlockUpdate(message, 0, message.Length)
        let sigBytes = signer.GenerateSignature()
        Signature sigBytes

    /// Verify Ed25519 signature
    let verifySignature (publicKey: PublicKey) (message: byte[]) (signature: Signature) : bool =
        try
            let (PublicKey pk) = publicKey
            let (Signature sigBytes) = signature
            let pub = Ed25519PublicKeyParameters(pk, 0)
            let verifier = new Ed25519Signer()
            verifier.Init(false, pub)
            verifier.BlockUpdate(message, 0, message.Length)
            verifier.VerifySignature(sigBytes)
        with _ -> false

    let blockHash (header: BlockHeader) : Hash =
        let heightBytes = match header.Height with | BlockHeight h -> BitConverter.GetBytes(h)
        let prevHashBytes = match header.PreviousHash with | Hash h -> h
        let timestampBytes = match header.Timestamp with | Timestamp t -> BitConverter.GetBytes(t.Ticks)
        let nonceBytes = BitConverter.GetBytes(header.Nonce)
        let difficultyBytes = BitConverter.GetBytes(header.Difficulty)
        let headerData = Array.concat [heightBytes; prevHashBytes; timestampBytes; nonceBytes; difficultyBytes]
        sha256 headerData

    let addressFromPublicKey (publicKey: PublicKey) : Address =
        let publicKeyBytes = publicKey.ToByteArray()
        let addressBytes = sha256 publicKeyBytes
        let (Hash hashBytes) = addressBytes
        Address hashBytes

    /// Multi-signature functions
    module MultiSig =

        /// Helper function to get config for an address
        let getConfigForAddress (address: Address) =
            try
                // This would typically look up the config from storage
                // For now, return a default config
                {
                    RequiredSignatures = 2u
                    TotalSignatures = 3u
                    Signers = []
                    IsActive = true
                    CreatedAt = Timestamp(DateTime.UtcNow)
                    UpdatedAt = None
                }
            with
            | ex ->
                // Return a safe default config on error
                {
                    RequiredSignatures = 1u
                    TotalSignatures = 1u
                    Signers = []
                    IsActive = false
                    CreatedAt = Timestamp(DateTime.UtcNow)
                    UpdatedAt = None
                }

        /// Validate multi-signature configuration
        let validateMultiSigConfig (config: MultiSigConfig) =
            config.RequiredSignatures > 0u &&
            config.RequiredSignatures <= config.TotalSignatures &&
            config.TotalSignatures <= uint32 config.Signers.Length &&
            config.Signers.Length > 0 &&
            config.Signers |> List.distinct |> List.length = config.Signers.Length

        /// Check if transaction has enough signatures
        let hasEnoughSignatures (tx: MultiSigTransaction) (config: MultiSigConfig) =
            let validSignatures = 
                tx.Signatures
                |> Map.filter (fun address _ -> config.Signers |> List.contains address)
                |> Map.count
            validSignatures >= int config.RequiredSignatures

        /// Validate signature for multi-signature transaction
        let validateSignature (tx: MultiSigTransaction) (signer: Address) (signature: Signature) =
            // Create message to sign (transaction hash + signer address)
            let (Hash txHash) = tx.TransactionId
            let (Address signerBytes) = signer
            let message = Array.append txHash signerBytes
            verifySignature (PublicKey signerBytes) message signature

        /// Add signature to multi-signature transaction
        let addSignature (tx: MultiSigTransaction) (signer: Address) (signature: Signature) =
            if validateSignature tx signer signature then
                let newSignatures = tx.Signatures.Add(signer, signature)
                let newStatus = 
                    if hasEnoughSignatures { tx with Signatures = newSignatures } (getConfigForAddress tx.From) then
                        MultiSigStatus.FullySigned
                    else
                        MultiSigStatus.PartiallySigned
                { tx with 
                    Signatures = newSignatures
                    Status = newStatus
                }
            else
                tx

        /// Execute multi-signature transaction
        let executeTransaction (tx: MultiSigTransaction) (wallet: MultiSigWallet) =
            if tx.Status = MultiSigStatus.FullySigned then
                // Validate sufficient balance
                let (Balance amount) = tx.Amount
                let (Balance fee) = tx.Fee
                let (Balance available) = wallet.Balance
                if amount + fee <= available then
                    let updatedBalance = Balance(available - amount - fee)
                    let executedTx = { tx with Status = MultiSigStatus.Executed }
                    let updatedWallet = {
                        wallet with
                            Balance = updatedBalance
                            PendingTransactions = wallet.PendingTransactions |> List.filter (fun t -> t.TransactionId <> tx.TransactionId)
                            ExecutedTransactions = executedTx :: wallet.ExecutedTransactions
                    }
                    Some updatedWallet
                else
                    None // Insufficient balance
            else
                None // Transaction not fully signed

        /// Create new multi-signature wallet
        let createMultiSigWallet (config: MultiSigConfig) (initialBalance: Balance) =
            if validateMultiSigConfig config then
                Some {
                    Address = Address(System.Guid.NewGuid().ToByteArray())
                    Config = config
                    Balance = initialBalance
                    PendingTransactions = []
                    ExecutedTransactions = []
                }
            else
                None

        /// Submit transaction for multi-signature approval
        let submitTransaction (wallet: MultiSigWallet) (tx: MultiSigTransaction) =
            if wallet.Config.IsActive then
                let pendingTx = { tx with Status = MultiSigStatus.Pending }
                { wallet with
                    PendingTransactions = pendingTx :: wallet.PendingTransactions
                }
            else
                wallet

        /// Get pending transactions for a wallet
        let getPendingTransactions (wallet: MultiSigWallet) =
            wallet.PendingTransactions
            |> List.filter (fun tx -> tx.Status = MultiSigStatus.Pending || tx.Status = MultiSigStatus.PartiallySigned)

        /// Get transaction history for a wallet
        let getTransactionHistory (wallet: MultiSigWallet) =
            wallet.ExecutedTransactions
            |> List.sortByDescending (fun tx -> tx.Timestamp)

        /// Check if address is a signer for a wallet
        let isSigner (wallet: MultiSigWallet) (address: Address) =
            wallet.Config.Signers |> List.contains address

        /// Get signers for a wallet
        let getSigners (wallet: MultiSigWallet) =
            wallet.Config.Signers

        /// Update multi-signature configuration
        let updateConfig (wallet: MultiSigWallet) (newConfig: MultiSigConfig) =
            if validateMultiSigConfig newConfig then
                { wallet with
                    Config = { newConfig with UpdatedAt = Some (Timestamp(DateTime.UtcNow)) }
                }
            else
                wallet

    /// Time-lock functions
    module TimeLock =

        /// Evaluate conditional expressions for time-locks
        let private evaluateCondition (condition: string) (currentTime: Timestamp) (currentHeight: BlockHeight) : bool =
            try
                let cond = condition.Trim().ToLowerInvariant()
                match cond with
                | "always" -> true
                | "never" -> false
                | "weekend" ->
                    let (Timestamp ct) = currentTime
                    ct.DayOfWeek = DayOfWeek.Saturday || ct.DayOfWeek = DayOfWeek.Sunday
                | "business_day" ->
                    let (Timestamp ct) = currentTime
                    ct.DayOfWeek <> DayOfWeek.Saturday && ct.DayOfWeek <> DayOfWeek.Sunday
                | _ when cond.StartsWith("height_") ->
                    let hStr = cond.Substring(7)
                    match UInt64.TryParse(hStr) with
                    | true, target -> let (BlockHeight h) = currentHeight in h >= target
                    | _ -> false
                | _ when cond.StartsWith("time_") ->
                    let tStr = cond.Substring(5)
                    match DateTime.TryParse(tStr) with
                    | true, target -> let (Timestamp ct) = currentTime in ct >= target
                    | _ -> false
                | _ -> false
            with _ -> false

        /// Check if time-lock is expired
        let isTimeLockExpired (tx: TimeLockedTransaction) (currentTime: Timestamp) (currentHeight: BlockHeight) =
            match tx.TimeLock with
            | Absolute timestamp -> currentTime >= timestamp
            | Relative timespan -> 
                let (Timestamp createdAt) = tx.CreatedAt
                currentTime >= Timestamp(createdAt.Add(timespan))
            | AtHeight height -> currentHeight >= height
            | Conditional condition -> evaluateCondition condition currentTime currentHeight

        /// Check if time-lock is ready to execute
        let isTimeLockReady (tx: TimeLockedTransaction) (currentTime: Timestamp) (currentHeight: BlockHeight) =
            match tx.Status with
            | TimeLockStatus.Pending | TimeLockStatus.Locked ->
                isTimeLockExpired tx currentTime currentHeight
            | _ -> false

        /// Lock funds for time-locked transaction
        let lockFunds (wallet: TimeLockWallet) (tx: TimeLockedTransaction) =
            let (Balance amount) = tx.Amount
            let (Balance fee) = tx.Fee
            let (Balance available) = wallet.Balance
            if amount + fee <= available then
                let lockedTx = { tx with Status = TimeLockStatus.Locked }
                let updatedBalance = Balance(available - amount - fee)
                { wallet with
                    LockedTransactions = lockedTx :: wallet.LockedTransactions
                    Balance = updatedBalance
                }
            else
                wallet

        /// Execute time-locked transaction
        let executeTimeLock (wallet: TimeLockWallet) (txId: Hash) (currentTime: Timestamp) (currentHeight: BlockHeight) =
            let tx = wallet.LockedTransactions |> List.find (fun t -> t.TransactionId = txId)
            if isTimeLockReady tx currentTime currentHeight then
                let executedTx = { tx with Status = TimeLockStatus.Executed }
                { wallet with
                    LockedTransactions = wallet.LockedTransactions |> List.filter (fun t -> t.TransactionId <> txId)
                    ExecutedTransactions = executedTx :: wallet.ExecutedTransactions
                }
            else
                wallet

        /// Cancel time-locked transaction
        let cancelTimeLock (wallet: TimeLockWallet) (txId: Hash) =
            let tx = wallet.LockedTransactions |> List.find (fun t -> t.TransactionId = txId)
            let cancelledTx = { tx with Status = TimeLockStatus.Cancelled }
            let (Balance amount) = tx.Amount
            let (Balance fee) = tx.Fee
            let (Balance available) = wallet.Balance
            let refundedBalance = Balance(available + amount + fee)
            { wallet with
                LockedTransactions = wallet.LockedTransactions |> List.filter (fun t -> t.TransactionId <> txId)
                ExecutedTransactions = cancelledTx :: wallet.ExecutedTransactions
                Balance = refundedBalance
            }

        /// Create time-locked transaction
        let createTimeLockedTransaction (from: Address) (toAddress: Address) (amount: Balance) (fee: Balance) (timeLock: TimeLockType) =
            let (Address fromBytes) = from
            let (Address toBytes) = toAddress
            let (Balance amountValue) = amount
            let message = Array.append fromBytes (Array.append toBytes (BitConverter.GetBytes(amountValue)))
            {
                TransactionId = sha256 message
                From = from
                ToAddress = toAddress
                Amount = amount
                Fee = fee
                TimeLock = timeLock
                CreatedAt = Timestamp(DateTime.UtcNow)
                Status = TimeLockStatus.Pending
                Metadata = None
            }

        /// Get active time-locked transactions
        let getActiveTimeLocks (wallet: TimeLockWallet) =
            wallet.LockedTransactions
            |> List.filter (fun tx -> tx.Status = TimeLockStatus.Locked || tx.Status = TimeLockStatus.Pending)

        /// Get expired time-locked transactions
        let getExpiredTimeLocks (wallet: TimeLockWallet) (currentTime: Timestamp) (currentHeight: BlockHeight) =
            wallet.LockedTransactions
            |> List.filter (fun tx -> isTimeLockExpired tx currentTime currentHeight)

        /// Get time-locked transaction by ID
        let getTimeLockedTransaction (wallet: TimeLockWallet) (txId: Hash) =
            wallet.LockedTransactions
            |> List.tryFind (fun tx -> tx.TransactionId = txId)

        /// Get time-lock status
        let getTimeLockStatus (tx: TimeLockedTransaction) (currentTime: Timestamp) (currentHeight: BlockHeight) =
            match tx.Status with
            | TimeLockStatus.Pending | TimeLockStatus.Locked ->
                if isTimeLockExpired tx currentTime currentHeight then
                    TimeLockStatus.Unlocked
                else
                    tx.Status
            | _ -> tx.Status

        /// Calculate time until unlock
        let timeUntilUnlock (tx: TimeLockedTransaction) (currentTime: Timestamp) (currentHeight: BlockHeight) =
            match tx.TimeLock with
            | Absolute timestamp -> 
                if timestamp > currentTime then
                    let (Timestamp ts) = timestamp
                    let (Timestamp ct) = currentTime
                    Some (TimeSpan(ts.Ticks - ct.Ticks))
                else
                    None
            | Relative timespan ->
                let (Timestamp createdAt) = tx.CreatedAt
                let unlockTime = Timestamp(createdAt.Add(timespan))
                if unlockTime > currentTime then
                    let (Timestamp ut) = unlockTime
                    let (Timestamp ct) = currentTime
                    Some (TimeSpan(ut.Ticks - ct.Ticks))
                else
                    None
            | AtHeight height ->
                // This would require knowing block time to calculate
                None
            | Conditional _ ->
                None

    /// Enterprise wallet functions
    module EnterpriseWallet =

        /// Create enterprise wallet
        let createEnterpriseWallet (config: EnterpriseWalletConfig) (initialBalance: Balance) =
            let address = Address(System.Guid.NewGuid().ToByteArray())
            let multiSigWallet = 
                if config.RequiredApprovals > 0u then
                    let multiSigConfig = {
                        RequiredSignatures = config.RequiredApprovals
                        TotalSignatures = config.RequiredApprovals + 1u
                        Signers = []
                        IsActive = true
                        CreatedAt = Timestamp(DateTime.UtcNow)
                        UpdatedAt = None
                    }
                    MultiSig.createMultiSigWallet multiSigConfig initialBalance
                else
                    None
            let timeLockWallet = 
                if config.WalletType = Escrow then
                    Some {
                        Address = address
                        LockedTransactions = []
                        ExecutedTransactions = []
                        Balance = Balance(0UL)
                    }
                else
                    None
            {
                Address = address
                Config = config
                MultiSigWallet = multiSigWallet
                TimeLockWallet = timeLockWallet
                Balance = initialBalance
                TransactionHistory = ([], [])
                ComplianceReports = Map.empty
                LastAudit = None
            }

        /// Validate enterprise transaction request
        let validateTransactionRequest (request: EnterpriseTransactionRequest) (wallet: EnterpriseWallet) =
            let (Balance amount) = request.Amount
            let (Balance maxAmount) = wallet.Config.MaxTransactionAmount
            let (Balance available) = wallet.Balance
            amount <= maxAmount && amount <= available

        /// Submit enterprise transaction request
        let submitTransactionRequest (wallet: EnterpriseWallet) (request: EnterpriseTransactionRequest) =
            if validateTransactionRequest request wallet then
                // Create multi-signature transaction if required
                match wallet.MultiSigWallet with
                | Some multiSigWallet ->
                    let multiSigTx = {
                        TransactionId = request.RequestId
                        From = request.From
                        To = request.To
                        Amount = request.Amount
                        Fee = request.Fee
                        Timestamp = request.CreatedAt
                        Signatures = Map.empty
                        Status = MultiSigStatus.Pending
                        Metadata = Some request.Metadata
                    }
                    let updatedMultiSig = MultiSig.submitTransaction multiSigWallet multiSigTx
                    { wallet with MultiSigWallet = Some updatedMultiSig }
                | None ->
                    // Direct transaction for non-multi-sig wallets
                    wallet
            else
                wallet

        /// Approve enterprise transaction
        let approveTransaction (wallet: EnterpriseWallet) (approval: EnterpriseTransactionApproval) =
            match wallet.MultiSigWallet with
            | Some multiSigWallet ->
                // Find the transaction and add signature
                let pendingTxs = MultiSig.getPendingTransactions multiSigWallet
                let targetTx = pendingTxs |> List.tryFind (fun tx -> tx.TransactionId = approval.RequestId)
                match targetTx with
                | Some tx ->
                    // Create signature (in real implementation, this would be proper signing)
                    let signature = Signature(System.Guid.NewGuid().ToByteArray())
                    let updatedTx = MultiSig.addSignature tx approval.Approver signature
                    let updatedMultiSig = { multiSigWallet with PendingTransactions = multiSigWallet.PendingTransactions |> List.map (fun t -> if t.TransactionId = approval.RequestId then updatedTx else t) }
                    { wallet with MultiSigWallet = Some updatedMultiSig }
                | None -> wallet
            | None -> wallet

        /// Execute approved transactions
        let executeApprovedTransactions (wallet: EnterpriseWallet) =
            match wallet.MultiSigWallet with
            | Some multiSigWallet ->
                let pendingTxs = MultiSig.getPendingTransactions multiSigWallet
                let fullySignedTxs = pendingTxs |> List.filter (fun tx -> tx.Status = MultiSigStatus.FullySigned)
                let updatedMultiSig = 
                    fullySignedTxs 
                    |> List.fold (fun acc tx -> 
                        match MultiSig.executeTransaction tx acc with
                        | Some updated -> updated
                        | None -> acc
                    ) multiSigWallet
                { wallet with MultiSigWallet = Some updatedMultiSig }
            | None -> wallet

        /// Get transaction history
        let getTransactionHistory (wallet: EnterpriseWallet) =
            let multiSigHistory = 
                match wallet.MultiSigWallet with
                | Some multiSig -> MultiSig.getTransactionHistory multiSig
                | None -> []
            let timeLockHistory = 
                match wallet.TimeLockWallet with
                | Some timeLock -> timeLock.ExecutedTransactions
                | None -> []
            (multiSigHistory, timeLockHistory)

        /// Generate compliance report
        let generateComplianceReport (wallet: EnterpriseWallet) =
            let (multiSigHistory, timeLockHistory) = getTransactionHistory wallet
            let totalTransactions = multiSigHistory.Length + timeLockHistory.Length
            let totalAmount = 
                let multiSigAmount = multiSigHistory |> List.sumBy (fun tx -> let (Balance amount) = tx.Amount in amount)
                let timeLockAmount = timeLockHistory |> List.sumBy (fun tx -> let (Balance amount) = tx.Amount in amount)
                Balance(multiSigAmount + timeLockAmount)
            let report = Map.ofList [
                ("total_transactions", totalTransactions.ToString())
                ("total_amount", let (Balance amount) = totalAmount in amount.ToString())
                ("wallet_type", wallet.Config.WalletType.ToString())
                ("compliance_level", wallet.Config.ComplianceLevel.ToString())
                ("last_audit", wallet.LastAudit |> Option.map (fun t -> let (Timestamp ts) = t in ts.ToString()) |> Option.defaultValue "Never")
            ]
            { wallet with 
                ComplianceReports = report
                LastAudit = Some (Timestamp(DateTime.UtcNow))
            }

        /// Check compliance requirements
        let checkCompliance (wallet: EnterpriseWallet) (request: EnterpriseTransactionRequest) =
            let (Balance amount) = request.Amount
            let (Balance maxAmount) = wallet.Config.MaxTransactionAmount
            let (Balance dailyLimit) = wallet.Config.DailyLimit
            
            // Check amount limits
            let amountCompliant = amount <= maxAmount
            
            // Check daily limits (simplified - would need actual daily tracking)
            let dailyCompliant = amount <= dailyLimit
            
            // Check KYC requirements
            let kycCompliant = not wallet.Config.KYCRequired || wallet.Config.ComplianceLevel >= 2u
            
            amountCompliant && dailyCompliant && kycCompliant

        /// Update enterprise wallet configuration
        let updateWalletConfig (wallet: EnterpriseWallet) (newConfig: EnterpriseWalletConfig) =
            { wallet with 
                Config = { newConfig with UpdatedAt = Some (Timestamp(DateTime.UtcNow)) }
            }

        /// Get wallet statistics
        let getWalletStatistics (wallet: EnterpriseWallet) =
            let (Balance balance) = wallet.Balance
            let (multiSigHistory, timeLockHistory) = getTransactionHistory wallet
            let totalTransactions = multiSigHistory.Length + timeLockHistory.Length
            let pendingTransactions = 
                match wallet.MultiSigWallet with
                | Some multiSig -> MultiSig.getPendingTransactions multiSig |> List.length
                | None -> 0
            {
                Balance = balance
                TotalTransactions = totalTransactions
                PendingTransactions = pendingTransactions
                WalletType = wallet.Config.WalletType
                ComplianceLevel = wallet.Config.ComplianceLevel
                LastAudit = wallet.LastAudit
            }

    /// Compliance and audit functions
    module Compliance =

        /// Create compliance audit record
        let createAuditRecord (auditType: ComplianceAuditType) (targetAddress: Address) (auditor: Address) (findings: Map<string, string>) =
            let auditId = sha256 (Array.append (auditor.ToByteArray()) (targetAddress.ToByteArray()))
            let complianceScore = 
                if findings.IsEmpty then 100u
                else 100u - uint32 findings.Count
            {
                AuditId = auditId
                AuditType = auditType
                TargetAddress = targetAddress
                Auditor = auditor
                Findings = findings
                ComplianceScore = complianceScore
                Recommendations = []
                Timestamp = Timestamp(DateTime.UtcNow)
                Status = "Completed"
            }

        /// Generate compliance report
        let generateComplianceReport (wallet: EnterpriseWallet) (reportType: string) =
            let (multiSigHistory, timeLockHistory) = EnterpriseWallet.getTransactionHistory wallet
            let totalTransactions = multiSigHistory.Length + timeLockHistory.Length
            let totalAmount = 
                let multiSigAmount = multiSigHistory |> List.sumBy (fun tx -> let (Balance amount) = tx.Amount in amount)
                let timeLockAmount = timeLockHistory |> List.sumBy (fun tx -> let (Balance amount) = tx.Amount in amount)
                multiSigAmount + timeLockAmount
            let violations = []
            let recommendations = [
                "Regular audit schedule recommended"
                "Transaction monitoring should be enabled"
                "Compliance training for users"
            ]
            let reportData = Map.ofList [
                ("total_transactions", totalTransactions.ToString())
                ("total_amount", totalAmount.ToString())
                ("wallet_type", wallet.Config.WalletType.ToString())
                ("compliance_level", wallet.Config.ComplianceLevel.ToString())
                ("kyc_required", wallet.Config.KYCRequired.ToString())
                ("audit_enabled", wallet.Config.AuditTrailEnabled.ToString())
            ]
            {
                ReportId = sha256 (Array.append (wallet.Address.ToByteArray()) (System.Text.Encoding.UTF8.GetBytes(reportType)))
                WalletAddress = wallet.Address
                ReportType = reportType
                GeneratedAt = Timestamp(DateTime.UtcNow)
                Data = reportData
                ComplianceLevel = wallet.Config.ComplianceLevel
                Violations = violations
                Recommendations = recommendations
            }

        /// Check regulatory compliance
        let checkRegulatoryCompliance (wallet: EnterpriseWallet) (regulation: string) =
            let checkId = sha256 (Array.append (wallet.Address.ToByteArray()) (System.Text.Encoding.UTF8.GetBytes(regulation)))
            let (Balance balanceValue) = wallet.Balance
            let (Balance maxAmountValue) = wallet.Config.MaxTransactionAmount
            let passed = 
                match regulation with
                | "AML" -> wallet.Config.ComplianceLevel >= 2u
                | "KYC" -> wallet.Config.KYCRequired
                | "TransactionLimits" -> balanceValue <= maxAmountValue
                | "AuditTrail" -> wallet.Config.AuditTrailEnabled
                | _ -> true
            {
                CheckId = checkId
                Regulation = regulation
                CheckType = "Automated"
                Required = true
                Passed = passed
                Details = if passed then "Compliance check passed" else "Compliance violation detected"
                Timestamp = Timestamp(DateTime.UtcNow)
            }

        /// Perform comprehensive compliance audit
        let performComplianceAudit (wallet: EnterpriseWallet) (auditor: Address) =
            let findings = Map.empty
            let auditRecord = createAuditRecord WalletAudit wallet.Address auditor findings
            let regulatoryChecks = [
                checkRegulatoryCompliance wallet "AML"
                checkRegulatoryCompliance wallet "KYC"
                checkRegulatoryCompliance wallet "TransactionLimits"
                checkRegulatoryCompliance wallet "AuditTrail"
            ]
            let failedChecks = regulatoryChecks |> List.filter (fun check -> not check.Passed)
            let updatedFindings = 
                if failedChecks.IsEmpty then
                    findings
                else
                    failedChecks |> List.fold (fun acc check -> acc.Add(check.Regulation, check.Details)) findings
            let updatedAuditRecord = { auditRecord with Findings = updatedFindings }
            let complianceReport = generateComplianceReport wallet "Comprehensive Audit"
            (updatedAuditRecord, complianceReport)

        /// Get compliance score
        let getComplianceScore (wallet: EnterpriseWallet) =
            let regulatoryChecks = [
                checkRegulatoryCompliance wallet "AML"
                checkRegulatoryCompliance wallet "KYC"
                checkRegulatoryCompliance wallet "TransactionLimits"
                checkRegulatoryCompliance wallet "AuditTrail"
            ]
            let passedChecks = regulatoryChecks |> List.filter (fun check -> check.Passed) |> List.length
            let totalChecks = regulatoryChecks.Length
            if totalChecks = 0 then 100u else (uint32 passedChecks * 100u) / uint32 totalChecks

        /// Generate audit trail
        let generateAuditTrail (wallet: EnterpriseWallet) =
            let (multiSigHistory, timeLockHistory) = EnterpriseWallet.getTransactionHistory wallet
            let allTransactions = 
                (multiSigHistory |> List.map (fun tx -> ("MultiSig", tx.TransactionId, tx.Timestamp, tx.Amount)))
                @ (timeLockHistory |> List.map (fun tx -> ("TimeLock", tx.TransactionId, tx.CreatedAt, tx.Amount)))
            let sortedTransactions = allTransactions |> List.sortBy (fun (_, _, timestamp, _) -> timestamp)
            sortedTransactions

        /// Check for suspicious activity
        let checkSuspiciousActivity (wallet: EnterpriseWallet) =
            let (Balance balanceValue) = wallet.Balance
            let (Balance maxAmountValue) = wallet.Config.MaxTransactionAmount
            let suspiciousActivities = []
            let suspiciousActivities = 
                if balanceValue > maxAmountValue then
                    ("High Balance", "Wallet balance exceeds maximum transaction amount") :: suspiciousActivities
                else
                    suspiciousActivities
            let suspiciousActivities = 
                if wallet.Config.ComplianceLevel < 2u && balanceValue > 1000000UL then
                    ("Low Compliance High Value", "High value wallet with low compliance level") :: suspiciousActivities
                else
                    suspiciousActivities
            suspiciousActivities

    /// Staking and fee burning functions
    module Staking =

        let private currentTokenEconomics () : TokenEconomics =
            let cfg = TrellisConfig.current
            {
                MaxSupply = Balance cfg.MaxSupply
                InitialSupply = Balance cfg.InitialTotalSupply
                BlockReward = Balance cfg.BlockReward
                HalvingInterval = uint32 cfg.HalvingInterval
                StakingRewardRate = cfg.StakingRewardRate
                BurnRate = cfg.BurnRate
            }

        /// Calculate staking rewards
        let calculateStakingRewards (stakingInfo: StakingInfo) (currentTime: Timestamp) =
            let (Timestamp stakingStart) = stakingInfo.StakingStartTime
            let (Timestamp current) = currentTime
            let stakingDuration = current - stakingStart
            let daysStaked = stakingDuration.TotalDays
            let (Balance stakedAmount) = stakingInfo.StakedAmount
            let economics = currentTokenEconomics ()
            let annualReward = decimal stakedAmount * economics.StakingRewardRate
            let reward = annualReward * decimal (daysStaked / 365.0)
            Balance(uint64 reward)

        /// Calculate block reward with halving
        let calculateBlockReward (blockHeight: BlockHeight) =
            let (BlockHeight height) = blockHeight
            let economics = currentTokenEconomics ()
            let halvings = height / uint64 economics.HalvingInterval
            let (Balance rewardAmount) = economics.BlockReward
            let halvedReward = rewardAmount >>> int halvings
            Balance(halvedReward)

        /// Calculate fee burning amount
        let calculateFeeBurn (totalFee: Balance) =
            let (Balance feeAmount) = totalFee
            let economics = currentTokenEconomics ()
            let burnAmount = uint64 (decimal feeAmount * economics.BurnRate)
            Balance(burnAmount)

        /// Create staking information
        let createStakingInfo (address: Address) (stakedAmount: Balance) (stakingDuration: TimeSpan) =
            let currentTime = Timestamp(DateTime.UtcNow)
            let unlockTime = Timestamp(DateTime.UtcNow.Add(stakingDuration))
            {
                Address = address
                StakedAmount = stakedAmount
                StakingStartTime = currentTime
                UnlockTime = unlockTime
                RewardsEarned = Balance(0UL)
                IsActive = true
                PublicKey = fst (generateKeyPair()) // Generate actual key pair
                Stake = stakedAmount
                Commission = 0UL
                LastVoteHeight = BlockHeight(0UL)
                SlashCount = 0UL
            }

        /// Update staking rewards
        let updateStakingRewards (stakingInfo: StakingInfo) (currentTime: Timestamp) =
            let newRewards = calculateStakingRewards stakingInfo currentTime
            { stakingInfo with RewardsEarned = newRewards }

        /// Check if staking is unlocked
        let isStakingUnlocked (stakingInfo: StakingInfo) (currentTime: Timestamp) =
            let (Timestamp current) = currentTime
            let (Timestamp unlockTime) = stakingInfo.UnlockTime
            current >= unlockTime

        /// Process staking rewards
        let processStakingRewards (stakingInfo: StakingInfo) (currentTime: Timestamp) =
            if stakingInfo.IsActive then
                let updatedRewards = updateStakingRewards stakingInfo currentTime
                let (Balance rewardAmount) = updatedRewards.RewardsEarned
                if rewardAmount > 0UL then
                    let (Timestamp start) = stakingInfo.StakingStartTime
                    let (Timestamp current) = currentTime
                    let stakingDuration = current - start
                    Some {
                        StakerAddress = stakingInfo.Address
                        RewardAmount = Balance(rewardAmount)
                        StakingDuration = stakingDuration
                        AnnualRate = (currentTokenEconomics ()).StakingRewardRate
                        CalculatedAt = currentTime
                    }
                else
                    None
            else
                None

        /// Create fee burning record
        let createFeeBurning (transactionId: Hash) (totalFee: Balance) =
            let burnedAmount = calculateFeeBurn totalFee
            {
                TransactionId = transactionId
                BurnedAmount = burnedAmount
                BurnRate = (currentTokenEconomics ()).BurnRate
                BurnedAt = Timestamp(DateTime.UtcNow)
            }

        /// Process fee burning
        let processFeeBurning (transactionId: Hash) (totalFee: Balance) =
            let feeBurning = createFeeBurning transactionId totalFee
            let (Balance burnedAmount) = feeBurning.BurnedAmount
            if burnedAmount > 0UL then
                Some feeBurning
            else
                None

        /// Get total staked amount for an address
        let getTotalStakedAmount (stakingInfos: StakingInfo list) (address: Address) =
            stakingInfos
            |> List.filter (fun info -> info.Address = address && info.IsActive)
            |> List.sumBy (fun info -> let (Balance amount) = info.StakedAmount in amount)
            |> Balance

        /// Get total rewards for an address
        let getTotalRewards (stakingInfos: StakingInfo list) (address: Address) =
            stakingInfos
            |> List.filter (fun info -> info.Address = address)
            |> List.sumBy (fun info -> let (Balance rewards) = info.RewardsEarned in rewards)
            |> Balance

        /// Validate staking transaction
        let validateStakingTransaction (stakingInfo: StakingInfo) (currentTime: Timestamp) =
            let (Balance stakedAmount) = stakingInfo.StakedAmount
            let isUnlocked = isStakingUnlocked stakingInfo currentTime
            stakedAmount > 0UL && stakingInfo.IsActive && isUnlocked

        /// Unlock staking
        let unlockStaking (stakingInfo: StakingInfo) (currentTime: Timestamp) =
            if isStakingUnlocked stakingInfo currentTime then
                { stakingInfo with IsActive = false }
            else
                stakingInfo
    // Canonical hash for Transaction (fields that affect execution)
    let transactionHashSimple (tx: Transaction) : Hash =
        let fromBytes = match tx.From with | Address a -> a
        let toBytes = match tx.To with | Address a -> a
        let amountBytes = match tx.Amount with | Balance b -> BitConverter.GetBytes(b)
        let feeBytes = match tx.Fee with | Balance f -> BitConverter.GetBytes(f)
        let nonceBytes = match tx.Nonce with | Nonce n -> BitConverter.GetBytes(n)
        let gasLimitBytes = BitConverter.GetBytes(tx.GasLimit)
        let gasPriceBytes = BitConverter.GetBytes(tx.GasPrice)
        let dataBytes = tx.Data
        let chainIdBytes = BitConverter.GetBytes(uint32 networkChainId)
        let payload = Array.concat [ fromBytes; toBytes; amountBytes; feeBytes; nonceBytes; gasLimitBytes; gasPriceBytes; dataBytes; chainIdBytes ]
        sha256 payload

    // Verify Transaction signature using public key provided in Metadata["pubkey"] as hex
    let verifyTransactionSimple (tx: Transaction) : bool =
        try
            // Extract public key from metadata (hex)
            let maybePkHex = tx.Metadata |> Map.tryFind "pubkey"
            match maybePkHex with
            | None -> false
            | Some pkHex ->
                let pubKeyBytes = System.Convert.FromHexString(pkHex)
                let publicKey = PublicKey pubKeyBytes
                let (Signature sigBytes) = tx.Signature
                let msgHash = transactionHashSimple tx
                let (Hash h) = msgHash
                verifySignature publicKey h (Signature sigBytes)
        with _ -> false

    /// Calculate Merkle root for transactions
    let calculateMerkleRoot (transactions: Transaction list) : Hash =
        if transactions.IsEmpty then
            Hash (Array.create 32 0uy)
        else
            let txHashes = transactions |> List.map (fun tx ->
                let txHash = transactionHashSimple tx
                let (Hash hashBytes) = txHash
                hashBytes
            )
            
            let rec merkleHash (hashes: byte array list) : byte array =
                match hashes with
                | [] -> [||]
                | [single] -> single
                | _ ->
                    let pairs = hashes |> List.chunkBySize 2
                    let newHashes = pairs |> List.map (fun pair ->
                        match pair with
                        | [a; b] -> 
                            let combined = Array.append a b
                            let hash = sha256 combined
                            let (Hash hashBytes) = hash
                            hashBytes
                        | [a] -> 
                            let combined = Array.append a a
                            let hash = sha256 combined
                            let (Hash hashBytes) = hash
                            hashBytes
                        | _ -> [||]
                    )
                    merkleHash newHashes
            
            Hash (merkleHash txHashes)

    /// Verify Merkle root matches computed root
    let verifyMerkleRoot (transactions: Transaction list) (expectedRoot: Hash) : bool =
        let computedRoot = calculateMerkleRoot transactions
        computedRoot = expectedRoot

    /// Calculate Merkle proof for a transaction at given index
    let calculateMerkleProof (transactions: Transaction list) (txIndex: int) : Hash list =
        if txIndex < 0 || txIndex >= transactions.Length then
            []
        else
            let txHashes = transactions |> List.map (fun tx ->
                let txHash = transactionHashSimple tx
                let (Hash hashBytes) = txHash
                hashBytes
            )
            
            let rec buildProof (hashes: byte array list) (index: int) (proof: Hash list) : Hash list =
                match hashes with
                | [] -> proof
                | [single] -> proof
                | _ ->
                    let pairs = hashes |> List.chunkBySize 2
                    let newHashes = pairs |> List.map (fun pair ->
                        match pair with
                        | [a; b] -> 
                            let combined = Array.append a b
                            let hash = sha256 combined
                            let (Hash hashBytes) = hash
                            hashBytes
                        | [a] -> 
                            let combined = Array.append a a
                            let hash = sha256 combined
                            let (Hash hashBytes) = hash
                            hashBytes
                        | _ -> [||]
                    )
                    
                    // Add sibling to proof if it exists
                    let siblingIndex = if index % 2 = 0 then index + 1 else index - 1
                    let newProof = 
                        if siblingIndex < hashes.Length then
                            let siblingHash = hashes.[siblingIndex]
                            Hash siblingHash :: proof
                        else
                            proof
                    
                    let newIndex = index / 2
                    buildProof newHashes newIndex newProof
            
            buildProof txHashes txIndex []

    /// Verify Merkle proof for a transaction
    let verifyMerkleProof (txHash: Hash) (proof: Hash list) (root: Hash) : bool =
        let (Hash txHashBytes) = txHash
        let (Hash rootBytes) = root
        
        let rec verify (currentHash: byte array) (proofHashes: Hash list) : byte array =
            match proofHashes with
            | [] -> currentHash
            | Hash sibling :: rest ->
                let combined = Array.append currentHash sibling
                let nextHash = sha256 combined
                let (Hash nextBytes) = nextHash
                verify nextBytes rest
        
        let computedRoot = verify txHashBytes proof
        Array.forall2 (fun a b -> a = b) computedRoot rootBytes

    /// Post-quantum cryptography using .NET 10 ML-DSA (FIPS 204)
    module PostQuantum =
        #nowarn "57"

        open System.Security.Cryptography

        /// Check if post-quantum crypto is available on this platform
        let isPostQuantumAvailable () =
            MLDsa.IsSupported

        /// Generate a post-quantum key pair for validators (ML-DSA-65)
        let generatePQKeyPair () =
            if not MLDsa.IsSupported then
                failwith "Post-quantum ML-DSA not supported on this platform"
            let key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65)
            let pubKey = key.ExportSubjectPublicKeyInfoPem()
            let privKey = key.ExportPkcs8PrivateKeyPem()
            key.Dispose()
            pubKey, privKey

        /// Sign data with post-quantum signature
        let pqSignData (privateKeyPem: string) (data: byte array) =
            if not MLDsa.IsSupported then None else
            try
                use key = MLDsa.ImportFromPem(privateKeyPem)
                let sigBytes = Array.zeroCreate<byte> key.Algorithm.SignatureSizeInBytes
                key.SignData(data.AsSpan(), sigBytes.AsSpan()) |> ignore
                Some sigBytes
            with _ -> None

        /// Verify post-quantum signature
        let pqVerifyData (publicKeyPem: string) (data: byte array) (signature: byte array) =
            if not MLDsa.IsSupported then false else
            try
                use key = MLDsa.ImportFromPem(publicKeyPem)
                key.VerifyData(data.AsSpan(), signature.AsSpan())
            with _ -> false

        /// Post-quantum BLS replacement — sign a validator vote
        let pqSignVote (privKey: string) (blockHash: byte array) (epoch: uint64) : byte array option =
            let message = Array.concat [blockHash; BitConverter.GetBytes(epoch)]
            pqSignData privKey message

        /// Verify a post-quantum validator vote
        let pqVerifyVote (pubKey: string) (blockHash: byte array) (epoch: uint64) (signature: byte array) : bool =
            let message = Array.concat [blockHash; BitConverter.GetBytes(epoch)]
            pqVerifyData pubKey message signature

        /// Aggregate PQ signatures (simple concatenation — for multi-sig threshold)
        let pqAggregateSignatures (signatures: byte array list) : byte array =
            signatures |> Array.concat

        /// Verify an aggregated signature set (all must pass)
        let pqVerifyAggregate (pubKeys: string list) (data: byte array) (aggregated: byte array) : bool =
            let sigSize = 3309 // ML-DSA-65 signature size
            if aggregated.Length <> sigSize * pubKeys.Length then false
            else
                pubKeys
                |> List.indexed
                |> List.forall (fun (i, pk) ->
                    let sigSlice = aggregated.[i * sigSize .. (i + 1) * sigSize - 1]
                    pqVerifyData pk data sigSlice
                )
