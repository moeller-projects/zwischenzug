# Implementation ledger

## Workspace

User chose in-place implementation on feature branch feat/chess-engine. Original checkout clean; only README existed. SDK 10.0.402 installed. User authorized restore, builds and tests; publish and Docker image builds are not authorized.

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
