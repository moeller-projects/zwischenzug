using Microsoft.Extensions.Logging;
using Zwischenzug.Engine;
using ChessEngine = Zwischenzug.Engine.Engine;

namespace Zwischenzug.Uci;

/// <summary>Serial command mutations, concurrent search, and atomic protocol output.</summary>
/// <param name="input">UCI command stream, normally standard input.</param>
/// <param name="output">Protocol-only output stream, normally standard output.</param>
/// <param name="engine">Engine whose state this protocol loop owns.</param>
/// <param name="logger">Diagnostics sink configured to write to standard error.</param>
public sealed class UciHost(TextReader input, TextWriter output, ChessEngine engine, ILogger<UciHost> logger) : ISearchObserver
{
    private readonly object _OutputGate = new();
    private readonly UciOptions _Options = new();
    private CancellationTokenSource? _SearchCancellation;
    private Task? _SearchTask;
    private TaskCompletionSource? _PonderRelease;
    private CancellationTokenSource? _LifetimeCancellation;

    /// <summary>Reads UCI commands until input closes, quit arrives, or a fatal search failure occurs.</summary>
    /// <remarks>All protocol output uses the supplied writer; host diagnostics use the logger.</remarks>
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
                    case UciCommand.PonderHit:
                        engine.PonderHit();
                        _PonderRelease?.TrySetResult();
                        break;
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
                        SearchLimits limits = go.Limits with
                        {
                            Threads = _Options.Threads,
                            MultiPv = _Options.MultiPv,
                            Ponder = go.Limits.Ponder && _Options.Ponder
                        };
                        if (go.Limits.Ponder && !limits.Ponder && limits.Depth == int.MaxValue) limits = limits with { Depth = 64 };
                        _PonderRelease = limits.Ponder ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
                        _SearchTask = SearchAndReportAsync(limits, _SearchCancellation.Token);
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

    /// <summary>Applies a recognized option and stops search first when that option changes active engine state.</summary>
    private async Task SetOptionAsync(UciCommand.SetOption option)
    {
        if (!_Options.TrySet(option.Name, option.Value)) return;
        switch (option.Name.ToLowerInvariant())
        {
            case "hash":
                await StopAsync().ConfigureAwait(false); engine.SetHashSize(_Options.Hash); break;
            case "move overhead":
                await StopAsync().ConfigureAwait(false); engine.MoveOverhead = _Options.MoveOverhead; break;
            case "threads":
            case "multipv":
            case "uci_showwdl":
            case "ponder":
                await StopAsync().ConfigureAwait(false); break;
        }
    }

    /// <summary>Runs a search, handles ponder/infinite release rules, and writes its final bestmove.</summary>
    private async Task SearchAndReportAsync(SearchLimits limits, CancellationToken ct)
    {
        try
        {
            SearchResult result = await engine.SearchAsync(limits, this, ct).ConfigureAwait(false);
            // UCI infinite and ponder searches withhold bestmove until the matching release command.
            if (_PonderRelease is not null && !ct.IsCancellationRequested)
            {
                try { await _PonderRelease.Task.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }
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

    /// <summary>Cancels and awaits the current search before allowing any state mutation.</summary>
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

    /// <summary>Formats a completed iteration, emitting one indexed line for each requested MultiPV candidate.</summary>
    public void OnIteration(SearchResult result)
    {
        if (result.MultiPvLines.Length > 1)
        {
            for (int i = 0; i < result.MultiPvLines.Length; i++)
            {
                PrincipalVariation line = result.MultiPvLines[i];
                Write(UciResponseFormatter.Info(result with { Score = line.Score, Pv = line.Moves }, _Options.ShowWdl, i + 1));
            }
            return;
        }
        Write(UciResponseFormatter.Info(result, _Options.ShowWdl));
    }

    /// <summary>Writes and flushes one protocol line atomically so worker reports cannot interleave.</summary>
    private void Write(string line)
    {
        lock (_OutputGate) { output.WriteLine(line); output.Flush(); }
    }
}
