namespace Zwischenzug.Lichess;

public sealed class BotEvent
{
    public string Type { get; set; } = string.Empty;
    public Challenge? Challenge { get; set; }
    public GameReference? Game { get; set; }
}

public sealed class GameReference
{
    public string Id { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
}

public sealed class Challenge
{
    public string Id { get; set; } = string.Empty;
    public bool Rated { get; set; }
    public Variant? Variant { get; set; }
    public TimeControl? TimeControl { get; set; }
}

public sealed class Variant
{
    public string Key { get; set; } = string.Empty;
}

public sealed class TimeControl
{
    public string Type { get; set; } = string.Empty;
}

public sealed class Player
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

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

public sealed class GameEvent : GameState
{
    public string Id { get; set; } = string.Empty;
    public string InitialFen { get; set; } = string.Empty;
    public Player? White { get; set; }
    public Player? Black { get; set; }
    public GameState? State { get; set; }
}
