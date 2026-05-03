namespace Trellis.Core

open System
open System.Threading
open System.Security.Cryptography
open SimpleTypes

/// Multi-threaded PoW mining with proper difficulty target
module Mining =

    /// Target threshold from difficulty
    let private difficultyToTarget (difficulty: uint64) : uint64 =
        if difficulty = 0UL then UInt64.MaxValue
        else UInt64.MaxValue / difficulty

    /// Hash block header with given nonce using SHA-256 (Bitcoin-style)
    let hashHeader (header: byte array) (nonce: uint64) : Hash =
        let nonceBytes = BitConverter.GetBytes(nonce)
        let headerWithNonce = Array.concat [header; nonceBytes]
        use sha = SHA256.Create()
        let h1 = sha.ComputeHash(headerWithNonce)
        let h2 = sha.ComputeHash(h1)
        Hash h2

    /// Check if block hash meets difficulty target
    let meetsDifficulty (blockHash: Hash) (difficulty: uint64) : bool =
        let (Hash hashBytes) = blockHash
        if hashBytes.Length < 8 then false else
        let hashPrefix = BitConverter.ToUInt64(hashBytes, 0)
        hashPrefix <= difficultyToTarget difficulty

    /// Mine a block: find a valid nonce for the given header and difficulty
    let mineBlock (header: byte array) (difficulty: uint64) (startNonce: uint64) (maxNonces: uint64) (ct: CancellationToken) : Hash option =
        let target = difficultyToTarget difficulty
        let mutable nonce = startNonce
        let endNonce = startNonce + maxNonces
        use sha = SHA256.Create()
        let mutable result: Hash option = None

        while nonce < endNonce && not ct.IsCancellationRequested && result.IsNone do
            let nonceBytes = BitConverter.GetBytes(nonce)
            let headerWithNonce = Array.concat [header; nonceBytes]
            let h1 = sha.ComputeHash(headerWithNonce)
            let h2 = sha.ComputeHash(h1)
            if h2.Length >= 8 && BitConverter.ToUInt64(h2, 0) <= target then
                result <- Some(Hash h2)
            nonce <- nonce + 1UL

        result

    /// Multi-threaded mining: spawn N threads, each scanning a nonce range
    let mineBlockParallel (header: byte array) (difficulty: uint64) (numThreads: int) (noncesPerThread: uint64) : Hash * uint64 =
        use cts = new CancellationTokenSource()
        let results = Array.create numThreads None
        let threads = Array.create numThreads (Unchecked.defaultof<Thread>)

        for i in 0 .. numThreads - 1 do
            let threadIndex = i
            threads.[i] <- Thread(fun () ->
                let startNonce = uint64 threadIndex * noncesPerThread
                let result = mineBlock header difficulty startNonce noncesPerThread cts.Token
                if result.IsSome then
                    try cts.Cancel() with _ -> ()
                results.[threadIndex] <- result
            )

        for t in threads do t.Start()
        for t in threads do t.Join()

        let found = results |> Array.tryPick id
        match found with
        | Some hash -> hash, 0UL
        | None -> Hash(Array.zeroCreate<byte> 32), noncesPerThread * uint64 numThreads

    /// Serialize block header to bytes for mining
    let serializeHeaderForMining (block: Block) : byte array =
        let (BlockHeight h) = block.Header.Height
        let (Hash prevHash) = block.Header.PreviousHash
        let (Hash merkleRoot) = block.Header.MerkleRoot
        let (Timestamp ts) = block.Header.Timestamp
        Array.concat [
            BitConverter.GetBytes(h)
            prevHash
            merkleRoot
            BitConverter.GetBytes(ts.Ticks)
            BitConverter.GetBytes(block.Header.Difficulty)
        ]
