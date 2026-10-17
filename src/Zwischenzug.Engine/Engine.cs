using Zwischenzug.Core;
namespace Zwischenzug.Engine;

/// <summary>Single-owner facade. Mutations are rejected while a search owns the position.</summary>
public sealed class Engine
{
    private readonly object _Gate = new();
    private Position _Position = new();
    private readonly TranspositionTable _Table;
    private readonly Searcher _Searcher;
    private bool _Searching;
    public int MoveOverhead { get; set; } = 10;
    public Engine(int hashMegabytes = 16, IEvaluator? evaluator = null)
    {
        _Table = new(hashMegabytes);
        _Searcher = new(_Table, evaluator ?? new MaterialPstEvaluator());
    }
    public void NewGame() { lock (_Gate) { EnsureIdle(); _Table.Clear(); } }
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
            EnsureIdle(); _Searching = true;
            return Task.Factory.StartNew(() =>
            {
                try { return _Searcher.Search(_Position, limits, MoveOverhead, observer, ct); }
                finally { lock (_Gate) _Searching = false; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
    private void EnsureIdle() { if (_Searching) throw new InvalidOperationException("Stop and await the active search before changing engine state."); }
}
