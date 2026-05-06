# Build a Blockchain in F#: From Types to Consensus

The blockchain world is often associated with C++, Go, or Rust. But what if I told you that F#—with its powerful type system, immutability by default, and .NET 10 performance—is actually one of the best languages for building distributed ledgers?

In this post, we'll walk through **Trellis**, an open-source F# reference implementation of a permissioned blockchain.

## Why F#?

Blockchain development is essentially about managing state transitions and verifying cryptographic proofs. F# shines here because:

1. **Domain Modeling**: Discriminated unions make it impossible to represent invalid transactions.
2. **Safety**: Immutability ensures that once a block is hashed, its contents can't be accidentally modified.
3. **Conciseness**: What takes 500 lines in C++ takes 50 in F#.

## 1. The Core: Modeling the Chain

We start with the basic building blocks: Addresses, Balances, and Blocks. In F#, these are just a few lines:

```fsharp
type Address = Address of byte[]
type Balance = Balance of uint64
type Hash = Hash of byte[]

type BlockHeader = {
    Height: BlockHeight
    PreviousHash: Hash
    Timestamp: Timestamp
    Nonce: uint64
    Difficulty: uint32
}

type Block = {
    Header: BlockHeader
    Transactions: Transaction list
}
```

## 2. Cryptography with BouncyCastle

Trellis uses Ed25519 for signatures—a modern, high-performance elliptic curve. Using the BouncyCastle library in F# is straightforward:

```fsharp
let sign (privateKey: PrivateKey) (message: byte[]) : Signature =
    let (PrivateKey sk) = privateKey
    let priv = Ed25519PrivateKeyParameters(sk, 0)
    let signer = new Ed25519Signer()
    signer.Init(true, priv)
    signer.BlockUpdate(message, 0, message.Length)
    Signature (signer.GenerateSignature())
```

## 3. Consensus: DPoS (Delegated Proof of Stake)

Unlike Bitcoin's energy-hungry Proof of Work, Trellis uses DPoS. Token holders vote for validators who are responsible for producing blocks. The state transition logic is a pure function:

```fsharp
let applyTransaction (state: ChainState) (tx: Transaction) =
    if not (verifySignature tx) then Error "Invalid Signature"
    elif state.Balances.[tx.From] < tx.Amount + tx.Fee then Error "Insufficient Funds"
    else
        let newState = { state with 
            Balances = state.Balances 
                |> Map.add tx.From (state.Balances.[tx.From] - tx.Amount - tx.Fee)
                |> Map.add tx.To (state.Balances.[tx.To] + tx.Amount)
        }
        Ok newState
```

## 4. The Node: ASP.NET Core & WebSockets

A blockchain is useless if it can't talk to the world. Trellis leverages ASP.NET Core for its gRPC API and WebSocket streaming. You can watch blocks being produced in real-time using a simple Zero-Dependency HTML explorer that ships with every node.

## Getting Started

You can run your own Trellis node today:

```bash
git clone https://github.com/trellis/trellis
dotnet build -c Release
dotnet run --project src/Trellis.Node
```

Visit `http://localhost:5000/explorer.html` and you're part of the chain.

## What's Next?

Trellis is more than a reference implementation—it's a dev kit. In the next post, we'll look at how to use the `Trellis.CLI` tool to initialize your own custom chain project in seconds.

---
*Trellis is open-source and built for the .NET community. Check it out on GitHub!*
