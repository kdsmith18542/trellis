# Verified Platforms

Trellis build and run coverage:

| Platform | Build | Run | Notes |
|----------|-------|-----|-------|
| Windows 10/11 x64 | Yes | Yes | Primary dev platform |
| Ubuntu 22.04/24.04 x64 | Yes | Yes | CI matrix |
| macOS 14/15 Arm64 | Yes | Yes | CI matrix |

## Requirements

- .NET 10 SDK
- No native dependencies required (SQLite is managed-only via Microsoft.Data.Sqlite)

## Verification Commands

```bash
git clone https://github.com/trellis/trellis
cd trellis
dotnet restore Trellis.sln
dotnet build Trellis.sln --configuration Release
dotnet run --project tests/Trellis.Tests --configuration Release
```

Expected warning profile:

- 2 vulnerability warnings from Avalonia transitive `Tmds.DBus.Protocol`.
