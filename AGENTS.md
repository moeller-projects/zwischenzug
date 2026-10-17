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
dotnet run --project benchmarks/Zwischenzug.Benchmarks -c Release --no-build -- --job Short --filter '*'
```

## Runtime invariants

- `Engine` has one search owner. Cancel and await before position/hash mutations.
- Never reuse TT scores across halfmove-clock contexts. Repetition keys remain clock-independent.
- Game streams are authoritative. Reconcile a fresh snapshot after an ambiguous move POST; never blindly retry.
- Challenge/game identity is exact; keep pending reservations keyed by challenge ID.
- Benchmark depth-five signature: `BenchmarkSuite.ExpectedDefaultNodes`. Update code, regression assertion and README intentionally when search semantics change.
- Exhaustive make/unmake restoration covers the deepest accepted perft trees, including incremental-key recomputation.
- Search depth is bounded at 94 plies; Position history at 1,024. UCI Threads supports 1–128 Lazy SMP workers with independent Searcher/Position/evaluator state and stripe-locked TT bucket access. Clear, resize, and generation changes only when workers are idle. Ponder time starts at ponderhit.
- Live Lichess/account upgrade, Native AOT publish and Docker verification require separate prerequisites/authorization. Never claim them from unit tests.
