# Zwischenzug engineering rules

- .NET 10, C# 14, nullable, warnings as errors; central NuGet versions.
- UCI owns stdout. Diagnostics and hosting logs go to stderr.
- Invariant culture; libraries are trim/AOT compatible. Use generated JSON metadata.
- Search hot path: preallocated storage, spans, value types; no LINQ or allocation.
- Core has no dependencies. Engine depends on Core; adapters depend on Engine and never on one another. CLI composes dependencies.
- Keep public APIs small, search deterministic, and update the benchmark signature intentionally.
- Never commit credentials. Lichess token: LICHESS__TOKEN.

## Commands

Explicit authorization is required independently for restore, build, test, publish and image-build.

```sh
dotnet restore Zwischenzug.slnx
dotnet build Zwischenzug.slnx -c Release --no-restore
dotnet test --solution Zwischenzug.slnx -c Release --no-build
dotnet run --project src/Zwischenzug.Cli -c Release --no-build -- perft 5
dotnet run --project src/Zwischenzug.Cli -c Release --no-build -- bench 5
```
