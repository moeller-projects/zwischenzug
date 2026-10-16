# Zwischenzug

Single-threaded .NET 10 chess engine, with UCI and Lichess Bot API adapters.

See [AGENTS.md](AGENTS.md) for build and verification commands. Implementation follows M0–M5 of the supplied plan; progress and evidence live in [docs/implementation-ledger.md](docs/implementation-ledger.md).

## Command-line usage

The CLI uses `uci` when started without a command (or when passed `uci`). UCI protocol replies are written to stdout; diagnostics are written to stderr.

```text
dotnet run --project src/Zwischenzug.Cli -- uci
dotnet run --project src/Zwischenzug.Cli -- perft 5
dotnet run --project src/Zwischenzug.Cli -- perft 4 --fen "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -" --divide
dotnet run --project src/Zwischenzug.Cli -- bench
dotnet run --project src/Zwischenzug.Cli -- bench 6
```

`bench` searches the fixed 25-position suite in `Zwischenzug.Engine.BenchmarkSuite` (depth 5 by default) and reports total nodes, elapsed time, NPS, and transposition-table probes/hits. The committed depth-five node signature is **290,830**, observed identically on two runs. Search changes must intentionally update `ExpectedDefaultNodes` and its regression test. Runtime and NPS are hardware-dependent.

## Ten-game self-play

The commands in [tools/match/](tools/match/) run ten games between two local engine binaries with `fastchess` or `cutechess-cli`. They use a 10-second clock plus 0.1-second increment and stop after ten games. Add `-Sprt` to the fastchess script for a separate strength comparison: H0 = 0 Elo, H1 = 5 Elo, alpha = 0.05, beta = 0.05, with up to 100,000 games. Install the selected match tool separately; scripts never download tools automatically.

## Lichess bot setup

1. Create a dedicated Lichess account with **no games played**. Choose its username; this repository does not provision an account.
2. Create an [OAuth personal access token](https://lichess.org/account/oauth/token/create) with **`bot:play`** permission. Load it securely into `LICHESS__TOKEN`; never commit it, put it in a command literal, or include it in logs.
3. Upgrade the account once, manually. This is irreversible account setup and is deliberately not part of the application:

   ```powershell
   curl.exe -X POST https://lichess.org/api/bot/account/upgrade -H "Authorization: Bearer $env:LICHESS__TOKEN"
   ```

4. Set `LICHESS__USERNAME` to that account's username and start the bot:

   ```powershell
   $env:LICHESS__USERNAME = "<bot-account-name>"
   dotnet run --project src/Zwischenzug.Cli -c Release --no-build -- lichess
   ```

`Lichess:Token` and `Lichess:Username` are also accepted through .NET configuration. In Development, the CLI uses user-secrets ID `Zwischenzug.Cli`; seed the token from an existing secure environment variable:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet user-secrets set --project src/Zwischenzug.Cli Lichess:Token $env:LICHESS__TOKEN
```

The bot accepts standard chess with clock time controls, rated and casual, and at most one game by default. Configuration keys under `Lichess`:

| Key | Default | Meaning |
| --- | --- | --- |
| `MaxConcurrentGames` | `1` | Active games plus accepted pending challenges; range 1–32 |
| `AllowRated`, `AllowCasual` | `true` | Independent challenge-policy switches |
| `AllowedTimeControls:0` | `clock` | Allowed API time-control types |
| `SearchDepth` | `64` | Maximum depth, also bounded by game clocks |
| `MoveOverhead` | `50` | Milliseconds reserved from the remaining clock |
| `BaseAddress` | `https://lichess.org/` | HTTPS required; loopback HTTP is allowed for fixtures |

Empty NDJSON lines are keep-alives. Streams reconnect with bounded exponential backoff; HTTP 429 waits 60 seconds. Move delivery failures trigger a fresh authoritative game snapshot before any retry. Authentication failures stop the affected operation with a token-free error. Redirects are disabled.

Automated coverage uses sanitized protocol fixtures and a loopback server, not a real account. **Live end-to-end verification is manual:** start `lichess`, issue a standard challenge from another account, confirm legal moves and clock updates, finish the game, then confirm the bot accepts a subsequent challenge. Check stderr without exposing the token. The manual test has not been performed in this implementation session.

## BenchmarkDotNet

```sh
dotnet run --project benchmarks/Zwischenzug.Benchmarks -c Release --no-build -- --job Short --filter '*'
```

The benchmark reports legal-move generation throughput, perft/search nodes per second, allocation and TT hit percentage over the same fixed bench suite. Search measurements include cold engine and position setup, not just recursive node work. Movegen and perft allocate no managed memory in the measured operation.

Observed ShortRun on an Intel i9-13900H, .NET 10.0.13 (three measurement iterations; not a long statistical performance study):

| Operation | Mean | Throughput/second | TT hit rate |
| --- | ---: | ---: | ---: |
| Legal generation, 25 positions | 6.691 μs | 99,392,754 moves | — |
| Start-position perft(5) | 40.33 ms | 120,646,222 nodes | — |
| Search suite, depth 5 | 226.34 ms | 1,284,898 nodes | 36.72% |

The initial cold Dry run took 220 ms for perft(5); JIT startup is not representative of warmed engine throughput. The warmed measurement meets the 200 ms target. The separate perft(6) CLI measurement was 1.26 seconds, below its five-second target.

## Packaging

The verified publish mode is **framework-dependent**, not Native AOT:

```sh
dotnet publish src/Zwischenzug.Cli -c Release --no-restore --self-contained false -p:UseAppHost=false -o artifacts/publish
dotnet artifacts/publish/Zwischenzug.Cli.dll bench 5
```

The multi-stage `Dockerfile` uses the pinned SDK and a .NET 10 runtime, runs as the runtime image's non-root `APP_UID`, and defaults to `uci`. Once Docker is available, these are the image verification commands:

```sh
docker build -t zwischenzug .
docker run --rm -i zwischenzug
docker run --rm zwischenzug bench 5
```

Docker image build/runtime verification was **not run** because Docker is unavailable. Native AOT publish was **not run**; library AOT compatibility and generated JSON do not establish whole-application AOT publishing. See [ADR 0004](docs/adr/0004-packaging-and-verification-boundaries.md) for these boundaries and plan deviations.

## Architecture and verification

See [architecture](docs/architecture.md), [ADRs](docs/adr/), and the [implementation ledger](docs/implementation-ledger.md) for ownership, limits, decisions, reviewed fixes and observed evidence. GitHub Actions runs restore, zero-warning Release build, all MTP tests, and the deterministic benchmark. A hosted CI run and live Lichess test are not claimed by local checks.
