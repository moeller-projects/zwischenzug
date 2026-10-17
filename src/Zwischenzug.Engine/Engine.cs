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
    public int MoveOverhead { get; set; } = 10;

    public Engine(int hashMegabytes = 16, IEvaluator? evaluator = null)
    {
        _Table = new(hashMegabytes);
        _Evaluator = evaluator ?? new MaterialPstEvaluator();
        _Searcher = new(_Table, _Evaluator);
    }

    public void NewGame() { lock (_Gate) { EnsureIdle(); _Table.Clear(); } }
    public void PonderHit() { lock (_Gate) _ActiveClock?.PonderHit(); }
    public void SetHashSize(int megabytes) { lock (_Gate) { EnsureIdle(); _Table.Resize(megabytes); } }

    public void SetPosition(string fen, IEnumerable<string>? moves = null)
    {
        var position = Fen.Parse(fen);
        if (moves is not null) foreach (string text in moves) position.MakeMove(Move.Parse(position, text));
        lock (_Gate) { EnsureIdle(); _Position = position; }
    }

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

    private sealed class SerializedEvaluator(IEvaluator inner) : IEvaluator
    {
        private readonly object _Gate = new();
        public int Evaluate(in Position pos, int ply)
        {
            lock (_Gate) return inner.Evaluate(in pos, ply);
        }
    }

    private sealed class NodeCountObserver(ISearchObserver inner, SearchNodeCounter nodes) : ISearchObserver
    {
        public void OnIteration(SearchResult result) => inner.OnIteration(result with { Nodes = nodes.Nodes });
    }

    private void EnsureIdle() { if (_Searching) throw new InvalidOperationException("Stop and await the active search before changing engine state."); }
}
