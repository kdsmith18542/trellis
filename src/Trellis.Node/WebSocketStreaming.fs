namespace Trellis.Node

open System
open System.Net.WebSockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open System.Collections.Concurrent

/// .NET 10 real-time blockchain streaming over WebSockets
module WebSocketStreaming =

    type StreamEvent =
        | NewBlock of blockJson: string
        | NewTransaction of txJson: string
        | ChainUpdate of chainInfoJson: string

    type ClientSession = {
        Id: string
        Socket: WebSocket
    }

    let private subscribers = ConcurrentDictionary<string, ClientSession>()

    let private encodeEvent (event: StreamEvent) =
        let prefix, json =
            match event with
            | NewBlock j -> "BLOCK:", j
            | NewTransaction j -> "TX:", j
            | ChainUpdate j -> "CHAIN:", j
        Encoding.UTF8.GetBytes(prefix + json) |> ArraySegment<byte>

    /// Broadcast an event to all connected clients
    let broadcastEvent (event: StreamEvent) = task {
        let message = encodeEvent event
        let deadClients = ResizeArray<string>()

        for kvp in subscribers do
            try
                if kvp.Value.Socket.State = WebSocketState.Open then
                    do! kvp.Value.Socket.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None)
                else
                    deadClients.Add(kvp.Key)
            with _ ->
                deadClients.Add(kvp.Key)

        for id in deadClients do
            subscribers.TryRemove(id) |> ignore
    }

    /// Handle a new WebSocket connection for streaming
    let handleStreamingConnection (ctx: HttpContext) = task {
        if ctx.WebSockets.IsWebSocketRequest then
            let! webSocket = ctx.WebSockets.AcceptWebSocketAsync()
            let clientId = Guid.NewGuid().ToString("N")
            let logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WS")

            logger.LogInformation("Streaming client connected: {ClientId}", clientId)
            subscribers.TryAdd(clientId, { Id = clientId; Socket = webSocket }) |> ignore

            // Welcome
            let welcome = Encoding.UTF8.GetBytes("""CONNECTED:{"protocol":"trellis-stream/v1"}""") |> ArraySegment<byte>
            do! webSocket.SendAsync(welcome, WebSocketMessageType.Text, true, CancellationToken.None)

            // Keep alive until client closes
            let buffer = Array.zeroCreate<byte> 4096 |> ArraySegment<byte>
            let mutable openConn = true
            while openConn && webSocket.State = WebSocketState.Open do
                try
                    let! result = webSocket.ReceiveAsync(buffer, CancellationToken.None)
                    openConn <- result.MessageType <> WebSocketMessageType.Close
                with _ ->
                    openConn <- false

            logger.LogInformation("Streaming client disconnected: {ClientId}", clientId)
            subscribers.TryRemove(clientId) |> ignore
        else
            ctx.Response.StatusCode <- 400
            do! ctx.Response.WriteAsync("WebSocket connections only")
    }

    let subscriberCount () = subscribers.Count

    let disconnectAll () = task {
        for kvp in subscribers do
            try
                if kvp.Value.Socket.State = WebSocketState.Open then
                    do! kvp.Value.Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server shutting down",
                        CancellationToken.None)
            with _ -> ()
        subscribers.Clear()
    }
