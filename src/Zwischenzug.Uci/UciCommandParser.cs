using System.Globalization;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Uci;

public static class UciCommandParser
{
    public static UciCommand? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;
        return tokens[0].ToLowerInvariant() switch
        {
            "uci" when tokens.Length == 1 => new UciCommand.Uci(),
            "isready" when tokens.Length == 1 => new UciCommand.IsReady(),
            "ucinewgame" when tokens.Length == 1 => new UciCommand.NewGame(),
            "stop" when tokens.Length == 1 => new UciCommand.Stop(),
            "quit" when tokens.Length == 1 => new UciCommand.Quit(),
            "ponderhit" when tokens.Length == 1 => new UciCommand.PonderHit(),
            "setoption" => ParseSetOption(tokens),
            "position" => ParsePosition(tokens),
            "go" => ParseGo(tokens),
            _ => null
        };
    }

    private static UciCommand? ParseSetOption(string[] tokens)
    {
        if (tokens.Length < 3 || !tokens[1].Equals("name", StringComparison.OrdinalIgnoreCase)) return null;
        int valueIndex = Array.FindIndex(tokens, 2, t => t.Equals("value", StringComparison.OrdinalIgnoreCase));
        string name = string.Join(' ', tokens[valueIndex < 0 ? 2.. : 2..valueIndex]);
        if (name.Length == 0) return null;
        string? value = valueIndex < 0 ? null : string.Join(' ', tokens[(valueIndex + 1)..]);
        return new UciCommand.SetOption(name, value);
    }

    private static UciCommand? ParsePosition(string[] tokens)
    {
        if (tokens.Length < 2) return null;
        int movesIndex = Array.FindIndex(tokens, 1, t => t.Equals("moves", StringComparison.OrdinalIgnoreCase));
        int end = movesIndex < 0 ? tokens.Length : movesIndex;
        string fen;
        if (tokens[1].Equals("startpos", StringComparison.OrdinalIgnoreCase))
        {
            if (end != 2) return null;
            fen = Fen.StartPosition;
        }
        else if (tokens[1].Equals("fen", StringComparison.OrdinalIgnoreCase))
        {
            int fields = end - 2;
            if (fields is not (4 or 6)) return null;
            fen = string.Join(' ', tokens[2..end]);
        }
        else return null;
        string[] moves = movesIndex < 0 ? [] : tokens[(movesIndex + 1)..];
        return new UciCommand.Position(fen, moves);
    }

    private static UciCommand? ParseGo(string[] tokens)
    {
        int depth = 64; long nodes = long.MaxValue; int? movetime = null, wtime = null, btime = null;
        int winc = 0, binc = 0, movestogo = 30; bool infinite = false, ponder = false, hasDepth = false;
        for (int i = 1; i < tokens.Length; i++)
        {
            string key = tokens[i].ToLowerInvariant();
            if (key == "infinite") { infinite = true; continue; }
            if (key == "ponder") { ponder = true; continue; }
            if (key is not ("depth" or "nodes" or "movetime" or "wtime" or "btime" or "winc" or "binc" or "movestogo") || ++i >= tokens.Length) return null;
            string value = tokens[i];
            switch (key)
            {
                case "depth": if (!TryPositive(value, out depth)) return null; hasDepth = true; break;
                case "nodes": if (!TryPositive(value, out nodes)) return null; break;
                case "movetime": if (!TryNonNegative(value, out int mt)) return null; movetime = mt; break;
                case "wtime": if (!TryNonNegative(value, out int wt)) return null; wtime = wt; break;
                case "btime": if (!TryNonNegative(value, out int bt)) return null; btime = bt; break;
                case "winc": if (!TryNonNegative(value, out winc)) return null; break;
                case "binc": if (!TryNonNegative(value, out binc)) return null; break;
                case "movestogo": if (!TryPositive(value, out movestogo)) return null; break;
            }
        }
        return new UciCommand.Go(new SearchLimits { Depth = (infinite || ponder) && !hasDepth ? int.MaxValue : depth, Nodes = nodes, MoveTime = movetime, WhiteTime = wtime, BlackTime = btime, WhiteIncrement = winc, BlackIncrement = binc, MovesToGo = movestogo, Infinite = infinite, Ponder = ponder });
    }

    private static bool TryPositive(string text, out int value)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    private static bool TryPositive(string text, out long value)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    private static bool TryNonNegative(string text, out int value)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
}
