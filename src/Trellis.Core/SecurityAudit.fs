namespace Trellis.Core

open System
open System.Security.Cryptography
open System.Text
open SimpleTypes

/// Security audit framework for Trellis blockchain
module SecurityAudit =

    /// Security vulnerability types
    type SecurityVulnerability =
        | CryptographicWeakness of string
        | LogicFlaw of string
        | InputValidation of string
        | TimingAttack of string
        | ReplayAttack of string
        | DoubleSpending of string

    /// Security audit result
    type SecurityAuditResult = {
        Vulnerability: SecurityVulnerability
        Severity: SecuritySeverity
        Description: string
        Recommendation: string
        CodeLocation: string option
        TestCase: string option
    }

    /// Security severity levels
    and SecuritySeverity =
        | Critical
        | High
        | Medium
        | Low
        | Info

    /// Security audit report
    type SecurityAuditReport = {
        AuditDate: DateTime
        Version: string
        TotalVulnerabilities: int
        CriticalCount: int
        HighCount: int
        MediumCount: int
        LowCount: int
        InfoCount: int
        Vulnerabilities: SecurityAuditResult list
        Recommendations: string list
        SecurityScore: int
    }

    /// Cryptographic security auditor
    module CryptographicAudit =

        /// Check for weak hash functions
        let checkHashSecurity (hash: Hash) : SecurityAuditResult list =
            let (Hash hashBytes) = hash
            let results = ResizeArray<SecurityAuditResult>()
            
            // Check hash length (should be 256 bits for SHA-256)
            if hashBytes.Length <> 32 then
                results.Add({
                    Vulnerability = CryptographicWeakness "Invalid hash length"
                    Severity = Critical
                    Description = sprintf "Hash length is %d bytes, expected 32 bytes" hashBytes.Length
                    Recommendation = "Use SHA-256 for all hash operations"
                    CodeLocation = Some "SimpleCrypto.sha256"
                    TestCase = Some "Hash length validation"
                })
            
            // Check for all-zero hash (weakness)
            if hashBytes |> Array.forall (fun b -> b = 0uy) then
                results.Add({
                    Vulnerability = CryptographicWeakness "All-zero hash detected"
                    Severity = High
                    Description = "Hash contains only zero bytes"
                    Recommendation = "Ensure proper input to hash function"
                    CodeLocation = Some "SimpleCrypto.sha256"
                    TestCase = Some "Zero hash detection"
                })
            
            results |> List.ofSeq

        /// Check for weak key generation
        let checkKeySecurity (publicKey: PublicKey, privateKey: PrivateKey) : SecurityAuditResult list =
            let results = ResizeArray<SecurityAuditResult>()
            
            let (PublicKey pubKeyBytes) = publicKey
            let (PrivateKey privKeyBytes) = privateKey
            
            // Check key lengths
            if pubKeyBytes.Length < 256 then
                results.Add({
                    Vulnerability = CryptographicWeakness "Weak public key length"
                    Severity = High
                    Description = sprintf "Public key length is %d bytes, minimum 256 bytes recommended" pubKeyBytes.Length
                    Recommendation = "Use RSA-2048 or ECDSA P-256 minimum"
                    CodeLocation = Some "SimpleCrypto.generateKeyPair"
                    TestCase = Some "Key length validation"
                })
            
            if privKeyBytes.Length < 256 then
                results.Add({
                    Vulnerability = CryptographicWeakness "Weak private key length"
                    Severity = Critical
                    Description = sprintf "Private key length is %d bytes, minimum 256 bytes recommended" privKeyBytes.Length
                    Recommendation = "Use RSA-2048 or ECDSA P-256 minimum"
                    CodeLocation = Some "SimpleCrypto.generateKeyPair"
                    TestCase = Some "Private key length validation"
                })
            
            // Check for weak keys (all zeros, all ones, etc.)
            if pubKeyBytes |> Array.forall (fun b -> b = 0uy) then
                results.Add({
                    Vulnerability = CryptographicWeakness "All-zero public key"
                    Severity = Critical
                    Description = "Public key contains only zero bytes"
                    Recommendation = "Ensure proper random key generation"
                    CodeLocation = Some "SimpleCrypto.generateKeyPair"
                    TestCase = Some "Zero key detection"
                })
            
            if privKeyBytes |> Array.forall (fun b -> b = 0uy) then
                results.Add({
                    Vulnerability = CryptographicWeakness "All-zero private key"
                    Severity = Critical
                    Description = "Private key contains only zero bytes"
                    Recommendation = "Ensure proper random key generation"
                    CodeLocation = Some "SimpleCrypto.generateKeyPair"
                    TestCase = Some "Zero private key detection"
                })
            
            results |> List.ofSeq

        /// Check signature security
        let checkSignatureSecurity (signature: Signature, data: byte array) : SecurityAuditResult list =
            let results = ResizeArray<SecurityAuditResult>()
            let (Signature sigBytes) = signature
            
            // Check signature length
            if sigBytes.Length < 256 then
                results.Add({
                    Vulnerability = CryptographicWeakness "Weak signature length"
                    Severity = High
                    Description = sprintf "Signature length is %d bytes, minimum 256 bytes recommended" sigBytes.Length
                    Recommendation = "Use RSA-2048 or ECDSA P-256 signatures"
                    CodeLocation = Some "SimpleCrypto.sign"
                    TestCase = Some "Signature length validation"
                })
            
            // Check for duplicate signatures (replay attack)
            if sigBytes |> Array.forall (fun b -> b = 0uy) then
                results.Add({
                    Vulnerability = ReplayAttack "All-zero signature"
                    Severity = Critical
                    Description = "Signature contains only zero bytes"
                    Recommendation = "Ensure proper signature generation"
                    CodeLocation = Some "SimpleCrypto.sign"
                    TestCase = Some "Zero signature detection"
                })
            
            results |> List.ofSeq

    /// Consensus security auditor
    module ConsensusAudit =

        /// Check for double spending vulnerabilities
        let checkDoubleSpending (transactions: Transaction list) : SecurityAuditResult list =
            let results = ResizeArray<SecurityAuditResult>()
            
            // Group transactions by sender
            let transactionsBySender = 
                transactions 
                |> List.groupBy (fun tx -> tx.From)
            
            for (sender, txs) in transactionsBySender do
                // Check for duplicate nonces (potential double spending)
                let nonces = txs |> List.map (fun tx -> tx.Nonce)
                let duplicateNonces = 
                    nonces 
                    |> List.groupBy id 
                    |> List.filter (fun (_, group) -> List.length group > 1)
                
                if not duplicateNonces.IsEmpty then
                    results.Add({
                        Vulnerability = DoubleSpending "Duplicate nonces detected"
                        Severity = Critical
                        Description = sprintf "Sender has duplicate nonces: %A" (duplicateNonces |> List.map fst)
                        Recommendation = "Ensure unique nonces for each transaction"
                        CodeLocation = Some "Transaction processing"
                        TestCase = Some "Duplicate nonce detection"
                    })
            
            results |> List.ofSeq

        /// Check for consensus manipulation
        let checkConsensusSecurity (validators: Address list, stakeDistribution: Map<Address, Balance>) : SecurityAuditResult list =
            let results = ResizeArray<SecurityAuditResult>()
            
            // Check for validator concentration (Sybil attack)
            let totalStake = 
                stakeDistribution 
                |> Map.toList 
                |> List.sumBy (fun (_, balance) -> 
                    match balance with | Balance b -> b)
            
            if totalStake > 0UL then
                for (validator, stake) in stakeDistribution |> Map.toList do
                    match stake with
                    | Balance stakeAmount ->
                        let percentage = float stakeAmount / float totalStake
                        if percentage > 0.33 then // More than 33% stake
                            results.Add({
                                Vulnerability = LogicFlaw "High validator concentration"
                                Severity = High
                                Description = sprintf "Validator controls %.2f%% of total stake" (percentage * 100.0)
                                Recommendation = "Implement stake distribution limits"
                                CodeLocation = Some "Consensus mechanism"
                                TestCase = Some "Stake concentration check"
                            })
            
            results |> List.ofSeq

    /// Input validation auditor
    module InputValidationAudit =

        /// Check for input validation vulnerabilities
        let checkInputValidation (inputs: obj list) : SecurityAuditResult list =
            let results = ResizeArray<SecurityAuditResult>()
            
            for input in inputs do
                match input with
                | :? string as str ->
                    // Check for SQL injection patterns
                    if str.ToUpper().Contains("';") || str.ToUpper().Contains("DROP") || str.ToUpper().Contains("DELETE") || 
                       str.ToUpper().Contains("INSERT") || str.ToUpper().Contains("UPDATE") || str.ToUpper().Contains("SELECT") then
                        results.Add({
                            Vulnerability = InputValidation "Potential SQL injection"
                            Severity = High
                            Description = "Input contains SQL injection patterns"
                            Recommendation = "Implement proper input sanitization"
                            CodeLocation = Some "Input validation"
                            TestCase = Some "SQL injection detection"
                        })
                    
                    // Check for XSS patterns
                    if str.ToLower().Contains("<script") || str.ToLower().Contains("javascript:") || 
                       str.ToLower().Contains("onload=") || str.ToLower().Contains("onerror=") then
                        results.Add({
                            Vulnerability = InputValidation "Potential XSS attack"
                            Severity = Medium
                            Description = "Input contains XSS patterns"
                            Recommendation = "Implement proper input sanitization"
                            CodeLocation = Some "Input validation"
                            TestCase = Some "XSS detection"
                        })
                
                | :? (byte array) as bytes ->
                    // Check for buffer overflow patterns
                    if bytes.Length > 10000 then
                        results.Add({
                            Vulnerability = InputValidation "Large input buffer"
                            Severity = Medium
                            Description = sprintf "Input buffer size is %d bytes" bytes.Length
                            Recommendation = "Implement input size limits"
                            CodeLocation = Some "Input validation"
                            TestCase = Some "Buffer size validation"
                        })
                
                | _ -> ()
            
            results |> List.ofSeq

    /// Main security auditor
    module SecurityAuditor =

        /// Run comprehensive security audit
        let runSecurityAudit () : SecurityAuditReport =
            let vulnerabilities = ResizeArray<SecurityAuditResult>()
            
            // Cryptographic security audit
            let testHash = SimpleCrypto.sha256 [|1uy; 2uy; 3uy|]
            let testPubKey, testPrivKey = SimpleCrypto.generateKeyPair()
            let testSignature = SimpleCrypto.sign testPrivKey [|1uy; 2uy; 3uy|]
            
            vulnerabilities.AddRange(CryptographicAudit.checkHashSecurity testHash)
            vulnerabilities.AddRange(CryptographicAudit.checkKeySecurity (testPubKey, testPrivKey))
            vulnerabilities.AddRange(CryptographicAudit.checkSignatureSecurity (testSignature, [|1uy; 2uy; 3uy|]))
            
            // Consensus security audit
            let testTransactions: Transaction list = [
                {
                    From = Address([|1uy|])
                    To = Address([|2uy|])
                    Amount = Balance(100UL)
                    Nonce = Nonce(1UL)
                    Fee = Balance(1UL)
                    Signature = Signature([|1uy; 2uy; 3uy|])
                    Hash = SimpleCrypto.sha256 (Array.concat [
                        [|1uy|] // From address
                        [|2uy|] // To address  
                        BitConverter.GetBytes(100UL) // Amount
                        BitConverter.GetBytes(1UL) // Nonce
                    ])
                    GasLimit = 21000UL
                    GasPrice = 20UL
                    Data = Array.empty
                    Metadata = Map.empty
                    MaxFeePerGas = None
                    MaxPriorityFeePerGas = None
                    TransactionType = 0uy
                }
                {
                    From = Address([|1uy|])
                    To = Address([|3uy|])
                    Amount = Balance(50UL)
                    Nonce = Nonce(2UL)
                    Fee = Balance(1UL)
                    Signature = Signature([|4uy; 5uy; 6uy|])
                    Hash = SimpleCrypto.sha256 (Array.concat [
                        [|1uy|] // From address
                        [|3uy|] // To address  
                        BitConverter.GetBytes(50UL) // Amount
                        BitConverter.GetBytes(2UL) // Nonce
                    ])
                    GasLimit = 21000UL
                    GasPrice = 20UL
                    Data = Array.empty
                    Metadata = Map.empty
                    MaxFeePerGas = None
                    MaxPriorityFeePerGas = None
                    TransactionType = 0uy
                }
            ]
            
            vulnerabilities.AddRange(ConsensusAudit.checkDoubleSpending testTransactions)
            
            // Input validation audit
            let testInputs: obj list = [
                "SELECT * FROM users"
                "<script>alert('xss')</script>"
                Array.create 20000 0x41uy // Realistic test data (all 'A' bytes)
            ]
            
            vulnerabilities.AddRange(InputValidationAudit.checkInputValidation testInputs)
            
            // Calculate statistics
            let vulnList = vulnerabilities |> List.ofSeq
            let criticalCount = vulnList |> List.filter (fun v -> v.Severity = Critical) |> List.length
            let highCount = vulnList |> List.filter (fun v -> v.Severity = High) |> List.length
            let mediumCount = vulnList |> List.filter (fun v -> v.Severity = Medium) |> List.length
            let lowCount = vulnList |> List.filter (fun v -> v.Severity = Low) |> List.length
            let infoCount = vulnList |> List.filter (fun v -> v.Severity = Info) |> List.length
            
            // Calculate security score (0-100)
            let securityScore = 
                let penalty = criticalCount * 20 + highCount * 10 + mediumCount * 5 + lowCount * 2
                max 0 (100 - penalty)
            
            // Generate recommendations
            let recommendations = [
                "Implement comprehensive input validation"
                "Use constant-time cryptographic operations"
                "Implement proper key management"
                "Add network diversity requirements"
                "Implement consensus security measures"
                "Add comprehensive logging and monitoring"
                "Implement rate limiting and DoS protection"
                "Add security headers and CORS policies"
            ]
            
            let report = {
                AuditDate = DateTime.UtcNow
                Version = "1.0.0"
                TotalVulnerabilities = vulnList.Length
                CriticalCount = criticalCount
                HighCount = highCount
                MediumCount = mediumCount
                LowCount = lowCount
                InfoCount = infoCount
                Vulnerabilities = vulnList
                Recommendations = recommendations
                SecurityScore = securityScore
            }
            
            report

        /// Generate security audit report
        let generateSecurityReport (report: SecurityAuditReport) : string =
            let sb = StringBuilder()
            
            sb.AppendLine("=== TRELLIS BLOCKCHAIN SECURITY AUDIT REPORT ===") |> ignore
            sb.AppendLine() |> ignore
            sb.AppendLine(sprintf "Audit Date: %s" (report.AuditDate.ToString("yyyy-MM-dd HH:mm:ss UTC"))) |> ignore
            sb.AppendLine(sprintf "Version: %s" report.Version) |> ignore
            sb.AppendLine(sprintf "Security Score: %d/100" report.SecurityScore) |> ignore
            sb.AppendLine() |> ignore
            
            sb.AppendLine("=== VULNERABILITY SUMMARY ===") |> ignore
            sb.AppendLine(sprintf "Total Vulnerabilities: %d" report.TotalVulnerabilities) |> ignore
            sb.AppendLine(sprintf "Critical: %d" report.CriticalCount) |> ignore
            sb.AppendLine(sprintf "High: %d" report.HighCount) |> ignore
            sb.AppendLine(sprintf "Medium: %d" report.MediumCount) |> ignore
            sb.AppendLine(sprintf "Low: %d" report.LowCount) |> ignore
            sb.AppendLine(sprintf "Info: %d" report.InfoCount) |> ignore
            sb.AppendLine() |> ignore
            
            if report.Vulnerabilities.Length > 0 then
                sb.AppendLine("=== DETAILED VULNERABILITIES ===") |> ignore
                for (i, vuln) in report.Vulnerabilities |> List.indexed do
                    sb.AppendLine(sprintf "%d. [%s] %s" (i+1) (vuln.Severity.ToString()) vuln.Description) |> ignore
                    sb.AppendLine(sprintf "   Recommendation: %s" vuln.Recommendation) |> ignore
                    match vuln.CodeLocation with
                    | Some loc -> sb.AppendLine(sprintf "   Location: %s" loc) |> ignore
                    | None -> ()
                    sb.AppendLine() |> ignore
            
            sb.AppendLine("=== RECOMMENDATIONS ===") |> ignore
            for (i, recommendation) in report.Recommendations |> List.indexed do
                sb.AppendLine(sprintf "%d. %s" (i+1) recommendation) |> ignore
            
            sb.ToString()

        /// Export security audit to JSON
        let exportSecurityAuditToJson (report: SecurityAuditReport) : string =
            let json = System.Text.Json.JsonSerializer.Serialize(report, System.Text.Json.JsonSerializerOptions(WriteIndented = true))
            json