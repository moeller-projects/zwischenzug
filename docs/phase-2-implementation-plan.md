# Phase 2 Chess Engine Plan

## Purpose and authority

This plan continues the existing Zwischenzug implementation; it is not a greenfield engine plan. The earlier expanded index referenced by the supplied draft is not present in this repository, so claims attributed to that index or external engine ratings are unverified. No `openspec/` root is initialized; this document and the implementation ledger are the working change record.

The repository already has .NET 10/C# 14 solution structure, Core move generation/perft, Engine search/evaluation/TT/SEE, UCI, Lichess adapter, deterministic depth-five benchmark, match scripts, and CI. Preserve these contracts unless a milestone explicitly changes them. Do not recreate projects or replace MSTest/Microsoft.Testing.Platform with xUnit.

## Execution constraints

- Work on dependent local milestone branches rooted at `feat/chess-engine`; create and commit one milestone at a time. Conventional Commit subjects, narrowly scoped to the milestone. No push, PR submission, or merge is authorized.
- Keep the existing solution/project/dependency layout and stdout/stderr protocol boundary.
- User authorized restore, build, tests, local fastchess/SPRT for candidates that pass correctness, Rust/toolchain installation, and one bounded 32-position Bullet trainer smoke. Large-scale data generation or representative training, publish, image builds, standalone benchmarks, and live Lichess require separate authorization.
- Record rulings, commit identifiers, verification actually run, and remaining gates in `docs/implementation-ledger.md`.
- Search changes retain deterministic 1-thread `bench` signature unless a deliberate behavior change updates `BenchmarkSuite.ExpectedDefaultNodes` and its regression assertion. Measure strength independently; node signature is not an Elo measure.
- Preserve existing clock-context TT keying, repetition semantics, 94-ply search bound, 1,024-ply position history bound, cancel-and-await mutation rule, fixed-storage/no-allocation hot-path intent, and adapter ownership boundaries.

## Rebased milestones

| Milestone | Branch layer | Scope | Exit criteria |
|---|---|---|---|
| P2-0 Baseline closure | `phase2/m0-baseline` | Compare delivered state to original gates; complete missing reproducible correctness evidence, perft corpus/provenance, baseline documentation and tooling gaps only. Do not recreate existing scripts, CI, or projects. | Exact published perft results for selected, versioned positions; reversible-state/key checks remain covered; benchmark signature and commands documented; local validation recorded. |
| P2-1 Search-core review | `phase2/m1-search-core` | Review and, only where evidence warrants, improve existing TT/order/PVS/aspiration/NMP/LMR/SEE/futility behavior. One behavior idea per commit. | Search correctness scenarios cover mate-distance TT normalization, halfmove/repetition context, qsearch terminal states and cancellation; each strength change has a separately authorized and executed match gate before strength claims. |
| P2-2 Selectivity | `phase2/m2-selectivity` | Candidate reverse futility, razoring, improving, continuation/capture history, IIR, ProbCut, singular extensions and correction history, each separately justified. | Each accepted patch preserves correctness gates and deterministic signature or intentionally updates it; strength claims only from authorized, fully reported experiments. No planned feature is presumed beneficial. |
| P2-3 NNUE feasibility and integration | `phase2/m3-nnue` | Spike first: select one feature set, model format, trainer contract, data budget and portable scalar inference. Then implement/export/incremental evaluation and optimized backends only when parity is proven. Initial datagen uses standard chess, not DFRC/Chess960. | Versioned format contract; scalar and quantized reference parity; incremental equals full recomputation across transitions; x86 and portable fallback parity; model provenance and measured resource requirements. Elo/NPS targets are aspirational until resources and statistical test criteria are approved and executed. |
| P2-4 Parallel search and UCI polish | `phase2/m4-smp` | Lazy SMP behind explicit thread support; safe worker lifetime; pondering, WDL/MultiPV, time-management refinements, optional tablebase integration only after dependency/licensing/portability decision. | Workers cancel/join before state mutation; races have deterministic safety tests; ponder transitions and MultiPV contracts tested; tablebase edge semantics tested if included; 1-thread regression signature unchanged unless intentionally revised. |
| P2-5 Release evidence | `phase2/m5-release` | Release/architecture documentation, optional AOT investigation, external rating submissions/deployment only with explicit prerequisites and authorization. | Each RID has actual authorized publish/runtime evidence before support claim; hosted CI, external ratings, deployment, and live service checks are reported only when performed. |

Milestone identifiers are new Phase 2 continuation IDs; the historical M0–M5 in the implementation ledger describe work already completed and remain unchanged.

## P2-0 task slice

