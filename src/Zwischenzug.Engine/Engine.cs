using Zwischenzug.Core;
namespace Zwischenzug.Engine;

/// <summary>Single-owner facade. Mutations are rejected while a search owns the position.</summary>
public sealed class Engine
{
    private readonly object _Gate = new();
    private Position _Position = new();
    private readonly TranspositionTable _Table;
    private readonly IEvaluator _Evaluator;
    private readonly Searcher _Searcher;
    private bool _Searching;
    private SearchClock? _ActiveClock;
    /// <summary>Milliseconds reserved for a GUI or network to receive and play the move.</summary>
    public int MoveOverhead { get; set; } = 10;

    /// <summary>Creates an engine with its own position, transposition table, and evaluator.</summary>
    public Engine(int hashMegabytes = 16, IEvaluator? evaluator = null)
    {
        _Table = new(hashMegabytes);
        _Evaluator = evaluator ?? new MaterialPstEvaluator();
        _Searcher = new(_Table, _Evaluator);
    }

    /// <summary>Clears cached search results for a new game; position setup is a separate call.</summary>
    public void NewGame() { lock (_Gate) { EnsureIdle(); _Table.Clear(); } }
    /// <summary>Starts the normal clock after a pondering search receives the UCI ponderhit command.</summary>
    public void PonderHit() { lock (_Gate) _ActiveClock?.PonderHit(); }
    /// <summary>Resizes the shared transposition table while no search owns it.</summary>
    public void SetHashSize(int megabytes) { lock (_Gate) { EnsureIdle(); _Table.Resize(megabytes); } }

    /// <summary>Replaces the current position and optionally replays legal UCI moves from that starting FEN.</summary>
    public void SetPosition(string fen, IEnumerable<string>? moves = null)
    {
        var position = Fen.Parse(fen);
        if (moves is not null) foreach (string text in moves) position.MakeMove(Move.Parse(position, text));
        lock (_Gate) { EnsureIdle(); _Position = position; }
    }

