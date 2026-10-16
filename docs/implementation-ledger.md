# Implementation ledger

## Workspace

User chose in-place implementation on feature branch feat/chess-engine. Original checkout clean; only README existed. SDK 10.0.402 installed. User authorized restore, builds, tests and later framework-dependent publish. Neither Docker nor clang is on PATH; image builds and Native AOT publish are not authorized.

## Preflight rulings

- No OpenSpec root exists (openspec list --json returned root: null). Auto-selected workflow explicitly prohibits initializing one without a user request. The supplied plan is the behavioral source of truth.
- Libraries expose Core types transitively through Engine. Adapters reference Engine only.
- Position is a mutable reference type owning fixed undo/history arrays; evaluator accepts it by readonly reference. Moves remain 16-bit values.
- Benchmark signature uses a bounded default depth so CI remains practical; deeper depth is selectable.

## Evidence

- Preparation: clean empty repository, .NET 10 SDK available.
- M0: Release solution build: zero warnings/errors. MTP starts all four empty test assemblies. Empty-scaffold exit code 8 explicitly ignored only for the M0 smoke, not for final validation; no fabricated tests added.
- M1 RED: Core acceptance suite could not compile before Move/MoveGenerator/Perft existed.
- M1 GREEN: 29 Core tests passed normally on MTP. Published perft counts cover start d1–5, Kiwipete d1–3, CPW positions 3–6 d1–4. Every move in depth-three trees restores mailbox, bitboards, FEN and incremental key.
- M1 CLI smoke: Release perft(5) = 4,865,609 in 192.29 ms; perft(6) = 119,060,324 in 1,261.32 ms. Both supplied timing targets met on this workstation.
- M0 and M1 independent reviewer verdicts: approve. Scaffold CI bench was intentionally not yet operational; final suite must use normal MTP exit handling.
- M2 RED: missing engine APIs prevented acceptance compilation. Two published mate-three positions subsequently failed (material scores instead of mate).
- M2 ruling: PV-node futility/LMR hid quiet mating lines. Restrict both to non-PV windows; keep all published fixtures rather than weakening the expected outcome.
- M2 GREEN: 37 Engine tests pass, including 20 exact mate-distance cases (eight mate-one, six mate-two, six published mate-three), terminal roots, immediate cancellation, clocks/node limits, deterministic repeated searches, quiescence, TT and evaluation.
- M2 standalone CLI smoke (temporary project, removed): start position depth five returned legal b1c3, score 8, 5,964 nodes; forced KQK scenarios emitted legal complete mating PVs. Release build zero warnings/errors.
- M2 review fixes: pinned SEE recaptures and shallow same-key TT overwrites each reproduced with failing assertions, then corrected. Halfmove-clock context is mixed into search TT keys; PV nodes cannot accept TT score cutoffs. All 41 Engine tests pass after fixes. Both independent reviewers confirmed their findings resolved.
- M3 GREEN: nine UCI tests pass: exact handshake, terminal null move, malformed-input handling, stdout protocol isolation, concurrent ready/stop latency below 100 ms, immediate search-fault propagation, parser and formatter boundaries. Release solution build has zero warnings/errors.
- M3 bench corpus: 25 distinct tactical/quiet/endgame FENs, cleared TT per position. Two actual CLI runs each returned 290,830 nodes (45,572 TT probes, 16,734 hits); committed ExpectedDefaultNodes.
- M3 first fastchess v1.8.2-alpha match: ten games at 5+0.05, concurrency two, all finished; no crashes, illegal moves or time losses. Outcome 5–5; not strength evidence. Runner noted no opening book; a second run exercises the documented script and fixed EPD openings.
- M3 review rulings: fatal search faults cancel pending input and propagate instead of hanging or inventing bestmove. Infinite mode requests the full supported search ceiling and withholds bestmove until stop; terminal/ceiling waiting is intentional, not repeated identical work.
- M3 second documented-script run: nine complete games (mates and fifty-move draw), tenth in progress when the 300-second harness deadline stopped the run. This is not recorded as a second ten-game pass; the first complete match satisfies that criterion.
- M4 GREEN: 18 Lichess tests pass. Fake HTTP fixtures cover challenge/gameStart/gameFull/gameState/chat/finish; legal moves, black-to-move initial FEN, duplicate snapshots, reservation ownership, cancellation, dropped-stream reconnect, 429 delay and token safety.
- M4 actual CLI loopback smoke: configuration/DI/typed HttpClient opened both streams, accepted a standard challenge and submitted legal b1c3. Stdout empty; HTTP logs on stderr; dummy credential absent from output.
- M4 review fixes: challenge-response races no longer kill unrelated games; move POST failures reconcile a fresh gameFull before retry; game starts release only their matching reservation. Official gameId wire field supported alongside id. Four new regression cases pass. Security review: no findings.
- Live Lichess play is manual-only and has not run; no real account/token used. Fixtures are sanitized representative protocol payloads, not claimed live traffic recordings.
- M4 review closed: primary Lichess source defines gameId as the challenge ID and creates the game with that ID. Corrected the inconsistent synthetic fixture rather than guessing reservation correlation. State reviewer withdrew the remaining finding; all 18 tests pass again.
- M5: committed default-depth benchmark regression assertion passes. Full-tree restoration now validates every move in the deepest accepted perft trees, recomputing each Zobrist key and comparing mailbox, bitboards, occupancy and metadata after undo.
- M5: BenchmarkDotNet Dry exercised all three reporters; its cold perft(5) was 220 ms and carried the expected short-iteration diagnostic. A subsequent ShortRun (three warmup/measurement iterations) measured movegen 6.691 μs, perft(5) 40.33 ms, search suite 226.34 ms and TT hit rate 36.72%. No measured movegen/perft managed allocations.
- M5: removed redundant initial-position allocation/copy; evaluation traverses populated bitboards and derives phase normalization from configured weights. The default search signature remains 290,830. Added configured-phase regression.
- M5 RED/GREEN: terminal -30000 UCI score formatted as mate -1; a new failing case exposed it. Corrected to mate 0; all twelve UCI tests pass.
- M5 framework-dependent publish: succeeded without warnings; current published DLL returned bench 290,830 and perft(5) 4,865,609. Actual published UCI process completed handshake/ready, searched depth four with legal PV and bestmove f1d3, and emitted no stderr/non-protocol stdout. Hardware intrinsics disabled: portable sliding fallback returned perft(4) 197,281.
- M5 packaging: multi-stage non-root Dockerfile and architecture/ADRs added. Docker image build/runtime not run (Docker unavailable); Native AOT not run (not authorized/toolchain not verified). Hosted CI and live Lichess remain unrun.
- Whole-solution verification before final review: Release build zero warnings/errors; all 102 MTP tests passed, none failed/skipped. Final review and end-of-session recheck are recorded below when complete.
- Final review passes:
  - Delivery review: resolved P2 omission by recording M5 evidence, publish outcome and explicit boundaries in the ledger; deleted fastchess local state config.json and ignored it.
  - Application review: identified P1 where awaiting ChallengeAsync in the event loop allowed rate-limited/slow challenge requests to block gameStart event processing. Corrected in BotRunner by making pending capacity reservation synchronous and dispatching the HTTP challenge task to the background while the event stream continues reading. Two new regression tests verify that delayed challenge responses cannot block an accepted game start, and that fatal background challenge authentication errors surface to the runner task. All 20 Lichess tests and all 104 solution tests pass. External subagent limits prevented a third subagent dispatch; the code change and regression results were verified directly.
