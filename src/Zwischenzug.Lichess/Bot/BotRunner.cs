using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zwischenzug.Lichess;

public sealed class BotRunner(ILichessClient client, IOptions<LichessOptions> options, ILogger<BotRunner> logger, ILoggerFactory loggerFactory)
{
    private readonly object _Gate = new();
    private readonly Dictionary<string, GameSlot> _Games = new(StringComparer.Ordinal);
    private readonly HashSet<string> _Pending = new(StringComparer.Ordinal);
    private readonly ChallengePolicy _Policy = new(options);
    /// <summary>Tracks the cancellation lifetime and completion task for one exact game ID.</summary>
    private sealed class GameSlot(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;
    }
    private readonly List<Task> _ChallengeTasks = [];
    private Exception? _FatalFailure;

    /// <summary>Consumes account events and manages independently cancellable challenge and game tasks.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken loopToken = runCancellation.Token;
        try
        {
            await foreach (var message in client.StreamEventsAsync(loopToken).WithCancellation(loopToken).ConfigureAwait(false))
            {
                string gameId = message.Game is { GameId.Length: > 0 } ? message.Game.GameId : message.Game?.Id ?? "";
                switch (message.Type)
                {
                    case "challenge" when message.Challenge is not null:
                        DispatchChallenge(message.Challenge,loopToken,runCancellation); break;
                    case "challengeCanceled" or "challengeDeclined" when message.Challenge is not null:
                        lock (_Gate) _Pending.Remove(message.Challenge.Id); break;
                    case "gameStart" when gameId.Length > 0:
                        StartGame(gameId, loopToken); break;
                    case "gameFinish" when gameId.Length > 0:
                        lock (_Gate) if (_Games.TryGetValue(gameId, out var game)) game.Cancellation.Cancel();
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested && !ct.IsCancellationRequested && _FatalFailure is not null)
        {
            throw _FatalFailure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            Task[] tasks;
            lock (_Gate)
            {
                foreach (var slot in _Games.Values.ToArray()) slot.Cancellation.Cancel();
                tasks = _Games.Values.Select(slot => slot.Task).Concat(_ChallengeTasks).ToArray(); _Pending.Clear();
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    /// <summary>Reserves game capacity synchronously, then handles the network response without blocking event intake.</summary>
    private void DispatchChallenge(Challenge challenge,CancellationToken ct,CancellationTokenSource abort)
    {
        string? reason = _Policy.DeclineReason(challenge);
        lock (_Gate)
        {
            if (_Pending.Contains(challenge.Id)) return;
            if (reason is null && _Games.Count + _Pending.Count >= options.Value.MaxConcurrentGames) reason = "later";
            if (reason is null) _Pending.Add(challenge.Id);
            Task? task = null;
            task = Task.Run(async () =>
            {
                try { await ChallengeAsync(challenge,reason,ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is LichessApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden })
                {
                    _FatalFailure = ex; abort.Cancel();
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex,"Unhandled failure handling challenge {ChallengeId}",challenge.Id);
                }
                finally { lock (_Gate) if (task is not null) _ChallengeTasks.Remove(task); }
            },CancellationToken.None);
            _ChallengeTasks.Add(task);
        }
    }

    /// <summary>Accepts or declines one challenge and resolves its reservation only when the outcome is known.</summary>
    private async Task ChallengeAsync(Challenge challenge,string? reason,CancellationToken ct)
    {
        try
        {
            if (reason is null) await client.AcceptChallengeAsync(challenge.Id, ct).ConfigureAwait(false);
            else { await client.DeclineChallengeAsync(challenge.Id, reason, ct).ConfigureAwait(false); return; }
        }
        catch (HttpRequestException exception) when (!ct.IsCancellationRequested &&
            exception.StatusCode is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
        {
            // A known rejection releases capacity; an ambiguous accept awaits start/cancel.
            if (exception.StatusCode is not null) lock (_Gate) _Pending.Remove(challenge.Id);
            logger.LogWarning("Challenge response failed for {ChallengeId}, HTTP {Status}", challenge.Id, exception.StatusCode);
        }
        catch { lock (_Gate) _Pending.Remove(challenge.Id); throw; }
        if (reason is not null) lock (_Gate) _Pending.Remove(challenge.Id);
    }

    /// <summary>Starts one session per exact game identity if configured capacity remains.</summary>
    private void StartGame(string id, CancellationToken ct)
    {
        lock (_Gate)
        {
            if (_Games.ContainsKey(id)) return;
            _Pending.Remove(id);
            if (_Games.Count >= options.Value.MaxConcurrentGames)
            {
                logger.LogError("Cannot start game {GameId}: configured concurrent game limit reached", id); return;
            }
            var slot = new GameSlot(CancellationTokenSource.CreateLinkedTokenSource(ct));
            _Games.Add(id, slot);
            slot.Task = RunGameAsync(id, slot);
        }
    }
    /// <summary>Runs and observes one game session, then releases its game slot and cancellation source.</summary>
    private async Task RunGameAsync(string id, GameSlot slot)
    {
        try { await new GameSession(client, options, loggerFactory.CreateLogger<GameSession>()).RunAsync(id, slot.Cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (slot.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Lichess game {GameId} session failed", id); }
        finally
        {
            lock (_Gate) _Games.Remove(id);
            slot.Cancellation.Dispose();
        }
    }
}
