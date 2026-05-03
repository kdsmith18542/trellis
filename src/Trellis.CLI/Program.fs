namespace Trellis.CLI

open System
open System.IO
open System.Text.Json
open Trellis.Core
open SimpleTypes

module Program =

    let private printUsage () =
        printfn "Trellis CLI"
        printfn ""
        printfn "Usage:"
        printfn "  trellis init <name>        Create a new chain project"
        printfn "  trellis key generate       Generate a new key pair"
        printfn "  trellis start              Start the node"
        printfn "  trellis dev-net            Create a 4-node dev network"
        printfn ""

    let private ensureDir (path: string) =
        if not (Directory.Exists path) then Directory.CreateDirectory(path) |> ignore

    let private writeJson (path: string) (value: obj) =
        let options = JsonSerializerOptions(WriteIndented = true)
        File.WriteAllText(path, JsonSerializer.Serialize(value, options))

    let private randomChainId () =
        let rng = Random()
        rng.Next(1000, 99999) |> uint32

    let init (name: string) =
        if String.IsNullOrWhiteSpace(name) then
            printfn "Error: project name is required"
            1
        else
            let dir = Path.Combine(Environment.CurrentDirectory, name)
            if Directory.Exists(dir) then
                printfn "Error: directory '%s' already exists" name
                1
            else
                ensureDir(dir)
                let chainId = randomChainId ()
                let cfg = { TrellisConfig.defaultConfig with ChainId = chainId; NetworkName = name.ToLowerInvariant() }
                let json = {| Trellis = cfg |}
                writeJson (Path.Combine(dir, "appsettings.json")) json
                File.WriteAllText(Path.Combine(dir, "README.md"), sprintf "# %s\n\nA Trellis chain project.\n\n## Start\n\n```bash\ntrellis start\n```\n" name)
                let runCmd = Path.Combine(dir, "run.cmd")
                File.WriteAllText(runCmd, "@echo off\ntrellis start\n")
                let runSh = Path.Combine(dir, "run.sh")
                File.WriteAllText(runSh, "#!/bin/sh\ntrellis start\n")
                printfn "Created chain project '%s' in ./%s" name name
                printfn "  Chain ID: %d" chainId
                printfn "  Network:  %s" (name.ToLowerInvariant())
                0

    let keyGenerate () =
        let (pk, sk) = SimpleCrypto.generateKeyPair ()
        let address = SimpleCrypto.addressFromPublicKey pk
        let (Address addrBytes) = address
        let (PublicKey pkBytes) = pk
        let (PrivateKey skBytes) = sk
        let hexAddr = System.Convert.ToHexString(addrBytes).ToLowerInvariant()
        let hexPk = System.Convert.ToHexString(pkBytes).ToLowerInvariant()
        let hexSk = System.Convert.ToHexString(skBytes).ToLowerInvariant()
        printfn "Address:     %s" hexAddr
        printfn "Public Key:  %s" hexPk
        printfn "Private Key: %s" hexSk
        printfn "WARNING: Store the private key securely. It cannot be recovered."
        0

    let start () =
        printfn "Starting Trellis node..."
        Trellis.Node.Program.main [| |]

    let devNet () =
        let baseDir = Path.Combine(Environment.CurrentDirectory, "dev-net")
        ensureDir(baseDir)
        for i in 1..4 do
            let nodeDir = Path.Combine(baseDir, sprintf "node%d" i)
            ensureDir(nodeDir)
            let apiPort = 5000 + i
            let cfg = {
                TrellisConfig.defaultConfig with
                    ChainId = 1337u
                    NetworkName = "trellis-dev-net"
                    ApiListenUrl = sprintf "http://localhost:%d" apiPort
            }
            let json = {| Trellis = cfg |}
            writeJson (Path.Combine(nodeDir, "appsettings.json")) json
            printfn "Created node%d (API: http://localhost:%d)" i apiPort
        printfn ""
        printfn "To start each node, run in separate terminals:"
        for i in 1..4 do
            let nodeDir = Path.Combine(baseDir, sprintf "node%d" i)
            printfn "  cd %s && trellis start" nodeDir
        printfn ""
        0

    [<EntryPoint>]
    let main args =
        try
            match args with
            | [| "init"; name |] -> init name
            | [| "key"; "generate" |] -> keyGenerate ()
            | [| "start" |] -> start ()
            | [| "dev-net" |] -> devNet ()
            | _ -> printUsage (); 1
        with ex ->
            printfn "Error: %s" ex.Message
            1
