# Trellis

Trellis is an open-source .NET blockchain development kit with a complete F# reference implementation.

## What It Is

Trellis serves two audiences:

1. Engineers learning blockchain internals in .NET through a readable end-to-end codebase.
2. Teams building permissioned chains with NuGet packages, JSON configuration, and local dev tooling.

## Quick Start

```bash
git clone https://github.com/trellis/trellis
cd trellis
dotnet restore Trellis.sln
dotnet build Trellis.sln --configuration Release
dotnet run --project src/Trellis.Node --configuration Release
```

Node endpoints:

- Explorer: `http://localhost:5000/explorer.html`
- Health: `http://localhost:5000/health`
- Stream: `ws://localhost:5000/stream`

## Build, Test, Pack

```bash
dotnet restore Trellis.sln
dotnet build Trellis.sln --configuration Release
dotnet run --project tests/Trellis.Tests --configuration Release
dotnet pack Trellis.sln --configuration Release -o artifacts
```

Notes:

- Expected warning profile is 2 NuGet vulnerability warnings from Avalonia's transitive `Tmds.DBus.Protocol`.
- Tests use the custom runner in `tests/Trellis.Tests` (not `dotnet test`).
- Pack output should contain 5 packages: `Trellis.Chain`, `Trellis.Node`, `Trellis.CLI`, `Trellis.Templates`, `Trellis.Wallet`.

## Packages

- `Trellis.Chain`: core types, crypto, consensus, storage, tokenomics.
- `Trellis.Node`: node host with P2P, mempool, APIs, streaming, explorer assets.
- `Trellis.CLI`: `trellis` tool for init, key generation, send-tx, start, dev-net.
- `Trellis.Templates`: `dotnet new trellis-chain` template.
- `Trellis.Wallet`: Avalonia desktop wallet.

## Project Structure

```text
src/
  Trellis.Core/        Core protocol and data modules (packaged)
  Trellis.Node/        Node runtime and network/API host (packaged)
  Trellis.CLI/         Command-line tool (packaged)
  Trellis.Templates/   dotnet new template package (packaged)
  Trellis.Wallet/      Avalonia wallet package (packaged)
  archive/             Legacy L1 experiments and scripts (not built)

tests/
  Trellis.Tests/       Custom unit + property + security-audit runner
```

## License

MIT
