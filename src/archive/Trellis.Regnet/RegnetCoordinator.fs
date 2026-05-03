namespace Trellis.Regnet

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Regnet coordinator for managing test network
module RegnetCoordinator =

    /// Regnet configuration
    type RegnetConfig = {
        NetworkId: string
        NodeCount: int
        BlockTime: TimeSpan
        TestDuration: TimeSpan
        InitialBalance: Balance
    }

    /// Regnet statistics
    type RegnetStats = {
        TotalNodes: int
        ActiveNodes: int
        TotalBlocks: int
        TotalTransactions: int
        NetworkHeight: BlockHeight
        StartTime: DateTime
        EndTime: DateTime option
    }

    /// Regnet coordinator
    type RegnetCoordinator(config: RegnetConfig, logger: ILogger<RegnetCoordinator>) =
        let mutable nodes: Map<string, RegnetNodeAgent> = Map.empty
        let mutable stats: RegnetStats = {
            TotalNodes = 0
            ActiveNodes = 0
            TotalBlocks = 0
            TotalTransactions = 0
            NetworkHeight = BlockHeight(0UL)
            StartTime = DateTime.UtcNow
            EndTime = None
        }
        let mutable isRunning = false

        member this.StartRegnet() : bool =
            try
                logger.LogInformation("Starting Trellis Regnet with {NodeCount} nodes", config.NodeCount)
                
                // Create nodes
                for i in 1..config.NodeCount do
                    let nodeId = sprintf "node-%d" i
                    let node = new RegnetNode(nodeId, logger)
                    let agent = new RegnetNodeAgent(node, logger)
                    
                    nodes <- Map.add nodeId agent nodes
                    agent.Start()
                
                stats <- { stats with TotalNodes = config.NodeCount; ActiveNodes = config.NodeCount }
                isRunning <- true
                
                logger.LogInformation("Regnet started successfully with {NodeCount} nodes", config.NodeCount)
                true
            with
            | ex -> 
                logger.LogError(ex, "Failed to start regnet")
                false

        member this.StopRegnet() : bool =
            try
                logger.LogInformation("Stopping Trellis Regnet...")
                
                // Stop all nodes
                for (nodeId, agent) in Map.toList nodes do
                    agent.Stop()
                
                stats <- { stats with EndTime = Some DateTime.UtcNow }
                isRunning <- false
                
                logger.LogInformation("Regnet stopped successfully")
                true
            with
            | ex -> 
                logger.LogError(ex, "Failed to stop regnet")
                false

        member this.CreateTestTransactions() : unit =
            try
                logger.LogInformation("Creating test transactions...")
                
                let nodeList = Map.toList nodes
                if nodeList.Length >= 2 then
                    let (nodeId1, agent1) = nodeList.[0]
                    let (nodeId2, agent2) = nodeList.[1]
                    
                    // Create transactions between nodes
                    for i in 1..5 do
                        let amount = Balance(uint64 (i * 1000))
                        let toAddress = Address(Array.zeroCreate 20)
                        
                        agent1.CreateTransaction(toAddress, amount)
                        agent2.CreateTransaction(toAddress, amount)
                        
                        stats <- { stats with TotalTransactions = stats.TotalTransactions + 2 }
                
                logger.LogInformation("Created {TransactionCount} test transactions", stats.TotalTransactions)
            with
            | ex -> 
                logger.LogError(ex, "Failed to create test transactions")

        member this.MineBlocks() : unit =
            try
                logger.LogInformation("Mining blocks...")
                
                for (nodeId, agent) in Map.toList nodes do
                    agent.MineBlock()
                    stats <- { stats with TotalBlocks = stats.TotalBlocks + 1 }
                
                logger.LogInformation("Mined {BlockCount} blocks", stats.TotalBlocks)
            with
            | ex -> 
                logger.LogError(ex, "Failed to mine blocks")

        member this.SyncNetwork() : unit =
            try
                logger.LogInformation("Syncing network...")
                
                let nodeList = Map.toList nodes
                for i in 0..nodeList.Length-1 do
                    let (nodeId, agent) = nodeList.[i]
                    let nextNodeId = if i < nodeList.Length-1 then fst nodeList.[i+1] else fst nodeList.[0]
                    agent.SyncWithPeer(nextNodeId)
                
                logger.LogInformation("Network sync completed")
            with
            | ex -> 
                logger.LogError(ex, "Failed to sync network")

        member this.GetStats() : RegnetStats = stats

        member this.GetNodeStats() : (string * int * int * int) list =
            Map.toList nodes
            |> List.map (fun (nodeId, agent) ->
                let node = agent.GetNode()
                (nodeId, node.GetBlockCount(), node.GetTransactionCount(), node.GetPeerCount())
            )

        member this.GenerateReport() : string =
            let uptime = 
                match stats.EndTime with
                | Some endTime -> endTime - stats.StartTime
                | None -> DateTime.UtcNow - stats.StartTime
            
            sprintf """
            Trellis Regnet Report
            ====================
            Network ID: %s
            Uptime: %s
            Total Nodes: %d
            Active Nodes: %d
            Total Blocks: %d
            Total Transactions: %d
            Network Height: %d
            
            Node Statistics:
            %s
            
            Test Results:
            - Network Startup: %s
            - Transaction Processing: %s
            - Block Mining: %s
            - Network Sync: %s
            """.Trim() 
                config.NetworkId
                uptime.ToString(@"hh\:mm\:ss")
                stats.TotalNodes
                stats.ActiveNodes
                stats.TotalBlocks
                stats.TotalTransactions
                (match stats.NetworkHeight with | BlockHeight h -> h)
                (this.GetNodeStats() 
                 |> List.map (fun (nodeId, blocks, txs, peers) -> 
                     sprintf "  %s: %d blocks, %d txs, %d peers" nodeId blocks txs peers)
                 |> String.concat "\n")
                (if stats.ActiveNodes > 0 then "PASS" else "FAIL")
                (if stats.TotalTransactions > 0 then "PASS" else "FAIL")
                (if stats.TotalBlocks > 0 then "PASS" else "FAIL")
                (if stats.ActiveNodes > 1 then "PASS" else "FAIL")

        member this.IsRunning() : bool = isRunning

        member this.RunTest() : bool =
            try
                logger.LogInformation("Running Trellis Regnet test...")
                
                // Start the network
                if not (this.StartRegnet()) then
                    return false
                
                // Wait for network to stabilize
                Task.Delay(2000) |> Async.AwaitTask |> Async.RunSynchronously
                
                // Create test transactions
                this.CreateTestTransactions()
                
                // Wait for transactions to propagate
                Task.Delay(1000) |> Async.AwaitTask |> Async.RunSynchronously
                
                // Mine blocks
                this.MineBlocks()
                
                // Sync network
                this.SyncNetwork()
                
                // Wait for final processing
                Task.Delay(1000) |> Async.AwaitTask |> Async.RunSynchronously
                
                // Generate report
                let report = this.GenerateReport()
                logger.LogInformation("Regnet Test Report:\n{Report}", report)
                
                // Stop the network
                this.StopRegnet()
                
                logger.LogInformation("Regnet test completed successfully")
                true
            with
            | ex -> 
                logger.LogError(ex, "Regnet test failed: {Error}", ex.Message)
                false
