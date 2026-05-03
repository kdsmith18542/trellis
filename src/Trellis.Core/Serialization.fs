namespace Trellis.Core

open System
open System.Text.Json
open System.Text.Json.Serialization
open SimpleTypes

/// Canonical JSON-based serialization for core types (temporary until protobuf/RLP)
module Serialization =

    let private toHex (b: byte[]) = Convert.ToHexString(b)
    let private fromHex (s: string) = Convert.FromHexString(s)

    // Transaction serialization with EIP-1559 support
    let serializeTransaction (tx: Transaction) : byte[] =
        let json = JsonSerializer.Serialize({|
            from = toHex (match tx.From with Address a -> a)
            toAddr = toHex (match tx.To with Address a -> a)
            amount = match tx.Amount with Balance v -> v
            fee = match tx.Fee with Balance v -> v
            nonce = match tx.Nonce with Nonce n -> n
            signature = toHex (match tx.Signature with Signature s -> s)
            hash = toHex (match tx.Hash with Hash h -> h)
            gasLimit = tx.GasLimit
            gasPrice = tx.GasPrice
            data = (tx.Data |> Convert.ToBase64String)
            metadata = tx.Metadata |> Map.toList
            maxFeePerGas = tx.MaxFeePerGas
            maxPriorityFeePerGas = tx.MaxPriorityFeePerGas
            transactionType = tx.TransactionType
        |})
        System.Text.Encoding.UTF8.GetBytes(json)

    let deserializeTransaction (data: byte[]) : Transaction =
        let root = JsonDocument.Parse(data).RootElement
        let tryProp (n: string) =
            match root.TryGetProperty(n) with | true, p -> Some p | _ -> None
        let from = Address (root.GetProperty("from").GetString() |> fromHex)
        let toAddr = Address (root.GetProperty("toAddr").GetString() |> fromHex)
        let amount = Balance (root.GetProperty("amount").GetUInt64())
        let fee = Balance (root.GetProperty("fee").GetUInt64())
        let nonce = Nonce (root.GetProperty("nonce").GetUInt64())
        let signature = Signature (root.GetProperty("signature").GetString() |> fromHex)
        let hash = Hash (root.GetProperty("hash").GetString() |> fromHex)
        let gasLimit = root.GetProperty("gasLimit").GetUInt64()
        let gasPrice = root.GetProperty("gasPrice").GetUInt64()
        let dataBytes = root.GetProperty("data").GetString() |> Convert.FromBase64String
        let metadata = 
            match tryProp "metadata" with
            | Some p -> 
                p.EnumerateArray()
                |> Seq.map (fun je -> (je.GetProperty("Key").GetString(), je.GetProperty("Value").GetString()))
                |> Map.ofSeq
            | None -> Map.empty
        let maxFeePerGas = match tryProp "maxFeePerGas" with | Some p -> Some (p.GetUInt64()) | None -> None
        let maxPriorityFeePerGas = match tryProp "maxPriorityFeePerGas" with | Some p -> Some (p.GetUInt64()) | None -> None
        let transactionType = match tryProp "transactionType" with | Some p -> p.GetByte() | None -> 0uy
        {
            From = from
            To = toAddr
            Amount = amount
            Fee = fee
            Nonce = nonce
            Signature = signature
            Hash = hash
            GasLimit = gasLimit
            GasPrice = gasPrice
            Data = dataBytes
            Metadata = metadata
            MaxFeePerGas = maxFeePerGas
            MaxPriorityFeePerGas = maxPriorityFeePerGas
            TransactionType = transactionType
        }

    // Block
    let serializeBlock (block: Block) : byte[] =
        let header = block.Header
        let txs = block.Transactions |> List.map serializeTransaction |> List.map System.Text.Encoding.UTF8.GetString
        let json =
            JsonSerializer.Serialize({|
                header = {|
                    hash = toHex (match header.Hash with Hash h -> h)
                    height = match header.Height with BlockHeight h -> h
                    timestamp = match header.Timestamp with Timestamp t -> t
                    previousHash = toHex (match header.PreviousHash with Hash h -> h)
                    merkleRoot = toHex (match header.MerkleRoot with Hash h -> h)
                    validator = toHex (match header.Validator with Address a -> a)
                    nonce = header.Nonce
                    difficulty = header.Difficulty
                    powHash = toHex (match header.PoWHash with Hash h -> h)
                    finalized = header.Finalized
                    baseFeePerGas = header.BaseFeePerGas
                    gasUsed = header.GasUsed
                    gasLimit = header.GasLimit
                |}
                transactions = txs
            |})
        System.Text.Encoding.UTF8.GetBytes(json)

    let deserializeBlock (data: byte[]) : Block =
        let root = JsonDocument.Parse(data).RootElement
        let h = root.GetProperty("header")
        let header = {
            Hash = Hash (h.GetProperty("hash").GetString() |> fromHex)
            Height = BlockHeight (h.GetProperty("height").GetUInt64())
            Timestamp = Timestamp (h.GetProperty("timestamp").GetDateTime())
            PreviousHash = Hash (h.GetProperty("previousHash").GetString() |> fromHex)
            MerkleRoot = Hash (h.GetProperty("merkleRoot").GetString() |> fromHex)
            Validator = Address (h.GetProperty("validator").GetString() |> fromHex)
            Nonce = h.GetProperty("nonce").GetUInt64()
            Difficulty = h.GetProperty("difficulty").GetUInt64()
            PoWHash = Hash (h.GetProperty("powHash").GetString() |> fromHex)
            Finalized = h.GetProperty("finalized").GetBoolean()
            BaseFeePerGas = h.GetProperty("baseFeePerGas").GetUInt64()
            GasUsed = h.GetProperty("gasUsed").GetUInt64()
            GasLimit = h.GetProperty("gasLimit").GetUInt64()
        }
        let txs =
            root.GetProperty("transactions").EnumerateArray()
            |> Seq.map (fun je ->
                je.GetString()
                |> System.Text.Encoding.UTF8.GetBytes
                |> deserializeTransaction)
            |> Seq.toList
        { Header = header; Transactions = txs }

    // ChainState (minimal for storage)
    let serializeChainState (state: ChainState) : byte[] =
        let accounts =
            state.Accounts
            |> Map.toList
            |> List.map (fun (addr, acc) ->
                {| addr = toHex (match addr with Address a -> a)
                   balance = match acc.Balance with Balance b -> b
                   nonce = match acc.Nonce with Nonce n -> n |})
        let validators = state.Validators |> List.map (fun a -> toHex (match a with Address b -> b))
        let json =
            JsonSerializer.Serialize({|
                accounts = accounts
                currentHeight = match state.CurrentHeight with BlockHeight h -> h
                lastBlockHash = toHex (match state.LastBlockHash with Hash h -> h)
                validators = validators
                totalSupply = match state.TotalSupply with Balance b -> b
                baseFeePerGas = state.BaseFeePerGas
                gasUsed = state.GasUsed
                gasLimit = state.GasLimit
            |})
        System.Text.Encoding.UTF8.GetBytes(json)

    let deserializeChainState (data: byte[]) : ChainState =
        let root = JsonDocument.Parse(data).RootElement
        let tryProp (name: string) =
            let mutable p = Unchecked.defaultof<JsonElement>
            if root.TryGetProperty(name, &p) then Some p else None
        let accounts =
            match tryProp "accounts" with
            | Some arr ->
                arr.EnumerateArray()
                |> Seq.fold (fun (acc: Map<Address, Account>) je ->
                    let addr = Address (je.GetProperty("addr").GetString() |> fromHex)
                    let bal = Balance (je.GetProperty("balance").GetUInt64())
                    let nonce = Nonce (je.GetProperty("nonce").GetUInt64())
                    acc.Add(addr, { Address = addr; Balance = bal; Nonce = nonce })) Map.empty
            | None -> Map.empty
        let height =
            match tryProp "currentHeight" with
            | Some p -> BlockHeight (p.GetUInt64())
            | None -> BlockHeight 0UL
        let lastHash =
            match tryProp "lastBlockHash" with
            | Some p -> Hash (p.GetString() |> fromHex)
            | None -> Hash [||]
        let validators =
            match tryProp "validators" with
            | Some arr -> arr.EnumerateArray() |> Seq.map (fun je -> Address (je.GetString() |> fromHex)) |> Seq.toList
            | None -> []
        let supply = Balance (match tryProp "totalSupply" with | Some p -> p.GetUInt64() | None -> 0UL)
        let baseFeePerGas = match tryProp "baseFeePerGas" with | Some p -> p.GetUInt64() | None -> 1000000000UL // 1 gwei default
        let gasUsed = match tryProp "gasUsed" with | Some p -> p.GetUInt64() | None -> 0UL
        let gasLimit = match tryProp "gasLimit" with | Some p -> p.GetUInt64() | None -> 30000000UL // 30M gas limit default
        {
            Accounts = accounts
            Blocks = []
            CurrentHeight = height
            LastBlockHash = lastHash
            Validators = validators
            TotalSupply = supply
            BaseFeePerGas = baseFeePerGas
            GasUsed = gasUsed
            GasLimit = gasLimit
        }


    // Account serialization
    let serializeAccount (account: Account) : byte[] =
        let json =
            JsonSerializer.Serialize({|
                address = toHex (match account.Address with Address a -> a)
                balance = match account.Balance with Balance b -> b
                nonce = match account.Nonce with Nonce n -> n
            |})
        System.Text.Encoding.UTF8.GetBytes(json)

    let deserializeAccount (data: byte[]) : Account =
        let root = JsonDocument.Parse(data).RootElement
        let address = Address (root.GetProperty("address").GetString() |> fromHex)
        let balance = Balance (root.GetProperty("balance").GetUInt64())
        let nonce = Nonce (root.GetProperty("nonce").GetUInt64())
        { Address = address; Balance = balance; Nonce = nonce }

    do ()
