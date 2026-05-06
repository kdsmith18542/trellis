namespace Trellis.CLI

open System
open System.Diagnostics
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
        printfn "  trellis send-tx <to> <amount> [fee] [--node <url>]"
        printfn "                             Send a transaction to the node"
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

    let sendTx (toAddr: string) (amount: uint64) (fee: uint64) (nodeUrl: string) =
        try
            let (pk, sk) = SimpleCrypto.generateKeyPair ()
            let (PublicKey pkBytes) = pk
            let (PrivateKey skBytes) = sk
            let pkHex = Convert.ToHexString(pkBytes).ToLowerInvariant()
            let skHex = Convert.ToHexString(skBytes).ToLowerInvariant()
            
            let fromHex = "0102030405"
            
            let unsignedTx: SimpleTypes.Transaction = {
                From = SimpleTypes.Address (Convert.FromHexString(fromHex))
                To = SimpleTypes.Address (Convert.FromHexString(toAddr))
                Amount = SimpleTypes.Balance amount
                Fee = SimpleTypes.Balance fee
                Nonce = SimpleTypes.Nonce 1UL
                Signature = SimpleTypes.Signature Array.empty
                Hash = SimpleTypes.Hash Array.empty
                GasLimit = 21000UL
                GasPrice = 20UL
                Data = Array.empty
                Metadata = Map.ofList [ ("pubkey", pkHex) ]
                MaxFeePerGas = None
                MaxPriorityFeePerGas = None
                TransactionType = 0uy
            }
            
            let (Hash msgHash) = SimpleCrypto.transactionHashSimple unsignedTx
            
            let signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer()
            signer.Init(true, Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(skBytes, 0))
            signer.BlockUpdate(msgHash, 0, msgHash.Length)
            let sigBytes = signer.GenerateSignature()
            let sigHex = Convert.ToHexString(sigBytes).ToLowerInvariant()
            
            let tx =
                { unsignedTx with 
                    Signature = SimpleTypes.Signature sigBytes
                    Hash = SimpleCrypto.transactionHashSimple unsignedTx
                }
            
            let (Hash txHash) = tx.Hash
            let txHashHex = Convert.ToHexString(txHash).ToLowerInvariant()
            
            printfn "Sending transaction..."
            printfn "  From:    %s" fromHex
            printfn "  To:      %s" toAddr
            printfn "  Amount:  %d" amount
            printfn "  Fee:     %d" fee
            printfn "  Nonce:   1"
            printfn "  Node:    %s" nodeUrl
            printfn ""
            
            let json = System.Text.Json.JsonSerializer.Serialize(
                {| from = fromHex
                   ``to`` = toAddr
                   amount = amount
                   fee = fee
                   nonce = 1UL
                   publicKey = pkHex
                   signature = sigHex |})
            
            let url = nodeUrl.TrimEnd('/') + "/api/send-transaction"
            use client = new System.Net.Http.HttpClient()
            use content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            use response = client.PostAsync(url, content).GetAwaiter().GetResult()
            let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if not response.IsSuccessStatusCode then
                failwithf "Node returned HTTP %d: %s" (int response.StatusCode) body
            printfn "Transaction submitted: %s" body
            printfn "Tx hash: %s" txHashHex
            printfn "Private key (for reference): %s" skHex
            0
        with ex ->
            printfn "Error: %s" ex.Message
            1

    let private startProcess (fileName: string) (arguments: string) =
        let psi = ProcessStartInfo()
        psi.FileName <- fileName
        psi.Arguments <- arguments
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- false
        psi.RedirectStandardError <- false
        use proc = Process.Start(psi)
        proc.WaitForExit()
        proc.ExitCode

    let start () =
        printfn "Starting Trellis node..."
        let nodeDll = Path.Combine(AppContext.BaseDirectory, "Trellis.Node.dll")
        if File.Exists(nodeDll) then
            startProcess "dotnet" (sprintf "\"%s\"" nodeDll)
        else
            let candidates = [
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Trellis.Node", "Trellis.Node.fsproj")
                Path.Combine(Environment.CurrentDirectory, "src", "Trellis.Node", "Trellis.Node.fsproj")
            ]
            match candidates |> List.tryFind File.Exists with
            | Some nodeProject ->
                startProcess "dotnet" (sprintf "run --project \"%s\" --configuration Release" (Path.GetFullPath(nodeProject)))
            | None ->
                printfn "Error: Trellis.Node runtime not found."
                printfn "Install Trellis.Node or run from the repository root with src/Trellis.Node available."
                1

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
            | [| "send-tx"; toAddr; amount; fee |] -> sendTx toAddr (UInt64.Parse(amount)) (UInt64.Parse(fee)) "http://localhost:5001"
            | [| "send-tx"; toAddr; amount |] -> sendTx toAddr (UInt64.Parse(amount)) 10UL "http://localhost:5001"
            | [| "start" |] -> start ()
            | [| "dev-net" |] -> devNet ()
            | _ -> printUsage (); 1
        with ex ->
            printfn "Error: %s" ex.Message
            1
