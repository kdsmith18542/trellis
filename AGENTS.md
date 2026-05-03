# AGENTS.md

Instructions for AI assistants and contributors working on Trellis.

## Build

```bash
dotnet restore Trellis.sln
dotnet build Trellis.sln --configuration Release
```

Must produce **0 errors, 0 warnings**. (2 NuGet vulnerability warnings from Avalonia's Tmds.DBus.Protocol transitive dependency are tolerated.)

## Test

```bash
dotnet run --project tests/Trellis.Tests --configuration Release
```

Tests use a custom runner (not xUnit). All 9 unit tests + 5 property test categories must pass. The security audit runs automatically and will always report some findings (it audits the codebase for known design decisions like BLS being hash-based).

## Pack

```bash
dotnet pack Trellis.sln --configuration Release -o artifacts
```

Produces 5 packages: Trellis.Chain, Trellis.Node, Trellis.CLI, Trellis.Templates, Trellis.Wallet.

## Project Structure

| Project | Output | Purpose |
|---------|--------|---------|
| `src/Trellis.Core/` | `Trellis.Chain` NuGet | Types, crypto, consensus, storage, tokenomics |
| `src/Trellis.Node/` | `Trellis.Node` NuGet | P2P, mempool, gRPC, WebSocket, explorer |
| `src/Trellis.Wallet/` | `Trellis.Wallet` NuGet | Avalonia desktop wallet GUI |
| `src/Trellis.CLI/` | `trellis` dotnet tool | CLI: init, key generate, start, dev-net |
| `src/Trellis.Templates/` | `Trellis.Templates` NuGet | `dotnet new trellis-chain` template |
| `tests/Trellis.Tests/` | Custom test runner | Unit + property-based tests + security audit |
| `src/archive/` | (not built) | Legacy L1 projects: Testnet, Beta, Indexer, Regnet |

## Key Files

- `Directory.Build.props` — shared build properties (F# 10 parallel compilation, NuGet metadata)
- `plan.md` — product strategy, architecture, roadmap
- `README.md` — project overview for visitors
- `todo.md` — current priorities
- `src/Trellis.Core/TrellisConfig.fs` — mutable global config; all chain parameters live here
- `src/Trellis.Node/ConfigLoader.fs` — loads config from `appsettings.json` at startup
- `src/Trellis.Node/Program.fs` — node entry point (ASP.NET Core host)
- `src/Trellis.Node/wwwroot/explorer.html` — static block explorer

## Conventions

- F# 10 with `LangVersion 10.0`, parallel compilation enabled
- No comments added to existing code unless fixing a bug or asked
- Use `edit` tool for modifications to existing files, `write` for new files
- Build with `--configuration Release` unless testing/debugging
- All chain parameters reading from `TrellisConfig.current` rather than hardcoded literals
- Packages: `dotnet pack` from solution level, output to `artifacts/`
