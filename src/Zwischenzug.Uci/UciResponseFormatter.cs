using System.Globalization;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Uci;

public static class UciResponseFormatter
{
    public static string Info(SearchResult result, bool showWdl = false, int? multiPv = null)
    {
        long nps = result.ElapsedMilliseconds > 0 ? result.Nodes * 1000 / result.ElapsedMilliseconds : 0;
        string score = Math.Abs(result.Score) >= 29000
            ? $"mate {MateDistance(result.Score).ToString(CultureInfo.InvariantCulture)}"
            : $"cp {result.Score.ToString(CultureInfo.InvariantCulture)}";
        string pv = result.Pv.Length == 0 ? string.Empty : " pv " + string.Join(' ', result.Pv);
        string index = multiPv is int number ? $" multipv {number.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        string wdl = string.Empty;
        if (showWdl)
        {
            (int win, int draw, int loss) = EstimateWdl(result.Score, result.MaterialUnits);
            wdl = string.Create(CultureInfo.InvariantCulture, $" wdl {win} {draw} {loss}");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"info depth {result.Depth} seldepth {result.SelectiveDepth}{index} score {score}{wdl} nodes {result.Nodes} nps {nps} time {result.ElapsedMilliseconds} hashfull {result.Hashfull}{pv}");
    }

    public static string BestMove(Move move) => $"bestmove {move}";

    public static string[] Handshake() =>
    [
        "id name Zwischenzug",
        "id author MoellerProjects",
        "option name Hash type spin default 16 min 1 max 1024",
        "option name Threads type spin default 1 min 1 max 128",
        "option name MultiPV type spin default 1 min 1 max 256",
        "option name UCI_ShowWDL type check default false",
        "option name Ponder type check default false",
        "option name Move Overhead type spin default 10 min 0 max 5000",
        "uciok"
    ];

    internal static (int Win, int Draw, int Loss) EstimateWdl(int score, int material)
    {
        double m = Math.Clamp(material, 17, 78) / 58.0;
        double a = ((-142.72052667 * m + 372.35176398) * m - 340.71073572) * m + 415.23490212;
        double b = ((5.93832785 * m + 15.61267078) * m - 30.57816876) * m + 69.63866711;
        static int WinRate(double center, double scale, int value) => (int)(0.5 + 1000 / (1 + Math.Exp((center - value) / scale)));
        int win = WinRate(a, b, score);
        int loss = WinRate(a, b, -score);
        return (win, 1000 - win - loss, loss);
    }

    private static int MateDistance(int score)
    {
        int distance = (30000 - Math.Abs(score) + 1) / 2;
        return Math.Sign(score) * distance;
    }
}
