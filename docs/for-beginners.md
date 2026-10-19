# Zwischenzug for beginners

This guide is for readers who may be new to chess, chess engines, C#, or this repository. It explains the terms used by the source, follows a position through the application, and points to the code that owns each decision. It complements the [README](../README.md), [architecture overview](architecture.md), and [architecture decision records](adr/).

## Learn by following one move

When the engine receives a position and a request to move, the main path is:

1. The CLI starts the UCI adapter, or the Lichess adapter receives an authoritative game state.
2. `Fen.Parse` creates a `Position`. UCI may then replay listed moves; a Lichess game rebuilds its position from the initial position and the full move list.
3. `MoveGenerator.Generate` creates legal candidate moves. It first creates pseudo-legal moves, then rejects moves that leave the moving side's king in check.
4. `Engine.SearchAsync` gives the position to a `Searcher`. With multiple threads, each worker receives its own position history and search state, while the workers share a locked transposition table.
5. The search returns a `SearchResult`. UCI formats it as `info` and `bestmove`; Lichess submits the move and waits for authoritative state before responding to later changes.

The dependency direction is deliberately one-way: `Core` knows chess rules; `Engine` uses `Core`; UCI and Lichess adapt the engine to external protocols; the CLI composes them. Core does not know about networking, console I/O, or search strategy.

### A tiny C# walkthrough

This example starts from the normal board, parses one legal move, applies it, and checks how many replies Black has. `Span<Move>` is a view over stack storage: `count` tells you which entries contain valid moves.

```csharp
Position board = Fen.Parse(Fen.StartPosition);
Move e4 = Move.Parse(board, "e2e4");
board.MakeMove(e4);

Span<Move> replies = stackalloc Move[256];
int replyCount = MoveGenerator.Generate(board, replies);
Console.WriteLine($"Black has {replyCount} legal replies");

board.UnmakeMove(); // Restore the exact start position, including its key and history.
```

The same flow can be used to make a whole line: parse each coordinate against the current position, then apply it before parsing the next one. Parsing against the current board is what rejects illegal moves.

## Chess terms used in the code

- **Square and coordinate:** The board has 64 squares. Internally, `a1` is square 0 and `h8` is square 63. UCI writes a move as its start and end squares, such as `e2e4`. A promotion adds a piece letter, for example `a7a8q`.
- **Board arrays and bitboards:** `Position.Board` is a 64-element mailbox: each element says which piece occupies that square. `Pieces` holds one 64-bit mask per piece code; `Colors` holds one mask per side; `Occupied` combines both. These are duplicate views of the same board so move generation can use fast bit operations. Every move must update all views together. Piece codes 1–6 mean White pawn, knight, bishop, rook, queen, king; 7–12 are the corresponding Black pieces.
- **Side to move:** White is `0`, Black is `1`. Search scores are from the current side's perspective: a positive score favors the side to move.
- **Check, checkmate, and stalemate:** A king is in check if an enemy piece attacks its square. Checkmate means the king is in check and no legal move can escape. Stalemate means there is no legal move but the king is not in check.
- **Castling:** A king and rook move together. The position stores four rights (white/black, king/queen side). A right is removed when the king or a relevant rook moves or the home rook square is involved in a move.
- **En passant:** A pawn may capture an opposing pawn immediately after it moves two squares, as though it had moved one square. The position stores the target square only for the next move; the move generator checks that the captured pawn is really there and that the capture does not expose the king.
- **Promotion:** A pawn reaching the far rank becomes a knight, bishop, rook, or queen. The generator creates each choice as a distinct move.
- **Packed move flags:** A `Move` stores source and destination in six bits each, then a four-bit flag. `0` is an ordinary quiet move, `1` a two-square pawn move, `2` castling, `3` en passant, `4` a capture, `8–11` quiet promotions, and `12–15` capture promotions. Keeping a move in a 16-bit value makes move lists cheap to copy and scan.
- **FEN:** Forsyth-Edwards Notation is a text description of a position: piece placement, side to move, castling rights, en-passant target, halfmove clock, and fullmove number. `Fen.Parse` validates and loads it; `Fen.Format` writes it back.
- **Halfmove clock and fifty-move draw:** The halfmove clock counts consecutive non-pawn moves that do not capture. At 100 halfmoves the search treats the position as a draw. It is part of search context, but not repetition identity.
- **Repetition:** The same position can occur more than once in a game. A repetition key identifies board pieces, side to move, castling rights, and a legally usable en-passant right. Null search moves are excluded from real game history.
- **Perft:** A move-generation check that counts all legal move sequences to a chosen depth. It does not evaluate who is winning. Published counts are useful for finding rule and make/unmake bugs.

