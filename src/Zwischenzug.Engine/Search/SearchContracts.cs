using System.Diagnostics;
using Zwischenzug.Core;
namespace Zwischenzug.Engine;

/// <summary>Input limits and options that control one engine search.</summary>
public sealed record SearchLimits
{
    /// <summary>Maximum iterative-deepening depth, clamped to the engine's supported range.</summary>
    public int Depth { get; init; } = 64;
    /// <summary>Node budget shared by search workers; long.MaxValue means no node limit.</summary>
    public long Nodes { get; init; } = long.MaxValue;
    /// <summary>Exact move-time budget in milliseconds, when supplied.</summary>
    public int? MoveTime { get; init; }
    /// <summary>Remaining White clock in milliseconds.</summary>
    public int? WhiteTime { get; init; }
    /// <summary>Remaining Black clock in milliseconds.</summary>
    public int? BlackTime { get; init; }
    /// <summary>White's per-move clock increment in milliseconds.</summary>
    public int WhiteIncrement { get; init; }
    /// <summary>Black's per-move clock increment in milliseconds.</summary>
    public int BlackIncrement { get; init; }
    /// <summary>Estimated moves remaining before the next time control.</summary>
    public int MovesToGo { get; init; } = 30;
    /// <summary>Requested Lazy SMP worker count; the engine clamps it to 1–128.</summary>
    public int Threads { get; init; } = 1;
    /// <summary>Number of distinct root alternatives to return.</summary>
    public int MultiPv { get; init; } = 1;
    /// <summary>Search until explicitly stopped rather than ending at a clock budget.</summary>
    public bool Infinite { get; init; }
    /// <summary>Wait for ponderhit before starting the normal clock budget.</summary>
    public bool Ponder { get; init; }
}

/// <summary>Search counters useful for UCI reporting and benchmark analysis.</summary>
public readonly record struct SearchStatistics(long QuiescenceNodes, long KillerUpdates, long HistoryUpdates, long TtFirstMoves, long TtProbes, long TtHits);
/// <summary>A score and ordered move sequence beginning at the search root.</summary>
public sealed record PrincipalVariation(int Score, Move[] Moves);
/// <summary>The best completed search result, including its principal variation and statistics.</summary>
public sealed record SearchResult(Move BestMove, int Score, int Depth, int SelectiveDepth, long Nodes, long ElapsedMilliseconds, int Hashfull, Move[] Pv, SearchStatistics Statistics)
{
    public int MaterialUnits { get; init; }
    public PrincipalVariation[] MultiPvLines { get; init; } = Array.Empty<PrincipalVariation>();
}
/// <summary>Receives a snapshot after each fully completed search depth.</summary>
public interface ISearchObserver
{
    /// <summary>Reports one completed iterative-deepening result.</summary>
    void OnIteration(SearchResult result);
}

internal sealed class SearchNodeCounter
{
    private long _Nodes;
    /// <summary>Reads the process-wide count shared by Lazy SMP workers.</summary>
    public long Nodes => Interlocked.Read(ref _Nodes);
    /// <summary>Atomically adds one node for whichever worker reached it.</summary>
    public void RecordNode() => Interlocked.Increment(ref _Nodes);
}

internal sealed class SearchClock
{
    private readonly long _NormalBudget;
    private readonly bool _Infinite;
    private long _Started;
    private long _Budget;
    private int _Pondering;

    /// <summary>Creates one monotonic clock shared by all workers for this search.</summary>
    public SearchClock(SearchLimits limits, int side, int overhead)
    {
        _NormalBudget = TimeManager.BudgetMilliseconds(limits with { Infinite = false, Ponder = false }, side, overhead);
        _Infinite = limits.Infinite;
        _Pondering = limits.Ponder ? 1 : 0;
        _Started = Stopwatch.GetTimestamp();
        _Budget = limits.Ponder || limits.Infinite ? long.MaxValue : _NormalBudget;
    }

    /// <summary>Gets elapsed monotonic time, including only time since the active clock start.</summary>
    public long ElapsedMilliseconds
    {
        get
        {
            long started = Interlocked.Read(ref _Started);
            return (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency;
        }
    }

    /// <summary>Reports timeout only after ponderhit and when the search has a finite budget.</summary>
    public bool IsTimeExpired => Volatile.Read(ref _Pondering) == 0 &&
        Interlocked.Read(ref _Budget) != long.MaxValue && ElapsedMilliseconds >= Interlocked.Read(ref _Budget);

    /// <summary>Starts the normal time budget once; repeated ponderhit calls have no effect.</summary>
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


/// <summary>Converts UCI clocks and move limits into a per-search millisecond budget.</summary>
public static class TimeManager
{
    // Reserve overhead, divide the usable clock over moves to go, add 75% of increment,
    // and never spend more than one quarter of the usable remaining clock.
    /// <summary>Computes usable time after overhead, expected moves-to-go, and increment are considered.</summary>
    /// <example>
    /// <code>
    /// long budget = TimeManager.BudgetMilliseconds(
    ///     new SearchLimits { WhiteTime = 10_000, MovesToGo = 20 }, side: 0, overhead: 100);
    /// // usable=9,900 ms; the smaller of 1/4 clock and 1/20 clock is 495 ms.
    /// </code>
    /// </example>
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
