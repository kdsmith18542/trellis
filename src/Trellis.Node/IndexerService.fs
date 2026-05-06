namespace Trellis.Node

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open Trellis.Core
open SimpleTypes
open Indexer

/// Background service for indexing blockchain data to PostgreSQL
type IndexerService(logger: ILogger<IndexerService>, configuration: IConfiguration) =
    inherit BackgroundService()
    
    let mutable indexer: IIndexer option = None
    let mutable isRunning = false
    let mutable lastIndexedHeight = 0UL
    
    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        async {
            logger.LogInformation("Starting Indexer Service...")
            
            try
                // Initialize indexer configuration
                let config = this.GetIndexerConfig()
                indexer <- Some (Indexer.createIndexer config)
                
                logger.LogInformation("Indexer Service initialized successfully")
                isRunning <- true
                
                // Start indexing loop
                this.StartIndexingLoop(stoppingToken)
                
            with
            | ex -> logger.LogError(ex, "Failed to initialize Indexer Service")
            
            logger.LogInformation("Indexer Service stopped")
        } |> Async.StartAsTask :> Task
    
    member private this.GetIndexerConfig() : IndexerConfig =
        {
            Database = {
                Host = configuration.GetValue<string>("Indexer:Database:Host", "localhost")
                Port = configuration.GetValue<int>("Indexer:Database:Port", 5432)
                Database = configuration.GetValue<string>("Indexer:Database:Database", "trellis_indexer")
                Username = configuration.GetValue<string>("Indexer:Database:Username", "trellis")
                Password = configuration.GetValue<string>("Indexer:Database:Password", "trellis_password")
                PoolSize = configuration.GetValue<int>("Indexer:Database:PoolSize", 10)
                CommandTimeout = configuration.GetValue<int>("Indexer:Database:CommandTimeout", 30)
            }
            BatchSize = configuration.GetValue<int>("Indexer:BatchSize", 1000)
            FlushInterval = TimeSpan.FromSeconds(configuration.GetValue<double>("Indexer:FlushInterval", 30.0))
            EnableCompression = configuration.GetValue<bool>("Indexer:EnableCompression", true)
            EnablePruning = configuration.GetValue<bool>("Indexer:EnablePruning", true)
            PruningAge = TimeSpan.FromDays(configuration.GetValue<double>("Indexer:PruningAge", 365.0))
            EnableTimeSeries = configuration.GetValue<bool>("Indexer:EnableTimeSeries", true)
        }
    
    member private this.StartIndexingLoop(cancellationToken: CancellationToken) =
        Task.Run(fun () ->
            async {
                while not cancellationToken.IsCancellationRequested && isRunning do
                    try
                        // Get latest blocks from chain state
                        let newBlocks = this.GetNewBlocks()
                        
                        // Index new blocks and transactions
                        for block in newBlocks do
                            do! this.IndexBlock(block)
                            
                            // Index transactions in the block
                            for (i, tx) in block.Transactions |> List.indexed do
                                let blockHash = match block.Header.Hash with | Hash h -> Convert.ToHexString(h)
                                let blockHeight = match block.Header.Height with | BlockHeight h -> h
                                do! this.IndexTransaction(tx, blockHash, blockHeight, i)
                        
                        // Update last indexed height
                        if newBlocks.Length > 0 then
                            let latestBlock = newBlocks |> List.maxBy (fun b -> match b.Header.Height with | BlockHeight h -> h)
                            lastIndexedHeight <- match latestBlock.Header.Height with | BlockHeight h -> h
                        
                        // Flush indexer periodically
                        do! this.FlushIndexer()
                        
                        // Wait before next indexing cycle
                        do! Async.Sleep(TimeSpan.FromSeconds(10.0))
                        
                    with
                    | ex -> logger.LogError(ex, "Error in indexing loop")
                
            } |> Async.RunSynchronously
        ) |> ignore
    
    member private this.GetNewBlocks() : Block list =
        try
            // This would typically read from chain state storage
            // For now, return empty list as we don't have access to the storage layer
            logger.LogDebug("Retrieving new blocks for indexing")
            []
        with
        | ex ->
            logger.LogError(ex, "Error retrieving new blocks")
            []
    
    member private this.IndexBlock(block: Block) =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexBlock(block)
                logger.LogDebug("Indexed block {BlockHeight}", match block.Header.Height with | BlockHeight h -> h)
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    member private this.IndexTransaction(tx: Transaction, blockHash: string, blockHeight: uint64, index: int) : Async<unit> =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexTransaction tx blockHash blockHeight index
                logger.LogDebug("Indexed transaction {TxHash}", match tx.Hash with | Hash h -> Convert.ToHexString(h))
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    member private this.FlushIndexer() =
        async {
            match indexer with
            | Some idx -> 
                do! idx.Flush()
                logger.LogDebug("Indexer flushed")
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    /// Get indexer statistics
    member this.GetStats() =
        async {
            match indexer with
            | Some idx -> 
                return! idx.GetStats()
            | None -> 
                return {
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
        }
    
    /// Index a specific block (for manual indexing)
    member this.IndexBlockManually(block: Block) =
        async {
            do! this.IndexBlock(block)
        }
    
    /// Index a specific transaction (for manual indexing)
    member this.IndexTransactionManually(tx: Transaction, blockHash: string, blockHeight: uint64, index: int) =
        async {
            do! this.IndexTransaction(tx, blockHash, blockHeight, index)
        }
    
    /// Index account data
    member this.IndexAccount(address: Address, account: Account) : Async<unit> =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexAccount address account
                logger.LogDebug("Indexed account {Address}", match address with | Address a -> Convert.ToHexString(a))
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    /// Index validator data
    member this.IndexValidator(publicKey: PublicKey, validator: obj) =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexValidator publicKey validator
                logger.LogDebug("Indexed validator {PublicKey}", match publicKey with | PublicKey pk -> Convert.ToHexString(pk))
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    /// Index compliance data
    member this.IndexCompliance(id: string, transactionHash: string, address: string, complianceType: string, status: string, timestamp: DateTime, metadata: string, auditTrail: string) =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexCompliance id transactionHash address complianceType status timestamp metadata auditTrail
                logger.LogDebug("Indexed compliance data {Id}", id)
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    /// Index time-lock data
    member this.IndexTimeLock(id: string, transactionHash: string, address: string, unlockTime: DateTime, amount: uint64, status: string) =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexTimeLock id transactionHash address unlockTime amount status
                logger.LogDebug("Indexed time-lock {Id}", id)
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    /// Index multi-signature data
    member this.IndexMultiSig(id: string, transactionHash: string, address: string, requiredSignatures: int, totalSignatures: int, signatures: string list, status: string) =
        async {
            match indexer with
            | Some idx -> 
                do! idx.IndexMultiSig id transactionHash address requiredSignatures totalSignatures signatures status
                logger.LogDebug("Indexed multi-sig {Id}", id)
            | None -> 
                logger.LogWarning("Indexer not available")
        }
    
    /// Get the indexer instance
    member this.GetIndexer() : IIndexer option =
        indexer

/// Indexer API service for external access
type IndexerApiService(logger: ILogger<IndexerApiService>, indexerService: IndexerService) =
    
    /// Get indexer statistics
    member this.GetStats() =
        async {
            return! indexerService.GetStats()
        }
    
    /// Get blocks by height range
    member this.GetBlocks(startHeight: uint64, endHeight: uint64) =
        async {
            try
                logger.LogInformation("Querying blocks from height {StartHeight} to {EndHeight}", startHeight, endHeight)
                // This would query the PostgreSQL database
                // For now, return empty list
                return []
            with
            | ex ->
                logger.LogError(ex, "Error querying blocks from {StartHeight} to {EndHeight}", startHeight, endHeight)
                return []
        }
    
    /// Get transactions by address
    member this.GetTransactionsByAddress(address: string, limit: int) =
        async {
            try
                logger.LogInformation("Querying transactions for address {Address} with limit {Limit}", address, limit)
                // This would query the PostgreSQL database
                // For now, return empty list
                return []
            with
            | ex ->
                logger.LogError(ex, "Error querying transactions for address {Address}", address)
                return []
        }
    
    /// Get compliance data by address
    member this.GetComplianceData(address: string, complianceType: string) =
        async {
            try
                logger.LogInformation("Querying compliance data for address {Address} type {Type}", address, complianceType)
                // This would query the PostgreSQL database
                // For now, return empty list
                return []
            with
            | ex ->
                logger.LogError(ex, "Error querying compliance data for address {Address}", address)
                return []
        }
    
    /// Get time-lock data by address
    member this.GetTimeLockData(address: string) =
        async {
            try
                logger.LogInformation("Querying time-lock data for address {Address}", address)
                // This would query the PostgreSQL database
                // For now, return empty list
                return []
            with
            | ex ->
                logger.LogError(ex, "Error querying time-lock data for address {Address}", address)
                return []
        }
    
    /// Get multi-signature data by address
    member this.GetMultiSigData(address: string) =
        async {
            try
                logger.LogInformation("Querying multi-signature data for address {Address}", address)
                // This would query the PostgreSQL database
                // For now, return empty list
                return []
            with
            | ex ->
                logger.LogError(ex, "Error querying multi-signature data for address {Address}", address)
                return []
        }
