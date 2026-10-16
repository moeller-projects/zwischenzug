using Zwischenzug.Core;
namespace Zwischenzug.Engine;

/// <summary>Single-owner facade. Mutations are rejected while a search owns the position.</summary>
public sealed class Engine
{
    private readonly object _gate = new();
    private Position _position = new();
    private readonly TranspositionTable _table;
    private readonly Searcher _searcher;
    private bool _searching;
    public int MoveOverhead { get; set; } = 10;
    public Engine(int hashMegabytes = 16, IEvaluator? evaluator = null)
    {
        _table = new(hashMegabytes);
        _searcher = new(_table, evaluator ?? new MaterialPstEvaluator());
    }
    public void NewGame() { lock (_gate) { EnsureIdle(); _table.Clear(); } }
    public void SetHashSize(int megabytes) { lock (_gate) { EnsureIdle(); _table.Resize(megabytes); } }
    public void SetPosition(string fen, IEnumerable<string>? moves = null)
    {
        var position = Fen.Parse(fen);
        if (moves is not null) foreach (string text in moves) position.MakeMove(Move.Parse(position, text));
        lock (_gate) { EnsureIdle(); _position = position; }
    }
    public Task<SearchResult> SearchAsync(SearchLimits limits, ISearchObserver? observer = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            EnsureIdle(); _searching = true;
            return Task.Factory.StartNew(() =>
            {
                try { return _searcher.Search(_position, limits, MoveOverhead, observer, ct); }
                finally { lock (_gate) _searching = false; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
    private void EnsureIdle() { if (_searching) throw new InvalidOperationException("Stop and await the active search before changing engine state."); }
}
