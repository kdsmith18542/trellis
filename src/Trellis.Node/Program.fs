namespace Trellis.Node

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Trellis.Core
open SimpleTypes

/// Main program entry point
module Program =

    [<EntryPoint>]
    let main args =
        // Load configuration before any services are instantiated
        ConfigLoader.load()

        let builder = WebApplication.CreateBuilder(args)
        
        builder.Logging.AddConsole() |> ignore
        
        builder.Services.AddHostedService<SimpleNode.NodeService>() |> ignore
        builder.Services.AddSingleton<MonitoringDashboardService>() |> ignore
        builder.Services.AddHostedService<MonitoringDashboardService>() |> ignore
        builder.Services.AddHostedService<MonitoringDataCollector>() |> ignore
        builder.Services.AddSingleton<IndexerService>() |> ignore
        builder.Services.AddHostedService<IndexerService>() |> ignore
        builder.Services.AddSingleton<IndexerApiService>() |> ignore
        
        builder.Services.AddSingleton<GrpcService.IBlockchainState>(fun serviceProvider ->
            let logger = serviceProvider.GetRequiredService<ILogger<BlockchainState.InMemoryBlockchainState>>()
            BlockchainState.createBlockchainState logger
        ) |> ignore
        
        let app = builder.Build()

        // Static files for explorer (wwwroot)
        app.UseStaticFiles() |> ignore

        let wsOptions = WebSocketOptions()
        wsOptions.KeepAliveInterval <- TimeSpan.FromSeconds(30.)
        app.UseWebSockets(wsOptions) |> ignore

        // API endpoints
        app.MapGet("/health", ExplorerApi.healthHandler) |> ignore
        app.MapGet("/block/{hash}", ExplorerApi.blockHandler) |> ignore
        app.MapGet("/validators", ExplorerApi.validatorsHandler) |> ignore
        app.MapGet("/chain-state", ExplorerApi.chainStateHandler) |> ignore
        app.MapGet("/account/{address}", ExplorerApi.accountHandler) |> ignore

        // Streaming endpoint
        app.MapGet("/stream", RequestDelegate(fun ctx ->
            task { do! WebSocketStreaming.handleStreamingConnection ctx } :> Task
        )) |> ignore
        
        printfn "Trellis Node .NET 10 starting..."
        printfn "  Explorer: http://localhost:5000/explorer.html"
        printfn "  Health:   http://localhost:5000/health"
        printfn "  Stream:   ws://localhost:5000/stream"
        printfn "Press Ctrl+C to stop"
        
        try app.Run(); 0
        with ex -> printfn "Error: %s" ex.Message; 1
