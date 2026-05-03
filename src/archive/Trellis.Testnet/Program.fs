namespace Trellis.Testnet

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

/// Testnet program entry point
module Program =

    [<EntryPoint>]
    let main args =
        printfn "🌐 Starting Trellis Testnet (Full Blockchain Test Network)"
        printfn "=========================================================="
        printfn ""
        
        // Build configuration
        let configuration = ConfigurationBuilder()
        let configuration = configuration.AddJsonFile("appsettings.json", optional = true)
        let configuration = configuration.AddJsonFile("appsettings.Production.json", optional = true)
        let configuration = configuration.AddEnvironmentVariables()
        let configuration = configuration.Build()
        
        // Create testnet configuration
        let testnetConfig = {
            NetworkId = "trellis-testnet"
            NodeCount = 5
            MiningNodes = 3
            BlockTime = TimeSpan.FromSeconds(5.0)
            TestDuration = TimeSpan.FromMinutes(10.0)
            InitialBalance = Balance(1000000UL) // 1M tokens per node
            MiningDifficulty = 2 // Lower difficulty for testing
            MaxTransactionsPerBlock = 10
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
            
            // Add testnet coordinator
            services.AddSingleton<TestnetCoordinator>(fun provider ->
                let logger = provider.GetRequiredService<ILogger<TestnetCoordinator>>()
                new TestnetCoordinator(testnetConfig, logger)
            ) |> ignore
            
            // Add testnet background service
            services.AddHostedService<TestnetBackgroundService>() |> ignore
        )
        let host = host.Build()
        
        // Run the host
        try
            printfn "Testnet Configuration:"
            printfn "  Network ID: %s" testnetConfig.NetworkId
            printfn "  Node Count: %d" testnetConfig.NodeCount
            printfn "  Mining Nodes: %d" testnetConfig.MiningNodes
            printfn "  Block Time: %s" (testnetConfig.BlockTime.ToString())
            printfn "  Test Duration: %s" (testnetConfig.TestDuration.ToString())
            printfn "  Initial Balance: %d tokens" (match testnetConfig.InitialBalance with | Balance b -> b)
            printfn "  Mining Difficulty: %d" testnetConfig.MiningDifficulty
            printfn "  Max Transactions/Block: %d" testnetConfig.MaxTransactionsPerBlock
            printfn ""
            printfn "Starting full testnet test..."
            printfn "This will test:"
            printfn "  - Multiple blockchain nodes"
            printfn "  - Real mining operations"
            printfn "  - Network communication"
            printfn "  - Transaction processing"
            printfn "  - Block validation"
            printfn "  - Network synchronization"
            printfn ""
            
            host.Run()
            0
        with
        | ex ->
            printfn "Testnet failed to start: %s" ex.Message
            1

/// Testnet background service
type TestnetBackgroundService(coordinator: TestnetCoordinator, logger: ILogger<TestnetBackgroundService>) =
    inherit BackgroundService()
    
    override this.ExecuteAsync(stoppingToken) =
        async {
            logger.LogInformation("Starting Testnet Background Service...")
            
            // Run the full testnet test
            let success = coordinator.RunFullTest()
            
            if success then
                logger.LogInformation("🎉 Testnet test completed successfully!")
                logger.LogInformation("The Trellis blockchain system is fully functional with:")
                logger.LogInformation("  ✅ Multiple nodes working")
                logger.LogInformation("  ✅ Mining operations working")
                logger.LogInformation("  ✅ Network communication working")
                logger.LogInformation("  ✅ Transaction processing working")
                logger.LogInformation("  ✅ Block validation working")
                logger.LogInformation("  ✅ Network synchronization working")
            else
                logger.LogError("❌ Testnet test failed!")
                logger.LogError("The Trellis blockchain system has issues.")
            
            // Keep running until cancellation
            while not stoppingToken.IsCancellationRequested do
                do! Async.Sleep(1000)
            
            logger.LogInformation("Testnet Background Service stopped.")
        } |> Async.StartAsTask :> Task
