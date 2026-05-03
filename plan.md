# Trellis — Open Source .NET Blockchain Development Kit

**SDK**: .NET 10 LTS (3-year support through 2029)  
**Language**: F# 10 (parallel compilation, TailCall CE, and!, post-quantum crypto)  
**Platform**: Cross-platform (Windows, Linux, macOS)  
**Repository**: `trellis`  
**Budget**: $6/month (domain + demo VPS — all tooling is free)

---

## Product Strategy

**Trellis is a codebase that does two things at once: a reference implementation and a dev kit.**

| Role | Audience | Value |
|------|----------|-------|
| Reference implementation | .NET devs learning blockchain internals | The only complete open-source .NET blockchain you can study, run, and modify |
| Dev kit (NuGet) | .NET teams building permissioned chains | Install packages, configure via JSON, run your own network |
| Educational resource | Enterprise teams, students, conference talks | Blog posts, workshops, courses — learn blockchain by reading real F# code |
| Open source (MIT) | Everyone | Free to use, modify, deploy — no tokens, no fees, no permission |
| No hosted service | We never touch your nodes | You run your own infrastructure |
| F# functional core | Immutable state transitions, discriminated unions for consensus actions, pattern matching for validation |
| Built-in explorer | Every node ships with a zero-dependency HTML block explorer at `/explorer.html` |

### Why .NET?

No other blockchain framework exists for .NET developers. Hyperledger Fabric is Go/JS. Substrate is Rust. Cosmos SDK is Go. Avalanche is Go. Ethereum clients are Go/Rust/Java.

.NET shops (banks, insurance, healthcare, government) who need a private chain currently have zero options in their native ecosystem. Trellis fills that gap.

### Market Reality (Reddit Research, May 2026)

Reddit shows zero demand pull — nobody is asking for a .NET blockchain dev kit. But it also shows zero supply — F# and C# developers asking "how do I learn blockchain in my language?" since 2018 with no good answers.

**The real gap is not tools. It's knowledge.** .NET developers want to understand blockchain internals. A working F# codebase they can read, run, and modify is more valuable to them right now than a polished framework. The NuGet packages serve the advanced user who graduates from study to deployment.

**Go-to-market:** Content first, tools second.
- Blog posts walking through each module (types → crypto → consensus → P2P → API)
- "Build a blockchain in F#" workshop/talk series
- The codebase IS the curriculum — every module is documented inline
- NuGet packages are the graduation step: "now deploy what you learned"
- No paid marketing. Organic discovery through .NET content channels.

---

## Architecture

```
Trellis.Core (NuGet: Trellis.Chain)
├── SimpleTypes.fs        — Canonical domain types (Block, Transaction, ChainState, Account)
├── SimpleCrypto.fs       — Ed25519 + BouncyCastle + Merkle + Post-Quantum module
├── SimpleDPoS.fs         — DPoS consensus (validators, staking, block producer selection)
├── DPoSGovernance.fs     — On-chain governance (proposals, voting)
├── DPoSTokenomics.fs     — Token economics (supply, rewards, halving, inflation)
├── TreasurySystem.fs     — Treasury management (funding, allocation, reserves)
├── Mining.fs             — Multi-threaded SHA-256 PoW with difficulty retargeting
├── SimpleRocksDB.fs      — RocksDB persistence layer
├── StateSnapshots.fs     — Write-ahead journal + state snapshots
├── Serialization.fs      — JSON codecs for all types
├── SecurityAudit.fs      — Built-in security auditing (crypto, consensus, input validation)
├── ChainParams.fs        — Network parameters (chainId, cutover config)
└── Monitoring.fs         — Metrics + health + alerting

Trellis.Node (NuGet: Trellis.Node)
├── P2PNetwork.fs         — Kademlia DHT + gossip (1083 lines)
├── BlockchainState.fs    — In-memory blockchain state with file persistence (704 lines)
├── Mempool.fs            — Transaction mempool with RocksDB persistence
├── EnhancedMempool.fs    — Per-sender queues + EIP-1559 priority + replacement
├── GrpcService.fs        — gRPC API + governance/tokenomics/treasury endpoints
├── WebSocketStreaming.fs — Real-time block/tx events over WebSocket
├── ExplorerApi.fs        — REST API for block explorer (/health, /block/, /validators, /account/)
├── SecurityAuditService.fs — Background security monitoring
├── MonitoringDashboard.fs  — Real-time metrics dashboard
├── Program.fs            — ASP.NET Core host with WebSocket + REST endpoints
└── wwwroot/explorer.html — Zero-dependency block explorer (vanilla JS)

Trellis.Wallet             — Avalonia desktop wallet (functional, UI dialogs stubbed)
Trellis.Testnet/Regnet/Beta — Testnet orchestrators + incentive systems
Trellis.Indexer             — PostgreSQL compliance indexer
```

---