For example, the start position has 20 legal moves at depth one and 8,902 move sequences at depth three. The CLI command `perft 3` prints the latter total. `perft 3 --divide` prints how many lines begin with each individual White move, which makes it easier to find the first branch that disagrees with a published count.

## How a chess engine chooses a move

Chess has too many possible future move sequences to search all the way to the end. This engine searches a bounded tree and uses evaluation to estimate positions at its leaves.

- **Evaluation:** `MaterialPstEvaluator` scores piece values and square placement, with different middle-game and endgame tables. An optional NNUE evaluator uses a packaged neural network. Neither is a guarantee of the game's result; they are estimates.
- **Ply and depth:** A ply is one move by one side. Depth is how many plies the search intends to examine. The search limit is 94 plies. Deeper search usually reveals more tactics but costs more time.
- **Negamax and alpha-beta:** The same search routine is used for both sides by negating the score after each move. Alpha and beta bound scores that could still affect the choice. If a branch cannot beat a known alternative, the search stops exploring it.
- **Iterative deepening:** The engine searches depth 1, then 2, and so on. Each completed iteration gives a usable move and helps order the next search. The engine deliberately completes depth one even when a stop or small node limit arrives immediately.
- **Quiescence:** At the normal depth limit, the engine continues captures (and all legal evasions while in check) until the position is quieter. This helps avoid evaluating in the middle of a simple capture sequence.
- **Move ordering:** The engine tries promising moves first: a transposition-table move, captures ranked partly by static exchange evaluation, then killer moves and history-scored quiet moves. Better ordering makes alpha-beta cutoffs more likely.
- **Static exchange evaluation (SEE):** SEE estimates the material result of a capture sequence on one square. It accounts for revealed sliding attacks and rejects recaptures by pinned pieces or unsafe kings. It is a move-ordering/pruning estimate, not a full search.
- **Transposition table (TT):** The same position can be reached by different move orders. The TT stores a search result and best move so the engine can reuse work. Entries contain a depth and bound (exact, lower, or upper), and mate scores are adjusted when stored at one ply and probed at another. Bucket locks protect shared entries in Lazy SMP.
- **Null-move pruning, late-move reductions, and SEE pruning:** These selectively reduce work. A null move passes the turn as a tactical test; late-move reduction searches later quiet moves less deeply first; SEE pruning may skip a losing capture. These are approximations and are constrained by checks, depth, and score context.
- **Lazy SMP:** Multiple workers search the same root independently, each with its own search history and evaluator state. They share the TT so one worker can benefit from another's discoveries. Parallel search can return a different move or node count from one-thread search; only the one-thread default benchmark signature is fixed.
- **MultiPV:** Instead of reporting only the top root move, MultiPV searches every legal root move and reports the best requested alternatives in score order. It costs more work as the requested count grows.
- **Pondering and clocks:** `go ponder` searches before the clock starts. `ponderhit` resets the search clock and releases the pending best move. Time management reserves move overhead, divides usable time across expected moves, adds part of the increment, and caps normal spending at one quarter of remaining time.

## Project map

