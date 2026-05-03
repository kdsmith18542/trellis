namespace Trellis.Node

open System
open System.IO
open System.Text.Json
open Trellis.Core

/// Loads Trellis configuration from appsettings.json at startup.
module ConfigLoader =

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o

    /// Read configuration from appsettings.json and override the global config.
    /// Checks current working directory first, then the app base directory.
    /// Safe to call multiple times — falls back to defaults if the file is missing or malformed.
    let load () : unit =
        let paths = [
            Path.Combine(Environment.CurrentDirectory, "appsettings.json")
            Path.Combine(AppContext.BaseDirectory, "appsettings.json")
            Path.Combine(AppContext.BaseDirectory, "..", "appsettings.json") // dev helper
        ]
        let path = paths |> List.tryFind File.Exists
        match path with
        | None ->
            printfn "ConfigLoader: appsettings.json not found, using defaults."
        | Some p ->
            try
                let json = File.ReadAllText(p)
                // The JSON may have a nested "Trellis" section or be flat.
                // Try nested first.
                let nested =
                    try
                        let doc = JsonDocument.Parse(json)
                        match doc.RootElement.TryGetProperty("Trellis") with
                        | true, section -> Some (section.GetRawText())
                        | false, _ -> None
                    with _ -> None
                let configJson = nested |> Option.defaultValue json
                let loaded = JsonSerializer.Deserialize<TrellisConfig>(configJson, options)
                if not (isNull (box loaded)) then
                    TrellisConfig.current <- loaded
                    printfn "ConfigLoader: loaded config from %s" p
                else
                    printfn "ConfigLoader: deserialized config was null, using defaults."
            with ex ->
                printfn "ConfigLoader: failed to parse config (%s), using defaults." ex.Message
