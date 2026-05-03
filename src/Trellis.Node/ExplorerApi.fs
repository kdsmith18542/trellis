namespace Trellis.Node

open System
open System.Threading.Tasks
open System.Text.Json
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Trellis.Core
open SimpleTypes

/// REST API endpoints for the block explorer
module ExplorerApi =

    let private getState (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<GrpcService.IBlockchainState>()

    let private jsonResponse (ctx: HttpContext) (obj: 'a) = task {
        let json = JsonSerializer.Serialize(obj)
        ctx.Response.ContentType <- "application/json"
        do! ctx.Response.WriteAsync(json)
    }

    let private toHandler (fn: HttpContext -> Task<unit>) =
        RequestDelegate(fun ctx -> fn ctx :> Task)

    /// GET /health
    let health (ctx: HttpContext) = task {
        let result = {| status = "ok"; version = "10.0"; subscribers = WebSocketStreaming.subscriberCount() |}
        do! jsonResponse ctx result
    }

    /// GET /block/{hash}
    let getBlock (ctx: HttpContext) = task {
        let hashStr = string ctx.Request.RouteValues.["hash"]
        let hashBytes = Convert.FromHexString(hashStr : string)
        let blockHash = Hash hashBytes
        let state = getState ctx
        match state.GetBlock blockHash with
        | Some block -> do! jsonResponse ctx block
        | None -> ctx.Response.StatusCode <- 404
    }

    /// GET /validators
    let getValidators (ctx: HttpContext) = task {
        let state = getState ctx
        let validators = state.GetValidators()
        let list = validators |> List.map (fun (_, si) ->
            let (PublicKey pkBytes) = si.PublicKey
            let (Balance stake) = si.StakedAmount
            {| address = Convert.ToHexString(pkBytes).ToLowerInvariant()
               stake = stake
               isActive = si.IsActive |}
        )
        do! jsonResponse ctx list
    }

    /// GET /chain-state
    let getChainState (ctx: HttpContext) = task {
        let state = getState ctx
        let cs = state.GetChainState()
        let (BlockHeight h) = cs.CurrentHeight
        let (Hash lastHash) = cs.LastBlockHash
        let result = {|
            blockHeight = h
            lastBlockHash = Convert.ToHexString(lastHash).ToLowerInvariant()
            validatorCount = cs.Validators.Length
            baseFeePerGas = cs.BaseFeePerGas
        |}
        do! jsonResponse ctx result
    }

    /// GET /account/{address}
    let getAccount (ctx: HttpContext) = task {
        let addrStr = string ctx.Request.RouteValues.["address"]
        let addrBytes = Convert.FromHexString(addrStr : string)
        let addr = Address addrBytes
        let state = getState ctx
        match state.GetAccount addr with
        | Some acc ->
            let (Balance b) = acc.Balance
            let (Nonce n) = acc.Nonce
            let result = {| address = addrStr; balance = b; nonce = n |}
            do! jsonResponse ctx result
        | None -> ctx.Response.StatusCode <- 404
    }

    // Handler wrappers (for MapGet registration)
    let healthHandler = RequestDelegate(fun ctx -> health ctx :> Task)
    let blockHandler = RequestDelegate(fun ctx -> getBlock ctx :> Task)
    let validatorsHandler = RequestDelegate(fun ctx -> getValidators ctx :> Task)
    let chainStateHandler = RequestDelegate(fun ctx -> getChainState ctx :> Task)
    let accountHandler = RequestDelegate(fun ctx -> getAccount ctx :> Task)
