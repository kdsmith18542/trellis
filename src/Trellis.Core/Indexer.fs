namespace Trellis.Core

open System
open System.Collections.Generic
open System.Data
open Npgsql
open SimpleTypes

/// PostgreSQL indexer for Trellis blockchain data
module Indexer =

    /// Database connection configuration
    type DatabaseConfig = {
        Host: string
        Port: int
        Database: string
        Username: string
        Password: string
        PoolSize: int
        CommandTimeout: int
    }

    /// Indexer configuration
    type IndexerConfig = {
        Database: DatabaseConfig
        BatchSize: int
        FlushInterval: TimeSpan
        EnableCompression: bool
        EnablePruning: bool
        PruningAge: TimeSpan
        EnableTimeSeries: bool
    }

    /// Indexed block data
    type IndexedBlock = {
        Hash: string
        Height: uint64
        Timestamp: DateTime
        PreviousHash: string
        MerkleRoot: string
        Validator: string
        TransactionCount: int
        Size: int64
        GasUsed: uint64
        GasLimit: uint64
        Difficulty: float
        Nonce: uint64
        Version: int
        ExtraData: string option
    }

    /// Indexed transaction data
    type IndexedTransaction = {
        Hash: string
        BlockHash: string
        BlockHeight: uint64
        Index: int
        From: string
        To: string
        Amount: uint64
        Fee: uint64
        GasLimit: uint64
        GasPrice: uint64
        Nonce: uint64
        Timestamp: DateTime
        Data: string option
        Signature: string
        Status: string
        Type: string
    }

    /// Indexed account data
    type IndexedAccount = {
        Address: string
        Balance: uint64
        Nonce: uint64
        FirstSeen: DateTime
        LastSeen: DateTime
        TransactionCount: int64
        IsContract: bool
        ContractCode: string option
    }

    /// Indexed validator data
    type IndexedValidator = {
        Address: string
        PublicKey: string
        Stake: uint64
        Commission: float
        IsActive: bool
        LastVoteHeight: uint64
        SlashCount: int
        CreatedAt: DateTime
        UpdatedAt: DateTime
    }

    /// Indexed compliance data
    type IndexedCompliance = {
        Id: string
        TransactionHash: string
        Address: string
        ComplianceType: string
        Status: string
        Timestamp: DateTime
        Metadata: string
        AuditTrail: string
    }

    /// Indexed time-lock data
    type IndexedTimeLock = {
        Id: string
        TransactionHash: string
        Address: string
        UnlockTime: DateTime
        Amount: uint64
        Status: string
        CreatedAt: DateTime
        ExecutedAt: DateTime option
    }

    /// Indexed multi-signature data
    type IndexedMultiSig = {
        Id: string
        TransactionHash: string
        Address: string
        RequiredSignatures: int
        TotalSignatures: int
        Signatures: string list
        Status: string
        CreatedAt: DateTime
        CompletedAt: DateTime option
    }

    /// Indexer statistics
    type IndexerStats = {
        TotalBlocks: int64
        TotalTransactions: int64
        TotalAccounts: int64
        TotalValidators: int64
        LastIndexedHeight: uint64
        LastIndexedTime: DateTime
        IndexingRate: float
        ErrorCount: int64
        ProcessingTime: TimeSpan
    }

    /// Indexer interface
    type IIndexer =
        abstract member IndexBlock: Block -> Async<unit>
        abstract member IndexTransaction: Transaction -> string -> uint64 -> int -> Async<unit>
        abstract member IndexAccount: Address -> Account -> Async<unit>
        abstract member IndexValidator: PublicKey -> obj -> Async<unit>
        abstract member IndexCompliance: string -> string -> string -> string -> string -> DateTime -> string -> string -> Async<unit>
        abstract member IndexTimeLock: string -> string -> string -> DateTime -> uint64 -> string -> Async<unit>
        abstract member IndexMultiSig: string -> string -> string -> int -> int -> string list -> string -> Async<unit>
        abstract member GetStats: unit -> Async<IndexerStats>
        abstract member Flush: unit -> Async<unit>
        abstract member Close: unit -> Async<unit>

    /// PostgreSQL indexer implementation
    type PostgreSQLIndexer(config: IndexerConfig) =
        let mutable connection: NpgsqlConnection option = None
        let mutable isConnected = false
        let mutable stats = {
            TotalBlocks = 0L
            TotalTransactions = 0L
            TotalAccounts = 0L
            TotalValidators = 0L
            LastIndexedHeight = 0UL
            LastIndexedTime = DateTime.UtcNow
            IndexingRate = 0.0
            ErrorCount = 0L
            ProcessingTime = TimeSpan.Zero
        }

        let connectionString = 
            sprintf "Host=%s;Port=%d;Database=%s;Username=%s;Password=%s;Pooling=true;MinPoolSize=1;MaxPoolSize=%d;CommandTimeout=%d"
                config.Database.Host config.Database.Port config.Database.Database 
                config.Database.Username config.Database.Password 
                config.Database.PoolSize config.Database.CommandTimeout

        let connect () =
            if not isConnected then
                try
                    let conn = new NpgsqlConnection(connectionString)
                    conn.Open()
                    connection <- Some conn
                    isConnected <- true
                with
                | ex -> 
                    stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                    raise ex

        let disconnect () =
            if isConnected then
                match connection with
                | Some conn -> 
                    conn.Close()
                    conn.Dispose()
                    connection <- None
                | None -> ()
                isConnected <- false

        let executeCommand (command: string) (parameters: (string * obj) list) : Result<int, string> =
            try
                connect()
                match connection with
                | Some conn ->
                    use cmd = new NpgsqlCommand(command, conn)
                    for (name, value) in parameters do
                        cmd.Parameters.AddWithValue(name, value) |> ignore
                    let result = cmd.ExecuteNonQuery()
                    Ok result
                | None -> Error "Database connection not available"
            with
            | ex -> Error (sprintf "Database command failed: %s" ex.Message)

        let executeQuery (command: string) (parameters: (string * obj) list) : Result<System.Data.Common.DbDataReader, string> =
            try
                connect()
                match connection with
                | Some conn ->
                    use cmd = new NpgsqlCommand(command, conn)
                    for (name, value) in parameters do
                        cmd.Parameters.AddWithValue(name, value) |> ignore
                    let reader = cmd.ExecuteReader()
                    Ok reader
                | None -> Error "Database connection not available"
            with
            | ex -> Error (sprintf "Database query failed: %s" ex.Message)

        let createTables () =
            let createBlocksTable = """
                CREATE TABLE IF NOT EXISTS blocks (
                    hash VARCHAR(64) PRIMARY KEY,
                    height BIGINT UNIQUE NOT NULL,
                    timestamp TIMESTAMP NOT NULL,
                    previous_hash VARCHAR(64),
                    merkle_root VARCHAR(64),
                    validator VARCHAR(64),
                    transaction_count INTEGER NOT NULL,
                    size BIGINT NOT NULL,
                    gas_used BIGINT NOT NULL,
                    gas_limit BIGINT NOT NULL,
                    difficulty DOUBLE PRECISION NOT NULL,
                    nonce BIGINT NOT NULL,
                    version INTEGER NOT NULL,
                    extra_data TEXT,
                    created_at TIMESTAMP DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_blocks_height ON blocks(height);
                CREATE INDEX IF NOT EXISTS idx_blocks_timestamp ON blocks(timestamp);
                CREATE INDEX IF NOT EXISTS idx_blocks_validator ON blocks(validator);
            """

            let createTransactionsTable = """
                CREATE TABLE IF NOT EXISTS transactions (
                    hash VARCHAR(64) PRIMARY KEY,
                    block_hash VARCHAR(64) NOT NULL,
                    block_height BIGINT NOT NULL,
                    index INTEGER NOT NULL,
                    from_address VARCHAR(64) NOT NULL,
                    to_address VARCHAR(64) NOT NULL,
                    amount BIGINT NOT NULL,
                    fee BIGINT NOT NULL,
                    gas_limit BIGINT NOT NULL,
                    gas_price BIGINT NOT NULL,
                    nonce BIGINT NOT NULL,
                    timestamp TIMESTAMP NOT NULL,
                    data TEXT,
                    signature VARCHAR(128) NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    type VARCHAR(20) NOT NULL,
                    created_at TIMESTAMP DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_transactions_block_hash ON transactions(block_hash);
                CREATE INDEX IF NOT EXISTS idx_transactions_block_height ON transactions(block_height);
                CREATE INDEX IF NOT EXISTS idx_transactions_from_address ON transactions(from_address);
                CREATE INDEX IF NOT EXISTS idx_transactions_to_address ON transactions(to_address);
                CREATE INDEX IF NOT EXISTS idx_transactions_timestamp ON transactions(timestamp);
                CREATE INDEX IF NOT EXISTS idx_transactions_status ON transactions(status);
            """

            let createAccountsTable = """
                CREATE TABLE IF NOT EXISTS accounts (
                    address VARCHAR(64) PRIMARY KEY,
                    balance BIGINT NOT NULL,
                    nonce BIGINT NOT NULL,
                    first_seen TIMESTAMP NOT NULL,
                    last_seen TIMESTAMP NOT NULL,
                    transaction_count BIGINT NOT NULL,
                    is_contract BOOLEAN NOT NULL DEFAULT FALSE,
                    contract_code TEXT,
                    created_at TIMESTAMP DEFAULT NOW(),
                    updated_at TIMESTAMP DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_accounts_balance ON accounts(balance);
                CREATE INDEX IF NOT EXISTS idx_accounts_last_seen ON accounts(last_seen);
                CREATE INDEX IF NOT EXISTS idx_accounts_is_contract ON accounts(is_contract);
            """

            let createValidatorsTable = """
                CREATE TABLE IF NOT EXISTS validators (
                    address VARCHAR(64) PRIMARY KEY,
                    public_key VARCHAR(128) NOT NULL,
                    stake BIGINT NOT NULL,
                    commission DOUBLE PRECISION NOT NULL,
                    is_active BOOLEAN NOT NULL,
                    last_vote_height BIGINT NOT NULL,
                    slash_count INTEGER NOT NULL,
                    created_at TIMESTAMP NOT NULL,
                    updated_at TIMESTAMP NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_validators_stake ON validators(stake);
                CREATE INDEX IF NOT EXISTS idx_validators_is_active ON validators(is_active);
                CREATE INDEX IF NOT EXISTS idx_validators_commission ON validators(commission);
            """

            let createComplianceTable = """
                CREATE TABLE IF NOT EXISTS compliance (
                    id VARCHAR(64) PRIMARY KEY,
                    transaction_hash VARCHAR(64) NOT NULL,
                    address VARCHAR(64) NOT NULL,
                    compliance_type VARCHAR(50) NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    timestamp TIMESTAMP NOT NULL,
                    metadata TEXT,
                    audit_trail TEXT,
                    created_at TIMESTAMP DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_compliance_transaction_hash ON compliance(transaction_hash);
                CREATE INDEX IF NOT EXISTS idx_compliance_address ON compliance(address);
                CREATE INDEX IF NOT EXISTS idx_compliance_type ON compliance(compliance_type);
                CREATE INDEX IF NOT EXISTS idx_compliance_status ON compliance(status);
                CREATE INDEX IF NOT EXISTS idx_compliance_timestamp ON compliance(timestamp);
            """

            let createTimeLocksTable = """
                CREATE TABLE IF NOT EXISTS time_locks (
                    id VARCHAR(64) PRIMARY KEY,
                    transaction_hash VARCHAR(64) NOT NULL,
                    address VARCHAR(64) NOT NULL,
                    unlock_time TIMESTAMP NOT NULL,
                    amount BIGINT NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    created_at TIMESTAMP NOT NULL,
                    executed_at TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_time_locks_transaction_hash ON time_locks(transaction_hash);
                CREATE INDEX IF NOT EXISTS idx_time_locks_address ON time_locks(address);
                CREATE INDEX IF NOT EXISTS idx_time_locks_unlock_time ON time_locks(unlock_time);
                CREATE INDEX IF NOT EXISTS idx_time_locks_status ON time_locks(status);
            """

            let createMultiSigTable = """
                CREATE TABLE IF NOT EXISTS multi_sigs (
                    id VARCHAR(64) PRIMARY KEY,
                    transaction_hash VARCHAR(64) NOT NULL,
                    address VARCHAR(64) NOT NULL,
                    required_signatures INTEGER NOT NULL,
                    total_signatures INTEGER NOT NULL,
                    signatures TEXT[] NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    created_at TIMESTAMP NOT NULL,
                    completed_at TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_multi_sigs_transaction_hash ON multi_sigs(transaction_hash);
                CREATE INDEX IF NOT EXISTS idx_multi_sigs_address ON multi_sigs(address);
                CREATE INDEX IF NOT EXISTS idx_multi_sigs_status ON multi_sigs(status);
            """

            // Execute table creation
            let results = [
                executeCommand createBlocksTable []
                executeCommand createTransactionsTable []
                executeCommand createAccountsTable []
                executeCommand createValidatorsTable []
                executeCommand createComplianceTable []
                executeCommand createTimeLocksTable []
                executeCommand createMultiSigTable []
            ]
            
            // Check for any errors
            let errors = results |> List.choose (fun r -> match r with | Error e -> Some e | _ -> None)
            if errors.Length > 0 then
                let msg = sprintf "Table creation failed: %s" (String.concat "; " errors)
                // Escalate with a specific exception type instead of failwith
                raise (System.InvalidOperationException(msg))

        interface IIndexer with
            member this.IndexBlock(block: Block) =
                async {
                    try
                        let startTime = DateTime.UtcNow
                        
                        let indexedBlock = {
                            Hash = match block.Header.Hash with | Hash h -> Convert.ToHexString(h)
                            Height = match block.Header.Height with | BlockHeight h -> h
                            Timestamp = match block.Header.Timestamp with | Timestamp t -> t
                            PreviousHash = match block.Header.PreviousHash with | Hash h -> Convert.ToHexString(h)
                            MerkleRoot = match block.Header.MerkleRoot with | Hash h -> Convert.ToHexString(h)
                            Validator = match block.Header.Validator with | Address a -> Convert.ToHexString(a)
                            TransactionCount = block.Transactions.Length
                            Size = int64 (block.Transactions.Length * 100) // Approximate size
                            GasUsed = 0UL // Calculate from transactions
                            GasLimit = 0UL // Calculate from transactions
                            Difficulty = 1.0 // Calculate from block
                            Nonce = 0UL // Extract from block
                            Version = 1
                            ExtraData = None
                        }

                        let command = """
                            INSERT INTO blocks (hash, height, timestamp, previous_hash, merkle_root, validator, 
                                               transaction_count, size, gas_used, gas_limit, difficulty, nonce, version, extra_data)
                            VALUES (@hash, @height, @timestamp, @previous_hash, @merkle_root, @validator,
                                   @transaction_count, @size, @gas_used, @gas_limit, @difficulty, @nonce, @version, @extra_data)
                            ON CONFLICT (hash) DO UPDATE SET
                                height = EXCLUDED.height,
                                timestamp = EXCLUDED.timestamp,
                                previous_hash = EXCLUDED.previous_hash,
                                merkle_root = EXCLUDED.merkle_root,
                                validator = EXCLUDED.validator,
                                transaction_count = EXCLUDED.transaction_count,
                                size = EXCLUDED.size,
                                gas_used = EXCLUDED.gas_used,
                                gas_limit = EXCLUDED.gas_limit,
                                difficulty = EXCLUDED.difficulty,
                                nonce = EXCLUDED.nonce,
                                version = EXCLUDED.version,
                                extra_data = EXCLUDED.extra_data
                        """

                        let parameters = [
                            ("@hash", indexedBlock.Hash :> obj)
                            ("@height", indexedBlock.Height :> obj)
                            ("@timestamp", indexedBlock.Timestamp :> obj)
                            ("@previous_hash", indexedBlock.PreviousHash :> obj)
                            ("@merkle_root", indexedBlock.MerkleRoot :> obj)
                            ("@validator", indexedBlock.Validator :> obj)
                            ("@transaction_count", indexedBlock.TransactionCount :> obj)
                            ("@size", indexedBlock.Size :> obj)
                            ("@gas_used", indexedBlock.GasUsed :> obj)
                            ("@gas_limit", indexedBlock.GasLimit :> obj)
                            ("@difficulty", indexedBlock.Difficulty :> obj)
                            ("@nonce", indexedBlock.Nonce :> obj)
                            ("@version", indexedBlock.Version :> obj)
                            ("@extra_data", indexedBlock.ExtraData :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                        
                        let processingTime = DateTime.UtcNow - startTime
                        stats <- { 
                            stats with 
                                TotalBlocks = stats.TotalBlocks + 1L
                                LastIndexedHeight = indexedBlock.Height
                                LastIndexedTime = DateTime.UtcNow
                                ProcessingTime = stats.ProcessingTime + processingTime
                        }
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.IndexTransaction(tx: Transaction) (blockHash: string) (blockHeight: uint64) (index: int) =
                async {
                    try
                        let indexedTx = {
                            Hash = match tx.Hash with | Hash h -> Convert.ToHexString(h)
                            BlockHash = blockHash
                            BlockHeight = blockHeight
                            Index = index
                            From = match tx.From with | Address a -> Convert.ToHexString(a)
                            To = match tx.To with | Address a -> Convert.ToHexString(a)
                            Amount = match tx.Amount with | Balance b -> b
                            Fee = match tx.Fee with | Balance f -> f
                            GasLimit = 21000UL
                            GasPrice = 20UL
                            Nonce = match tx.Nonce with | Nonce n -> n
                            Timestamp = DateTime.UtcNow
                            Data = None
                            Signature = match tx.Signature with | Signature s -> Convert.ToHexString(s)
                            Status = "confirmed"
                            Type = "transfer"
                        }

                        let command = """
                            INSERT INTO transactions (hash, block_hash, block_height, index, from_address, to_address,
                                                    amount, fee, gas_limit, gas_price, nonce, timestamp, data, signature, status, type)
                            VALUES (@hash, @block_hash, @block_height, @index, @from_address, @to_address,
                                   @amount, @fee, @gas_limit, @gas_price, @nonce, @timestamp, @data, @signature, @status, @type)
                            ON CONFLICT (hash) DO UPDATE SET
                                block_hash = EXCLUDED.block_hash,
                                block_height = EXCLUDED.block_height,
                                index = EXCLUDED.index,
                                from_address = EXCLUDED.from_address,
                                to_address = EXCLUDED.to_address,
                                amount = EXCLUDED.amount,
                                fee = EXCLUDED.fee,
                                gas_limit = EXCLUDED.gas_limit,
                                gas_price = EXCLUDED.gas_price,
                                nonce = EXCLUDED.nonce,
                                timestamp = EXCLUDED.timestamp,
                                data = EXCLUDED.data,
                                signature = EXCLUDED.signature,
                                status = EXCLUDED.status,
                                type = EXCLUDED.type
                        """

                        let parameters = [
                            ("@hash", indexedTx.Hash :> obj)
                            ("@block_hash", indexedTx.BlockHash :> obj)
                            ("@block_height", indexedTx.BlockHeight :> obj)
                            ("@index", indexedTx.Index :> obj)
                            ("@from_address", indexedTx.From :> obj)
                            ("@to_address", indexedTx.To :> obj)
                            ("@amount", indexedTx.Amount :> obj)
                            ("@fee", indexedTx.Fee :> obj)
                            ("@gas_limit", indexedTx.GasLimit :> obj)
                            ("@gas_price", indexedTx.GasPrice :> obj)
                            ("@nonce", indexedTx.Nonce :> obj)
                            ("@timestamp", indexedTx.Timestamp :> obj)
                            ("@data", indexedTx.Data :> obj)
                            ("@signature", indexedTx.Signature :> obj)
                            ("@status", indexedTx.Status :> obj)
                            ("@type", indexedTx.Type :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                        
                        stats <- { stats with TotalTransactions = stats.TotalTransactions + 1L }
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.IndexAccount(address: Address) (account: Account) =
                async {
                    try
                        let indexedAccount = {
                            Address = match address with | Address a -> Convert.ToHexString(a)
                            Balance = match account.Balance with | Balance b -> b
                            Nonce = match account.Nonce with | Nonce n -> n
                            FirstSeen = DateTime.UtcNow
                            LastSeen = DateTime.UtcNow
                            TransactionCount = 0L
                            IsContract = false
                            ContractCode = None
                        }

                        let command = """
                            INSERT INTO accounts (address, balance, nonce, first_seen, last_seen, transaction_count, is_contract, contract_code)
                            VALUES (@address, @balance, @nonce, @first_seen, @last_seen, @transaction_count, @is_contract, @contract_code)
                            ON CONFLICT (address) DO UPDATE SET
                                balance = EXCLUDED.balance,
                                nonce = EXCLUDED.nonce,
                                last_seen = EXCLUDED.last_seen,
                                transaction_count = accounts.transaction_count + 1,
                                updated_at = NOW()
                        """

                        let parameters = [
                            ("@address", indexedAccount.Address :> obj)
                            ("@balance", indexedAccount.Balance :> obj)
                            ("@nonce", indexedAccount.Nonce :> obj)
                            ("@first_seen", indexedAccount.FirstSeen :> obj)
                            ("@last_seen", indexedAccount.LastSeen :> obj)
                            ("@transaction_count", indexedAccount.TransactionCount :> obj)
                            ("@is_contract", indexedAccount.IsContract :> obj)
                            ("@contract_code", indexedAccount.ContractCode :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                        
                        stats <- { stats with TotalAccounts = stats.TotalAccounts + 1L }
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.IndexValidator(publicKey: PublicKey) (validator: obj) =
                async {
                    try
                        let indexedValidator = {
                            Address = match validator with | :? StakingInfo as staking -> match staking.Address with | Address a -> Convert.ToHexString(a) | _ -> "unknown"
                            PublicKey = match publicKey with | PublicKey pk -> Convert.ToHexString(pk)
                            Stake = 1000000UL
                            Commission = 0.05
                            IsActive = true
                            LastVoteHeight = 0UL
                            SlashCount = 0
                            CreatedAt = DateTime.UtcNow
                            UpdatedAt = DateTime.UtcNow
                        }

                        let command = """
                            INSERT INTO validators (address, public_key, stake, commission, is_active, last_vote_height, slash_count, created_at, updated_at)
                            VALUES (@address, @public_key, @stake, @commission, @is_active, @last_vote_height, @slash_count, @created_at, @updated_at)
                            ON CONFLICT (address) DO UPDATE SET
                                public_key = EXCLUDED.public_key,
                                stake = EXCLUDED.stake,
                                commission = EXCLUDED.commission,
                                is_active = EXCLUDED.is_active,
                                last_vote_height = EXCLUDED.last_vote_height,
                                slash_count = EXCLUDED.slash_count,
                                updated_at = EXCLUDED.updated_at
                        """

                        let parameters = [
                            ("@address", indexedValidator.Address :> obj)
                            ("@public_key", indexedValidator.PublicKey :> obj)
                            ("@stake", indexedValidator.Stake :> obj)
                            ("@commission", indexedValidator.Commission :> obj)
                            ("@is_active", indexedValidator.IsActive :> obj)
                            ("@last_vote_height", indexedValidator.LastVoteHeight :> obj)
                            ("@slash_count", indexedValidator.SlashCount :> obj)
                            ("@created_at", indexedValidator.CreatedAt :> obj)
                            ("@updated_at", indexedValidator.UpdatedAt :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                        
                        stats <- { stats with TotalValidators = stats.TotalValidators + 1L }
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.IndexCompliance(id: string) (transactionHash: string) (address: string) (complianceType: string) (status: string) (timestamp: DateTime) (metadata: string) (auditTrail: string) =
                async {
                    try
                        let command = """
                            INSERT INTO compliance (id, transaction_hash, address, compliance_type, status, timestamp, metadata, audit_trail)
                            VALUES (@id, @transaction_hash, @address, @compliance_type, @status, @timestamp, @metadata, @audit_trail)
                            ON CONFLICT (id) DO UPDATE SET
                                transaction_hash = EXCLUDED.transaction_hash,
                                address = EXCLUDED.address,
                                compliance_type = EXCLUDED.compliance_type,
                                status = EXCLUDED.status,
                                timestamp = EXCLUDED.timestamp,
                                metadata = EXCLUDED.metadata,
                                audit_trail = EXCLUDED.audit_trail
                        """

                        let parameters = [
                            ("@id", id :> obj)
                            ("@transaction_hash", transactionHash :> obj)
                            ("@address", address :> obj)
                            ("@compliance_type", complianceType :> obj)
                            ("@status", status :> obj)
                            ("@timestamp", timestamp :> obj)
                            ("@metadata", metadata :> obj)
                            ("@audit_trail", auditTrail :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.IndexTimeLock(id: string) (transactionHash: string) (address: string) (unlockTime: DateTime) (amount: uint64) (status: string) =
                async {
                    try
                        let command = """
                            INSERT INTO time_locks (id, transaction_hash, address, unlock_time, amount, status, created_at)
                            VALUES (@id, @transaction_hash, @address, @unlock_time, @amount, @status, @created_at)
                            ON CONFLICT (id) DO UPDATE SET
                                transaction_hash = EXCLUDED.transaction_hash,
                                address = EXCLUDED.address,
                                unlock_time = EXCLUDED.unlock_time,
                                amount = EXCLUDED.amount,
                                status = EXCLUDED.status
                        """

                        let parameters = [
                            ("@id", id :> obj)
                            ("@transaction_hash", transactionHash :> obj)
                            ("@address", address :> obj)
                            ("@unlock_time", unlockTime :> obj)
                            ("@amount", amount :> obj)
                            ("@status", status :> obj)
                            ("@created_at", DateTime.UtcNow :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.IndexMultiSig(id: string) (transactionHash: string) (address: string) (requiredSignatures: int) (totalSignatures: int) (signatures: string list) (status: string) =
                async {
                    try
                        let command = """
                            INSERT INTO multi_sigs (id, transaction_hash, address, required_signatures, total_signatures, signatures, status, created_at)
                            VALUES (@id, @transaction_hash, @address, @required_signatures, @total_signatures, @signatures, @status, @created_at)
                            ON CONFLICT (id) DO UPDATE SET
                                transaction_hash = EXCLUDED.transaction_hash,
                                address = EXCLUDED.address,
                                required_signatures = EXCLUDED.required_signatures,
                                total_signatures = EXCLUDED.total_signatures,
                                signatures = EXCLUDED.signatures,
                                status = EXCLUDED.status
                        """

                        let parameters = [
                            ("@id", id :> obj)
                            ("@transaction_hash", transactionHash :> obj)
                            ("@address", address :> obj)
                            ("@required_signatures", requiredSignatures :> obj)
                            ("@total_signatures", totalSignatures :> obj)
                            ("@signatures", signatures :> obj)
                            ("@status", status :> obj)
                            ("@created_at", DateTime.UtcNow :> obj)
                        ]

                        match executeCommand command parameters with
                        | Ok _ -> ()
                        | Error msg -> 
                            printfn "Warning: Database operation failed: %s" msg
                    with
                    | ex -> 
                        stats <- { stats with ErrorCount = stats.ErrorCount + 1L }
                        raise ex
                }

            member this.GetStats() =
                async {
                    return stats
                }

            member this.Flush() =
                async {
                    // Flush any pending operations
                    do! Async.Sleep(100) // Small delay to ensure all operations complete
                }

            member this.Close() =
                async {
                    disconnect()
                }

        /// Initialize the indexer
        member this.Initialize() =
            createTables()

        interface IDisposable with
            member this.Dispose() =
                disconnect()

    /// Create a new indexer instance
    let createIndexer (config: IndexerConfig) : IIndexer =
        let indexer = new PostgreSQLIndexer(config)
        indexer.Initialize()
        indexer :> IIndexer

    /// Default indexer configuration
    let defaultConfig = {
        Database = {
            Host = "localhost"
            Port = 5432
            Database = "trellis_indexer"
            Username = "trellis"
            Password = "trellis_password"
            PoolSize = 10
            CommandTimeout = 30
        }
        BatchSize = 1000
        FlushInterval = TimeSpan.FromSeconds(30.0)
        EnableCompression = true
        EnablePruning = true
        PruningAge = TimeSpan.FromDays(365.0)
        EnableTimeSeries = true
    }
