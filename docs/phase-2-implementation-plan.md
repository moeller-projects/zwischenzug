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

- **U1 NNUE stack:** trainer/version, feature set, record format, licensing, training hardware and storage/throughput remain unresolved; owner: project owner. Resolve in a bounded feasibility spike before P2-3 implementation.
- **U2 Tablebases:** Fathom/Syzygy library, distribution/licensing, supported platforms and 50-move/DTZ semantics remain unresolved; owner: project owner. Keep optional and do not claim support until decided and tested.
- **U3 External gates:** CCRL/CEGT/OpenBench/Lichess eligibility, accounts, hardware, and authorization are prerequisites, not assumed work products.
- **U4 Original Expanded Index:** absent from this repository; original citations and calibration claims were not independently checked.

## Current start point

Begin at P2-0, not the draft's greenfield scaffold. First inspect existing perft fixtures and tests; then implement only the highest-value missing correctness coverage. The user-authorized restore/build/test operations permit normal local validation. No perft performance benchmark, fastchess/SPRT, publish, or deployment command is implied.