| Project or area | Responsibility | Good first files |
| --- | --- | --- |
| `src/Zwischenzug.Core` | Board representation, legal moves, FEN, keys, perft | `Position/Position.cs`, `Moves/MoveGenerator.cs`, `Position/Fen.cs` |
| `src/Zwischenzug.Engine` | Evaluation, search, TT, benchmark suite | `Engine.cs`, `Search/Searcher.cs`, `Evaluation/MaterialPstEvaluator.cs` |
| `src/Zwischenzug.Uci` | UCI input, options, output, search lifecycle | `UciHost.cs`, `UciCommandParser.cs` |
| `src/Zwischenzug.Lichess` | HTTP protocol, challenge policy, game lifecycle | `Api/LichessClient.cs`, `Bot/BotRunner.cs`, `Bot/GameSession.cs` |
| `src/Zwischenzug.Cli` | Configuration and dependency composition | `Program.cs` |
| `tests` | Rule, search, protocol, resilience, and regression checks | Matching `*.Tests` project |
| `benchmarks` | BenchmarkDotNet measurement harness | `ChessBenchmarks.cs` |
| `docs/adr` | Reasons for long-lived representation, search, evaluation, and release decisions | ADR 0001–0004 |

## Configuration reference

### UCI options

UCI is a line-based protocol. The engine writes protocol replies to standard output and diagnostics to standard error. The `uci` handshake lists the following options; changes that affect search state first stop and await the current search.

| Option | Default and range | Effect |
| --- | --- | --- |
| `Hash` | 16 MB; 1–1024 | Size of the transposition table. Resize occurs only after search workers are idle. |
| `Threads` | 1; 1–128 | Number of independent Lazy SMP search workers. |
| `MultiPV` | 1; 1–256 | Number of distinct root alternatives to report. |
| `UCI_ShowWDL` | false | Add empirical win/draw/loss estimates. These are not calibrated to this engine. |
| `Ponder` | false | Allow `go ponder` and `ponderhit` clock transitions. |
| `Move Overhead` | 10 ms; 0–5000 | Time kept in reserve for the GUI or network to deliver a move. |

`go` also accepts search limits such as `depth`, `nodes`, `movetime`, `wtime`, `btime`, `winc`, `binc`, and `movestogo`. `infinite` keeps searching until `stop`; `ponder` waits for `ponderhit` before starting the clock.

Example UCI exchange:

```text
uci
uciok
isready
readyok
position startpos moves e2e4 e7e5
go depth 6
info depth 6 ... pv g1f3 ...
bestmove g1f3
```

The engine may emit several `info` lines while deepening. The final `bestmove` is the move the GUI should play. Ellipses above stand for fields that depend on search results and timing.

### Lichess settings

Lichess values use .NET configuration keys under `Lichess`. Environment variables use double underscores, such as `LICHESS__TOKEN` and `LICHESS__USERNAME`. In Development, the CLI also loads the user-secrets store identified in its project file. Do not place a token in source control, command arguments, or logs.

| Key | Default | Valid values or meaning |
| --- | --- | --- |
| `Lichess:Token` | required | Personal access token with `bot:play` permission. |
| `Lichess:Username` | required | Account identity used to determine the bot's color in a game. |
| `Lichess:BaseAddress` | `https://lichess.org/` | Must use HTTPS; plain HTTP is accepted only for loopback test fixtures. |
| `Lichess:MaxConcurrentGames` | 1 | 1–32 active games plus pending accepted challenges. |
| `Lichess:AllowRated` / `AllowCasual` | true / true | Independently allow rated and casual challenges. |
| `Lichess:AllowedTimeControls:0` | `clock` | Allowed API time-control type strings; at least one is required. |
| `Lichess:SearchDepth` | 64 | Maximum requested depth, bounded by the engine's 94-ply limit. |
| `Lichess:MoveOverhead` | 50 ms | 0–5000 milliseconds reserved from the clock. |

For a local fixture, use a loopback base address such as `http://127.0.0.1:port/`; a real service must use HTTPS. Supply the token through `LICHESS__TOKEN` and the account name through `LICHESS__USERNAME`. The token value is intentionally omitted from examples.

