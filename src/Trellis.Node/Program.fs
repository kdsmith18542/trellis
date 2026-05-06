namespace Trellis.Node

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.FileProviders
open Trellis.Core
open SimpleTypes

/// Main program entry point
module Program =

    [<EntryPoint>]
    let main args =
        // Load configuration before any services are instantiated
        ConfigLoader.load()

        let builder = WebApplication.CreateBuilder(args)
        
        // Resolve wwwroot for static files (output dir or project dir in dev)
        let wwwrootPath =
            let outputDir = Path.Combine(AppContext.BaseDirectory, "wwwroot")
            if Directory.Exists(outputDir) then outputDir
            else
                let projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"))
                if Directory.Exists(projectDir) then projectDir
                else outputDir
        builder.Environment.WebRootPath <- wwwrootPath
        
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
        let staticFileOptions = new StaticFileOptions()
        staticFileOptions.FileProvider <- new PhysicalFileProvider(wwwrootPath)
        app.UseStaticFiles(staticFileOptions) |> ignore

        let wsOptions = new WebSocketOptions()
        wsOptions.KeepAliveInterval <- TimeSpan.FromSeconds(30.)
        app.UseWebSockets(wsOptions) |> ignore

        // API endpoints
        app.MapGet("/health", ExplorerApi.healthHandler) |> ignore
        app.MapGet("/block/{hash}", ExplorerApi.blockHandler) |> ignore
        app.MapGet("/validators", ExplorerApi.validatorsHandler) |> ignore
        app.MapGet("/chain-state", ExplorerApi.chainStateHandler) |> ignore
        app.MapGet("/account/{address}", ExplorerApi.accountHandler) |> ignore
        
        app.MapPost("/api/send-transaction", fun (ctx: HttpContext) ->
            task {
                try
                    use sr = new StreamReader(ctx.Request.Body)
                    let! body = sr.ReadToEndAsync()
                    let json = System.Text.Json.JsonDocument.Parse(body).RootElement
                    let fromHex = json.GetProperty("from").GetString()
                    let toHex = json.GetProperty("to").GetString()
                    let amount = json.GetProperty("amount").GetUInt64()
                    let fee = json.GetProperty("fee").GetUInt64()
                    let nonce = json.GetProperty("nonce").GetUInt64()
                    let pubKeyHex = json.GetProperty("publicKey").GetString()
                    let sigHex = json.GetProperty("signature").GetString()
                    
                    let bs = ctx.RequestServices.GetRequiredService<GrpcService.IBlockchainState>()
                    
                    let tx: SimpleTypes.Transaction = {
                        From = SimpleTypes.Address (Convert.FromHexString(fromHex))
                        To = SimpleTypes.Address (Convert.FromHexString(toHex))
                        Amount = SimpleTypes.Balance amount
                        Fee = SimpleTypes.Balance fee
                        Nonce = SimpleTypes.Nonce nonce
                        Signature = SimpleTypes.Signature (Convert.FromHexString(sigHex))
                        Hash = SimpleTypes.Hash [||]
                        GasLimit = 21000UL
                        GasPrice = 20UL
                        Data = Array.empty
                        Metadata = Map.ofList [ ("pubkey", pubKeyHex) ]
                        MaxFeePerGas = None
                        MaxPriorityFeePerGas = None
                        TransactionType = 0uy
                    }
                    
                    let hash = Trellis.Core.SimpleCrypto.transactionHashSimple tx
                    let tx' = { tx with Hash = hash }
                    
                    let admit = Trellis.Node.Mempool.acceptTransaction tx' (fun a -> bs.GetAccount a)
                    match admit with
                    | Ok _ ->
                        let (Hash hb) = hash
                        ctx.Response.ContentType <- "application/json"
                        do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = true; hash = Convert.ToHexString(hb) |}))
                    | Error err ->
                        let ok = bs.SubmitTransaction(tx')
                        ctx.Response.ContentType <- "application/json"
                        if ok then
                            let (Hash hb) = tx'.Hash
                            do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = true; hash = Convert.ToHexString(hb); warning = err |}))
                        else
                            ctx.Response.StatusCode <- 400
                            do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = false; error = err |}))
                with ex ->
                    ctx.Response.StatusCode <- 400
                    do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = false; error = ex.Message |}))
            } :> Task
        ) |> ignore

        // Streaming endpoint
        app.MapGet("/stream", RequestDelegate(fun ctx ->
            task { do! WebSocketStreaming.handleStreamingConnection ctx } :> Task
        )) |> ignore

        let listenUrl = TrellisConfig.current.ApiListenUrl
        let endpointBase =
            try
                let uri = Uri(listenUrl)
                if uri.IsDefaultPort then sprintf "%s://%s" uri.Scheme uri.Host
                else sprintf "%s://%s:%d" uri.Scheme uri.Host uri.Port
            with
            | _ -> listenUrl.TrimEnd('/')
        let streamScheme =
            if endpointBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then "wss://"
            elif endpointBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) then "ws://"
            else "ws://"
        let streamBase =
            if endpointBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then endpointBase.Replace("https://", streamScheme)
            elif endpointBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) then endpointBase.Replace("http://", streamScheme)
            else endpointBase

        printfn "Trellis Node .NET 10 starting..."
        printfn "  Explorer: %s/explorer.html" endpointBase
        printfn "  Health:   %s/health" endpointBase
        printfn "  Stream:   %s/stream" streamBase
        printfn "Press Ctrl+C to stop"

        try app.Run(listenUrl); 0
        with ex -> printfn "Error: %s" ex.Message; 1
