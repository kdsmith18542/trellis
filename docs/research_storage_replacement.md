# Research: Replacing RocksDbSharp

## Status
`RocksDbSharp` is currently used for persistent storage but has been unmaintained for several years. To ensure long-term stability and .NET 10 compatibility, we need a replacement.

## Candidates

### 1. SQLite (Microsoft.Data.Sqlite)
- **Pros**: extremely stable, ACID compliant, native .NET support, great tooling.
- **Cons**: higher overhead for simple key-value lookups compared to LSM-trees.
- **Verdict**: Best for complex state, but might require performance testing for block indexing.

### 2. FASTER (Microsoft.FASTER)
- **Pros**: highest performance on .NET, designed for high-throughput KV.
- **Cons**: significant architectural change, more complex than RocksDB.
- **Verdict**: Strong candidate if we need extreme performance.

### 3. FasterKv (via Microsoft.FASTER.core)
- **Pros**: Modern, high-performance, managed-heap options.
- **Verdict**: Highly recommended for .NET 10.

### 4. Managed-only Alternatives (e.g., Bitcask-style)
- **Pros**: no native dependencies, easy deployment.
- **Cons**: often less feature-rich than RocksDB.

## Recommendation
Initial research suggests **FasterKv** (Microsoft.FASTER) as the primary candidate for high-throughput state storage, or **SQLite** if we want to simplify the storage layer and add relational capabilities for the explorer.

## Next Steps
- [ ] Benchmarking `RocksDbSharp` vs `SQLite` for block storage.
- [ ] Prototype `FasterKv` for the `InMemoryBlockchainState` persistence.
