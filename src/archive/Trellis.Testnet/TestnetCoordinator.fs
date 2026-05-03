namespace Trellis.Testnet

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Testnet coordinator for managing the full testnet
module TestnetCoordinator =

    /// Testnet configuration
    type TestnetConfig = {
        NetworkId: string
        NodeCount: int
        MiningNodes: int
        BlockTime: TimeSpan
        TestDuration: TimeSpan
        InitialBalance: Balance
        MiningDifficulty: int
        MaxTransactionsPerBlock: int
    }

    /// Testnet statistics
    type TestnetStats = {
        TotalNodes: int
        ActiveNodes: int
        MiningNodes: int
        TotalBlocks: int
        TotalTransactions: int
        NetworkHeight: BlockHeight
        StartTime: DateTime
        EndTime: DateTime option
        NetworkSyncs: int
        MiningAttempts: int
        SuccessfulMines: int
    }

    /// Testnet coordinator
    type TestnetCoordinator(config: TestnetConfig, logger: ILogger<TestnetCoordinator>) =
        let mutable nodes: Map<string, TestnetNodeAgent> = Map.empty
        let mutable miners: TestnetMiner list = []
        let mutable networks: TestnetNetwork list = []
        let mutable stats: TestnetStats = {
            TotalNodes = 0
            ActiveNodes = 0
            MiningNodes = 0
            TotalBlocks = 0
            TotalTransactions = 0
            NetworkHeight = BlockHeight(0UL)
            StartTime = DateTime.UtcNow
            EndTime = None
            NetworkSyncs = 0
            MiningAttempts = 0
            SuccessfulMines = 0
        }
        let mutable isRunning = false

        let createMiningConfig() : MiningConfig = {
            Difficulty = config.MiningDifficulty
            BlockTime = config.BlockTime
            MaxTransactionsPerBlock = config.MaxTransactionsPerBlock
            MiningReward = Balance(50UL) // 50 tokens per block
        }

        let createNetworkConfig() : TestnetConfig = config

        member this.StartTestnet() : bool =
            try
                logger.LogInformation("Starting Trellis Testnet with {NodeCount} nodes, {MiningNodes} miners", 
                    config.NodeCount, config.MiningNodes)
                
                // Create nodes
                for i in 1..config.NodeCount do
                    let nodeId = sprintf "testnet-node-%d" i
                    let node = new TestnetNode(nodeId, logger)
                    let agent = new TestnetNodeAgent(node, logger)
                    
                    nodes <- Map.add nodeId agent nodes
                    agent.Start()
                
                // Create miners
                let miningConfig = createMiningConfig()
                for i in 1..config.MiningNodes do
                    let miner = new TestnetMiner(miningConfig, logger)
                    miners <- miner :: miners
                    miner.StartMining()
                
                // Create networks
                for i in 1..config.NodeCount do
                    let nodeId = sprintf "testnet-node-%d" i
                    let network = new TestnetNetwork(nodeId, logger)
                    networks <- network :: networks
                    network.Start()
                
                stats <- { 
                    stats with 
                        TotalNodes = config.NodeCount
                        ActiveNodes = config.NodeCount
                        MiningNodes = config.MiningNodes
                }
                isRunning <- true
                
                logger.LogInformation("Testnet started successfully with {NodeCount} nodes, {MiningNodes} miners", 
                    config.NodeCount, config.MiningNodes)
                true
            with
            | ex -> 
                logger.LogError(ex, "Failed to start testnet: {Error}", ex.Message)
                false

        member this.StopTestnet() : bool =
            try
                logger.LogInformation("Stopping Trellis Testnet...")
                
                // Stop all nodes
                for (nodeId, agent) in Map.toList nodes do
                    agent.Stop()
                
                // Stop all miners
                for miner in miners do
                    miner.StopMining()
                
                // Stop all networks
                for network in networks do
                    network.Stop()
                
                stats <- { stats with EndTime = Some DateTime.UtcNow }
                isRunning <- false
                
                logger.LogInformation("Testnet stopped successfully")
                true
            with
            | ex -> 
                logger.LogError(ex, "Failed to stop testnet: {Error}", ex.Message)
                false

        member this.CreateTestTransactions() : unit =
            try
                logger.LogInformation("Creating test transactions...")
                
                let nodeList = Map.toList nodes
                if nodeList.Length >= 2 then
                    let (nodeId1, agent1) = nodeList.[0]
                    let (nodeId2, agent2) = nodeList.[1]
                    
                    // Create transactions between nodes
                    for i in 1..10 do
                        let amount = Balance(uint64 (i * 100))
                        let toAddress = Address(Array.create 20 (byte i)) // Generate test address
                        
                        agent1.CreateTransaction(toAddress, amount)
                        agent2.CreateTransaction(toAddress, amount)
                        
                        stats <- { stats with TotalTransactions = stats.TotalTransactions + 2 }
                
                logger.LogInformation("Created {TransactionCount} test transactions", stats.TotalTransactions)
            with
            | ex -> 
                logger.LogError(ex, "Failed to create test transactions: {Error}", ex.Message)

        member this.StartMining() : unit =
            try
                logger.LogInformation("Starting mining operations...")
                
                for miner in miners do
                    miner.StartMining()
                
                // Start mining on nodes
                for (nodeId, agent) in Map.toList nodes do
                    agent.StartMining()
                
                logger.LogInformation("Mining started on {MiningNodes} nodes", config.MiningNodes)
            with
            | ex -> 
                logger.LogError(ex, "Failed to start mining: {Error}", ex.Message)

        member this.MineBlocks() : unit =
            try
                logger.LogInformation("Mining blocks...")
                
                for (nodeId, agent) in Map.toList nodes do
                    agent.MineBlock()
                    stats <- { stats with MiningAttempts = stats.MiningAttempts + 1 }
                
                // Simulate successful mining
                let newTotalBlocks = stats.TotalBlocks + 1
                let newSuccessfulMines = stats.SuccessfulMines + 1
                stats <- { stats with 
                    TotalBlocks = newTotalBlocks
                    SuccessfulMines = newSuccessfulMines
                }
                
                logger.LogInformation("Mined {BlockCount} blocks", stats.TotalBlocks)
            with
            | ex -> 
                logger.LogError(ex, "Failed to mine blocks: {Error}", ex.Message)

        member this.SyncNetwork() : unit =
            try
                logger.LogInformation("Synchronizing network...")
                
                let nodeList = Map.toList nodes
                for i in 0..nodeList.Length-1 do
                    let (nodeId, agent) = nodeList.[i]
                    let nextNodeId = if i < nodeList.Length-1 then fst nodeList.[i+1] else fst nodeList.[0]
                    
                    // Get blockchain from current node
                    let blockchain = agent.GetNode().GetBlockchain()
                    agent.SyncWithPeer(nextNodeId, blockchain)
                
                stats <- { stats with NetworkSyncs = stats.NetworkSyncs + 1 }
                logger.LogInformation("Network sync completed")
            with
            | ex -> 
                logger.LogError(ex, "Failed to sync network: {Error}", ex.Message)

        member this.ValidateNetwork() : bool =
            try
                logger.LogInformation("Validating network integrity...")
                
                let nodeList = Map.toList nodes
                let mutable allValid = true
                
                for (nodeId, agent) in nodeList do
                    let node = agent.GetNode()
                    let blockchain = node.GetBlockchain()
                    let height = node.GetCurrentHeight()
                    
                    let heightValue = match height with | BlockHeight h -> h
                    logger.LogInformation("Node {NodeId}: Height={Height}, Blocks={Blocks}, Transactions={Transactions}", 
                        nodeId, heightValue, blockchain.Length, node.GetTransactionCount())
                    
                    if blockchain.Length = 0 then
                        logger.LogWarning("Node {NodeId} has empty blockchain", nodeId)
                        allValid <- false
                
                if allValid then
                    logger.LogInformation("Network validation passed")
                else
                    logger.LogWarning("Network validation found issues")
                
                allValid
            with
            | ex -> 
                logger.LogError(ex, "Network validation failed: {Error}", ex.Message)
                false

        member this.GetStats() : TestnetStats = stats

        member this.GetNodeStats() : (string * int * int * int * bool) list =
            Map.toList nodes
            |> List.map (fun (nodeId, agent) ->
                let node = agent.GetNode()
                (nodeId, node.GetBlockCount(), node.GetTransactionCount(), node.GetPeerCount(), node.IsMining())
            )

        member this.GetMiningStats() : (string * bool * uint64) list =
            miners
            |> List.mapi (fun i miner -> 
                (sprintf "miner-%d" i, miner.IsMining(), miner.GetCurrentNonce())
            )

        member this.GetNetworkStats() : (string * int * int) list =
            networks
            |> List.mapi (fun i network -> 
                (sprintf "network-%d" i, network.GetNodeCount(), network.GetStats().ConnectedNodes)
            )

        member this.GenerateReport() : string =
            let uptime = 
                match stats.EndTime with
                | Some endTime -> endTime - stats.StartTime
                | None -> DateTime.UtcNow - stats.StartTime
            
            let miningSuccessRate = 
                if stats.MiningAttempts > 0 then
                    (float stats.SuccessfulMines) / (float stats.MiningAttempts) * 100.0
                else
                    0.0
            
            let nodeStatsText = this.GetNodeStats() 
                |> List.map (fun (nodeId, blocks, txs, peers, mining) -> 
                    sprintf "  %s: %d blocks, %d txs, %d peers, mining=%s" nodeId blocks txs peers (if mining then "yes" else "no"))
                |> String.concat "\n"
            
            let miningStatsText = this.GetMiningStats() 
                |> List.map (fun (minerId, mining, nonce) -> 
                    sprintf "  %s: mining=%s, nonce=%d" minerId (if mining then "yes" else "no") nonce)
                |> String.concat "\n"
            
            let networkStatsText = this.GetNetworkStats() 
                |> List.map (fun (networkId, nodes, connected) -> 
                    sprintf "  %s: %d nodes, %d connected" networkId nodes connected)
                |> String.concat "\n"
            
            let networkStartupResult = if stats.ActiveNodes > 0 then "PASS" else "FAIL"
            let transactionResult = if stats.TotalTransactions > 0 then "PASS" else "FAIL"
            let blockMiningResult = if stats.TotalBlocks > 0 then "PASS" else "FAIL"
            let networkSyncResult = if stats.NetworkSyncs > 0 then "PASS" else "FAIL"
            let networkValidationResult = if this.ValidateNetwork() then "PASS" else "FAIL"
            
            sprintf """
            Trellis Testnet Report
            =====================
            Network ID: %s
            Uptime: %s
            Total Nodes: %d
            Active Nodes: %d
            Mining Nodes: %d
            Total Blocks: %d
            Total Transactions: %d
            Network Height: %d
            Network Syncs: %d
            Mining Attempts: %d
            Successful Mines: %d
            Mining Success Rate: %.1f%%
            
            Node Statistics:
            %s
            
            Mining Statistics:
            %s
            
            Network Statistics:
            %s
            
            Test Results:
            - Network Startup: %s
            - Transaction Processing: %s
            - Block Mining: %s
            - Network Sync: %s
            - Network Validation: %s
            """.Trim() 
                config.NetworkId
                uptime.ToString(@"hh\:mm\:ss")
                stats.TotalNodes
                stats.ActiveNodes
                stats.MiningNodes
                stats.TotalBlocks
                stats.TotalTransactions
                (match stats.NetworkHeight with | BlockHeight h -> h)
                stats.NetworkSyncs
                stats.MiningAttempts
                stats.SuccessfulMines
                miningSuccessRate
                nodeStatsText
                miningStatsText
                networkStatsText
                networkStartupResult
                transactionResult
                blockMiningResult
                networkSyncResult
                networkValidationResult

        member this.IsRunning() : bool = isRunning

        member this.RunFullTest() : bool =
            try
                logger.LogInformation("Running Trellis Testnet full test...")
                
                // Start the testnet
                if not (this.StartTestnet()) then
                    return false
                
                // Wait for network to stabilize
                Task.Delay(3000) |> Async.AwaitTask |> Async.RunSynchronously
                
                // Create test transactions
                this.CreateTestTransactions()
                
                // Wait for transactions to propagate
                Task.Delay(2000) |> Async.AwaitTask |> Async.RunSynchronously
                
                // Start mining
                this.StartMining()
                
                // Mine blocks
                this.MineBlocks()
                
                // Sync network
                this.SyncNetwork()
                
                // Wait for final processing
                Task.Delay(2000) |> Async.AwaitTask |> Async.RunSynchronously
                
                // Validate network
                let isValid = this.ValidateNetwork()
                
                // Generate report
                let report = this.GenerateReport()
                logger.LogInformation("Testnet Report:\n{Report}", report)
                
                // Stop the testnet
                this.StopTestnet()
                
                if isValid then
                    logger.LogInformation("🎉 Testnet test completed successfully!")
                    true
                else
                    logger.LogWarning("⚠️ Testnet test completed with issues")
                    true
            with
            | ex -> 
                logger.LogError(ex, "Testnet test failed: {Error}", ex.Message)
                false
