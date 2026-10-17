# Phase 2 Chess Engine Plan

## Purpose and authority

This plan continues the existing Zwischenzug implementation; it is not a greenfield engine plan. The earlier expanded index referenced by the supplied draft is not present in this repository, so claims attributed to that index or external engine ratings are unverified. No `openspec/` root is initialized; this document and the implementation ledger are the working change record.

The repository already has .NET 10/C# 14 solution structure, Core move generation/perft, Engine search/evaluation/TT/SEE, UCI, Lichess adapter, deterministic depth-five benchmark, match scripts, and CI. Preserve these contracts unless a milestone explicitly changes them. Do not recreate projects or replace MSTest/Microsoft.Testing.Platform with xUnit.

## Execution constraints

- Work on dependent local milestone branches rooted at `feat/chess-engine`; create and commit one milestone at a time. Conventional Commit subjects, narrowly scoped to the milestone. No push, PR submission, or merge is authorized.
- Keep the existing solution/project/dependency layout and stdout/stderr protocol boundary.
- User authorized restore, build, and test for this work. Publish, image builds, standalone benchmarks, strength matches/SPRT, NNUE training, and live Lichess are not authorized by that grant; request authorization before each such operation.
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
- Perft speed, NPS, allocation, thread scaling, pondering, and rating targets are measurements, not correctness substitutes. Run performance/strength commands only after separate authorization.

## Risks and unresolved decisions

- **U1 NNUE stack:** Bullet is the selected trainer; Chess768 is the recommended v1 feature set. The pinned trainer contract is documented below. Training hardware, dataset provenance/storage, and authorization for a controlled training run remain unresolved; owner: project owner. Resolve before generating large datasets or claiming a trained NNUE deliverable.
- **U2 Tablebases:** Fathom/Syzygy library, distribution/licensing, supported platforms and 50-move/DTZ semantics remain unresolved; owner: project owner. Keep optional and do not claim support until decided and tested.
- **U3 External gates:** CCRL/CEGT/OpenBench/Lichess eligibility, accounts, hardware, and authorization are prerequisites, not assumed work products.
- **U4 Original Expanded Index:** absent from this repository; original citations and calibration claims were not independently checked.

### P2-3 feasibility decision

Prefer a **Bullet `Chess768` first network** over the draft's HalfKA_hm architecture for the initial implementation. This is a deliberate scope choice: Bullet documents the exact 768-feature mapping, dual perspectives, quantized checkpoint format, and simple `(768 -> 128)x2 -> 1` inference example. It removes king-bucket feature refresh from the first correctness-critical path. Tradeoff: no king-conditioned features; treat playing strength as unproven.

Proposed frozen v1 contract, pending implementation:

- Sparse input: Bullet Chess768 mapping with 768 features and at most 32 active pieces. Normalize black-to-move positions to side-to-move-as-white (`square ^= 56`, swap piece colors); then piece type is Pawn=0 through King=5. For each normalized piece, STM index is `384*color + 64*type + square`; NTM index is `384*(1-color) + 64*type + (square ^ 56)`. Bullet's data loader also negates score and flips result for black-to-move records.
- Shared feature affine transform to 128 int16 accumulators for each perspective; SCReLU both accumulators, concatenate, one output.
- Quantization/scaling: QA=255, QB=64, output scale=400; int64 scalar accumulation is the reference to avoid intermediate overflow. No architecture-specific backend until exact parity with scalar.
- Model file: Bullet `quantised.bin` from a named `SavedFormat`, with fixed versioned header/length/endian/layout defined by this project before accepting any external model. Bullet's raw output has little-endian, column-major tensor weights and padding to 64 bytes; do not infer compatibility from file length alone.
- Initial data interchange: Bullet's documented FEN/score/result text format, score and result White-relative. Use a dedicated converter/validator and a tiny dry dataset before any large-scale generation or training.
- Inference availability: no trained model exists in this repository. HCE remains the operational default until a model is trained, loaded, parity-checked, and separately strength-tested.

Primary references (checked 2026-10-17): [Bullet simple network example](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/examples/simple.rs), [Chess768 feature mapping](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/crates/bullet_lib/src/game/inputs/chess768.rs), [saved-network format](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/docs/4-saved-networks.md), and [training data](https://github.com/jw1912/bullet/blob/869e30934402d912609d3ee2baa9696a12e1f783/docs/3-data.md). Bullet is MIT-licensed per its [repository metadata](https://api.github.com/repos/jw1912/bullet). The pinned revision is `869e30934402d912609d3ee2baa9696a12e1f783`.

The source-format review is complete. The remaining gate is a representative model exported by this pinned trainer and a controlled data/training run; training/data generation is not authorized. Do not create random placeholder production weights or claim a completed NNUE evaluator without a real model.

## Current execution status

- P2-0 baseline closure completed: Kiwipete perft coverage now reaches d5; Release solution tests passed 106/106 and CLI `perft 5` returned 4,865,609.
- P2-1 search-core audit completed: existing mate-score normalization, halfmove TT context, draw precedence, cancellation, and PV coverage are documented in `docs/implementation-ledger.md`.
- P2-2 selectivity experiments: reverse futility pruning and shallow razoring both violated existing exact mate-in-three regressions. Both were rejected and removed; no search tuning is currently accepted.
- Local fastchess v1.8.2-alpha was installed under ignored `artifacts/local-tools/` after the user authorized installation and local matches. Do not run SPRT for candidates that fail correctness tests.
- P2-3 bounded local compatibility smoke is authorized. Rustup, Visual Studio C++ Build Tools, and CUDA 13.4 are installed; Bullet built successfully, but its first CUDA run failed because the installed driver supports CUDA 13.2 and PTX from toolkit 13.4 is unsupported. A compatible CUDA 13.2.1 toolkit install is underway; retry the 32-position test-only training job after it completes. No representative training, production model, or strength claim is authorized or implied. Native AOT, publishing, and external service actions remain separate gates.