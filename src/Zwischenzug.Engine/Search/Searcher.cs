using System.Diagnostics;
using System.Numerics;
using Zwischenzug.Core;
namespace Zwischenzug.Engine;

internal sealed class Searcher(TranspositionTable table,IEvaluator evaluator)
{
    private const int Mate = 30000, Infinity = 32000, MaxPly = 96;
    private readonly Move[,] _killers = new Move[MaxPly,2];
    private readonly int[,] _history = new int[13,64];
    private readonly Move[,] _pv = new Move[MaxPly,MaxPly];
    private readonly int[] _pvLength = new int[MaxPly];
    private readonly Stopwatch _watch = new();
    private SearchLimits _limits = new();
    private CancellationToken _ct;
    private long _budget, _nodes, _qnodes, _killerUpdates, _historyUpdates, _ttFirst;
    private int _iteration, _seldepth;
    private bool _aborted;

    public SearchResult Search(Position p,SearchLimits limits,int overhead,ISearchObserver? observer,CancellationToken ct)
    {
        _limits = limits; _ct = ct; _budget = TimeManager.BudgetMilliseconds(limits,p.SideToMove,overhead);
        _nodes = _qnodes = _killerUpdates = _historyUpdates = _ttFirst = 0;
        _seldepth = 0; _aborted = false;
        Array.Clear(_killers); Array.Clear(_history); Array.Clear(_pvLength);
        table.NewGeneration(); _watch.Restart();
        Span<Move> legal = stackalloc Move[256]; int count = MoveGenerator.Generate(p,legal);
        if (count == 0) return Snapshot(default,Attacks.InCheck(p,p.SideToMove) ? -Mate : 0,0,[]);
        Move best = legal[0]; int score = 0;
        SearchResult result = Snapshot(best,score,0,[best]);
        int maxDepth = Math.Clamp(limits.Depth,1,MaxPly-2);
        for (_iteration = 1; _iteration <= maxDepth; _iteration++)
        {
            int alpha = _iteration > 1 ? Math.Max(-Infinity,score-50) : -Infinity;
            int beta = _iteration > 1 ? Math.Min(Infinity,score+50) : Infinity;
            int value = Negamax(p,_iteration,alpha,beta,0,true);
            if (!_aborted && (value <= alpha || value >= beta)) value = Negamax(p,_iteration,-Infinity,Infinity,0,true);
            if (_aborted) break;
            score = value;
            if (_pvLength[0] > 0) best = _pv[0,0];
            int length = Math.Max(1,_pvLength[0]); var pv = new Move[length];
            pv[0] = best; for (int i=1;i<length;i++) pv[i] = _pv[0,i];
            result = Snapshot(best,score,_iteration,pv);
            observer?.OnIteration(result);
            if (_ct.IsCancellationRequested || _nodes >= limits.Nodes || _watch.ElapsedMilliseconds >= _budget) break;
            if (Math.Abs(score) >= Mate-MaxPly && _iteration >= Mate-Math.Abs(score)) break;
        }
        _watch.Stop();
        return result with { Nodes=_nodes,ElapsedMilliseconds=_watch.ElapsedMilliseconds,Statistics=Stats() };
    }

    private SearchStatistics Stats() => new(_qnodes,_killerUpdates,_historyUpdates,_ttFirst,table.Probes,table.Hits);
    private SearchResult Snapshot(Move best,int score,int depth,Move[] pv) => new(best,score,depth,_seldepth,_nodes,_watch.ElapsedMilliseconds,table.Hashfull,pv,Stats());
    private bool Stop()
    {
        // A complete depth-one result is guaranteed even if stop arrived before the worker started.
        if (_iteration <= 1) return false;
        if (_nodes >= _limits.Nodes || _ct.IsCancellationRequested || _watch.ElapsedMilliseconds >= _budget) _aborted = true;
        return _aborted;
    }
    private bool Draw(Position p) => p.HalfmoveClock >= 100 || p.IsRepetition() || p.HasInsufficientMaterial();