1. Audit existing correctness corpus, CI, CLI perft/divide, benchmark signature, and match scripts against the acceptance evidence above. Reuse them; record actual gaps.
2. Ensure a documented, licensed/provenance-tracked perft fixture corpus and exact expected counts. Pseudo-legal generators MUST check king safety for every counted move, including final-ply moves; alternatively use fully legal generation. Never count raw pseudo-legal moves at leaves.
3. Add only missing regression cases for special-move legality and complete make/unmake restoration; do not duplicate incidental implementation tests.
4. Document repeatable commands and observed results in `docs/implementation-ledger.md`; do not claim strength from perft or benchmark node counts.

## Correctness and strength gates

- Before search tuning: test checkmate versus stalemate at normal and quiescence boundaries; repetition and fifty-move draws; TT mate-score ply normalization; halfmove-clock-sensitive score reuse; interruption with legal completed result; position/hash restoration.
- Before NNUE/SMP: establish a portable scalar reference and explicit backend equivalence/lifecycle contracts.
- Correctness gates are mandatory. Strength experiments are a separate gate: record engine revisions/options, hardware, time control, paired opening set/book version and hash, opponents, concurrency, SPRT hypotheses/error rates, result and stopping decision. An inconclusive match is not acceptance. Do not claim fixed Elo deltas or ratings from generic `[0,10]` SPRT or another engine's published rating.
- Perft speed, NPS, allocation, thread scaling, pondering, and rating targets are measurements, not correctness substitutes. Standalone performance benchmarks and production training remain separately gated; local fastchess/SPRT authorization applies only after correctness gates pass.

## Risks and unresolved decisions

- **U1 NNUE stack:** Bullet and Chess768 are selected; the bounded trainer/toolchain compatibility smoke is complete. Dataset provenance/storage and authorization for representative training remain unresolved; resolve before generating large datasets or claiming a production-trained model.
- **U2 Tablebases:** Fathom/Syzygy library, distribution/licensing, supported platforms and 50-move/DTZ semantics remain unresolved; owner: project owner. Keep optional and do not claim support until decided and tested.
- **U3 External gates:** CCRL/CEGT/OpenBench/Lichess eligibility, accounts, hardware, and authorization are prerequisites, not assumed work products.
- **U4 Original Expanded Index:** absent from this repository; original citations and calibration claims were not independently checked.

### P2-3 feasibility decision

Prefer a **Bullet `Chess768` first network** over the draft's HalfKA_hm architecture for the initial implementation. This is a deliberate scope choice: Bullet documents the exact 768-feature mapping, dual perspectives, quantized checkpoint format, and simple `(768 -> 128)x2 -> 1` inference example. It removes king-bucket feature refresh from the first correctness-critical path. Tradeoff: no king-conditioned features; treat playing strength as unproven.

- Implemented v1 sparse input: Bullet Chess768 mapping with 768 features and at most 32 active pieces. Black-to-move positions normalize by rank-flipping (`square ^= 56`) and swapping piece colors; piece type is Pawn=0 through King=5. STM index is `384*color + 64*type + square`; NTM index is `384*(1-color) + 64*type + (square ^ 56)`. Bullet normalizes training scores/results to STM.
- Implemented shared feature weights and 128 int16 accumulators per perspective; SCReLU, concatenate, then one output. Quantization is QA=255, QB=64, output scale=400. Scalar output accumulation uses 64-bit intermediates.
- Implemented package v1: 72-byte little-endian `ZNNUE768` header with version, fixed dimensions/scales, payload length, and SHA-256; followed by the 197,440-byte pinned Bullet `quantised.bin` payload in its documented column-major tensor order and 64-byte alignment. `nnue pack` rejects unexpected payload lengths. Header/checksum/layout validation runs when loading.
- Implemented board-diff per-ply accumulator updates, an AVX2 feature-weight update path, and a portable scalar fallback. Engine tests verify backend parity and full recomputation equivalence across sibling moves, captures, castling, en passant, and promotion. `Nnue__ModelPath` selects the evaluator; HCE remains default when unset.
- Bullet data interchange remains the documented FEN/score/result format, with scores and results White-relative. No production datagen/converter or large dataset was added.
- No trained model is bundled. The generated 32-position, one-batch model is a compatibility fixture only; it is not a quality, strength, or release model.

