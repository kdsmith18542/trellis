namespace Trellis.Core

open System
open RocksDbSharp
open SimpleTypes

/// Simplified RocksDB integration for Phase 3
module SimpleRocksDB =

    /// Simple RocksDB storage with head pointer
    type SimpleRocksDBStorage(dbPath: string) =
        let mutable db: RocksDb option = None
        let mutable isOpen = false

        let openDB () =
            if not isOpen then
                let options = new DbOptions()
                
                // Basic configuration
                options.SetCreateIfMissing(true) |> ignore
                
                // Production performance optimizations
                options.SetMaxOpenFiles(10000) |> ignore
                options.SetWriteBufferSize(128UL * 1024UL * 1024UL) |> ignore
                options.SetMaxWriteBufferNumber(6) |> ignore
                options.SetTargetFileSizeBase(128UL * 1024UL * 1024UL) |> ignore
                options.SetMaxBytesForLevelBase(1024UL * 1024UL * 1024UL) |> ignore
                
                // Compression for space efficiency
                options.SetCompression(Compression.Lz4) |> ignore
                
                // Bloom filters for faster lookups
                options.SetBloomLocality(1u) |> ignore
                options.SetMemtablePrefixBloomSizeRatio(0.1) |> ignore
                
                // Advanced optimizations
                options.SetLevel0FileNumCompactionTrigger(8) |> ignore
                options.SetLevel0StopWritesTrigger(32) |> ignore
                options.SetMaxBackgroundCompactions(8) |> ignore
                options.SetMaxBackgroundFlushes(4) |> ignore
                
                db <- Some (RocksDb.Open(options, dbPath))
                isOpen <- true

        let closeDB () =
            if isOpen then
                match db with
                | Some database -> 
                    database.Dispose()
                    db <- None
                | None -> ()
                isOpen <- false

        /// Store canonical head pointer
        member this.SetCanonicalHead(head: Hash) : unit =
            openDB()
            match db with
            | Some database ->
                let (Hash headBytes) = head
                database.Put("canonical_head"B, headBytes)
            | None -> ()

        /// Get canonical head pointer
        member this.GetCanonicalHead() : Hash option =
            openDB()
            match db with
            | Some database ->
                let headBytes = database.Get("canonical_head"B)
                if headBytes <> null then
                    Some (Hash headBytes)
                else None
            | None -> None

        /// Store block
        member this.StoreBlock(block: Block) : unit =
            openDB()
            match db with
            | Some database ->
                let (Hash blockHash) = block.Header.Hash
                let blockData = Serialization.serializeBlock block
                database.Put(blockHash, blockData)
            | None -> ()

        /// Get block by hash
        member this.GetBlock(hash: Hash) : Block option =
            openDB()
            match db with
            | Some database ->
                let (Hash hashBytes) = hash
                let blockData = database.Get(hashBytes)
                if blockData <> null then
                    try
                        Some (Serialization.deserializeBlock blockData)
                    with _ -> None
                else None
            | None -> None

        /// Store account
        member this.StoreAccount(account: Account) : unit =
            openDB()
            match db with
            | Some database ->
                let (Address addrBytes) = account.Address
                let accountData = Serialization.serializeAccount account
                database.Put(Array.append "account:"B addrBytes, accountData)
            | None -> ()

        /// Get account by address
        member this.GetAccount(address: Address) : Account option =
            openDB()
            match db with
            | Some database ->
                let (Address addrBytes) = address
                let accountData = database.Get(Array.append "account:"B addrBytes)
                if accountData <> null then
                    try
                        Some (Serialization.deserializeAccount accountData)
                    with _ -> None
                else None
            | None -> None

        /// Store chain state
        member this.StoreChainState(state: ChainState) : unit =
            openDB()
            match db with
            | Some database ->
                let stateData = Serialization.serializeChainState state
                database.Put("chainstate"B, stateData)
            | None -> ()

        /// Get chain state
        member this.GetChainState() : ChainState option =
            openDB()
            match db with
            | Some database ->
                let stateData = database.Get("chainstate"B)
                if stateData <> null then
                    try
                        Some (Serialization.deserializeChainState stateData)
                    with _ -> None
                else None
            | None -> None

        /// Delete account
        member this.DeleteAccount(address: Address) : unit =
            openDB()
            match db with
            | Some database ->
                let (Address addrBytes) = address
                database.Remove(Array.append "account:"B addrBytes)
            | None -> ()

        /// Delete block
        member this.DeleteBlock(hash: Hash) : unit =
            openDB()
            match db with
            | Some database ->
                let (Hash hashBytes) = hash
                database.Remove(hashBytes)
            | None -> ()

        /// Batch operations for atomic transactions
        member this.BatchOperations(operations: (byte array * byte array * bool) list) : unit =
            openDB()
            match db with
            | Some database ->
                let writeOptions = new WriteOptions()
                let batch = new WriteBatch()
                
                for (key, value, isDelete) in operations do
                    if isDelete then
                        batch.Delete(key)
                    else
                        batch.Put(key, value)
                
                database.Write(batch, writeOptions)
                batch.Dispose()
            | None -> ()

        /// Store validator
        member this.StoreValidator(publicKey: PublicKey, stake: Balance, performance: float, lastActive: uint64) : unit =
            openDB()
            match db with
            | Some database ->
                let (PublicKey pkBytes) = publicKey
                let validatorData = System.Text.Json.JsonSerializer.Serialize({| 
                    stake = (match stake with Balance s -> s)
                    performance = performance
                    lastActive = lastActive
                |})
                database.Put(Array.append "validator:"B pkBytes, System.Text.Encoding.UTF8.GetBytes(validatorData))
            | None -> ()

        /// Get validator
        member this.GetValidator(publicKey: PublicKey) : (Balance * float * uint64) option =
            openDB()
            match db with
            | Some database ->
                let (PublicKey pkBytes) = publicKey
                let validatorData = database.Get(Array.append "validator:"B pkBytes)
                if validatorData <> null then
                    try
                        let json = System.Text.Encoding.UTF8.GetString(validatorData)
                        let root = System.Text.Json.JsonDocument.Parse(json).RootElement
                        let stake = Balance (root.GetProperty("stake").GetUInt64())
                        let performance = root.GetProperty("performance").GetDouble()
                        let lastActive = root.GetProperty("lastActive").GetUInt64()
                        Some (stake, performance, lastActive)
                    with _ -> None
                else None
            | None -> None

        /// Get all validators
        member this.GetAllValidators() : (PublicKey * Balance * float * uint64) list =
            openDB()
            match db with
            | Some database ->
                let iterator = database.NewIterator()
                iterator.SeekToFirst()
                let mutable validators = []
                
                while iterator.Valid() do
                    let key = iterator.Key()
                    if key.Length > 10 && key.[0..9] = "validator:"B then
                        try
                            let pkBytes = key.[10..] // Skip "validator:" prefix
                            let publicKey = PublicKey pkBytes
                            let value = iterator.Value()
                            let json = System.Text.Encoding.UTF8.GetString(value)
                            let root = System.Text.Json.JsonDocument.Parse(json).RootElement
                            let stake = Balance (root.GetProperty("stake").GetUInt64())
                            let performance = root.GetProperty("performance").GetDouble()
                            let lastActive = root.GetProperty("lastActive").GetUInt64()
                            validators <- (publicKey, stake, performance, lastActive) :: validators
                        with _ -> ()
                    iterator.Next()
                
                iterator.Dispose()
                validators
            | None -> []

        /// Store transaction with simple indexing
        member this.StoreTransaction(tx: Transaction) : unit =
            openDB()
            match db with
            | Some database ->
                let (Hash txHash) = SimpleCrypto.transactionHashSimple tx
                let txData = Serialization.serializeTransaction tx
                database.Put(txHash, txData)
                
                // Simple address indexing using comma-separated values
                let (Address fromAddr) = tx.From
                let (Address toAddr) = tx.To
                let txHashHex = System.Convert.ToHexString(txHash)
                
                // Index by from address
                let fromKey = Array.append "tx_from:"B fromAddr
                let fromData = database.Get(fromKey)
                let fromList = 
                    if fromData <> null then 
                        System.Text.Encoding.UTF8.GetString(fromData).Split(',') |> Array.toList
                    else []
                let newFromList = txHashHex :: fromList
                let fromListStr = String.concat "," newFromList
                database.Put(fromKey, System.Text.Encoding.UTF8.GetBytes(fromListStr))
                
                // Index by to address  
                let toKey = Array.append "tx_to:"B toAddr
                let toData = database.Get(toKey)
                let toList = 
                    if toData <> null then 
                        System.Text.Encoding.UTF8.GetString(toData).Split(',') |> Array.toList
                    else []
                let newToList = txHashHex :: toList
                let toListStr = String.concat "," newToList
                database.Put(toKey, System.Text.Encoding.UTF8.GetBytes(toListStr))
            | None -> ()

        /// Get transaction hashes for address
        member this.GetTransactionHashesForAddress(address: Address) : string list =
            openDB()
            match db with
            | Some database ->
                let (Address addrBytes) = address
                let fromKey = Array.append "tx_from:"B addrBytes
                let toKey = Array.append "tx_to:"B addrBytes
                let mutable hashes = []
                
                // Get from address hashes
                let fromData = database.Get(fromKey)
                if fromData <> null then
                    let fromList = System.Text.Encoding.UTF8.GetString(fromData).Split(',') |> Array.toList
                    hashes <- fromList @ hashes
                
                // Get to address hashes
                let toData = database.Get(toKey)
                if toData <> null then
                    let toList = System.Text.Encoding.UTF8.GetString(toData).Split(',') |> Array.toList
                    hashes <- toList @ hashes
                
                hashes |> List.distinct
            | None -> []

        interface IDisposable with
            member this.Dispose() =
                closeDB()

    /// Create new storage instance
    let createStorage (dbPath: string) : SimpleRocksDBStorage =
        new SimpleRocksDBStorage(dbPath)