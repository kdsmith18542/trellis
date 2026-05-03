namespace Trellis.Testnet

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes

/// Simple testnet implementation
module SimpleTestnet =

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

    /// Simple testnet node
    type SimpleTestnetNode(nodeId: string, config: TestnetConfig, logger: ILogger<SimpleTestnetNode>) =
        let mutable isRunning = false
        let mutable blockHeight = BlockHeight(0UL)
        let mutable transactions = []
        
        member this.Start() =
            logger.LogInformation($"Starting node {nodeId}")
            isRunning <- true
            
            // Start mining loop
            Task.Run(fun () -> this.MiningLoop()) |> ignore
            
            logger.LogInformation($"Node {nodeId} started")
        
        member this.Stop() =
            logger.LogInformation($"Stopping node {nodeId}")
            isRunning <- false
            logger.LogInformation($"Node {nodeId} stopped")
        
        member private this.MiningLoop() =
            while isRunning do
                try
                    // Simulate mining
                    this.MineBlock() |> ignore
                    Task.Delay(config.BlockTime).Wait()
                with
                | ex -> logger.LogError(ex, $"Mining error on node {nodeId}")
        
        member private this.MineBlock() : Block option =
            try
                let newHeight = BlockHeight(match blockHeight with | BlockHeight h -> h + 1UL)
                blockHeight <- newHeight
                
                let block = {
                    Header = {
                        Hash = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes($"block-{nodeId}-{newHeight}"))
                        Height = newHeight
                        Timestamp = Timestamp(DateTime.UtcNow)
                        PreviousHash = Hash(Array.create 32 0uy)
                        MerkleRoot = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes($"merkle-{nodeId}-{newHeight}"))
                        Validator = Address(Array.create 20 (byte (nodeId.GetHashCode() % 256)))
                        Nonce = 0UL
                        Difficulty = uint64 config.MiningDifficulty
                        PoWHash = SimpleCrypto.sha256 (System.Text.Encoding.UTF8.GetBytes($"pow-{nodeId}-{newHeight}"))
                        Finalized = true
                    }
                    Transactions = transactions
                }
                
                logger.LogInformation($"Node {nodeId} mined block at height {newHeight}")
                Some block
            with
            | ex -> 
                logger.LogError(ex, $"Mining failed on node {nodeId}")
                None
        
        member this.GetStatus() =
            {
                NodeId = nodeId
                IsRunning = isRunning
                BlockHeight = blockHeight
                TransactionCount = transactions.Length
            }

    /// Simple testnet coordinator
    type SimpleTestnetCoordinator(config: TestnetConfig, logger: ILogger<SimpleTestnetCoordinator>) =
        let mutable nodes: Map<string, SimpleTestnetNode> = Map.empty
        let mutable isRunning = false
        
        member this.Start() =
            logger.LogInformation("Starting Simple Testnet Coordinator")
            isRunning <- true
            
            // Create nodes
            for i in 1..config.NodeCount do
                let nodeId = $"node-{i}"
                let nodeLogger = logger
                let node = SimpleTestnetNode(nodeId, config, nodeLogger)
                nodes <- Map.add nodeId node nodes
                node.Start()
            
            logger.LogInformation($"Started {config.NodeCount} nodes")
        
        member this.Stop() =
            logger.LogInformation("Stopping Simple Testnet Coordinator")
            isRunning <- false
            
            for node in nodes.Values do
                node.Stop()
            
            logger.LogInformation("All nodes stopped")
        
        member this.GetStatus() =
            {
                IsRunning = isRunning
                NodeCount = nodes.Count
                Nodes = nodes |> Map.toList |> List.map (fun (_, node) -> node.GetStatus())
            }

    /// Testnet status
    type TestnetStatus = {
        IsRunning: bool
        NodeCount: int
        Nodes: NodeStatus list
    }

    /// Node status
    type NodeStatus = {
        NodeId: string
        IsRunning: bool
        BlockHeight: BlockHeight
        TransactionCount: int
    }
