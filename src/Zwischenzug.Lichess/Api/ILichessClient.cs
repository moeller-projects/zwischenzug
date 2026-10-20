using System.Runtime.CompilerServices;

namespace Zwischenzug.Lichess;

/// <summary>Transport boundary used by the bot and replaceable with deterministic test fixtures.</summary>
public interface ILichessClient
{
    /// <summary>Streams account events such as challenges and game starts.</summary>
    IAsyncEnumerable<BotEvent> StreamEventsAsync(CancellationToken cancellationToken = default);
    /// <summary>Streams authoritative state updates for one game.</summary>
    IAsyncEnumerable<GameEvent> StreamGameAsync(string gameId, CancellationToken cancellationToken = default);
    /// <summary>Accepts one challenge by exact identity.</summary>
    Task AcceptChallengeAsync(string id, CancellationToken cancellationToken = default);
    /// <summary>Declines one challenge with an API reason code.</summary>
    Task DeclineChallengeAsync(string id, string reason, CancellationToken cancellationToken = default);
    /// <summary>Submits a coordinate move to one game.</summary>
    Task MakeMoveAsync(string gameId, string move, CancellationToken cancellationToken = default);
}
