// Standalone F# script to sign and send a transaction
// Only references Trellis.Core (not Node) to avoid HTTP interference
#r "G:/BACKUP/trellis/src/Trellis.Core/bin/Release/net10.0/Trellis.Core.dll"
#r "G:/BACKUP/trellis/src/Trellis.Core/bin/Release/net10.0/Org.BouncyCastle.Crypto.dll"

open System
open System.Net
open System.Text
open Trellis.Core
open SimpleTypes

let nodeUrl = "http://127.0.0.1:5001/api/send-transaction"
let fromHex = "0102030405"
let toAddr = "060708090A"

printfn "=== Trellis Transaction Test ==="
printfn "Node: %s" nodeUrl
printfn ""

// Generate key pair
let (pk, sk) = SimpleCrypto.generateKeyPair ()
let (PublicKey pkBytes) = pk
let (PrivateKey skBytes) = sk
let pkHex = Convert.ToHexString(pkBytes).ToLowerInvariant()
let skHex = Convert.ToHexString(skBytes).ToLowerInvariant()

printfn "Public key:  %s" pkHex
printfn "Private key: %s" skHex
printfn ""

// Build unsigned transaction
let unsignedTx: Transaction = {
    From = Address (Convert.FromHexString(fromHex))
    To = Address (Convert.FromHexString(toAddr))
    Amount = Balance 100UL
    Fee = Balance 10UL
    Nonce = Nonce 1UL
    Signature = Signature Array.empty
    Hash = Hash Array.empty
    GasLimit = 21000UL
    GasPrice = 20UL
    Data = Array.empty
    Metadata = Map.ofList [ ("pubkey", pkHex) ]
    MaxFeePerGas = None
    MaxPriorityFeePerGas = None
    TransactionType = 0uy
}

// Compute hash and sign
let (Hash msgHash) = SimpleCrypto.transactionHashSimple unsignedTx
let signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer()
signer.Init(true, Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(skBytes, 0))
signer.BlockUpdate(msgHash, 0, msgHash.Length)
let sigBytes = signer.GenerateSignature()
let sigHex = Convert.ToHexString(sigBytes).ToLowerInvariant()

let tx =
    { unsignedTx with 
        Signature = Signature sigBytes
        Hash = SimpleCrypto.transactionHashSimple unsignedTx
    }

let (Hash txHash) = tx.Hash
let txHashHex = Convert.ToHexString(txHash).ToLowerInvariant()

printfn "Tx hash:     %s" txHashHex
printfn ""

// Build JSON and send
let json = sprintf """{"from":"%s","to":"%s","amount":100,"fee":10,"nonce":1,"publicKey":"%s","signature":"%s"}""" fromHex toAddr pkHex sigHex

printfn "Sending: %s" (json.[.. min 99 (json.Length-1)]) + "..."
printfn ""

try
    use client = new WebClient()
    client.Encoding <- Encoding.UTF8
    client.Headers.Add(HttpRequestHeader.ContentType, "application/json")
    let response = client.UploadString(nodeUrl, json)
    printfn "SUCCESS: %s" response
    0
with e ->
    match e with
    | :? WebException as ex ->
        use stream = ex.Response.GetResponseStream()
        use reader = new System.IO.StreamReader(stream)
        let body = reader.ReadToEnd()
        let statusCode = (ex.Response :?> HttpWebResponse).StatusCode
        printfn "Response (%d): %s" (int statusCode) body
        1
    | ex ->
        printfn "Error: %s" ex.Message
        1
