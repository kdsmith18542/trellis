namespace Trellis.Node

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Trellis.Core
open SimpleTypes

/// Simple node implementation for Trellis
module SimpleNode =

    /// Node state
    type NodeState = {
        CurrentBlock: Block option
        Accounts: Map<Address, Account>
        IsRunning: bool
    }

    /// Node messages
    type NodeMessage =
        | Start
        | Stop
        | ProcessTransaction of Transaction
        | GetState of AsyncReplyChannel<NodeState>

    /// Simple node agent
    let createNodeAgent (logger: ILogger) =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (state: NodeState) = async {
                let! msg = inbox.Receive()
                
                match msg with
                | Start ->
                    logger.LogInformation("Node starting...")
                    let newState = { state with IsRunning = true }
                    return! loop newState
                    
                | Stop ->
                    logger.LogInformation("Node stopping...")
                    let newState = { state with IsRunning = false }
                    return! loop newState
                    
                | ProcessTransaction tx ->
                    logger.LogInformation("Processing transaction")
                    // Simple transaction processing
                    let newState = { state with Accounts = state.Accounts }
                    return! loop newState
                    
                | GetState replyChannel ->
                    replyChannel.Reply(state)
                    return! loop state
            }
            
            loop {
                CurrentBlock = None
                Accounts = Map.empty
                IsRunning = false
            }
        )

    /// Node service
    type NodeService(logger: ILogger<NodeService>) =
        inherit BackgroundService()
        
        let nodeAgent = createNodeAgent logger
        
        override this.ExecuteAsync(stoppingToken) =
            async {
                logger.LogInformation("Trellis Node Service starting...")
                
                // Start the node
                nodeAgent.Post(Start)
                
                // Keep running until cancellation
                while not stoppingToken.IsCancellationRequested do
                    do! Async.Sleep(1000)
                
                // Stop the node
                nodeAgent.Post(Stop)
                logger.LogInformation("Trellis Node Service stopped.")
            } |> Async.StartAsTask :> Task
