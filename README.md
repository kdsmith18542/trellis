# Trellis

The open source .NET blockchain — a complete reference implementation you can study, run, and build on.

## What is this?

Trellis is two things:

**1. A working blockchain you can read.** The only complete open-source .NET blockchain codebase. Study how types, crypto, consensus, P2P, and APIs fit together — all in idiomatic F#. Every module is documented inline.

**2. A dev kit you can build on.** Install NuGet packages, configure via JSON, and run your own permissioned chain. Ships with a CLI, block explorer, Docker dev-net, and `dotnet new` template.

## Why

No blockchain framework exists for .NET. No blockchain *learning resource* exists for .NET developers. Trellis fills both gaps — a readable F# codebase you can learn from, and NuGet packages you can deploy.

## What

- **DPoS consensus** — validators, staking, governance, treasury
- **EIP-1559 transactions** — base fee, priority fee, per-sender mempool queues
- **P2P networking** — Kademlia DHT, gossip, peer health scoring
- **RocksDB storage** — persistent state with write-ahead journal
- **gRPC + REST API** — blocks, transactions, accounts, validators
- **WebSocket streaming** — real-time block and transaction events
- **Built-in explorer** — zero-dependency HTML block explorer ships with every node
- **Post-quantum ready** — ML-DSA (FIPS 204) via BouncyCastle + .NET 10
- **Enterprise features** — security audit framework, compliance indexer, monitoring dashboard

## Quick Start

```bash
git clone https://github.com/trellis/trellis
cd trellis
dotnet build --configuration Release
dotnet run --project src/Trellis.Node
```

Open `http://localhost:5000/explorer.html`

## API

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Node health + connected clients |
| `/block/{hash}` | GET | Block by hash |
| `/validators` | GET | Validator list with stake info |
| `/chain-state` | GET | Current height, hash, base fee |
| `/account/{address}` | GET | Account balance + nonce |
| `/stream` | WebSocket | Real-time block/tx events |

## Stack

- .NET 10 LTS (F# 10)
- BouncyCastle 2.6.2 (Ed25519 + post-quantum)
- RocksDB (persistent state)
- ASP.NET Core (Kestrel, WebSocket, static files)

## Structure

```
src/
├── Trellis.Core/    → Types, crypto, consensus, storage, tokenomics, mining
├── Trellis.Node/    → P2P, mempool, API, explorer, monitoring, host
├── Trellis.Wallet/  → Avalonia desktop wallet
├── Trellis.Indexer/ → PostgreSQL compliance indexer
├── Trellis.Testnet/ → Multi-node testnet orchestrator
├── Trellis.Regnet/  → Regression test runner
└── Trellis.Beta/    → Beta testnet with incentives

tests/
└── Trellis.Tests/   → xUnit + FsCheck tests
```

## Build

```bash
dotnet build --configuration Release   # Build all projects
dotnet test --configuration Release    # Run tests
```

## License

MIT
