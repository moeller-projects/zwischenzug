using System.Diagnostics;
using Zwischenzug.Core;

namespace Zwischenzug.Engine;

/// <summary>Fixed, source-controlled positions used by the command-line benchmark.</summary>
public static class BenchmarkSuite
{
    public const int DefaultDepth = 5;
    public const long ExpectedDefaultNodes = 290830;

    // CPW positions: https://www.chessprogramming.org/Perft_Results
    // Mate studies: https://talkchess.com/viewtopic.php?t=78478

    public static IReadOnlyList<string> Positions { get; } =
    [
        Fen.StartPosition,
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
        "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1",
        "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8",
        "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
        "r1bqk2r/pppp1ppp/2n2n2/4p3/2bPP3/2N2N2/PPP2PPP/R1BQKB1R w KQkq - 4 5",
        "rnbqk2r/ppp1bppp/3p1n2/8/2PP4/2N2N2/PP2BPPP/R1BQK2R w KQkq - 2 6",
        "r1bq1rk1/ppp2ppp/2n1pn2/3p4/3P4/2P1PN2/PP1NBPPP/R1BQ1RK1 w - - 2 7",
        "r2q1rk1/ppp1bppp/2n1pn2/8/3PP3/2P1BN2/PP1N1PPP/R2Q1RK1 w - - 2 9",
        "r3k2r/ppp2ppp/2n1bn2/3p4/3P4/2P1PN2/PP1NBPPP/R2Q1RK1 w kq - 2 10",
        "r2q1rk1/1pp1bppp/p1np1n2/3Np3/3PP3/2P1B3/PP1N1PPP/R2Q1RK1 w - - 3 10",
        "r3r1k1/ppp2ppp/2n1bn2/3p4/3P4/2P1PN2/PP1NBPPP/R2QR1K1 w - - 6 12",
        "4k3/4n3/6p1/5Q2/8/8/8/K3R3 w - - 0 1",
        "8/5pk1/1p2p2p/2p1P1p1/2P3P1/1P3P2/5K1P/8 w - - 0 35",
        "8/6p1/3k2p1/2pPp3/2P1P3/1P3KP1/6P1/8 w - - 0 40",
        "8/p4pk1/1p2p2p/2p1P1p1/2P3P1/1P3P2/P4K1P/8 b - - 0 30",
        "6k1/5pp1/3p3p/1p1P4/1P2P3/P3K2P/5PP1/8 w - - 0 32",
        "8/2p3k1/1p1p2p1/p2Pp1Pp/P2P1P1P/1P2K3/8/8 w - - 0 40",
        "4k3/pp3pp1/2p4p/3pP3/3P1P2/2P3P1/PP3K2/8 b - - 0 28",
        "6R1/2K5/8/8/5p2/6p1/1Q3Nk1/8 w - - 0 1",
        "8/P3P3/p7/Pk6/4N3/1K6/8/8 w - - 0 1",
        "8/7p/8/7k/5K2/8/6R1/8 w - - 0 1",
        "8/6P1/8/1K6/2R5/8/1k6/8 w - - 0 1",
        "8/8/1Q3K2/3pN3/4kp2/8/8/8 w - - 0 1"
    ];

    public static async Task<BenchmarkResult> RunAsync(int depth = DefaultDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        var engine = new Engine();
        long nodes = 0, probes = 0, hits = 0;
        var watch = Stopwatch.StartNew();
        foreach (string fen in Positions)
        {
            engine.NewGame();
            engine.SetPosition(fen);
            SearchResult result = await engine.SearchAsync(new SearchLimits { Depth = depth }).ConfigureAwait(false);
            nodes += result.Nodes;
            probes += result.Statistics.TtProbes;
            hits += result.Statistics.TtHits;
        }
        watch.Stop();
        long elapsed = Math.Max(1, watch.ElapsedMilliseconds);
        long nps = nodes * 1000 / elapsed;
        return new BenchmarkResult(nodes, elapsed, nps, probes, hits);
    }
}

public sealed record BenchmarkResult(long TotalNodes, long ElapsedMilliseconds, long Nps, long TtProbes, long TtHits);
