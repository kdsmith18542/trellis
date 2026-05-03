namespace Trellis.Node

open System
open System.Collections.Generic
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Enhanced mempool with per-sender queues, nonce gap handling, and EIP-1559 support
module EnhancedMempool =

    /// Transaction with computed effective priority fee
    type MempoolTransaction = {
        Transaction: Transaction
        EffectivePriorityFee: uint64
        ArrivalTime: DateTime
    }

    /// Per-sender transaction queue with nonce management
    type SenderQueue = {
        Address: Address
        Transactions: Map<Nonce, MempoolTransaction>
        NextNonce: Nonce
        PromotableCount: int
    }

    /// Mempool state with per-sender queues
    type MempoolState = {
        mutable SenderQueues: Map<Address, SenderQueue>
        mutable AllTransactions: Map<Hash, MempoolTransaction>
        mutable PromotableTransactions: MempoolTransaction list
        MaxSize: int
        MaxPerSender: int
        mutable BaseFeePerGas: uint64
    }

    /// Calculate effective priority fee for a transaction
    let calculateEffectivePriorityFee (tx: Transaction) (baseFee: uint64) : uint64 =
        match tx.MaxFeePerGas, tx.MaxPriorityFeePerGas with
        | Some maxFee, Some maxPriorityFee ->
            // EIP-1559 transaction
            let effectiveGasPrice = min maxFee (baseFee + maxPriorityFee)
            if effectiveGasPrice > baseFee then
                effectiveGasPrice - baseFee
            else
                0UL
        | _ ->
            // Legacy transaction
            if tx.GasPrice > baseFee then
                tx.GasPrice - baseFee
            else
                0UL

    /// Create a new mempool transaction
    let createMempoolTransaction (tx: Transaction) (baseFee: uint64) : MempoolTransaction =
        {
            Transaction = tx
            EffectivePriorityFee = calculateEffectivePriorityFee tx baseFee
            ArrivalTime = DateTime.UtcNow
        }

    /// Create a new sender queue
    let createSenderQueue (address: Address) (nextNonce: Nonce) : SenderQueue =
        {
            Address = address
            Transactions = Map.empty
            NextNonce = nextNonce
            PromotableCount = 0
        }

    /// Check if a transaction is promotable (no nonce gap)
    let isPromotable (queue: SenderQueue) (nonce: Nonce) : bool =
        let (Nonce queueNonce) = queue.NextNonce
        let (Nonce txNonce) = nonce
        txNonce = queueNonce

    /// Get all promotable transactions from a sender queue
    let rec getPromotableTransactions (queue: SenderQueue) : MempoolTransaction list =
        let (Nonce currentNonce) = queue.NextNonce
        match queue.Transactions.TryFind(Nonce currentNonce) with
        | Some tx ->
            tx :: getPromotableTransactions { queue with NextNonce = Nonce (currentNonce + 1UL) }
        | None -> []

    /// Add transaction to sender queue
    let addToSenderQueue (queue: SenderQueue) (mtx: MempoolTransaction) : SenderQueue =
        let tx = mtx.Transaction
        let newTransactions = queue.Transactions.Add(tx.Nonce, mtx)
        let promotable = getPromotableTransactions { queue with Transactions = newTransactions }
        {
            queue with
                Transactions = newTransactions
                PromotableCount = promotable.Length
        }

    /// Remove transaction from sender queue
    let removeFromSenderQueue (queue: SenderQueue) (nonce: Nonce) : SenderQueue =
        let newTransactions = queue.Transactions.Remove(nonce)
        let promotable = getPromotableTransactions { queue with Transactions = newTransactions }
        {
            queue with
                Transactions = newTransactions
                PromotableCount = promotable.Length
        }

    /// Replace transaction in sender queue if new one has higher priority fee
    let tryReplaceTransaction (queue: SenderQueue) (newMtx: MempoolTransaction) (minBumpPercent: float) : (SenderQueue * bool) =
        let tx = newMtx.Transaction
        match queue.Transactions.TryFind(tx.Nonce) with
        | Some existingMtx ->
            // Check if new transaction has sufficiently higher priority fee
            let minRequiredFee = uint64 (float existingMtx.EffectivePriorityFee * (1.0 + minBumpPercent / 100.0))
            if newMtx.EffectivePriorityFee >= minRequiredFee then
                let newQueue = removeFromSenderQueue queue tx.Nonce
                let finalQueue = addToSenderQueue newQueue newMtx
                (finalQueue, true)
            else
                (queue, false)
        | None ->
            // No existing transaction, just add
            (addToSenderQueue queue newMtx, true)

    /// Create a new mempool state
    let createMempoolState (maxSize: int) (maxPerSender: int) (baseFee: uint64) : MempoolState =
        {
            SenderQueues = Map.empty
            AllTransactions = Map.empty
            PromotableTransactions = []
            MaxSize = maxSize
            MaxPerSender = maxPerSender
            BaseFeePerGas = baseFee
        }

    /// Update promotable transactions list
    let updatePromotableTransactions (state: MempoolState) : unit =
        let promotable =
            state.SenderQueues
            |> Map.toList
            |> List.collect (fun (_, queue) -> getPromotableTransactions queue)
            |> List.sortByDescending (fun mtx -> mtx.EffectivePriorityFee)
        state.PromotableTransactions <- promotable

    /// Add transaction to mempool
    let addTransaction (state: MempoolState) (tx: Transaction) (getAccountNonce: Address -> Nonce option) (minBumpPercent: float) : Result<unit, string> =
        // Check mempool size limit
        if state.AllTransactions.Count >= state.MaxSize then
            Error "Mempool is full"
        else
            // Get sender's current nonce from blockchain state
            let senderNonce = 
                match getAccountNonce tx.From with
                | Some nonce -> nonce
                | None -> Nonce 0UL

            // Create mempool transaction
            let mtx = createMempoolTransaction tx state.BaseFeePerGas

            // Get or create sender queue
            let queue =
                match state.SenderQueues.TryFind(tx.From) with
                | Some q -> q
                | None -> createSenderQueue tx.From senderNonce

            // Check per-sender limit
            if queue.Transactions.Count >= state.MaxPerSender then
                Error "Per-sender transaction limit reached"
            else
                // Try to add or replace transaction
                let (newQueue, added) = tryReplaceTransaction queue mtx minBumpPercent
                
                if added then
                    // Update state
                    state.SenderQueues <- state.SenderQueues.Add(tx.From, newQueue)
                    state.AllTransactions <- state.AllTransactions.Add(tx.Hash, mtx)
                    updatePromotableTransactions state
                    Ok ()
                else
                    Error "Transaction replacement requires higher priority fee"

    /// Remove transaction from mempool
    let removeTransaction (state: MempoolState) (txHash: Hash) : unit =
        match state.AllTransactions.TryFind(txHash) with
        | Some mtx ->
            let tx = mtx.Transaction
            // Remove from sender queue
            match state.SenderQueues.TryFind(tx.From) with
            | Some queue ->
                let newQueue = removeFromSenderQueue queue tx.Nonce
                if newQueue.Transactions.IsEmpty then
                    state.SenderQueues <- state.SenderQueues.Remove(tx.From)
                else
                    state.SenderQueues <- state.SenderQueues.Add(tx.From, newQueue)
            | None -> ()
            
            // Remove from all transactions
            state.AllTransactions <- state.AllTransactions.Remove(txHash)
            updatePromotableTransactions state
        | None -> ()

    /// Get transactions for block assembly (EIP-1559 aware selection)
    let selectTransactionsForBlock (state: MempoolState) (gasLimit: uint64) : Transaction list =
        let mutable selectedTxs = []
        let mutable gasUsed = 0UL
        
        for mtx in state.PromotableTransactions do
            let tx = mtx.Transaction
            let txGasLimit = tx.GasLimit
            
            if gasUsed + txGasLimit <= gasLimit then
                selectedTxs <- tx :: selectedTxs
                gasUsed <- gasUsed + txGasLimit
        
        List.rev selectedTxs

    /// Get mempool statistics
    let getStatistics (state: MempoolState) : Map<string, obj> =
        Map.ofList [
            ("TotalTransactions", box state.AllTransactions.Count)
            ("PromotableTransactions", box state.PromotableTransactions.Length)
            ("SenderQueues", box state.SenderQueues.Count)
            ("BaseFeePerGas", box state.BaseFeePerGas)
        ]

    /// Update base fee (called when new block is mined)
    let updateBaseFee (state: MempoolState) (newBaseFee: uint64) : unit =
        state.BaseFeePerGas <- newBaseFee
        // Recalculate effective priority fees for all transactions
        let updatedTransactions =
            state.AllTransactions
            |> Map.map (fun _ mtx ->
                { mtx with EffectivePriorityFee = calculateEffectivePriorityFee mtx.Transaction newBaseFee })
        state.AllTransactions <- updatedTransactions
        updatePromotableTransactions state

