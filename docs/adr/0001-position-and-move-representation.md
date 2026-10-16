# ADR 0001: Mutable bitboard position and packed moves

Status: accepted.

Squares use little-endian rank-file numbering (a1=0). A 16-bit move contains six-bit source and destination squares and four flag bits. Promotion flags distinguish all four pieces and capture promotions. UCI notation belongs to Core and is shared by both adapters.

Position owns piece/color bitboards, an integer mailbox, metadata, an incremental deterministic Zobrist key, and fixed undo/history arrays. Make/unmake modifies only affected squares. No position copies or heap allocations occur during tree traversal. The public Position is a reference type rather than a struct: copying a struct owning arrays would alias state deceptively. Evaluators receive it through a readonly reference.

The attack facade isolates sliding lookup details. Occupancy-indexed attack tables use guarded hardware PEXT where available and portable software extraction otherwise. This intentionally replaces the plan's initial magic multiplier implementation: the public API and legal move behavior are unchanged, and no mandatory CPU feature is introduced.

Legal generation filters check evasions, king moves, en passant and pinned-piece moves through make/check/unmake. Unpinned moves outside check bypass that work. Castling requires the correct rook, empty path and safe king transit squares. Perft bulk-counts legal moves at leaf depth.

Zobrist keys normalize en passant to legally capturable rights, so a pinned pawn does not prevent repetition. Null-search history is separated from actual game repetition. Fixed storage supports 1,024 accumulated game/search plies; exceeding that bound is an explicit error, not silent corruption.

Verification uses the six Chess Programming Wiki perft positions, incremental-key recomputation via FEN, full state restoration through special-move trees, and root divide counts. Timing is measured separately in Release, not asserted in machine-dependent unit tests.
