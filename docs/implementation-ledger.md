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
