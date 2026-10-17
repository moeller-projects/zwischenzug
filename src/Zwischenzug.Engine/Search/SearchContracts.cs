using System.Diagnostics;
using Zwischenzug.Core;
namespace Zwischenzug.Engine;

public sealed record SearchLimits
{
    public int Depth { get; init; } = 64;
    public long Nodes { get; init; } = long.MaxValue;
    public int? MoveTime { get; init; }
    public int? WhiteTime { get; init; }
    public int? BlackTime { get; init; }
    public int WhiteIncrement { get; init; }
    public int BlackIncrement { get; init; }
    public int MovesToGo { get; init; } = 30;
    public int Threads { get; init; } = 1;
    public int MultiPv { get; init; } = 1;
    public bool Infinite { get; init; }
    public bool Ponder { get; init; }
}

public readonly record struct SearchStatistics(long QuiescenceNodes, long KillerUpdates, long HistoryUpdates, long TtFirstMoves, long TtProbes, long TtHits);
public sealed record PrincipalVariation(int Score, Move[] Moves);
public sealed record SearchResult(Move BestMove, int Score, int Depth, int SelectiveDepth, long Nodes, long ElapsedMilliseconds, int Hashfull, Move[] Pv, SearchStatistics Statistics)
{
    public int MaterialUnits { get; init; }
    public PrincipalVariation[] MultiPvLines { get; init; } = Array.Empty<PrincipalVariation>();
}
public interface ISearchObserver { void OnIteration(SearchResult result); }

internal sealed class SearchNodeCounter
{
    private long _Nodes;
    public long Nodes => Interlocked.Read(ref _Nodes);
    public void RecordNode() => Interlocked.Increment(ref _Nodes);
}

internal sealed class SearchClock
{
    private readonly long _NormalBudget;
    private readonly bool _Infinite;
    private long _Started;
    private long _Budget;
    private int _Pondering;

    public SearchClock(SearchLimits limits, int side, int overhead)
    {
        _NormalBudget = TimeManager.BudgetMilliseconds(limits with { Infinite = false, Ponder = false }, side, overhead);
        _Infinite = limits.Infinite;
        _Pondering = limits.Ponder ? 1 : 0;
        _Started = Stopwatch.GetTimestamp();
        _Budget = limits.Ponder || limits.Infinite ? long.MaxValue : _NormalBudget;
    }

    public long ElapsedMilliseconds
    {
        get
        {
            long started = Interlocked.Read(ref _Started);
            return (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency;
        }
    }

    public bool IsTimeExpired => Volatile.Read(ref _Pondering) == 0 &&
        Interlocked.Read(ref _Budget) != long.MaxValue && ElapsedMilliseconds >= Interlocked.Read(ref _Budget);

    public void PonderHit()
    {
        if (Interlocked.CompareExchange(ref _Pondering, 2, 1) == 1)
        {
            Interlocked.Exchange(ref _Started, Stopwatch.GetTimestamp());
            if (!_Infinite) Interlocked.Exchange(ref _Budget, _NormalBudget);
            Volatile.Write(ref _Pondering, 0);
        }
    }
}


public static class TimeManager
{
    // Reserve overhead, divide the usable clock over moves to go, add 75% of increment,
    // and never spend more than one quarter of the usable remaining clock.
    public static long BudgetMilliseconds(SearchLimits limits, int side, int overhead)
    {
        if (limits.Ponder) return long.MaxValue;
        if (limits.MoveTime is int exact) return Math.Max(1, exact);
        if (limits.Infinite) return long.MaxValue;
        int? remaining = side == 0 ? limits.WhiteTime : limits.BlackTime;
        if (remaining is null) return long.MaxValue;
        long usable = Math.Max(1, (long)remaining.Value - Math.Max(0, overhead));
        int increment = side == 0 ? limits.WhiteIncrement : limits.BlackIncrement;
        return Math.Max(1, Math.Min(usable / 4, usable / Math.Max(1, limits.MovesToGo) + Math.Max(0, increment) * 3L / 4));
    }
}
