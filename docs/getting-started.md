# Getting Started

## Prerequisites

- .NET 10 SDK — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows, Linux, or macOS

## Install the CLI

```bash
dotnet tool install -g Trellis.CLI
```

Or install from a local package:

```bash
dotnet tool install -g Trellis.CLI --add-source ./artifacts
```

## Create a Chain

```bash
trellis init my-chain
cd my-chain
```

This creates:
- `appsettings.json` — your chain configuration
- `README.md` — project documentation
- `run.cmd` / `run.sh` — startup scripts

## Start the Node

```bash
trellis start
```

The node starts on `http://localhost:5000` by default. Open the block explorer:

- Explorer: [http://localhost:5000/explorer.html](http://localhost:5000/explorer.html)
- Health: [http://localhost:5000/health](http://localhost:5000/health)
- Stream: `ws://localhost:5000/stream`

## Generate a Key Pair

```bash
trellis key generate
```

Output:
```
Address:     821d57cd56d06d98dbf314b6e349568278009dd5965cb49a6351b805f1a9cea9
Public Key:  45938e77687066762a2c975c9dd9bd001c7d586dd650ef419262352e311e551f
Private Key: d1c6004cfd005652d9bf74b36c63d8f686f570fe996f35eed117770a5037dbf0
```

**Store the private key securely.** It cannot be recovered.

## Using the Template

If you prefer a full project with code you can modify:

```bash
dotnet new install Trellis.Templates
dotnet new trellis-chain -n my-chain
cd my-chain
dotnet run
```

The template scaffolds a complete F# project with NuGet references to `Trellis.Chain` and `Trellis.Node`.

## Dev Network

For testing with multiple nodes:

```bash
trellis dev-net
```

This creates 4 node directories with different API ports (5001–5004). Start each in a separate terminal.