The bot accepts standard chess only. Streams use NDJSON (one JSON event per line); an empty line is a keep-alive. Reconnects use bounded backoff and HTTP 429 waits 60 seconds. Move POSTs are not blindly retried: a fresh game snapshot is used to determine whether the move took effect.

### NNUE model selection

The CLI reads `Nnue:ModelPath` (environment form `Nnue__ModelPath`). If absent, material/PST evaluation remains the default. If present, the CLI loads a versioned Chess768 model and fails when its header, payload size, or SHA-256 checksum is invalid. Use `nnue pack <bullet-model> <output-model>` to wrap a compatible Bullet Chess768 quantized network. The repository does not include a trained model, and packaging compatibility does not show playing strength.

There is an important current integration boundary: `Program.cs` configures the CLI's singleton `Engine`, while `GameSession` constructs its own default `Engine`. Therefore `Nnue:ModelPath` currently affects UCI searches but not Lichess game searches. See the code comments at those construction sites before extending evaluator configuration.

## Following the code by responsibility

- **`Position`** is the mutable board. `MakeMove` updates board arrays, bitboards, metadata, and the incremental Zobrist key while saving an undo record. `UnmakeMove` restores that record. Do not mutate its exposed arrays directly.
- **`MoveGenerator`** returns legal moves into caller-provided storage to avoid allocating in search. Its fast path accepts moves by unpinned pieces when the king is not in check; king moves, en-passant moves, pinned moves, and all moves while in check use make/check/unmake validation.
- **`Zobrist`** assigns deterministic pseudo-random values to position features. `HasEp` adds an en-passant file/square to identity only when a legal en-passant capture exists, so irrelevant FEN metadata does not split repetitions.
- **`Searcher`** owns per-search killers, history scores, principal variation arrays, counters, and cancellation state. `Negamax` searches ordinary depth; `Quiescence` handles tactical continuations; `Order` ranks candidates; `Pick` selects the next candidate without sorting the whole list.
- **`TranspositionTable`** divides storage into four-entry buckets. A key check prevents an index collision from being treated as a position match. Age and depth guide replacement; a stripe lock protects each bucket access.
- **`Engine`** is the public ownership facade. It rejects position/hash mutation while search is active and waits for Lazy SMP workers before releasing the search task.
- **`UciHost`** is the protocol loop. It stops and awaits search before applying a command that mutates state, keeps output lines atomic, and treats an unexpected search fault as fatal rather than inventing a move.
- **`LichessClient`** owns HTTP details and generated JSON deserialization. `BotRunner` reserves capacity by challenge identity and starts game sessions. `GameSession` owns one engine, cancels obsolete searches, and reconstructs positions from server move history.
- **`BenchmarkSuite`** runs a fixed set of positions. Its node count is a behavioral signature, not an Elo or strength score. Runtime and nodes per second depend on hardware and runtime configuration.

The benchmark harness separates three workloads: legal move generation, start-position perft to depth five, and the fixed search suite. Search throughput includes fresh engine and position setup. The custom TT hit-rate column is measured from a reference search of the fixed suite, so it is a suite-level diagnostic rather than a per-iteration counter.

## Safe first changes

For a chess-rule change, start with a focused FEN and a published perft position, then read `MoveGenerator` and `Position` together. For search behavior, preserve the fixed depth-five node signature unless you intend to change search semantics; when it changes intentionally, update the expected value and its regression assertion together. For UCI or Lichess changes, test the adapter boundary with transcript or fake-HTTP tests and keep protocol output separate from diagnostics. For performance changes, collect a benchmark before making a speed claim.

The repository's `AGENTS.md` lists commands, but restore, build, test, publish, Docker image build, live Lichess/account setup, and additional AOT runtime checks have separate authorization and evidence boundaries. Historical evidence in the implementation ledger describes earlier code and runs; check the current branch and rerun the relevant authorized command before relying on it.
