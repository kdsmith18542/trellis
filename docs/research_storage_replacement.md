# Research: Replacing RocksDbSharp

## Status: COMPLETE (May 2026)
`RocksDbSharp` has been replaced with `Microsoft.Data.Sqlite` for mempool persistence. The dead `SimpleRocksDB.fs` and `StorageOptimization.fs` modules have been removed. The project no longer has any native database dependencies.

## Decision
**SQLite (Microsoft.Data.Sqlite v9.5.0)** was chosen because:
- No native dependencies (pure managed .NET)
- Excellent stability and ACID compliance
- Mempool persistence is simple key-value; SQLite's overhead is negligible for this use case
- The blockchain state (`InMemoryBlockchainState`) already uses JSON file persistence, not RocksDB

## Migration Details
- `src/Trellis.Node/Mempool.fs`: Replaced `RocksDbSharp.RocksDb` with `Microsoft.Data.Sqlite.SqliteConnection`
- Removed `src/Trellis.Core/SimpleRocksDB.fs` (310 lines, dead code — never instantiated)
- Removed `src/Trellis.Core/StorageOptimization.fs` (149 lines, unused config types)
- Removed `RocksDbSharp` NuGet dependency from `src/Trellis.Core/Trellis.Core.fsproj`
- Added `Microsoft.Data.Sqlite` to `src/Trellis.Node/Trellis.Node.fsproj`

## Candidates (Historical)

### 1. SQLite (Microsoft.Data.Sqlite) — SELECTED
- **Pros**: extremely stable, ACID compliant, native .NET support, great tooling.
- **Cons**: higher overhead for simple key-value lookups compared to LSM-trees.
- **Verdict**: Chosen for simplicity and managed-only deployment.

### 2. FASTER (Microsoft.FASTER)
- **Pros**: highest performance on .NET, designed for high-throughput KV.
- **Cons**: significant architectural change, more complex than RocksDB.
- **Verdict**: Overkill for current mempool-only usage. Consider if blockchain state moves to KV storage.

### 3. FasterKv (via Microsoft.FASTER.core)
- **Pros**: Modern, high-performance, managed-heap options.
- **Verdict**: Same considerations as FASTER.

### 4. Managed-only Alternatives (e.g., Bitcask-style)
- **Pros**: no native dependencies, easy deployment.
- **Cons**: often less feature-rich than RocksDB.