Primary references (checked 2026-10-17): [Bullet simple network example](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/examples/simple.rs), [Chess768 feature mapping](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/crates/bullet_lib/src/game/inputs/chess768.rs), [saved-network format](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/docs/4-saved-networks.md), and [training data](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/docs/3-data.md). Bullet is MIT-licensed per its [repository metadata](https://api.github.com/repos/jw1912/bullet). The pinned revision is `869e30934402d912609d3ee2baa9696a12e1f783`.

P2-3 implementation and bounded compatibility checks are complete: scalar reference parity, AVX2/portable parity, special-move cache transitions, and package validation pass. The pinned trainer fixture's model package is 197,512 bytes; retained evaluator arrays total 320,736 bytes, excluding CLR object/array overhead. Inference throughput was not benchmarked because standalone benchmark execution was not authorized. Production training, large-scale data generation, AOT, and strength evidence remain unapproved or unperformed. Do not claim playing strength or package the local smoke model as a usable engine model.

## Current execution status

- P2-0 baseline closure completed: Kiwipete perft coverage now reaches d5; Release solution tests passed 106/106 and CLI `perft 5` returned 4,865,609.
- P2-1 search-core audit completed: existing mate-score normalization, halfmove TT context, draw precedence, cancellation, and PV coverage are documented in `docs/implementation-ledger.md`.
- P2-2 selectivity experiments: reverse futility pruning and shallow razoring both violated existing exact mate-in-three regressions. Both were rejected and removed; no search tuning is currently accepted.
- Local fastchess v1.8.2-alpha was installed under ignored `artifacts/local-tools/` after the user authorized installation and local matches. Do not run SPRT for candidates that fail correctness tests.
- P2-3 bounded smoke: Rustup 1.99.1, Visual Studio 2022 C++ Build Tools, and CUDA 13.2.1/13.4 toolkits are installed side-by-side. The original PTX mismatch was resolved with CUDA 13.2.1; the NVIDIA driver remained 596.58. Pinned Bullet built and trained one 32-position batch using its test fixture, saved a 197,440-byte quantised payload, and emitted reference raw evaluations. The CLI packed it to a 197,512-byte versioned model; the C# AVX2 evaluator agreed with Bullet's reference outputs within 5 centipawns on four white/black FENs. This is compatibility evidence only, not representative training or strength evidence. Local model/toolchain files remain ignored. Native AOT, publishing, and external service actions remain separate gates.
- P2-4 local implementation is complete: Lazy SMP worker ownership, effective UCI Threads, distinct MultiPV, opt-in WDL, ponderhit timing, and local tooling scripts are implemented. Release build succeeded with 0 warnings/errors; all 119 tests passed, including the unchanged 1-thread benchmark signature. No fastchess strength match or standalone performance benchmark was run; tablebases remain deferred under U2.

## P2-4 implementation decisions

- Source review: `Searcher` and evaluator scratch state are mutable; `Position` stores repetition/null-move history privately; `TranspositionTable` has unsynchronized slots and counters; UCI `Threads` is accepted but ignored and `ponderhit` is a no-op. MultiPV and WDL output are absent.
- Safety design: workers receive independent searchers, history-preserving position clones, and isolated evaluator scratch. Shared TT bucket reads/writes use striped locks; clear/resize/generation changes remain coordinator-only after workers join. The Engine coordinator owns worker cancellation and joins every worker before returning.
- UCI behavior: make `Threads` effective, report distinct root MultiPV lines, expose WDL only through an explicit option, and let ponderhit transition the active search into normal time management without terminating it. Keep a single protocol writer/observer. Defer tablebases while U2 is unresolved.
- WDL output uses the Stockfish source model pinned at [`effdc3fd3f6de6e75fe1374a51a7a7eed66f880b`](https://github.com/official-stockfish/Stockfish/blob/effdc3fd3f6de6e75fe1374a51a7a7eed66f880b/src/uci.cpp#L540-L608): material-dependent cubic coefficients, no ply adjustment. It is an empirical Stockfish estimate, not calibrated to Zwischenzug or a universal game probability.
- Gate order: worker ownership/thread correctness first; MultiPV/WDL second; ponder/time-management transitions last. Preserve the 1-thread benchmark signature. Do not run standalone performance benchmarks without separate authorization.

## Local tooling bootstrap

- `tools/download-requirements.ps1` downloads the pinned fastchess Windows release and Bullet source snapshot into ignored `artifacts/local-tools/`; it verifies the fastchess archive checksum and does not overwrite existing downloads unless `-Force` is passed.
- Rust installation is opt-in with `-InstallRust`; on Windows the script uses the verified `Rustlang.Rustup` winget package, then installs the stable toolchain on a subsequent run. It does not install .NET, CUDA, or Visual Studio Build Tools.
- `tools/start-uci.ps1` starts an already-built CLI with `--no-build --no-restore`. Existing `tools/match/` scripts remain the supported fastchess/cutechess match entry points.
- Do not download training datasets, opening books, tablebases, or run trainer jobs through generic bootstrap scripts. Their licensing, provenance, size, and authorization need separate decisions.
