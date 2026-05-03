namespace Trellis.Core

open System
open System.IO
open SimpleTypes

/// Production-grade state snapshot system for Trellis
module StateSnapshots =

    /// Snapshot metadata
    type SnapshotMetadata = {
        Height: BlockHeight
        Hash: Hash
        Timestamp: Timestamp
        AccountCount: uint64
        ValidatorCount: uint64
    }

    /// State snapshot at a specific height
    type StateSnapshot = {
        Metadata: SnapshotMetadata
        Accounts: Map<Address, Account>
        Validators: Map<PublicKey, (Balance * float * uint64)>
        ChainState: ChainState
    }

    /// Write-ahead journal entry
    type JournalEntry = {
        Timestamp: Timestamp
        Operation: string
        Key: string
        Value: byte array option
        Height: BlockHeight
    }

    /// Write-ahead journal for state changes
    type WriteAheadJournal(dbPath: string) =
        let mutable entries: JournalEntry list = []
        let journalFile = Path.Combine(dbPath, "journal.json")

        let loadJournal() =
            if File.Exists(journalFile) then
                try
                    let json = File.ReadAllText(journalFile)
                    let parsed = System.Text.Json.JsonSerializer.Deserialize<JournalEntry list>(json)
                    entries <- parsed
                with _ -> entries <- []
            else
                entries <- []

        let saveJournal() =
            let json = System.Text.Json.JsonSerializer.Serialize(entries)
            File.WriteAllText(journalFile, json)

        do loadJournal()

        member this.AddEntry(entry: JournalEntry) =
            entries <- entry :: entries
            saveJournal()

        member this.GetEntries() = entries

        member this.Clear() =
            entries <- []
            if File.Exists(journalFile) then
                File.Delete(journalFile)

        interface IDisposable with
            member this.Dispose() = ()

    /// Snapshot manager for state reconstruction
    type SnapshotManager(dbPath: string) =
        let snapshotsDir = Path.Combine(dbPath, "snapshots")
        let journal = new WriteAheadJournal(dbPath)

        do
            if not (Directory.Exists(snapshotsDir)) then
                Directory.CreateDirectory(snapshotsDir) |> ignore

        /// Create snapshot at current state
        member this.CreateSnapshot(height: BlockHeight, hash: Hash, accounts: Map<Address, Account>, 
                                 validators: Map<PublicKey, (Balance * float * uint64)>, chainState: ChainState) : StateSnapshot =
            let timestamp = Timestamp DateTime.UtcNow
            let accountCount = uint64 accounts.Count
            let validatorCount = uint64 validators.Count
            
            let metadata = {
                Height = height
                Hash = hash
                Timestamp = timestamp
                AccountCount = accountCount
                ValidatorCount = validatorCount
            }

            {
                Metadata = metadata
                Accounts = accounts
                Validators = validators
                ChainState = chainState
            }

        /// Save snapshot to disk
        member this.SaveSnapshot(snapshot: StateSnapshot) : unit =
            let filename = sprintf "snapshot_%d_%s.json" (match snapshot.Metadata.Height with BlockHeight h -> h) 
                                                      (match snapshot.Metadata.Hash with Hash h -> System.Convert.ToHexString(h))
            let filepath = Path.Combine(snapshotsDir, filename)
            
            let json = System.Text.Json.JsonSerializer.Serialize(snapshot, 
                System.Text.Json.JsonSerializerOptions(WriteIndented = true))
            File.WriteAllText(filepath, json)

        /// Load snapshot from disk
        member this.LoadSnapshot(height: BlockHeight) : StateSnapshot option =
            let filename = sprintf "snapshot_%d_*.json" (match height with BlockHeight h -> h)
            let files = Directory.GetFiles(snapshotsDir, filename)
            
            if files.Length > 0 then
                try
                    let json = File.ReadAllText(files.[0])
                    Some (System.Text.Json.JsonSerializer.Deserialize<StateSnapshot>(json))
                with _ -> None
            else
                None

        /// Get latest snapshot
        member this.GetLatestSnapshot() : StateSnapshot option =
            let files = Directory.GetFiles(snapshotsDir, "snapshot_*.json")
            if files.Length > 0 then
                let latestFile = files |> Array.maxBy (fun f -> File.GetCreationTime(f))
                try
                    let json = File.ReadAllText(latestFile)
                    Some (System.Text.Json.JsonSerializer.Deserialize<StateSnapshot>(json))
                with _ -> None
            else
                None

        /// Clean old snapshots (keep last N)
        member this.CleanOldSnapshots(keepCount: int) : unit =
            let files = Directory.GetFiles(snapshotsDir, "snapshot_*.json")
                           |> Array.sortByDescending (fun f -> File.GetCreationTime(f))
            
            if files.Length > keepCount then
                for i in keepCount .. (files.Length - 1) do
                    File.Delete(files.[i])

        interface IDisposable with
            member this.Dispose() =
                (journal :> IDisposable).Dispose()