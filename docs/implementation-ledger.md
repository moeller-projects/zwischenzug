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
