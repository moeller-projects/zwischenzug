using Microsoft.Extensions.Logging;
using Zwischenzug.Engine;
using ChessEngine = Zwischenzug.Engine.Engine;

namespace Zwischenzug.Uci;

/// <summary>Serial command mutations, concurrent search, and atomic protocol output.</summary>
public sealed class UciHost(TextReader input, TextWriter output, ChessEngine engine, ILogger<UciHost> logger) : ISearchObserver
{
    private readonly object _outputGate = new();
    private readonly UciOptions _options = new();
    private CancellationTokenSource? _searchCancellation;
    private Task? _searchTask;
    private CancellationTokenSource? _lifetimeCancellation;

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _lifetimeCancellation = lifetime;
        try
        {
            while (await input.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is string line)
            {
                UciCommand? command = UciCommandParser.Parse(line);
                switch (command)
                {
                    case UciCommand.Uci:
                        foreach (string response in UciResponseFormatter.Handshake()) Write(response);
                        break;
                    case UciCommand.IsReady: Write("readyok"); break;
                    case UciCommand.Quit: return;
                    case UciCommand.Stop: await StopAsync().ConfigureAwait(false); break;
                    case UciCommand.PonderHit: break;
                    case UciCommand.NewGame:
                        await StopAsync().ConfigureAwait(false); engine.NewGame(); break;
                    case UciCommand.Position position:
                        await StopAsync().ConfigureAwait(false);
                        try { engine.SetPosition(position.Fen, position.Moves); }
                        catch (FormatException) { logger.LogDebug("Ignoring invalid UCI position"); }
                        break;
                    case UciCommand.SetOption option:
                        await SetOptionAsync(option).ConfigureAwait(false); break;
                    case UciCommand.Go go:
                        await StopAsync().ConfigureAwait(false);
                        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        _searchTask = SearchAndReportAsync(go.Limits, _searchCancellation.Token);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            try { await StopAsync().ConfigureAwait(false); }
            finally { _lifetimeCancellation = null; }
        }
    }

    private async Task SetOptionAsync(UciCommand.SetOption option)
    {
        if (!_options.TrySet(option.Name, option.Value)) return;
        switch (option.Name.ToLowerInvariant())
        {
            case "hash":
                await StopAsync().ConfigureAwait(false); engine.SetHashSize(_options.Hash); break;
            case "move overhead":
                await StopAsync().ConfigureAwait(false); engine.MoveOverhead = _options.MoveOverhead; break;
            case "threads": break; // Accepted, deliberately single-threaded.
        }
    }

    private async Task SearchAndReportAsync(SearchLimits limits, CancellationToken ct)
    {
        try
        {
            SearchResult result = await engine.SearchAsync(limits, this, ct).ConfigureAwait(false);
            // UCI infinite must not emit bestmove until stop, even after a forced mate is found.
            if (limits.Infinite && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }
            Write(UciResponseFormatter.BestMove(result.BestMove));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogError(exception, "Search failed");
            // Wake the pending input read and surface the original fault immediately.
            // Never fabricate a bestmove after an unexpected engine failure.
            _lifetimeCancellation?.Cancel();
            throw;
        }
    }

    private async Task StopAsync()
    {
        if (_searchTask is null) return;
        _searchCancellation!.Cancel();
        try { await _searchTask.ConfigureAwait(false); }
        finally
        {
            _searchTask = null; _searchCancellation.Dispose(); _searchCancellation = null;
        }
    }

    public void OnIteration(SearchResult result) => Write(UciResponseFormatter.Info(result));

    private void Write(string line)
    {
        lock (_outputGate) { output.WriteLine(line); output.Flush(); }
    }
}
