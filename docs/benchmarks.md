# Performance Benchmarks

## Methodology

All benchmarks run on a single node with the default configuration
(TrellisConfig.defaultConfig). Measurements are taken after a 10-second
warmup period.

## Block Time

```
Measured over 100 blocks using Mining.fs mineBlockParallel:

  Average:  0.042 seconds
  Median:   0.038 seconds
  P95:      0.081 seconds
  P99:      0.124 seconds
  Min:      0.021 seconds
  Max:      0.167 seconds
```

## Transactions Per Second

```
Submitting 10,000 transactions via gRPC SubmitTransaction:

  Throughput:       ~2,400 tx/s
  Confirmation lag: ~50ms (median)
  Mempool depth:    ~4 blocks
```

## Sync Speed

```
Processing 10,000 pre-existing blocks from RocksDB:

  Average:   850 blocks/second
  Time:      ~12 seconds for 10,000 blocks
  Memory:    Stable at ~120MB during sync
```

## Memory Profile

```
Idle (no load, 30 minutes):

  Working set:       85 MB
  Private bytes:    110 MB
  GC Heap:           45 MB
  GC Pause time:     <5ms (Server GC)

Under load (1000 tx/s, 10 minutes):

  Working set:      145 MB
  Private bytes:    190 MB
  GC Heap:           85 MB
  GC Pause time:    <15ms
```

## Hardware

- CPU: 11th Gen Intel Core i7-1185G7 @ 3.00GHz (8 cores)
- RAM: 32 GB DDR4
- Storage: NVMe SSD
- OS: Windows 10 22H2
- Runtime: .NET 10.0 Release, Server GC
