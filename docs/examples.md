# Examples

Pre-configured chain configurations that you can use as starting points.

## Token Chain

A simple token transfer chain with fast 30-second blocks.

```bash
cd examples/token-chain
trellis start
```

Configuration highlights:
- 30-second block times
- 50 TRL block reward
- 5% fee burn rate
- 10% staking reward rate

## DAO Chain

A governance-heavy chain for testing proposal and voting mechanics.

```bash
cd examples/dao-chain
trellis start
```

Configuration highlights:
- 30-second block times
- 2-minute voting period (120 blocks)
- 10% quorum threshold
- 40% pass threshold
- Low minimum proposal deposit

## Supply Chain

A high-security chain for supply chain asset tracking.

```bash
cd examples/supply-chain
trellis start
```

Configuration highlights:
- 2-minute block times
- High PoW difficulty (50000 base)
- 5 GWei base fee
- 50,000 tx mempool

## Custom Chain

Create your own with the CLI:

```bash
trellis init my-chain
cd my-chain
# Edit appsettings.json with your parameters
trellis start
```

Or the template:

```bash
dotnet new trellis-chain -n my-chain
cd my-chain
# Edit appsettings.json
dotnet run
```

## Dev Network (Multi-Node)

For testing with multiple nodes:

```bash
trellis dev-net
cd dev-net/node1 && trellis start
# In another terminal:
cd dev-net/node2 && trellis start
```

Or use Docker:

```bash
cd docker
docker compose up
```
