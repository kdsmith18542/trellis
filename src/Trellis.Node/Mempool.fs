namespace Trellis.Node

open System
open System.IO
open Trellis.Core
open SimpleTypes

/// Minimal mempool with signature + nonce checks and simple fee priority
module Mempool =

    type private MempoolState = {
        Txs: Map<Hash, Transaction>
        PerSender: Map<Address, Set<Nonce>>
        MaxSize: int
        Db: RocksDbSharp.RocksDb
    }

    let private dataDir = Path.Combine(AppContext.BaseDirectory, "data")
    let private dbPath = Path.Combine(dataDir, "mempool.db")

    let private toHex (bytes: byte[]) = System.Convert.ToHexString(bytes)
    let private fromHex (hex: string) = System.Convert.FromHexString(hex)

    let private serializeTx (tx: Transaction) : string =
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

    let private loadDb (db: RocksDbSharp.RocksDb) : (Hash * Transaction) list =
        use it = db.NewIterator()
        it.Seek([| byte 'm'; byte 'p'; byte ':' |])
        let results = System.Collections.Generic.List<Hash * Transaction>()
        let prefix = [| byte 'm'; byte 'p'; byte ':' |]
        let mutable cont = it.Valid()
        while cont do
            let key = it.Key()
            if key.Length >= 3 && key.[0..2] = prefix then
                let value = it.Value()
                let line = System.Text.Encoding.UTF8.GetString(value)
                match deserializeTx line with
                | Some tx -> results.Add((tx.Hash, tx))
                | None -> ()
                it.Next(); cont <- it.Valid()
            else
                cont <- false
        results |> Seq.toList

    let private putTx (db: RocksDbSharp.RocksDb) (tx: Transaction) =
        let key =
            match tx.Hash with | Hash b ->
                let id = System.Convert.ToHexString(b)
                System.Text.Encoding.UTF8.GetBytes("mp:" + id)
        let value = System.Text.Encoding.UTF8.GetBytes(serializeTx tx)
        db.Put(key, value)

    let private delTx (db: RocksDbSharp.RocksDb) (h: Hash) =
        let key =
            match h with | Hash b -> System.Text.Encoding.UTF8.GetBytes("mp:" + System.Convert.ToHexString(b))
        db.Remove(key)

    type private Msg =
        | Add of Transaction * AsyncReplyChannel<Result<Hash,string>>
        | Remove of Hash * AsyncReplyChannel<unit>
        | Get of int * AsyncReplyChannel<Transaction list>

    let private agent = lazy (MailboxProcessor.Start(fun inbox ->
        let db = openDb()
        let initMap = loadDb db
        let perSender =
            initMap
            |> List.fold (fun acc (_, tx) ->
                let (Nonce n) = tx.Nonce
                let set = acc |> Map.tryFind tx.From |> Option.defaultValue Set.empty
                let set' = set.Add(Nonce n)
                acc.Add(tx.From, set')
            ) Map.empty
        let initState = {
            Txs = Map.ofList initMap
            PerSender = perSender
            MaxSize = Trellis.Core.TrellisConfig.current.MempoolMaxSize
            Db = db
        }
        let rec loop (st: MempoolState) = async {
            let! msg = inbox.Receive()
            match msg with
            | Add (tx, reply) ->
                if st.Txs.Count >= st.MaxSize then
                    // evict lowest fee (simple)
                    let toList = st.Txs |> Map.toList |> List.map snd
                    let sorted = toList |> List.sortBy (fun t -> match t.Fee with | Balance f -> f)
                    match sorted with
                    | evict :: _ ->
                        let st' = { st with Txs = st.Txs.Remove(evict.Hash) }
                        delTx st'.Db evict.Hash
                        let st'' = { st' with Txs = st'.Txs.Add(tx.Hash, tx) }
                        putTx st''.Db tx
                        reply.Reply (Ok tx.Hash)
                        return! loop st''
                    | [] -> reply.Reply (Error "mempool_full"); return! loop st
                else
                    // Check for duplicate transaction hash (transaction already in mempool)
                    if st.Txs.ContainsKey(tx.Hash) then
                        reply.Reply (Error "duplicate_transaction"); return! loop st
                    else
                        // Prevent duplicate nonce per sender
                        let existingNonces = st.PerSender |> Map.tryFind tx.From |> Option.defaultValue Set.empty
                        let (Nonce tn) = tx.Nonce
                        if existingNonces.Contains(Nonce tn) then
                            reply.Reply (Error "duplicate_nonce"); return! loop st
                        else
                            putTx st.Db tx
                            let perSender' = st.PerSender.Add(tx.From, existingNonces.Add(Nonce tn))
                            reply.Reply (Ok tx.Hash)
                            return! loop { st with Txs = st.Txs.Add(tx.Hash, tx); PerSender = perSender' }
            | Remove (h, ack) ->
                // Also remove from PerSender map
                let toRemoveTx = st.Txs |> Map.tryFind h
                let st' = { st with Txs = st.Txs.Remove(h) }
                let st'' =
                    match toRemoveTx with
                    | Some tx ->
                        let nonces = st'.PerSender |> Map.tryFind tx.From |> Option.defaultValue Set.empty
                        let nonces' = nonces.Remove(tx.Nonce)
                        { st' with PerSender = st'.PerSender.Add(tx.From, nonces') }
                    | None -> st'
                delTx st'.Db h
                ack.Reply(()); return! loop st''
            | Get (count, reply) ->
                let txs = st.Txs |> Map.toList |> List.map snd |> List.sortByDescending (fun t -> match t.Fee with | Balance f -> f) |> List.truncate count
                reply.Reply txs; return! loop st
        }
        loop initState))

    /// Accept a transaction into the mempool
    let acceptTransaction (tx: Transaction) (getAccount: Address -> Account option) : Result<Hash,string> =
        // Signature check
        if not (Trellis.Core.SimpleCrypto.verifyTransactionSimple tx) then Error "bad_signature" else
        // Nonce policy: tx.Nonce must be account.Nonce + 1, or 1 if new account
        let account = getAccount tx.From
        let okNonce =
            match account with
            | Some acc -> let (Nonce an) = acc.Nonce in let (Nonce tn) = tx.Nonce in tn = an + 1UL
            | None -> let (Nonce tn) = tx.Nonce in tn = 1UL
        if not okNonce then Error "invalid_nonce" else
        // Add
        let res = agent.Value.PostAndReply(fun rc -> Add (tx, rc))
        res

    /// Remove a transaction after inclusion
    let remove (hash: Hash) : unit =
        agent.Value.PostAndReply(fun rc -> Remove (hash, rc)) |> ignore

    /// Get up to count transactions sorted by fee desc
    let getTransactions (count: int) : Transaction list =
        agent.Value.PostAndReply(fun rc -> Get (count, rc))