## Current State (May 2026)

| Component | Status |
|-----------|--------|
| Types + Crypto | ✅ Single canonical module, Ed25519 + post-quantum |
| DPoS Consensus | ✅ Validators, staking, block producer selection |
| Governance + Treasury | ✅ Proposals, voting, funding, reserves |
| PoW Mining | ✅ Multi-threaded SHA-256 with difficulty target |
| P2P Network | ✅ Kademlia DHT, peer health, message serialization |
| Mempool | ✅ Per-sender queues, EIP-1559, replacement |
| Storage | ✅ RocksDB + JSON fallback + WAL |
| gRPC API | ✅ Service methods + streaming |
| WebSocket Streaming | ✅ Real-time block/tx push |
| Block Explorer | ✅ Static HTML served by node at /explorer.html |
| REST API | ✅ /health, /block/{hash}, /validators, /chain-state, /account/{address} |
| Documentation | ✅ GitHub Pages site in `docs/` |
| CLI Tool | ✅ `trellis init`, `key generate`, `start`, `dev-net` |
| NuGet Packages | ✅ Trellis.Chain, Trellis.Node, Trellis.CLI, Trellis.Templates |
| `dotnet new` Template | ✅ `dotnet new trellis-chain` |
| Performance Benchmarks | ✅ Documented in `docs/benchmarks.md` |
| CI Pipeline | ✅ GitHub Actions (build, test, publish) |
| Cross-platform | ✅ Verified on Windows, Linux, macOS |

---

## Development Roadmap

### Phase A: Developer Experience (Priority: NOW)

| Task | Deliverable |
|------|------------|
| 1. Configuration system | `appsettings.json` — extract all hardcoded params (chainId, blockTime, validatorCount, rewards, genesis) |
| 2. CLI tool | `dotnet tool install -g Trellis.CLI` — `trellis init`, `trellis key generate`, `trellis start`, `trellis dev-net` |
| 3. NuGet packaging | Three packages: `Trellis.Chain` (Core), `Trellis.Network` (P2P), `Trellis.Node` (full host) |

### Phase B: Developer Tooling (Priority: NEXT)

| Task | Deliverable |
|------|------------|
| 4. `dotnet new` template | `dotnet new trellis-chain -n MyChain` scaffolds a working project |
| 5. Docker compose dev-net | 4-node local testnet, 30-second startup, pre-funded accounts |
| 6. Example chains | Token chain, DAO voting chain, supply chain asset tracking |

### Phase C: Polish & Launch (✅ COMPLETE)

| Task | Deliverable |
|------|------------|
| 7. Documentation site | ✅ GitHub Pages site in `docs/` |
| 8. Performance benchmarks | ✅ Documented in `docs/benchmarks.md`; script in `benchmarks/` |
| 9. CI pipeline | ✅ GitHub Actions in `.github/workflows/ci.yml` |
| 10. Cross-platform verification | ✅ CI matrix covers ubuntu, windows, macOS |

### Anti-Scope (What We Don't Build)

- Smart contract VM (WASM/eBPF)
- Cross-chain bridges (IBC)
- Mobile wallet app
- Hosted explorer service
- Fiat on-ramp / exchange listing
- Consulting / node maintenance obligations
- Marketing budget / paid advertising

---

## Costs

| Item | Monthly |
|------|---------|
| Domain | ~$1 |
| Demo VPS (Hetzner/Vultr) | ~$5 |
| GitHub (repo + CI + Pages + NuGet) | Free |
| **Total** | **~$6/month** |

---

## Quick Start (Current)

```bash
git clone https://github.com/trellis/trellis
cd trellis
dotnet build --configuration Release
dotnet run --project src/Trellis.Node

# Open http://localhost:5000/explorer.html
# Stream: ws://localhost:5000/stream
# Health: http://localhost:5000/health
```

## Quick Start (After Phase A)

```bash
dotnet tool install -g Trellis.CLI
trellis init mychain
trellis start
# Open http://localhost:5000/explorer.html
```

## Quick Start (After Phase B)

```bash
dotnet new install Trellis.Templates
dotnet new trellis-chain -n MyChain
cd MyChain
dotnet run
# Open http://localhost:5000/explorer.html
```

---

## Why This Wins

1. **No competition** — the only .NET blockchain framework in existence
2. **Zero cost to use** — open source, no token, no hosted service requirement
3. **Zero cost to build** — free CI, free hosting, free NuGet
4. **Solo-maintainable** — single developer can own the entire project
5. **Enterprise-ready** — F# types, RocksDB, BouncyCastle, ASP.NET Core — all production-grade
6. **Built-in tooling** — block explorer, health checks, monitoring ship with every node
7. **Post-quantum ready** — ML-DSA (FIPS 204) integration via .NET 10
8. **Server GC + NativeAOT** — .NET 10 runtime optimizations for production deployment
