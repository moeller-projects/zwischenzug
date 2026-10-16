# ADR 0002: Deterministic single-owner PVS search

Status: accepted.

Iterative deepening always completes depth one before honoring stop, so an immediate stop produces a searched legal move. Higher iterations check cancellation, node limits and the monotonic clock at every node. The last complete iteration supplies the result; incomplete iterations never replace it. Explicit movetime uses the requested milliseconds. Clock allocation reserves Move Overhead, divides usable time by moves-to-go (default 30), adds 75% of increment and caps spending at one quarter of usable time. Depth, nodes and clocks combine by whichever ends first.

Negamax uses alpha-beta/PVS, aspiration windows, TT ordering, MVV-LVA/SEE, two killers per ply, and bounded history scores. Null move requires at least two non-pawn/non-king pieces, no check, sufficient depth and a static beta advantage. Reduction is 2 + depth/6. LMR and frontier futility apply only at non-principal-variation nodes, on quiet non-checking moves, never while in check. Mate-three regression fixtures caught and prohibited reducing/pruning PV nodes. Quiescence searches captures and quiet promotions or all legal check evasions; it detects stalemate before stand-pat.

The TT is fixed-sized, four-way associative, single-owner and uses full keys, generation aging and depth-preferred replacement. Shallower same-generation results preserve deeper entries. Mate scores are normalized on store/probe for ply-independent distance. The search mixes the halfmove clock into its TT key without changing Core repetition identity; fifty-move-dependent scores cannot leak between clocks. Rule draws precede TT use; repeated positions do not accept TT cutoffs. Only non-PV windows accept cutoffs, preserving searched principal variations. Mate/no-move detection precedes rule draws.

Each search runs on a dedicated LongRunning worker. Facade state mutations are rejected until its task finishes. Adapters must cancel and await the old task before changing position/options. Arrays for PV, killers and history are allocated once; recursive search uses stack spans. Observer snapshots allocate only at iteration boundaries, outside the node hot path.

Depth is bounded by the fixed search stack (94 plies); the prototype is single-threaded, with no pondering. Heuristics reset each search; NewGame clears TT. Deterministic benchmark positions start from cleared state.
