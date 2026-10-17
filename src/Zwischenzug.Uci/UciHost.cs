using Microsoft.Extensions.Logging;
using Zwischenzug.Engine;
using ChessEngine = Zwischenzug.Engine.Engine;

namespace Zwischenzug.Uci;

/// <summary>Serial command mutations, concurrent search, and atomic protocol output.</summary>
public sealed class UciHost(TextReader input, TextWriter output, ChessEngine engine, ILogger<UciHost> logger) : ISearchObserver
{
    private readonly object _OutputGate = new();
    private readonly UciOptions _Options = new();
    private CancellationTokenSource? _SearchCancellation;
    private Task? _SearchTask;
    private CancellationTokenSource? _LifetimeCancellation;

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _LifetimeCancellation = lifetime;
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
                        _SearchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        _SearchTask = SearchAndReportAsync(go.Limits, _SearchCancellation.Token);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            try { await StopAsync().ConfigureAwait(false); }
            finally { _LifetimeCancellation = null; }
        }
    }

    private async Task SetOptionAsync(UciCommand.SetOption option)
    {
        if (!_Options.TrySet(option.Name, option.Value)) return;
        switch (option.Name.ToLowerInvariant())
        {
            case "hash":
                await StopAsync().ConfigureAwait(false); engine.SetHashSize(_Options.Hash); break;
            case "move overhead":
                await StopAsync().ConfigureAwait(false); engine.MoveOverhead = _Options.MoveOverhead; break;
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
            _LifetimeCancellation?.Cancel();
            throw;
        }
    }

    private async Task StopAsync()
    {
        if (_SearchTask is null) return;
        _SearchCancellation!.Cancel();
        try { await _SearchTask.ConfigureAwait(false); }
        finally
        {
            _SearchTask = null; _SearchCancellation.Dispose(); _SearchCancellation = null;
        }
    }

    public void OnIteration(SearchResult result) => Write(UciResponseFormatter.Info(result));

    private void Write(string line)
    {
        lock (_OutputGate) { output.WriteLine(line); output.Flush(); }
    }
}
