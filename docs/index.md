# Trellis

Open source .NET blockchain development kit. Build your own permissioned blockchain in F#.

- [Getting Started](/trellis/getting-started)
- [Configuration](/trellis/configuration)
- [API Reference](/trellis/api-reference)
- [Examples](/trellis/examples)

## What is Trellis?

Trellis is a **development kit**, not a hosted blockchain. You install it via NuGet, configure your chain parameters in `appsettings.json`, and run your own nodes. No tokens, no fees, no permission needed.

- **DPoS consensus** with on-chain governance and treasury
- **EIP-1559 transactions** with priority fee mempool
- **P2P networking** via Kademlia DHT and gossip
- **Built-in block explorer** served by every node
- **Post-quantum ready** — ML-DSA (FIPS 204)

## Quick Start

```bash
# Install the CLI tool
dotnet tool install -g Trellis.CLI

# Create a new chain
trellis init my-chain
cd my-chain

# Start the node
trellis start
```

Or use the `dotnet new` template:

```bash
dotnet new install Trellis.Templates
dotnet new trellis-chain -n my-chain
cd my-chain
dotnet run
```

Open [http://localhost:5000/explorer.html](http://localhost:5000/explorer.html)
