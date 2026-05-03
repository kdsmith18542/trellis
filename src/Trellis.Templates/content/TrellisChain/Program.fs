namespace TrellisChain

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Trellis.Core
open SimpleTypes

module Program =

    [<EntryPoint>]
    let main args =
        // Config is loaded automatically from appsettings.json
        let builder = WebApplication.CreateBuilder(args)
        builder.Logging.AddConsole() |> ignore

        builder.Services.AddHostedService<Trellis.Node.SimpleNode.NodeService>() |> ignore
        builder.Services.AddSingleton<Trellis.Node.GrpcService.IBlockchainState>(fun sp ->
            let logger = sp.GetRequiredService<ILogger<Trellis.Node.BlockchainState.InMemoryBlockchainState>>()
            Trellis.Node.BlockchainState.createBlockchainState logger
        ) |> ignore

        let app = builder.Build()
        app.UseStaticFiles() |> ignore

        app.MapGet("/health", Trellis.Node.ExplorerApi.healthHandler) |> ignore
        app.MapGet("/block/{hash}", Trellis.Node.ExplorerApi.blockHandler) |> ignore
        app.MapGet("/validators", Trellis.Node.ExplorerApi.validatorsHandler) |> ignore
        app.MapGet("/chain-state", Trellis.Node.ExplorerApi.chainStateHandler) |> ignore
        app.MapGet("/account/{address}", Trellis.Node.ExplorerApi.accountHandler) |> ignore

        printfn "TrellisChain node starting..."
        try app.Run(); 0
        with ex -> printfn "Error: %s" ex.Message; 1
