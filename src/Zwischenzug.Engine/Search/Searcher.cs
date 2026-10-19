using System.Diagnostics;
using System.Numerics;
using Zwischenzug.Core;
namespace Zwischenzug.Engine;

/// <summary>Mutable worker-local search state; create one instance per concurrent worker.</summary>
internal sealed class Searcher(TranspositionTable table, IEvaluator evaluator, SearchNodeCounter? nodeCounter = null)
{
    private const int Mate = 30000, Infinity = 32000, MaxPly = 96;
    private readonly Move[,] _Killers = new Move[MaxPly, 2];
    private readonly int[,] _History = new int[13, 64];
    private readonly Move[,] _Pv = new Move[MaxPly, MaxPly];
    private readonly int[] _PvLength = new int[MaxPly];
    private readonly Stopwatch _Watch = new();
    private SearchLimits _Limits = new();
    private CancellationToken _Ct;
    private SearchClock? _Clock;
    private long _Budget, _Nodes, _Qnodes, _KillerUpdates, _HistoryUpdates, _TtFirst;
    private int _Iteration, _Seldepth, _MaterialUnits;
    private bool _Aborted;
    private Move[]? _RootPvMoves;
    private int[]? _RootPvLengths;

    /// <summary>Searches one root position, completing progressively deeper iterations and retaining the last complete answer.</summary>
    public SearchResult Search(Position p, SearchLimits limits, int overhead, ISearchObserver? observer, CancellationToken ct, SearchClock? clock = null)
    {
        _Limits = limits; _Ct = ct; _Clock = clock; _Budget = TimeManager.BudgetMilliseconds(limits, p.SideToMove, overhead);
        _MaterialUnits = MaterialUnits(p);
        _Nodes = _Qnodes = _KillerUpdates = _HistoryUpdates = _TtFirst = 0;
        _Seldepth = 0; _Aborted = false;
        Array.Clear(_Killers); Array.Clear(_History); Array.Clear(_PvLength);
        _Watch.Restart();
        Span<Move> legal = stackalloc Move[256]; int count = MoveGenerator.Generate(p, legal);
        if (count == 0) return Snapshot(default, Attacks.InCheck(p, p.SideToMove) ? -Mate : 0, 0, []);
        Move best = legal[0]; int score = 0;
        SearchResult result = Snapshot(best, score, 0, [best]);
        int maxDepth = Math.Clamp(limits.Depth, 1, MaxPly - 2);
        for (_Iteration = 1; _Iteration <= maxDepth; _Iteration++)
        {
            if (limits.MultiPv > 1)
            {
                if (!SearchMultiPvRoot(p, _Iteration, Math.Clamp(limits.MultiPv, 1, legal.Length), out PrincipalVariation[] lines)) break;
                best = lines[0].Moves[0];
                score = lines[0].Score;
                result = Snapshot(best, score, _Iteration, lines[0].Moves) with { MultiPvLines = lines };
            }
            else
            {
                int alpha = _Iteration > 1 ? Math.Max(-Infinity, score - 50) : -Infinity;
                int beta = _Iteration > 1 ? Math.Min(Infinity, score + 50) : Infinity;
                int value = Negamax(p, _Iteration, alpha, beta, 0, true);
                if (!_Aborted && (value <= alpha || value >= beta)) value = Negamax(p, _Iteration, -Infinity, Infinity, 0, true);
                if (_Aborted) break;
                score = value;
                if (_PvLength[0] > 0) best = _Pv[0, 0];
                int length = Math.Max(1, _PvLength[0]); var pv = new Move[length];
                pv[0] = best; for (int i = 1; i < length; i++) pv[i] = _Pv[0, i];
                result = Snapshot(best, score, _Iteration, pv);
            }
            observer?.OnIteration(result);
            if (_Ct.IsCancellationRequested || (nodeCounter?.Nodes ?? _Nodes) >= limits.Nodes || (_Clock?.IsTimeExpired ?? _Watch.ElapsedMilliseconds >= _Budget)) break;
            if (Math.Abs(score) >= Mate - MaxPly && _Iteration >= Mate - Math.Abs(score)) break;
        }
        _Watch.Stop();
        return result with { Nodes = _Nodes, ElapsedMilliseconds = _Clock?.ElapsedMilliseconds ?? _Watch.ElapsedMilliseconds, Statistics = Stats() };
    }

