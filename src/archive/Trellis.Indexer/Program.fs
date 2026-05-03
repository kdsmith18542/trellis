namespace Trellis.Indexer

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

/// Compliance indexer program entry point
module Program =

    [<EntryPoint>]
    let main args =
        printfn "Starting Trellis Compliance Indexer..."
        
        // Build configuration
        let configuration = ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional = true)
            .AddJsonFile("appsettings.Production.json", optional = true)
            .AddEnvironmentVariables()
            .Build()
        
        // Get connection string
        let connectionString = configuration.GetConnectionString("DefaultConnection") 
                              ?? "Host=localhost;Database=trellis_compliance;Username=trellis;Password=trellis"
        
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
                
                // Add compliance indexer
                services.AddSingleton<ComplianceIndexerService>(fun provider ->
                    let logger = provider.GetRequiredService<ILogger<ComplianceIndexerService>>()
                    new ComplianceIndexerService(connectionString, logger)
                ) |> ignore
                
                // Add background service
                services.AddHostedService<ComplianceIndexerBackgroundService>() |> ignore
            )
            .Build()
        
        // Run the host
        try
            printfn "Compliance Indexer started successfully"
            host.Run()
            0
        with
        | ex ->
            printfn "Compliance Indexer failed to start: %s" ex.Message
            1