    /// <summary>Starts an asynchronous search and owns the current position until the returned task completes.</summary>
    /// <remarks>Cancel and await the task before calling methods that mutate position or table state.</remarks>
    /// <example>
    /// <code>
    /// var engine = new Engine();
    /// engine.SetPosition(Fen.StartPosition);
    /// SearchResult result = await engine.SearchAsync(new SearchLimits { Depth = 4 });
    /// Console.WriteLine(result.BestMove); // For example, b1c3.
    /// </code>
    /// </example>
    public Task<SearchResult> SearchAsync(SearchLimits limits, ISearchObserver? observer = null, CancellationToken ct = default)
    {
        lock (_Gate)
        {
            EnsureIdle();
            var clock = new SearchClock(limits, _Position.SideToMove, MoveOverhead);
            _ActiveClock = clock;
            _Searching = true;
            return Task.Factory.StartNew(() =>
            {
                try
                {
                    _Table.NewGeneration();
                    int threads = Math.Clamp(limits.Threads, 1, 128);
                    return threads == 1
                        ? _Searcher.Search(_Position, limits, MoveOverhead, observer, ct, clock)
                        : SearchParallel(limits, threads, observer, ct, clock);
                }
                finally { lock (_Gate) { _ActiveClock = null; _Searching = false; } }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    /// <summary>Runs one leader and independent helper searches over cloned positions with a shared table and clock.</summary>
    private SearchResult SearchParallel(SearchLimits limits, int threadCount, ISearchObserver? observer, CancellationToken ct, SearchClock clock)
    {
        var nodeCounter = new SearchNodeCounter();
        IEvaluator[] evaluators = CreateWorkerEvaluators(threadCount);
        var positions = new Position[threadCount];
        var searchers = new Searcher[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            positions[i] = _Position.CloneForSearch();
            searchers[i] = new Searcher(_Table, evaluators[i], nodeCounter);
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = new Task<SearchResult>[threadCount - 1];
        int overhead = MoveOverhead;
        for (int i = 1; i < threadCount; i++)
        {
            int workerIndex = i;
            workers[i - 1] = Task.Factory.StartNew(() =>
            {
                try { return searchers[workerIndex].Search(positions[workerIndex], limits, overhead, null, cancellation.Token, clock); }
                catch { cancellation.Cancel(); throw; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        SearchResult leader;
        try
        {
            ISearchObserver? aggregateObserver = observer is null ? null : new NodeCountObserver(observer, nodeCounter);
            leader = searchers[0].Search(positions[0], limits, overhead, aggregateObserver, cancellation.Token, clock);
            Task.WaitAll(workers);
        }
        catch
        {
            cancellation.Cancel();
            try { Task.WaitAll(workers); }
            catch { }
            throw;
        }

        var results = new SearchResult[threadCount];
        results[0] = leader;
        for (int i = 1; i < threadCount; i++) results[i] = workers[i - 1].Result;
        SearchResult best = results[0];
        long quiescenceNodes = 0, killerUpdates = 0, historyUpdates = 0, ttFirstMoves = 0, elapsed = 0;
        int selectiveDepth = 0;
        foreach (SearchResult result in results)
        {
            if (result.Depth > best.Depth || result.Depth == best.Depth && result.Score > best.Score) best = result;
            SearchStatistics stats = result.Statistics;
            quiescenceNodes += stats.QuiescenceNodes;
            killerUpdates += stats.KillerUpdates;
            historyUpdates += stats.HistoryUpdates;
            ttFirstMoves += stats.TtFirstMoves;
            selectiveDepth = Math.Max(selectiveDepth, result.SelectiveDepth);
            elapsed = Math.Max(elapsed, result.ElapsedMilliseconds);
        }

        return best with
        {
            Nodes = nodeCounter.Nodes,
            SelectiveDepth = selectiveDepth,
            ElapsedMilliseconds = elapsed,
            Hashfull = _Table.Hashfull,
            Statistics = new SearchStatistics(quiescenceNodes, killerUpdates, historyUpdates, ttFirstMoves, _Table.Probes, _Table.Hits)
        };
    }

    /// <summary>Creates isolated evaluator state when supported, or serializes calls to a custom shared evaluator.</summary>
    private IEvaluator[] CreateWorkerEvaluators(int count)
    {
        var evaluators = new IEvaluator[count];
        if (_Evaluator is IWorkerEvaluatorFactory factory)
        {
            for (int i = 0; i < count; i++)
                evaluators[i] = factory.CreateWorkerEvaluator() ?? throw new InvalidOperationException("Evaluator factory returned null.");
        }
        else
        {
            Array.Fill(evaluators, new SerializedEvaluator(_Evaluator));
        }
        return evaluators;
    }

    /// <summary>Adapts a custom evaluator that has no worker factory by locking its potentially mutable state.</summary>
    private sealed class SerializedEvaluator(IEvaluator inner) : IEvaluator
    {
        private readonly object _Gate = new();
        /// <summary>Evaluates one position while excluding concurrent calls into the wrapped evaluator.</summary>
        public int Evaluate(in Position pos, int ply)
        {
            lock (_Gate) return inner.Evaluate(in pos, ply);
        }
    }

    /// <summary>Reports the shared Lazy SMP node total with each leader iteration.</summary>
    private sealed class NodeCountObserver(ISearchObserver inner, SearchNodeCounter nodes) : ISearchObserver
    {
        /// <summary>Forwards the leader result with a current aggregate node count.</summary>
        public void OnIteration(SearchResult result) => inner.OnIteration(result with { Nodes = nodes.Nodes });
    }

    /// <summary>Rejects state changes until all search workers have been joined.</summary>
    private void EnsureIdle() { if (_Searching) throw new InvalidOperationException("Stop and await the active search before changing engine state."); }
}