    /// <summary>Snapshots this worker's counters together with shared table probe statistics.</summary>
    private SearchStatistics Stats() => new(_Qnodes, _KillerUpdates, _HistoryUpdates, _TtFirst, table.Probes, table.Hits);
    /// <summary>Builds a result value from the best completed iteration and current counters.</summary>
    private SearchResult Snapshot(Move best, int score, int depth, Move[] pv) =>
        new SearchResult(best, score, depth, _Seldepth, _Nodes, _Clock?.ElapsedMilliseconds ?? _Watch.ElapsedMilliseconds, table.Hashfull, pv, Stats())
        {
            MaterialUnits = _MaterialUnits
        };

    /// <summary>Searches every legal root move so MultiPV scores are comparable and alternatives are distinct.</summary>
    private bool SearchMultiPvRoot(Position p, int depth, int requestedCount, out PrincipalVariation[] lines)
    {
        Span<Move> moves = stackalloc Move[256];
        int count = MoveGenerator.Generate(p, moves);
        if (count == 0) { lines = Array.Empty<PrincipalVariation>(); return false; }
        if (Draw(p))
        {
            int drawLineCount = Math.Min(requestedCount, count);
            lines = new PrincipalVariation[drawLineCount];
            for (int i = 0; i < drawLineCount; i++) lines[i] = new PrincipalVariation(0, [moves[i]]);
            return true;
        }

        _RootPvMoves ??= new Move[256 * MaxPly];
        _RootPvLengths ??= new int[256];
        int lineCount = Math.Min(requestedCount, count);
        ulong key = p.Key ^ (0x9E3779B97F4A7C15UL * (ulong)(p.HalfmoveClock + 1));
        Move ttMove = table.TryGet(key, 0, out var entry) ? entry.BestMove : default;
        Span<int> scores = stackalloc int[256];
        Span<int> indexes = stackalloc int[256];
        Span<int> rootScores = stackalloc int[256];
        for (int i = 0; i < count; i++) { scores[i] = Order(p, moves[i], ttMove, 0); indexes[i] = i; }
        for (int i = 0; i < count; i++)
        {
            int best = i;
            for (int j = i + 1; j < count; j++) if (scores[j] > scores[best]) best = j;
            (moves[i], moves[best]) = (moves[best], moves[i]);
            (scores[i], scores[best]) = (scores[best], scores[i]);
            (indexes[i], indexes[best]) = (indexes[best], indexes[i]);
            Move move = moves[i];
            if (i == 0 && move == ttMove) _TtFirst++;
            p.MakeMove(move);
            int value = -Negamax(p, depth - 1, -Infinity, Infinity, 1, true);
            p.UnmakeMove();
            if (_Aborted) { lines = Array.Empty<PrincipalVariation>(); return false; }
            int rootIndex = indexes[i];
            rootScores[rootIndex] = value;
            int offset = rootIndex * MaxPly;
            _RootPvMoves[offset] = move;
            int childLength = Math.Min(_PvLength[1], MaxPly - 1);
            for (int j = 0; j < childLength; j++) _RootPvMoves[offset + j + 1] = _Pv[1, j];
            _RootPvLengths[rootIndex] = childLength + 1;
        }

        Span<int> order = stackalloc int[256];
        for (int i = 0; i < count; i++) order[i] = i;
        for (int i = 0; i < lineCount; i++)
        {
            int best = i;
            for (int j = i + 1; j < count; j++) if (rootScores[order[j]] > rootScores[order[best]]) best = j;
            (order[i], order[best]) = (order[best], order[i]);
        }
        lines = new PrincipalVariation[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int rootIndex = order[i], length = _RootPvLengths[rootIndex];
            var pv = new Move[length];
            Array.Copy(_RootPvMoves, rootIndex * MaxPly, pv, 0, length);
            lines[i] = new PrincipalVariation(rootScores[rootIndex], pv);
        }
        return true;
    }

    /// <summary>Counts remaining non-pawn material in pawn units for the UCI WDL estimate model.</summary>
    private static int MaterialUnits(Position p) =>
        BitOperations.PopCount(p.Pieces[1] | p.Pieces[7]) +
        3 * (BitOperations.PopCount(p.Pieces[2] | p.Pieces[8]) + BitOperations.PopCount(p.Pieces[3] | p.Pieces[9])) +
        5 * BitOperations.PopCount(p.Pieces[4] | p.Pieces[10]) +
        9 * BitOperations.PopCount(p.Pieces[5] | p.Pieces[11]);

