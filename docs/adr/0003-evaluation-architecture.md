# ADR 0003: Tapered data-driven handcrafted evaluation

Status: accepted.

IEvaluator evaluates from side-to-move perspective and is independent of protocol adapters. MaterialPstEvaluator interpolates middlegame/endgame PST totals using non-pawn game phase; all weights live in EvalParams arrays. Black tables mirror white vertically. Drawn insufficient-material positions return zero immediately.

This keeps the evaluator replaceable by a future NNUE implementation without changing the search facade. No NNUE network, tablebase or book is part of this prototype. Material and PST evaluation traverses bitboards without heap allocation. Parameters are constructed outside search.

Static exchange evaluation computes an exchange sequence through occupancy changes and least-valuable attackers, including x-rays, en passant and promotions. Every recapturer is filtered for king safety, including pinned defenders and king captures. Legal generation remains the source of truth for the initial move and emitted moves.
