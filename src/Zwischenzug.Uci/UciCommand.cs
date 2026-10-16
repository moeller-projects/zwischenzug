using Zwischenzug.Engine;

namespace Zwischenzug.Uci;

public abstract record UciCommand
{
    public sealed record Uci : UciCommand;
    public sealed record IsReady : UciCommand;
    public sealed record NewGame : UciCommand;
    public sealed record Stop : UciCommand;
    public sealed record Quit : UciCommand;
    public sealed record PonderHit : UciCommand;
    public sealed record SetOption(string Name, string? Value) : UciCommand;
    public sealed record Position(string Fen, string[] Moves) : UciCommand;
    public sealed record Go(SearchLimits Limits) : UciCommand;
}