    /// <summary>Checks shared node/time limits and cancellation, but preserves a complete first iteration.</summary>
    private bool Stop()
    {
        // A complete depth-one result is guaranteed even if stop arrived before the worker started.
        if (_Iteration <= 1) return false;
        if ((nodeCounter?.Nodes ?? _Nodes) >= _Limits.Nodes || _Ct.IsCancellationRequested || (_Clock?.IsTimeExpired ?? _Watch.ElapsedMilliseconds >= _Budget)) _Aborted = true;
        return _Aborted;
    }
    /// <summary>Recognizes the draw rules modeled by this engine at a search node.</summary>
    private bool Draw(Position p) => p.HalfmoveClock >= 100 || p.IsRepetition() || p.HasInsufficientMaterial();

    /// <summary>Searches legal moves with negamax, alpha-beta bounds, TT reuse, and selective pruning.</summary>
    private int Negamax(Position p, int depth, int alpha, int beta, int ply, bool allowNull)
    {
        _PvLength[ply] = 0;
        if (Stop()) return 0;
        _Seldepth = Math.Max(_Seldepth, ply);
        bool inCheck = Attacks.InCheck(p, p.SideToMove);
        if (ply >= MaxPly - 2) return evaluator.Evaluate(in p, ply);
        if (depth <= 0) return Quiescence(p, alpha, beta, ply);
        _Nodes++; nodeCounter?.RecordNode();
        Span<Move> moves = stackalloc Move[256]; int count = MoveGenerator.Generate(p, moves);
        if (count == 0) return inCheck ? -Mate + ply : 0;
        if (Draw(p)) return 0;
        int originalAlpha = alpha; Move ttMove = default;
        bool nonPv = beta - alpha == 1;
        // The fifty-move clock changes whether a line can draw, so separate TT scores by that context even though repetitions ignore the clock.
        ulong tableKey = p.Key ^ (0x9E3779B97F4A7C15UL * (ulong)(p.HalfmoveClock + 1));
        bool hasEntry = table.TryGet(tableKey, ply, out var entry);
        if (hasEntry)
        {
            ttMove = entry.BestMove;
            if (nonPv && ply > 0 && entry.Depth >= depth && !p.IsRepetition(2))
            {
                if (entry.Bound == Bound.Exact || entry.Bound == Bound.Lower && entry.Score >= beta || entry.Bound == Bound.Upper && entry.Score <= alpha) return entry.Score;
            }
        }
        int staticEval = evaluator.Evaluate(in p, ply);
        ulong nonPawns = p.Colors[p.SideToMove] & ~(p.Pieces[1 + p.SideToMove * 6] | p.Pieces[6 + p.SideToMove * 6]);
        if (allowNull && ply > 0 && depth >= 3 && !inCheck && beta < Mate - MaxPly && staticEval >= beta && BitOperations.PopCount(nonPawns) >= 2)
        {
            // Passing is unsafe in checks, near mate, at shallow depth, or with too little non-pawn material; those guards reduce false cutoffs.
            p.MakeNullMove(); int value = -Negamax(p, depth - 1 - (2 + depth / 6), -beta, -beta + 1, ply + 1, false); p.UnmakeMove();
            if (_Aborted) return 0;
            if (value >= beta) return beta;
        }
        Span<int> scores = stackalloc int[256];
        for (int i = 0; i < count; i++) scores[i] = Order(p, moves[i], ttMove, ply);
        int bestScore = -Infinity; Move bestMove = default; int searched = 0;
        for (int i = 0; i < count; i++)
        {
            Pick(moves, scores, i, count); Move move = moves[i];
            if (i == 0 && move == ttMove) _TtFirst++;
            bool quiet = !move.IsCapture && move.Promotion == 0;
            int piece = p.Board[move.From];
            p.MakeMove(move); bool givesCheck = Attacks.InCheck(p, p.SideToMove);
            if (nonPv && searched > 0 && ply > 0 && depth <= 2 && !inCheck && quiet && !givesCheck && staticEval + 120 * depth <= alpha && Math.Abs(alpha) < Mate - MaxPly)
            { p.UnmakeMove(); continue; }
            int value;
            if (searched == 0) value = -Negamax(p, depth - 1, -beta, -alpha, ply + 1, true);
            else
            {
                // Later quiet moves are reduced only after an earlier move has been searched; a score near alpha gets a full-depth re-search.
                int reduction = nonPv && depth >= 3 && i >= 3 && quiet && !inCheck && !givesCheck ? 1 + (depth >= 6 && i >= 8 ? 1 : 0) : 0;
                value = -Negamax(p, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, true);
                if (reduction > 0 && value > alpha) value = -Negamax(p, depth - 1, -alpha - 1, -alpha, ply + 1, true);
                if (value > alpha && value < beta) value = -Negamax(p, depth - 1, -beta, -alpha, ply + 1, true);
            }
            p.UnmakeMove();
            if (_Aborted) return 0;
            searched++;
            if (value > bestScore) { bestScore = value; bestMove = move; }
            if (value > alpha)
            {
                alpha = value; _Pv[ply, 0] = move;
                int length = _PvLength[ply + 1];
                for (int j = 0; j < length; j++) _Pv[ply, j + 1] = _Pv[ply + 1, j];
                _PvLength[ply] = length + 1;
            }
            if (alpha >= beta)
            {
                if (quiet)
                {
                    if (_Killers[ply, 0] != move) { _Killers[ply, 1] = _Killers[ply, 0]; _Killers[ply, 0] = move; _KillerUpdates++; }
                    _History[piece, move.To] = Math.Min(200000, _History[piece, move.To] + depth * depth); _HistoryUpdates++;
                }
                break;
            }
        }
        Bound bound = bestScore <= originalAlpha ? Bound.Upper : bestScore >= beta ? Bound.Lower : Bound.Exact;
        table.Store(tableKey, depth, bestScore, bound, bestMove, ply);
        return bestScore;
    }

