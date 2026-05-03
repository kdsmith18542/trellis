# Verified Platforms

Trellis is confirmed to build and run on:

| Platform | Build | Run | Notes |
|----------|-------|-----|-------|
| Windows 10/11 x64 | ✅ | ✅ | Primary dev platform |
| Ubuntu 22.04/24.04 x64 | ✅ | ✅ | CI-verified |
| macOS 14/15 Arm64 | ✅ | ✅ | CI-verified |

## Requirements

- .NET 10 SDK (available on all platforms)
- No native dependencies (RocksDB is managed via RocksDbSharp)

## Build Verification

```bash
git clone https://github.com/trellis/trellis
cd trellis
dotnet restore Trellis.sln
dotnet build Trellis.sln --configuration Release
dotnet test Trellis.Tests --configuration Release --no-restore
```

This produces 0 errors, 0 warnings on all three platforms.
