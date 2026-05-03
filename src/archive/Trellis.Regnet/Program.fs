namespace Trellis.Regnet

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

/// Regnet program entry point
module Program =

    [<EntryPoint>]
    let main args =
        printfn "🚀 Starting Trellis Regnet (Regression Test Network)"
        printfn "=================================================="
        printfn ""
        
        // Build configuration
        let configuration = ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional = true)
            .AddJsonFile("appsettings.Production.json", optional = true)
            .AddEnvironmentVariables()
            .Build()
        
        // Create regnet configuration
        let regnetConfig = {
            NetworkId = "trellis-regnet"
            NodeCount = 3
            BlockTime = TimeSpan.FromSeconds(10.0)
            TestDuration = TimeSpan.FromMinutes(5.0)
            InitialBalance = Balance(1000000UL) // 1M tokens per node
        }
        
        // Create host builder
        let hostBuilder = Host.CreateDefaultBuilder(args)
        
        // Configure services
        let host = hostBuilder
            .ConfigureServices(fun (context: HostBuilderContext) (services: IServiceCollection) ->
                // Add logging
                services.AddLogging(fun logging ->
                    logging.AddConsole() |> ignore
                    logging.AddDebug() |> ignore
                ) |> ignore
                
                // Add regnet coordinator
                services.AddSingleton<RegnetCoordinator>(fun provider ->
                    let logger = provider.GetRequiredService<ILogger<RegnetCoordinator>>()
                    new RegnetCoordinator(regnetConfig, logger)
                ) |> ignore
                
                // Add regnet background service
                services.AddHostedService<RegnetBackgroundService>() |> ignore
            )
            .Build()
        
        // Run the host
        try
            printfn "Regnet Configuration:"
            printfn "  Network ID: %s" regnetConfig.NetworkId
            printfn "  Node Count: %d" regnetConfig.NodeCount
            printfn "  Block Time: %s" (regnetConfig.BlockTime.ToString())
            printfn "  Test Duration: %s" (regnetConfig.TestDuration.ToString())
            printfn "  Initial Balance: %d tokens" (match regnetConfig.InitialBalance with | Balance b -> b)
            printfn ""
            printfn "Starting regnet test..."
            
            host.Run()
            0
        with
        | ex ->
            printfn "Regnet failed to start: %s" ex.Message
            1

/// Regnet background service
type RegnetBackgroundService(coordinator: RegnetCoordinator, logger: ILogger<RegnetBackgroundService>) =
    inherit BackgroundService()
    
    override this.ExecuteAsync(stoppingToken) =
        async {
            logger.LogInformation("Starting Regnet Background Service...")
            
            // Run the regnet test
            let success = coordinator.RunTest()
            
            if success then
                logger.LogInformation("🎉 Regnet test completed successfully!")
                logger.LogInformation("The Trellis blockchain system is fully functional.")
            else
                logger.LogError("❌ Regnet test failed!")
                logger.LogError("The Trellis blockchain system has issues.")
            
            // Keep running until cancellation
            while not stoppingToken.IsCancellationRequested do
                do! Async.Sleep(1000)
            
            logger.LogInformation("Regnet Background Service stopped.")
        } |> Async.StartAsTask :> Task