    /// <summary>Extends a leaf through captures, or through every legal evasion while the side is in check.</summary>
    private int Quiescence(Position p, int alpha, int beta, int ply)
    {
        _PvLength[ply] = 0;
        if (Stop()) return 0;
        _Nodes++; _Qnodes++; nodeCounter?.RecordNode(); _Seldepth = Math.Max(_Seldepth, ply);
        bool inCheck = Attacks.InCheck(p, p.SideToMove);
        Span<Move> all = stackalloc Move[256]; int allCount = MoveGenerator.Generate(p, all);
        if (allCount == 0) return inCheck ? -Mate + ply : 0;
        if (Draw(p)) return 0;
        int stand = evaluator.Evaluate(in p, ply);
        if (ply >= MaxPly - 2) return stand;
        if (!inCheck) { if (stand >= beta) return stand; alpha = Math.Max(alpha, stand); }
        Span<Move> moves = stackalloc Move[256];
        int count;
        if (inCheck) { all[..allCount].CopyTo(moves); count = allCount; }
        else count = MoveGenerator.Generate(p, moves, true);
        Span<int> scores = stackalloc int[256];
        for (int i = 0; i < count; i++) scores[i] = Order(p, moves[i], default, ply);
        for (int i = 0; i < count; i++)
        {
            Pick(moves, scores, i, count); Move move = moves[i];
            // Keep promotions even when their exchange looks bad: the material change can make them tactically essential.
            if (!inCheck && move.Promotion == 0 && StaticExchange.Evaluate(p, move) < 0) continue;
            p.MakeMove(move); int value = -Quiescence(p, -beta, -alpha, ply + 1); p.UnmakeMove();
            if (_Aborted) return 0;
            if (value >= beta) return value;
            if (value > alpha)
            {
                alpha = value; _Pv[ply, 0] = move;
                int length = _PvLength[ply + 1];
                for (int j = 0; j < length; j++) _Pv[ply, j + 1] = _Pv[ply + 1, j];
                _PvLength[ply] = length + 1;
            }
        }
        return alpha;
    }
    /// <summary>Assigns a heuristic score so promising moves are searched first and alpha-beta cuts more work.</summary>
    private int Order(Position p, Move move, Move ttMove, int ply)
    {
        if (move == ttMove && ttMove.Raw != 0) return 2000000;
        if (move.IsCapture || move.Promotion != 0)
        {
            int victim = move.Flag == 3 ? 1 : (p.Board[move.To] - 1) % 6 + 1;
            int attacker = (p.Board[move.From] - 1) % 6 + 1;
            int see = StaticExchange.Evaluate(p, move);
            return (see >= 0 ? 1000000 : -1000000) + victim * 100 - attacker + move.Promotion * 1000;
        }
        if (_Killers[ply, 0] == move) return 900000;
        if (_Killers[ply, 1] == move) return 800000;
        return _History[p.Board[move.From], move.To];
    }
    /// <summary>Moves the highest-scoring remaining candidate into the next search slot without sorting the full list.</summary>
    private static void Pick(Span<Move> moves, Span<int> scores, int start, int count)
    {
        int best = start;
        for (int i = start + 1; i < count; i++) if (scores[i] > scores[best]) best = i;
        if (best == start) return;
        (moves[start], moves[best]) = (moves[best], moves[start]);
        (scores[start], scores[best]) = (scores[best], scores[start]);
    }
}