    private int Negamax(Position p,int depth,int alpha,int beta,int ply,bool allowNull)
    {
        _pvLength[ply] = 0;
        if (Stop()) return 0;
        _seldepth = Math.Max(_seldepth,ply);
        bool inCheck = Attacks.InCheck(p,p.SideToMove);
        if (ply >= MaxPly-2) return evaluator.Evaluate(in p,ply);
        if (depth <= 0) return Quiescence(p,alpha,beta,ply);
        _nodes++;
        Span<Move> moves = stackalloc Move[256]; int count = MoveGenerator.Generate(p,moves);
        if (count == 0) return inCheck ? -Mate+ply : 0;
        if (Draw(p)) return 0;
        int originalAlpha = alpha; Move ttMove = default;
        bool nonPv = beta - alpha == 1;
        // The fifty-move counter is not part of repetition identity, but is score context.
        ulong tableKey = p.Key ^ (0x9E3779B97F4A7C15UL * (ulong)(p.HalfmoveClock + 1));
        bool hasEntry = table.TryGet(tableKey,ply,out var entry);
        if (hasEntry)
        {
            ttMove = entry.BestMove;
            if (nonPv && ply > 0 && entry.Depth >= depth && !p.IsRepetition(2))
            {
                if (entry.Bound == Bound.Exact || entry.Bound == Bound.Lower && entry.Score >= beta || entry.Bound == Bound.Upper && entry.Score <= alpha) return entry.Score;
            }
        }
        int staticEval = evaluator.Evaluate(in p,ply);
        ulong nonPawns = p.Colors[p.SideToMove] & ~(p.Pieces[1+p.SideToMove*6] | p.Pieces[6+p.SideToMove*6]);
        if (allowNull && ply > 0 && depth >= 3 && !inCheck && beta < Mate-MaxPly && staticEval >= beta && BitOperations.PopCount(nonPawns) >= 2)
        {
            p.MakeNullMove(); int value = -Negamax(p,depth-1-(2+depth/6),-beta,-beta+1,ply+1,false); p.UnmakeMove();
            if (_aborted) return 0;
            if (value >= beta) return beta;
        }
        Span<int> scores = stackalloc int[256];
        for (int i=0;i<count;i++) scores[i] = Order(p,moves[i],ttMove,ply);
        int bestScore = -Infinity; Move bestMove = default; int searched = 0;
        for (int i=0;i<count;i++)
        {
            Pick(moves,scores,i,count); Move move = moves[i];
            if (i == 0 && move == ttMove) _ttFirst++;
            bool quiet = !move.IsCapture && move.Promotion == 0;
            int piece = p.Board[move.From];
            p.MakeMove(move); bool givesCheck = Attacks.InCheck(p,p.SideToMove);
            if (nonPv && searched > 0 && ply > 0 && depth <= 2 && !inCheck && quiet && !givesCheck && staticEval+120*depth <= alpha && Math.Abs(alpha) < Mate-MaxPly)
            { p.UnmakeMove(); continue; }
            int value;
            if (searched == 0) value = -Negamax(p,depth-1,-beta,-alpha,ply+1,true);
            else
            {
                int reduction = nonPv && depth >= 3 && i >= 3 && quiet && !inCheck && !givesCheck ? 1 + (depth>=6 && i>=8 ? 1 : 0) : 0;
                value = -Negamax(p,depth-1-reduction,-alpha-1,-alpha,ply+1,true);
                if (reduction > 0 && value > alpha) value = -Negamax(p,depth-1,-alpha-1,-alpha,ply+1,true);
                if (value > alpha && value < beta) value = -Negamax(p,depth-1,-beta,-alpha,ply+1,true);
            }
            p.UnmakeMove();
            if (_aborted) return 0;
            searched++;
            if (value > bestScore) { bestScore = value; bestMove = move; }
            if (value > alpha)
            {
                alpha = value; _pv[ply,0] = move;
                int length = _pvLength[ply+1];
                for (int j=0;j<length;j++) _pv[ply,j+1] = _pv[ply+1,j];
                _pvLength[ply] = length+1;
            }
            if (alpha >= beta)
            {
                if (quiet)
                {
                    if (_killers[ply,0] != move) { _killers[ply,1] = _killers[ply,0]; _killers[ply,0] = move; _killerUpdates++; }
                    _history[piece,move.To] = Math.Min(200000,_history[piece,move.To]+depth*depth); _historyUpdates++;
                }
                break;
            }
        }
        Bound bound = bestScore <= originalAlpha ? Bound.Upper : bestScore >= beta ? Bound.Lower : Bound.Exact;
        table.Store(tableKey,depth,bestScore,bound,bestMove,ply);
        return bestScore;
    }

    private int Quiescence(Position p,int alpha,int beta,int ply)
    {
        _pvLength[ply] = 0;
        if (Stop()) return 0;
        _nodes++; _qnodes++; _seldepth = Math.Max(_seldepth,ply);
        bool inCheck = Attacks.InCheck(p,p.SideToMove);
        Span<Move> all = stackalloc Move[256]; int allCount = MoveGenerator.Generate(p,all);
        if (allCount == 0) return inCheck ? -Mate+ply : 0;
        if (Draw(p)) return 0;
        int stand = evaluator.Evaluate(in p,ply);
        if (ply >= MaxPly-2) return stand;
        if (!inCheck) { if (stand >= beta) return stand; alpha = Math.Max(alpha,stand); }
        Span<Move> moves = stackalloc Move[256];
        int count;
        if (inCheck) { all[..allCount].CopyTo(moves); count = allCount; }
        else count = MoveGenerator.Generate(p,moves,true);
        Span<int> scores = stackalloc int[256];
        for (int i=0;i<count;i++) scores[i] = Order(p,moves[i],default,ply);
        for (int i=0;i<count;i++)
        {
            Pick(moves,scores,i,count); Move move = moves[i];
            if (!inCheck && move.Promotion == 0 && StaticExchange.Evaluate(p,move) < 0) continue;
            p.MakeMove(move); int value = -Quiescence(p,-beta,-alpha,ply+1); p.UnmakeMove();
            if (_aborted) return 0;
            if (value >= beta) return value;
            if (value > alpha)
            {
                alpha = value; _pv[ply,0] = move;
                int length = _pvLength[ply+1];
                for (int j=0;j<length;j++) _pv[ply,j+1] = _pv[ply+1,j];
                _pvLength[ply] = length+1;
            }
        }
        return alpha;
    }
    private int Order(Position p,Move move,Move ttMove,int ply)
    {
        if (move == ttMove && ttMove.Raw != 0) return 2000000;
        if (move.IsCapture || move.Promotion != 0)
        {
            int victim = move.Flag == 3 ? 1 : (p.Board[move.To]-1)%6+1;
            int attacker = (p.Board[move.From]-1)%6+1;
            int see = StaticExchange.Evaluate(p,move);
            return (see >= 0 ? 1000000 : -1000000)+victim*100-attacker+move.Promotion*1000;
        }
        if (_killers[ply,0] == move) return 900000;
        if (_killers[ply,1] == move) return 800000;
        return _history[p.Board[move.From],move.To];
    }
    private static void Pick(Span<Move> moves,Span<int> scores,int start,int count)
    {
        int best = start;
        for (int i=start+1;i<count;i++) if (scores[i] > scores[best]) best = i;
        if (best == start) return;
        (moves[start],moves[best]) = (moves[best],moves[start]);
        (scores[start],scores[best]) = (scores[best],scores[start]);
    }
}
