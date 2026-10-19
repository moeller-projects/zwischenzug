using Zwischenzug.Engine;

namespace Zwischenzug.Uci;

/// <summary>Typed commands recognized by the UCI text parser.</summary>
public abstract record UciCommand
{
    /// <summary>Requests the engine identity and supported option list.</summary>
    public sealed record Uci : UciCommand;
    /// <summary>Requests a readiness response without stopping an active search.</summary>
    public sealed record IsReady : UciCommand;
    /// <summary>Starts a new game by clearing the transposition table.</summary>
    public sealed record NewGame : UciCommand;
    /// <summary>Requests cancellation of active search work.</summary>
    public sealed record Stop : UciCommand;
    /// <summary>Ends the input loop after active work is stopped.</summary>
    public sealed record Quit : UciCommand;
    /// <summary>Starts the clock for an active ponder search.</summary>
    public sealed record PonderHit : UciCommand;
    /// <summary>Changes one advertised engine option.</summary>
    public sealed record SetOption(string Name, string? Value) : UciCommand;
    /// <summary>Sets a FEN root and optional moves to replay.</summary>
    public sealed record Position(string Fen, string[] Moves) : UciCommand;
    /// <summary>Starts a search with parsed depth, time, node, and thread limits.</summary>
    public sealed record Go(SearchLimits Limits) : UciCommand;
}
