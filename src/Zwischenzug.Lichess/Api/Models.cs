namespace Zwischenzug.Lichess;

/// <summary>One account-level event received from the Lichess event stream.</summary>
public sealed class BotEvent
{
    /// <summary>Wire event kind, such as challenge, gameStart, or gameFinish.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Challenge payload when this event concerns a challenge.</summary>
    public Challenge? Challenge { get; set; }
    /// <summary>Game identity payload for game lifecycle events.</summary>
    public GameReference? Game { get; set; }
}

/// <summary>Game identity fields used across Lichess event variants.</summary>
public sealed class GameReference
{
    /// <summary>General event identity field.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Explicit game ID field used by game lifecycle events.</summary>
    public string GameId { get; set; } = string.Empty;
}

/// <summary>The challenge fields needed to apply local acceptance policy.</summary>
public sealed class Challenge
{
    public string Id { get; set; } = string.Empty;
    public bool Rated { get; set; }
    public Variant? Variant { get; set; }
    public TimeControl? TimeControl { get; set; }
}

/// <summary>Wire representation of a chess variant, such as standard chess.</summary>
public sealed class Variant
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>Wire representation of a Lichess time-control category.</summary>
public sealed class TimeControl
{
    public string Type { get; set; } = string.Empty;
}

/// <summary>Player identity from an authoritative game snapshot.</summary>
public sealed class Player
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Current move prefix, status, and clocks supplied by the game stream.</summary>
public class GameState
{
    public string Type { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Wtime { get; set; }
    public int Btime { get; set; }
    public int Winc { get; set; }
    public int Binc { get; set; }
}

/// <summary>Game snapshot or update; a full event also carries initial FEN, players, and nested state.</summary>
public sealed class GameEvent : GameState
{
    public string Id { get; set; } = string.Empty;
    public string InitialFen { get; set; } = string.Empty;
    public Player? White { get; set; }
    public Player? Black { get; set; }
    public GameState? State { get; set; }
}
