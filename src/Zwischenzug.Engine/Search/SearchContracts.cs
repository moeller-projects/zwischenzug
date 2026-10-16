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
    public bool Infinite { get; init; }
}

public readonly record struct SearchStatistics(long QuiescenceNodes, long KillerUpdates, long HistoryUpdates, long TtFirstMoves, long TtProbes, long TtHits);
public sealed record SearchResult(Move BestMove, int Score, int Depth, int SelectiveDepth, long Nodes, long ElapsedMilliseconds, int Hashfull, Move[] Pv, SearchStatistics Statistics);
public interface ISearchObserver { void OnIteration(SearchResult result); }

public static class TimeManager
{
    // Reserve overhead, divide the usable clock over moves to go, add 75% of increment,
    // and never spend more than one quarter of the usable remaining clock.
    public static long BudgetMilliseconds(SearchLimits limits, int side, int overhead)
    {
        if (limits.MoveTime is int exact) return Math.Max(1, exact);
        if (limits.Infinite) return long.MaxValue;
        int? remaining = side == 0 ? limits.WhiteTime : limits.BlackTime;
        if (remaining is null) return long.MaxValue;
        long usable = Math.Max(1, (long)remaining.Value - Math.Max(0, overhead));
        int increment = side == 0 ? limits.WhiteIncrement : limits.BlackIncrement;
        return Math.Max(1, Math.Min(usable / 4, usable / Math.Max(1, limits.MovesToGo) + Math.Max(0, increment) * 3L / 4));
    }
}
