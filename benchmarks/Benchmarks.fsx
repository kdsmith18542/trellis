#!/usr/bin/env dotnet fsi

// Trellis Benchmark Runner
// Usage: dotnet fsi Benchmarks.fsx [--duration 30] [--tx-count 1000]

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Text.Json
open System.Net.Http

let args = fsi.CommandLineArgs |> Array.skip 1
let duration = ref 30
let txCount = ref 1000

for i in 0..2..args.Length-2 do
    match args.[i] with
    | "--duration" -> duration := int args.[i+1]
    | "--tx-count" -> txCount := int args.[i+1]
    | _ -> ()

let baseUrl = "http://localhost:5000"
let http = new HttpClient()

printfn "Trellis Benchmarks"
printfn "=================="
printfn ""

// 1. Health check
printfn "1. Health check..."
try
    let! health = http.GetStringAsync($"{baseUrl}/health") |> Async.AwaitTask
    printfn "   OK: %s" health
with ex ->
    printfn "   ERROR: Node not reachable at %s — is it running?" baseUrl
    printfn "   %s" ex.Message
    exit 1

// 2. Block time measurement
printfn ""
printfn "2. Block time (measuring over %d seconds)..." !duration
let stopwatch = Stopwatch.StartNew()
let mutable lastHeight = 0UL
let blockTimes = System.Collections.Generic.List<float>()

while stopwatch.Elapsed.TotalSeconds < float !duration do
    try
        let! stateJson = http.GetStringAsync($"{baseUrl}/chain-state") |> Async.AwaitTask
        let state = JsonDocument.Parse(stateJson)
        let height = state.RootElement.GetProperty("blockHeight").GetUInt64()
        if height > lastHeight && lastHeight > 0UL then
            blockTimes.Add(stopwatch.Elapsed.TotalSeconds)
            lastHeight <- height
        elif lastHeight = 0UL then
            lastHeight <- height
    with _ -> ()
    do! Async.Sleep(1000)

printfn "   Blocks produced: %d" blockTimes.Count
if blockTimes.Count > 0 then
    let avg = blockTimes |> List.ofSeq |> List.average
    printfn "   Average block time: %.3f s" avg

// 3. Validator info
printfn ""
printfn "3. Validators..."
try
    let! validators = http.GetStringAsync($"{baseUrl}/validators") |> Async.AwaitTask
    printfn "   %s" validators
with ex ->
    printfn "   ERROR: %s" ex.Message

// 4. Chain state summary
printfn ""
printfn "4. Chain state summary..."
try
    let! state = http.GetStringAsync($"{baseUrl}/chain-state") |> Async.AwaitTask
    let doc = JsonDocument.Parse(state)
    printfn "   Block height:  %d" (doc.RootElement.GetProperty("blockHeight").GetUInt64())
    printfn "   Validator count: %d" (doc.RootElement.GetProperty("validatorCount").GetInt32())
    printfn "   Base fee:      %d" (doc.RootElement.GetProperty("baseFeePerGas").GetUInt64())
with ex ->
    printfn "   ERROR: %s" ex.Message

printfn ""
printfn "Done."
