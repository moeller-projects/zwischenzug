using System.Runtime.CompilerServices;

namespace Zwischenzug.Lichess;

public interface ILichessClient
{
    IAsyncEnumerable<BotEvent> StreamEventsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<GameEvent> StreamGameAsync(string gameId, CancellationToken cancellationToken = default);
    Task AcceptChallengeAsync(string id, CancellationToken cancellationToken = default);
    Task DeclineChallengeAsync(string id, string reason, CancellationToken cancellationToken = default);
    Task MakeMoveAsync(string gameId, string move, CancellationToken cancellationToken = default);
}
