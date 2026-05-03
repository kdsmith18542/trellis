namespace Trellis.Indexer

open System
open System.Data
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Npgsql
open Dapper
open Trellis.Core
open SimpleTypes

/// Compliance data indexer for Trellis blockchain
module ComplianceIndexer =

    /// Compliance data record
    type ComplianceData = {
        Id: int64
        TransactionHash: Hash
        BlockHash: Hash
        BlockHeight: BlockHeight
        FromAddress: Address
        ToAddress: Address
        Amount: Balance
        Timestamp: DateTime
        ComplianceFlags: string list
        Metadata: byte array option
        CreatedAt: DateTime
    }

    /// Compliance query types
    type ComplianceQuery =
        | ByAddress of Address
        | ByAmountRange of Balance * Balance
        | ByTimeRange of DateTime * DateTime
        | ByComplianceFlag of string
        | AllTransactions

    /// Compliance query result
    type ComplianceResult = {
        Transactions: ComplianceData list
        TotalCount: int64
        QueryTime: TimeSpan
    }

    /// Compliance flags for institutional tracking
    type ComplianceFlag =
        | KYCRequired
        | AMLFlagged
        | SanctionsCheck
        | RegulatoryReport
        | AuditTrail
        | SettlementPending

    /// Compliance indexer service
    type ComplianceIndexerService(connectionString: string, logger: ILogger<ComplianceIndexerService>) =
        let mutable connection: NpgsqlConnection option = None

        let openConnection () =
            if connection.IsNone then
                let conn = new NpgsqlConnection(connectionString)
                conn.Open()
                connection <- Some conn
                logger.LogInformation("Connected to PostgreSQL database")

        let closeConnection () =
            match connection with
            | Some conn -> 
                conn.Close()
                conn.Dispose()
                connection <- None
                logger.LogInformation("Disconnected from PostgreSQL database")
            | None -> ()

        let createTables () =
            openConnection()
            match connection with
            | Some conn ->
                let createTableSql = """
                    CREATE TABLE IF NOT EXISTS compliance_transactions (
                        id BIGSERIAL PRIMARY KEY,
                        transaction_hash BYTEA NOT NULL UNIQUE,
                        block_hash BYTEA NOT NULL,
                        block_height BIGINT NOT NULL,
                        from_address BYTEA NOT NULL,
                        to_address BYTEA NOT NULL,
                        amount BIGINT NOT NULL,
                        timestamp TIMESTAMP NOT NULL,
                        compliance_flags TEXT[],
                        metadata BYTEA,
                        created_at TIMESTAMP DEFAULT NOW()
                    );
                    
                    CREATE INDEX IF NOT EXISTS idx_compliance_from_address ON compliance_transactions(from_address);
                    CREATE INDEX IF NOT EXISTS idx_compliance_to_address ON compliance_transactions(to_address);
                    CREATE INDEX IF NOT EXISTS idx_compliance_timestamp ON compliance_transactions(timestamp);
                    CREATE INDEX IF NOT EXISTS idx_compliance_amount ON compliance_transactions(amount);
                    CREATE INDEX IF NOT EXISTS idx_compliance_flags ON compliance_transactions USING GIN(compliance_flags);
                """
                conn.Execute(createTableSql) |> ignore
                logger.LogInformation("Compliance tables created successfully")
            | None -> ()

        member this.Initialize() : unit =
            try
                createTables()
                logger.LogInformation("Compliance indexer initialized successfully")
            with
            | ex -> logger.LogError(ex, "Failed to initialize compliance indexer")

        member this.IndexTransaction(tx: Transaction, blockHash: Hash, blockHeight: BlockHeight) : unit =
            try
                openConnection()
                match connection with
                | Some conn ->
                    let (Hash txHashBytes) = txHash tx
                    let (Hash blockHashBytes) = blockHash
                    let (Address fromBytes) = tx.From
                    let (Address toBytes) = tx.To
                    let (Balance amount) = tx.Amount
                    let (Timestamp timestamp) = tx.Timestamp
                    
                    let complianceFlags = this.DetermineComplianceFlags(tx)
                    
                    let insertSql = """
                        INSERT INTO compliance_transactions 
                        (transaction_hash, block_hash, block_height, from_address, to_address, 
                         amount, timestamp, compliance_flags, metadata, created_at)
                        VALUES (@TransactionHash, @BlockHash, @BlockHeight, @FromAddress, @ToAddress,
                                @Amount, @Timestamp, @ComplianceFlags, @Metadata, @CreatedAt)
                        ON CONFLICT (transaction_hash) DO NOTHING
                    """
                    
                    let parameters = {
                        TransactionHash = txHashBytes
                        BlockHash = blockHashBytes
                        BlockHeight = match blockHeight with | BlockHeight h -> h
                        FromAddress = fromBytes
                        ToAddress = toBytes
                        Amount = amount
                        Timestamp = timestamp
                        ComplianceFlags = complianceFlags
                        Metadata = None
                        CreatedAt = DateTime.UtcNow
                    }
                    
                    conn.Execute(insertSql, parameters) |> ignore
                    logger.LogDebug("Indexed transaction {TransactionHash}", Convert.ToHexString(txHashBytes))
                | None -> logger.LogError("Database connection not available")
            with
            | ex -> logger.LogError(ex, "Failed to index transaction")

        member this.IndexBlock(block: Block) : unit =
            try
                let blockHash = blockHash block.Header
                let (BlockHeight height) = block.Header.Height
                
                for tx in block.Transactions do
                    this.IndexTransaction(tx, blockHash, height)
                
                logger.LogInformation("Indexed block {BlockHeight} with {TransactionCount} transactions", 
                    height, block.Transactions.Length)
            with
            | ex -> logger.LogError(ex, "Failed to index block")

        member this.QueryComplianceData(query: ComplianceQuery) : ComplianceResult =
            try
                openConnection()
                match connection with
                | Some conn ->
                    let startTime = DateTime.UtcNow
                    
                    let (sql, parameters) = this.BuildQuery(query)
                    let transactions = conn.Query<ComplianceData>(sql, parameters) |> List.ofSeq
                    let totalCount = int64 transactions.Length
                    let queryTime = DateTime.UtcNow - startTime
                    
                    {
                        Transactions = transactions
                        TotalCount = totalCount
                        QueryTime = queryTime
                    }
                | None -> 
                    {
                        Transactions = []
                        TotalCount = 0L
                        QueryTime = TimeSpan.Zero
                    }
            with
            | ex -> 
                logger.LogError(ex, "Failed to query compliance data")
                {
                    Transactions = []
                    TotalCount = 0L
                    QueryTime = TimeSpan.Zero
                }

        member private this.DetermineComplianceFlags(tx: Transaction) : string list =
            let flags = ResizeArray<string>()
            
            // KYC requirements based on amount
            let (Balance amount) = tx.Amount
            if amount > 1000000UL then // > 1M tokens
                flags.Add("KYCRequired")
            
            // AML flagging for large amounts
            if amount > 10000000UL then // > 10M tokens
                flags.Add("AMLFlagged")
            
            // Regulatory reporting for significant amounts
            if amount > 5000000UL then // > 5M tokens
                flags.Add("RegulatoryReport")
            
            // Always add audit trail
            flags.Add("AuditTrail")
            
            List.ofSeq flags

        member private this.BuildQuery(query: ComplianceQuery) : (string * obj) =
            match query with
            | ByAddress address ->
                let (Address addrBytes) = address
                let sql = """
                    SELECT * FROM compliance_transactions 
                    WHERE from_address = @Address OR to_address = @Address
                    ORDER BY timestamp DESC
                """
                (sql, {| Address = addrBytes |})
            
            | ByAmountRange (minAmount, maxAmount) ->
                let (Balance min) = minAmount
                let (Balance max) = maxAmount
                let sql = """
                    SELECT * FROM compliance_transactions 
                    WHERE amount BETWEEN @MinAmount AND @MaxAmount
                    ORDER BY amount DESC
                """
                (sql, {| MinAmount = min; MaxAmount = max |})
            
            | ByTimeRange (startTime, endTime) ->
                let sql = """
                    SELECT * FROM compliance_transactions 
                    WHERE timestamp BETWEEN @StartTime AND @EndTime
                    ORDER BY timestamp DESC
                """
                (sql, {| StartTime = startTime; EndTime = endTime |})
            
            | ByComplianceFlag flag ->
                let sql = """
                    SELECT * FROM compliance_transactions 
                    WHERE @Flag = ANY(compliance_flags)
                    ORDER BY timestamp DESC
                """
                (sql, {| Flag = flag |})
            
            | AllTransactions ->
                let sql = "SELECT * FROM compliance_transactions ORDER BY timestamp DESC"
                (sql, {||})

        member this.GetComplianceStats() : (int64 * int64 * int64) =
            try
                openConnection()
                match connection with
                | Some conn ->
                    let totalTransactions = conn.QuerySingle<int64>("SELECT COUNT(*) FROM compliance_transactions")
                    let kycRequired = conn.QuerySingle<int64>("SELECT COUNT(*) FROM compliance_transactions WHERE 'KYCRequired' = ANY(compliance_flags)")
                    let amlFlagged = conn.QuerySingle<int64>("SELECT COUNT(*) FROM compliance_transactions WHERE 'AMLFlagged' = ANY(compliance_flags)")
                    (totalTransactions, kycRequired, amlFlagged)
                | None -> (0L, 0L, 0L)
            with
            | ex -> 
                logger.LogError(ex, "Failed to get compliance stats")
                (0L, 0L, 0L)

        member this.GenerateComplianceReport(startDate: DateTime, endDate: DateTime) : string =
            try
                openConnection()
                match connection with
                | Some conn ->
                    let sql = """
                        SELECT 
                            COUNT(*) as total_transactions,
                            SUM(amount) as total_amount,
                            COUNT(CASE WHEN 'KYCRequired' = ANY(compliance_flags) THEN 1 END) as kyc_required,
                            COUNT(CASE WHEN 'AMLFlagged' = ANY(compliance_flags) THEN 1 END) as aml_flagged,
                            COUNT(CASE WHEN 'RegulatoryReport' = ANY(compliance_flags) THEN 1 END) as regulatory_reports
                        FROM compliance_transactions 
                        WHERE timestamp BETWEEN @StartDate AND @EndDate
                    """
                    let result = conn.QuerySingle(sql, {| StartDate = startDate; EndDate = endDate |})
                    
                    sprintf """
                    Compliance Report (%s to %s)
                    ======================================
                    Total Transactions: %d
                    Total Amount: %d
                    KYC Required: %d
                    AML Flagged: %d
                    Regulatory Reports: %d
                    """.Trim() startDate.ToString("yyyy-MM-dd") endDate.ToString("yyyy-MM-dd") 
                        result.total_transactions result.total_amount result.kyc_required 
                        result.aml_flagged result.regulatory_reports
                | None -> "Database connection not available"
            with
            | ex -> 
                logger.LogError(ex, "Failed to generate compliance report")
                "Error generating compliance report"

        interface IDisposable with
            member this.Dispose() =
                closeConnection()

    /// Compliance indexer background service
    type ComplianceIndexerBackgroundService(indexer: ComplianceIndexerService, logger: ILogger<ComplianceIndexerBackgroundService>) =
        inherit BackgroundService()
        
        override this.ExecuteAsync(stoppingToken) =
            async {
                logger.LogInformation("Starting Compliance Indexer Background Service...")
                
                // Initialize the indexer
                indexer.Initialize()
                
                // Keep running until cancellation
                while not stoppingToken.IsCancellationRequested do
                    do! Async.Sleep(1000)
                
                logger.LogInformation("Compliance Indexer Background Service stopped.")
            } |> Async.StartAsTask :> Task

        member this.IndexTransaction(tx: Transaction, blockHash: Hash, blockHeight: BlockHeight) : unit =
            indexer.IndexTransaction(tx, blockHash, blockHeight)

        member this.IndexBlock(block: Block) : unit =
            indexer.IndexBlock(block)

        member this.QueryComplianceData(query: ComplianceQuery) : ComplianceResult =
            indexer.QueryComplianceData(query)

        member this.GetComplianceStats() : (int64 * int64 * int64) =
            indexer.GetComplianceStats()

        member this.GenerateComplianceReport(startDate: DateTime, endDate: DateTime) : string =
            indexer.GenerateComplianceReport(startDate, endDate)
