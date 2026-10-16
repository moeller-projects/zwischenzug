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
