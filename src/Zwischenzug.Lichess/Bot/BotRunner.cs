using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zwischenzug.Lichess;

public sealed class BotRunner(ILichessClient client,IOptions<LichessOptions> options,ILogger<BotRunner> logger,ILoggerFactory loggerFactory)
{
    private readonly object _gate = new();
    private readonly Dictionary<string,GameSlot> _games = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly ChallengePolicy _policy = new(options);
    private sealed class GameSlot(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            await foreach (var message in client.StreamEventsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                string gameId = message.Game is { GameId.Length: > 0 } ? message.Game.GameId : message.Game?.Id ?? "";
                switch (message.Type)
                {
                    case "challenge" when message.Challenge is not null:
                        await ChallengeAsync(message.Challenge,ct).ConfigureAwait(false); break;
                    case "challengeCanceled" or "challengeDeclined" when message.Challenge is not null:
                        lock (_gate) _pending.Remove(message.Challenge.Id); break;
                    case "gameStart" when gameId.Length > 0: StartGame(gameId,ct); break;
                    case "gameFinish" when gameId.Length > 0:
                        lock (_gate) if (_games.TryGetValue(gameId,out var game)) game.Cancellation.Cancel();
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            Task[] tasks;
            lock (_gate)
            {
                foreach (var slot in _games.Values.ToArray()) slot.Cancellation.Cancel();
                tasks = _games.Values.Select(slot => slot.Task).ToArray(); _pending.Clear();
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task ChallengeAsync(Challenge challenge,CancellationToken ct)
    {
        string? reason = _policy.DeclineReason(challenge);
        lock (_gate)
        {
            if (_pending.Contains(challenge.Id)) return;
            if (reason is null && _games.Count+_pending.Count >= options.Value.MaxConcurrentGames) reason = "later";
            if (reason is null) _pending.Add(challenge.Id);
        }
        try
        {
            if (reason is null) await client.AcceptChallengeAsync(challenge.Id,ct).ConfigureAwait(false);
            else await client.DeclineChallengeAsync(challenge.Id,reason,ct).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (!ct.IsCancellationRequested &&
            exception.StatusCode is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
        {
            // A known rejection releases capacity; an ambiguous accept awaits start/cancel.
            if (exception.StatusCode is not null) lock (_gate) _pending.Remove(challenge.Id);
            logger.LogWarning("Challenge response failed for {ChallengeId}, HTTP {Status}",challenge.Id,exception.StatusCode);
        }
        catch { lock (_gate) _pending.Remove(challenge.Id); throw; }
    }

    private void StartGame(string id,CancellationToken ct)
    {
        lock (_gate)
        {
            if (_games.ContainsKey(id)) return;
            if (_games.Count >= options.Value.MaxConcurrentGames)
            {
                logger.LogError("Cannot start game {GameId}: configured concurrent game limit reached",id); return;
            }
            // Never release another challenge's reservation for an unrelated game.
            _pending.Remove(id);
            var slot = new GameSlot(CancellationTokenSource.CreateLinkedTokenSource(ct));
            _games.Add(id,slot);
            slot.Task = RunGameAsync(id,slot);
        }
    }

    private async Task RunGameAsync(string id,GameSlot slot)
    {
        try { await new GameSession(client,options,loggerFactory.CreateLogger<GameSession>()).RunAsync(id,slot.Cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (slot.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception,"Lichess game {GameId} session failed",id); }
        finally
        {
            lock (_gate) _games.Remove(id);
            slot.Cancellation.Dispose();
        }
    }
}
