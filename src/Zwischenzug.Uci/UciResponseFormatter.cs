using System.Globalization;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Uci;

public static class UciResponseFormatter
{
    public static string Info(SearchResult result)
    {
        long nps = result.ElapsedMilliseconds > 0 ? result.Nodes * 1000 / result.ElapsedMilliseconds : 0;
        string score = Math.Abs(result.Score) >= 29000
            ? $"mate {MateDistance(result.Score).ToString(CultureInfo.InvariantCulture)}"
            : $"cp {result.Score.ToString(CultureInfo.InvariantCulture)}";
        string pv = result.Pv.Length == 0 ? string.Empty : " pv " + string.Join(' ', result.Pv);
        return string.Create(CultureInfo.InvariantCulture,
            $"info depth {result.Depth} seldepth {result.SelectiveDepth} score {score} nodes {result.Nodes} nps {nps} time {result.ElapsedMilliseconds} hashfull {result.Hashfull}{pv}");
    }

    public static string BestMove(Move move) => $"bestmove {move}";

    public static string[] Handshake() =>
    [
        "id name Zwischenzug",
        "id author MoellerProjects",
        "option name Hash type spin default 16 min 1 max 1024",
        "option name Threads type spin default 1 min 1 max 128",
        "option name Move Overhead type spin default 10 min 0 max 5000",
        "uciok"
    ];

    private static int MateDistance(int score)
    {
        int distance = (30000 - Math.Abs(score) + 1) / 2;
        return Math.Sign(score) * Math.Max(1, distance);
    }
}
