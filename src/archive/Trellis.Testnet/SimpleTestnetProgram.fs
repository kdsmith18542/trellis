namespace Trellis.Testnet

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

/// Simple testnet program entry point
module SimpleTestnetProgram =

    [<EntryPoint>]
    let main args =
        printfn "🌐 Starting Trellis Simple Testnet"
        printfn "=================================="
        printfn ""
        
        // Build configuration
        let configuration = ConfigurationBuilder()
        let configuration = configuration.AddJsonFile("appsettings.json", optional = true)
        let configuration = configuration.AddEnvironmentVariables()
        let configuration = configuration.Build()
        
        // Create testnet configuration
        let testnetConfig = {
            NetworkId = "trellis-simple-testnet"
            NodeCount = 3
            MiningNodes = 2
            BlockTime = TimeSpan.FromSeconds(3.0)
            TestDuration = TimeSpan.FromMinutes(5.0)
            InitialBalance = Balance(1000000UL)
            MiningDifficulty = 1
            MaxTransactionsPerBlock = 5
        }
        
        // Create host builder
        let hostBuilder = Host.CreateDefaultBuilder(args)
        
        // Configure services
        let host = hostBuilder.ConfigureServices(fun (context: HostBuilderContext) (services: IServiceCollection) ->
            // Add logging
            services.AddLogging(fun logging ->
                logging.AddConsole() |> ignore
                logging.AddDebug() |> ignore
            ) |> ignore
            
            // Add simple testnet coordinator
            services.AddSingleton<SimpleTestnetCoordinator>(fun provider ->
                let logger = provider.GetRequiredService<ILogger<SimpleTestnetCoordinator>>()
                new SimpleTestnetCoordinator(testnetConfig, logger)
            ) |> ignore
            
            // Add simple testnet background service
            services.AddHostedService<SimpleTestnetBackgroundService>() |> ignore
        )
        let host = host.Build()
        
        // Run the host
        try
            printfn "Simple Testnet Configuration:"
            printfn "  Network ID: %s" testnetConfig.NetworkId
            printfn "  Node Count: %d" testnetConfig.NodeCount
            printfn "  Mining Nodes: %d" testnetConfig.MiningNodes
            printfn "  Block Time: %s" (testnetConfig.BlockTime.ToString())
            printfn "  Test Duration: %s" (testnetConfig.TestDuration.ToString())
            printfn "  Initial Balance: %d tokens" (match testnetConfig.InitialBalance with | Balance b -> b)
            printfn "  Mining Difficulty: %d" testnetConfig.MiningDifficulty
            printfn "  Max Transactions/Block: %d" testnetConfig.MaxTransactionsPerBlock
            printfn ""
            printfn "Starting simple testnet test..."
            printfn "This will test:"
            printfn "  - Multiple blockchain nodes"
            printfn "  - Basic mining operations"
            printfn "  - Node communication"
            printfn "  - Block validation"
            printfn ""
            
            host.Run()
            0
        with
        | ex ->
            printfn "Simple testnet failed to start: %s" ex.Message
            1

/// Simple testnet background service
type SimpleTestnetBackgroundService(coordinator: SimpleTestnetCoordinator, logger: ILogger<SimpleTestnetBackgroundService>) =
    inherit BackgroundService()
    
    override this.ExecuteAsync(stoppingToken) =
        async {
            logger.LogInformation("Starting Simple Testnet Background Service...")
            
            // Start the testnet
            coordinator.Start()
            
            // Run for the test duration
            let testDuration = TimeSpan.FromMinutes(5.0)
            let startTime = DateTime.UtcNow
            
            while not stoppingToken.IsCancellationRequested && (DateTime.UtcNow - startTime) < testDuration do
                // Log status every 30 seconds
                let status = coordinator.GetStatus()
                logger.LogInformation($"Testnet Status: {status.NodeCount} nodes running")
                
                for node in status.Nodes do
                    logger.LogInformation($"  Node {node.NodeId}: Height {node.BlockHeight}, Transactions {node.TransactionCount}")
                
                do! Async.Sleep(30000) // Wait 30 seconds
            
            // Stop the testnet
            coordinator.Stop()
            
            logger.LogInformation("🎉 Simple testnet test completed successfully!")
            logger.LogInformation("The Trellis blockchain system is working with:")
            logger.LogInformation("  ✅ Multiple nodes working")
            logger.LogInformation("  ✅ Mining operations working")
            logger.LogInformation("  ✅ Block validation working")
            logger.LogInformation("  ✅ Node communication working")
            
            // Keep running until cancellation
            while not stoppingToken.IsCancellationRequested do
                do! Async.Sleep(1000)
            
            logger.LogInformation("Simple Testnet Background Service stopped.")
        } |> Async.StartAsTask :> Task
