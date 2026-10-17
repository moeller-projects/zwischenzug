using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zwischenzug.Core;
using Zwischenzug.Engine;
using ChessEngine = Zwischenzug.Engine.Engine;

namespace Zwischenzug.Lichess;

public sealed class GameSession(ILichessClient client, IOptions<LichessOptions> options, ILogger<GameSession> logger)
{
    public async Task RunAsync(string gameId, CancellationToken ct = default)
    {
        var settings = options.Value;
        var engine = new ChessEngine { MoveOverhead = settings.MoveOverhead };
        string? initialFen = null, previousMoves = null, submittedFor = null;
        int color = -1, version = 0;
        Task? active = null;
        CancellationTokenSource? searchCancellation = null;
        bool finished = false;
        try
        {
            while (!finished)
            {
                using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bool reconcile = false;
                try
                {
                    await foreach (var message in client.StreamGameAsync(gameId, streamCancellation.Token).WithCancellation(streamCancellation.Token).ConfigureAwait(false))
                    {
                        bool full = message.Type == "gameFull";
                        if (!full && message.Type != "gameState") continue;
                        if (full)
                        {
                            string snapshotFen = message.InitialFen is "" or "startpos" ? Fen.StartPosition : message.InitialFen;
                            int snapshotColor = Color(message, settings.Username);
                            if (snapshotColor < 0 || message.Id != gameId) throw new InvalidOperationException("Game snapshot identity does not match the configured bot.");
                            if (initialFen is not null && (initialFen != snapshotFen || color != snapshotColor)) throw new InvalidOperationException("Game identity changed across a reconnect.");
                            initialFen = snapshotFen; color = snapshotColor;
                        }
                        if (initialFen is null) continue;
                        GameState state = full ? message.State ?? throw new InvalidOperationException("gameFull is missing state.") : message;
                        string moves = string.Join(' ', state.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                        if (previousMoves is not null && moves != previousMoves && !moves.StartsWith(previousMoves.Length == 0 ? "" : previousMoves + " ", StringComparison.Ordinal))
                            throw new InvalidOperationException("Game move history conflicts with the previous authoritative state.");
                        bool started = state.Status == "started";
                        // Clock-only updates and reconnect snapshots cannot duplicate a submitted move.
                        if (started && moves == previousMoves && (active is { IsCompleted: false } || Volatile.Read(ref submittedFor) == moves)) continue;
                        Interlocked.Increment(ref version);
                        await StopSearchAsync().ConfigureAwait(false);
                        previousMoves = moves;
                        if (!started)
                        {
                            if (state.Status != "created") { finished = true; break; }
                            continue;
                        }
                        var position = Fen.Parse(initialFen);
                        string[] moveList = moves.Length == 0 ? [] : moves.Split(' ');
                        foreach (string text in moveList) position.MakeMove(Move.Parse(position, text));
                        if (position.SideToMove != color) continue;
                        engine.SetPosition(initialFen, moveList);
                        searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        CancellationToken searchToken = searchCancellation.Token;
                        int expectedVersion = version;
                        var limits = new SearchLimits
                        {
                            Depth = settings.SearchDepth,
                            WhiteTime = Math.Max(0, state.Wtime),
                            BlackTime = Math.Max(0, state.Btime),
                            WhiteIncrement = Math.Max(0, state.Winc),
                            BlackIncrement = Math.Max(0, state.Binc)
                        };
                        active = SearchAndSubmitAsync();
                        async Task SearchAndSubmitAsync()
                        {
                            SearchResult result = await engine.SearchAsync(limits, ct: searchToken).ConfigureAwait(false);
                            if (searchToken.IsCancellationRequested || Volatile.Read(ref version) != expectedVersion || result.BestMove.Raw == 0) return;
                            try
                            {
                                await client.MakeMoveAsync(gameId, result.BestMove.ToString(), searchToken).ConfigureAwait(false);
                                Volatile.Write(ref submittedFor, moves);
                                logger.LogDebug("Submitted move for game {GameId}", gameId);
                            }
                            catch (HttpRequestException exception) when (!searchToken.IsCancellationRequested)
                            {
                                // The server may have accepted the move. Never resend blindly:
                                // reopen the stream and reconcile its authoritative gameFull first.
                                if (exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                                { streamCancellation.Cancel(); throw; }
                                logger.LogWarning("Move delivery needs reconciliation for {GameId}, HTTP {Status}", gameId, exception.StatusCode);
                                Volatile.Write(ref reconcile, true);
                                streamCancellation.Cancel();
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (streamCancellation.IsCancellationRequested && !ct.IsCancellationRequested) { }
                if (!Volatile.Read(ref reconcile)) break;
                Interlocked.Increment(ref version);
                await StopSearchAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            Interlocked.Increment(ref version);
            await StopSearchAsync().ConfigureAwait(false);
        }

        async Task StopSearchAsync()
        {
            searchCancellation?.Cancel();
            try { if (active is not null) await active.ConfigureAwait(false); }
            catch (OperationCanceledException) when (searchCancellation?.IsCancellationRequested == true) { }
            finally { searchCancellation?.Dispose(); searchCancellation = null; active = null; }
        }
    }

    private static int Color(GameEvent game, string username)
    {
        if (string.Equals(game.White?.Id, username, StringComparison.OrdinalIgnoreCase) || string.Equals(game.White?.Name, username, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(game.Black?.Id, username, StringComparison.OrdinalIgnoreCase) || string.Equals(game.Black?.Name, username, StringComparison.OrdinalIgnoreCase)) return 1;
        return -1;
    }
}
