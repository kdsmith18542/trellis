namespace Trellis.Node

open System
open System.IO
open Trellis.Core
open SimpleTypes
open EnhancedMempool

/// Production-grade mempool service with per-sender queues, EIP-1559 support, and persistence
module Mempool =

    type private MempoolMsg =
        | Add of Transaction * (Address -> Nonce option) * AsyncReplyChannel<Result<unit, string>>
        | Remove of Hash
        | GetForBlock of uint64 * AsyncReplyChannel<Transaction list>
        | UpdateBaseFee of uint64
        | GetStats of AsyncReplyChannel<Map<string, obj>>

    let private dataDir = Path.Combine(AppContext.BaseDirectory, "data")
    let private dbPath = Path.Combine(dataDir, "mempool.db")

    let private toHex (bytes: byte[]) = Convert.ToHexString(bytes)
    let private fromHex (hex: string) = Convert.FromHexString(hex)

    let private serializeTx (tx: Transaction) : string =
        // Simple CSV serialization for mempool persistence
        let h = match tx.Hash with | Hash b -> toHex b
        let fromA = match tx.From with | Address a -> toHex a
        let toA = match tx.To with | Address a -> toHex a
        let (Balance amt) = tx.Amount
        let (Balance fee) = tx.Fee
        let (Nonce n) = tx.Nonce
        let sigHex = match tx.Signature with | Signature s -> toHex s
        let pk = tx.Metadata |> Map.tryFind "pubkey" |> Option.defaultValue ""
        sprintf "%s,%s,%s,%d,%d,%d,%s,%s" h fromA toA amt fee n sigHex pk

    let private deserializeTx (line: string) : Transaction option =
        try
            let parts = line.Split(',')
            if parts.Length < 8 then None else
            let h = Hash (fromHex parts.[0])
            let fromA = Address (fromHex parts.[1])
            let toA = Address (fromHex parts.[2])
            let amt = Balance (UInt64.Parse(parts.[3]))
            let fee = Balance (UInt64.Parse(parts.[4]))
            let nonce = Nonce (UInt64.Parse(parts.[5]))
            let signatureValue = Signature (fromHex parts.[6])
            let pk = parts.[7]
            Some {
                From = fromA
                To = toA
                Amount = amt
                Nonce = nonce
                Fee = fee
                Signature = signatureValue
                Hash = h
                GasLimit = 21000UL
                GasPrice = 20UL
                Data = Array.empty
                Metadata = Map.ofList [ ("pubkey", pk) ]
                MaxFeePerGas = None
                MaxPriorityFeePerGas = None
                TransactionType = 0uy
            }
        with _ -> None

    let private ensureDir() = if not (Directory.Exists(dataDir)) then Directory.CreateDirectory(dataDir) |> ignore

    let private openDb() =
        ensureDir()
        let opts = new RocksDbSharp.DbOptions()
        opts.SetCreateIfMissing(true) |> ignore
        RocksDbSharp.RocksDb.Open(opts, dbPath)

    let private loadDb (db: RocksDbSharp.RocksDb) : Transaction list =
        use it = db.NewIterator()
        it.Seek([| byte 'm'; byte 'p'; byte ':' |])
        let results = ResizeArray<Transaction>()
        let prefix = [| byte 'm'; byte 'p'; byte ':' |]
        let mutable cont = it.Valid()
        while cont do
            let key = it.Key()
            if key.Length >= 3 && key.[0..2] = prefix then
                let value = it.Value()
                let line = System.Text.Encoding.UTF8.GetString(value)
                match deserializeTx line with
                | Some tx -> results.Add(tx)
                | None -> ()
                it.Next(); cont <- it.Valid()
            else
                cont <- false
        results |> Seq.toList

    let private persistAdd (db: RocksDbSharp.RocksDb) (tx: Transaction) =
        let (Hash hb) = tx.Hash
        let key = System.Text.Encoding.UTF8.GetBytes("mp:" + toHex hb)
        let value = System.Text.Encoding.UTF8.GetBytes(serializeTx tx)
        db.Put(key, value)

    let private persistRemove (db: RocksDbSharp.RocksDb) (h: Hash) =
        let (Hash hb) = h
        let key = System.Text.Encoding.UTF8.GetBytes("mp:" + toHex hb)
        db.Remove(key)

    let private agent = lazy (MailboxProcessor.Start(fun inbox ->
        let db = openDb()
        let cfg = TrellisConfig.current
        let initialState = createMempoolState cfg.MempoolMaxSize cfg.MaxTransactionsPerSender cfg.GenesisBaseFeePerGas
        
        // Hydrate from DB
        let savedTxs = loadDb db
        for tx in savedTxs do
            // We use a dummy nonce check during hydration since we trust the DB
            addTransaction initialState tx (fun _ -> Some tx.Nonce) 0.0 |> ignore
            
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | Add (tx, getNonce, reply) ->
                let res = addTransaction initialState tx getNonce 10.0 // 10% min bump
                if res.IsOk then persistAdd db tx
                reply.Reply res
            | Remove hash ->
                removeTransaction initialState hash
                persistRemove db hash
            | GetForBlock (gasLimit, reply) ->
                let txs = selectTransactionsForBlock initialState gasLimit
                reply.Reply txs
            | UpdateBaseFee fee ->
                updateBaseFee initialState fee
            | GetStats reply ->
                reply.Reply (getStatistics initialState)
            
            return! loop()
        }
        loop()))

    /// Public API for the Mempool
    
    let acceptTransaction (tx: Transaction) (getAccount: Address -> Account option) : Result<Hash, string> =
        // Basic validation
        if not (Trellis.Core.SimpleCrypto.verifyTransactionSimple tx) then Error "bad_signature"
        else
            let getNonce addr = getAccount addr |> Option.map (fun a -> a.Nonce)
            let res = agent.Value.PostAndReply(fun rc -> Add (tx, getNonce, rc))
            match res with
            | Ok () -> Ok tx.Hash
            | Error e -> Error e

    let remove (hash: Hash) : unit =
        agent.Value.Post(Remove hash)

    let selectTransactions (gasLimit: uint64) : Transaction list =
        agent.Value.PostAndReply(fun rc -> GetForBlock (gasLimit, rc))

    let updateBaseFee (fee: uint64) : unit =
        agent.Value.Post(UpdateBaseFee fee)

    let getStats () : Map<string, obj> =
        agent.Value.PostAndReply(fun rc -> GetStats rc)

    // Backwards compatibility for old API if needed
    let getTransactions (count: int) : Transaction list =
        selectTransactions 1000000UL |> List.truncate count
