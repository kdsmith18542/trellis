# Configuration

All chain parameters are configured in `appsettings.json` under the `Trellis` section. Parameters not specified use sensible defaults.

## Chain Identity

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChainId` | uint32 | `1337` | Domain separation for signatures. Change per network. |
| `NetworkName` | string | `"trellis-dev"` | Human-readable network name. |

## Block Timing & Difficulty

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `TargetBlockTime` | uint64 | `600` | Target seconds between blocks (for retargeting). |
| `RetargetInterval` | uint64 | `2016` | Blocks between difficulty retargets. |
| `MinDifficulty` | uint64 | `1` | Minimum allowed difficulty. |
| `MaxDifficulty` | uint64 | `486604799` | Maximum allowed difficulty. |
| `GenesisDifficultyBase` | uint64 | `1000` | Starting difficulty for the genesis block. |
| `GenesisDifficultyNetworkFactor` | uint64 | `1` | Multiplier applied to base difficulty. |

## Gas & Fees

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `GenesisBaseFeePerGas` | uint64 | `1000000000` | Starting base fee per gas (wei-equivalent). |
| `GenesisGasLimit` | uint64 | `30000000` | Block gas limit. |
| `BaseFeeIncreaseMultiplier` | float | `1.125` | Multiplier when block is above target gas. |
| `BaseFeeDecreaseMultiplier` | float | `0.875` | Multiplier when block is below target gas. |

## Genesis Identity

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `GenesisBlockData` | string | `"Trellis Genesis Block..."` | Seed string for genesis block hash. |
| `GenesisValidatorSeed` | string | `"Trellis Genesis Validator"` | Seed for genesis validator address. |
| `GenesisPreviousHashSeed` | string | `"GENESIS_NULL_HASH"` | Seed for genesis previous hash. |
| `GenesisMerkleRootSeed` | string | `"GENESIS_TRANSACTION_ROOT"` | Seed for genesis merkle root. |

## Supply & Rewards

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `InitialTotalSupply` | uint64 | `10_000_000 * 10^18` | Starting token supply (18 decimals). |
| `MaxSupply` | uint64 | `21_000_000 * 10^18` | Maximum token supply. |
| `BlockReward` | uint64 | `2 * 10^18` | Base block reward (halves periodically). |
| `HalvingInterval` | uint64 | `210000` | Blocks between reward halvings. |
| `BurnRate` | decimal | `0.01` | Fraction of fees burned (1%). |
| `StakingRewardRate` | decimal | `0.05` | Annual staking reward rate (5%). |

## Governance

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `MinProposalDeposit` | uint64 | `1 * 10^18` | Minimum deposit to create a proposal. |
| `VotingPeriod` | uint64 | `10080` | Blocks a vote remains open. |
| `ExecutionDelay` | uint64 | `1440` | Blocks delay before executing a passed proposal. |
| `QuorumThreshold` | decimal | `0.2` | Minimum fraction of total stake required (20%). |
| `PassThreshold` | decimal | `0.5` | Minimum fraction of votes required to pass (50%). |
| `MaxProposalsPerBlock` | uint32 | `10` | Maximum proposals that can be submitted per block. |
| `ProposalLifetime` | uint64 | `20160` | Maximum blocks a proposal stays active. |

## P2P Network

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `P2PProtocolVersionMajor` | uint16 | `1` | Network protocol major version. |
| `P2PProtocolVersionMinor` | uint16 | `0` | Network protocol minor version. |
| `P2PProtocolVersionPatch` | uint16 | `0` | Network protocol patch version. |
| `P2PMaxBucketSize` | int | `20` | Kademlia bucket size. |
| `P2PMaxBuckets` | int | `160` | Number of Kademlia buckets. |
| `P2PRateLimitWindowSeconds` | float | `60.0` | Rate limit window for peer messages. |
| `P2PMaxMessagesPerWindow` | int | `100` | Max messages per peer per window. |
| `P2PPeerTimeoutMinutes` | float | `10.0` | Inactivity timeout for peer eviction. |
| `P2PPeerEvictionHealthThreshold` | float | `0.2` | Minimum health score to keep a peer. |
| `P2PPeerEvictionMinInteractions` | int | `10` | Minimum interactions before eviction check. |
| `P2PBlockTimeTolerancePastSeconds` | float | `1800.0` | Max seconds a block can be in the past. |
| `P2PBlockTimeToleranceFutureSeconds` | float | `3600.0` | Max seconds a block can be in the future. |

## API & Mempool

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ApiListenUrl` | string | `"http://localhost:5000"` | HTTP server listen address. |
| `MempoolMaxSize` | int | `10000` | Maximum pending transactions in mempool. |

## Cutover (PoW → DPoS transition)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `CutoverMinHeight` | uint64 | `1500000` | Minimum block height for cutover. |
| `CutoverMinMedianTimeDays` | float | `180.0` | Minimum days since genesis for cutover. |
| `CutoverMinTotalDifficulty` | uint64? | `null` | Optional terminal total difficulty. |

## Example: Low-fee Token Chain

```json
{
  "Trellis": {
    "NetworkName": "token-chain",
    "TargetBlockTime": 30,
    "GenesisBaseFeePerGas": 100000000,
    "GenesisDifficultyBase": 100,
    "BlockReward": 50000000000000000000
  }
}
```

## Example: Governance-heavy DAO Chain

```json
{
  "Trellis": {
    "NetworkName": "dao-chain",
    "VotingPeriod": 120,
    "ExecutionDelay": 10,
    "QuorumThreshold": 0.10,
    "PassThreshold": 0.40
  }
}
```

## Example: High-security Supply Chain

```json
{
  "Trellis": {
    "NetworkName": "supply-chain",
    "TargetBlockTime": 120,
    "GenesisDifficultyBase": 50000,
    "MinDifficulty": 1000,
    "GenesisBaseFeePerGas": 5000000000,
    "MempoolMaxSize": 50000
  }
}
```
