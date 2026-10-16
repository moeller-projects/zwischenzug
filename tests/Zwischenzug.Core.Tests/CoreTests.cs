using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;

namespace Zwischenzug.Core.Tests;

[TestClass]
public sealed class CoreTests
{
    // Reference FENs and counts: https://www.chessprogramming.org/Perft_Results
    public static IEnumerable<object[]> PerftCases()
    {
        long[] start = [20, 400, 8902, 197281, 4865609];
        for (int i = 0; i < start.Length; i++) yield return [Fen.StartPosition, i + 1, start[i]];
        const string kiwi = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -";
        yield return [kiwi, 1, 48L]; yield return [kiwi, 2, 2039L]; yield return [kiwi, 3, 97862L];
        string[] fens = ["8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10"];
        long[][] counts = [[14, 191, 2812, 43238], [6, 264, 9467, 422333], [44, 1486, 62379, 2103487], [46, 2079, 89890, 3894594]];
        for (int p = 0; p < fens.Length; p++) for (int d = 0; d < 4; d++) yield return [fens[p], d + 1, counts[p][d]];
    }

    [TestMethod, DynamicData(nameof(PerftCases))]
    public void PublishedPerft(string fen, int depth, long expected)
    {
        var position = Fen.Parse(fen);
        Assert.AreEqual(expected, Perft.Count(position, depth));
    }

    [TestMethod]
    public void MakeUnmakeRestoresAllStateAcrossSpecialMoveTrees()
    {
        foreach (var group in PerftCases().GroupBy(row => (string)row[0]))
        {
            var p = Fen.Parse(group.Key);
            Assert.AreEqual(Fen.Format(p), Fen.Format(Fen.Parse(Fen.Format(p))));
            VerifyTree(p, group.Max(row => (int)row[1]));
        }
    }

    private static void VerifyTree(Position p, int depth)
    {
        ulong key = p.Key, occupied = p.Occupied;
        var metadata = (p.SideToMove, p.Castling, p.EnPassant, p.HalfmoveClock, p.FullmoveNumber, p.Ply);
        Span<int> board = stackalloc int[64]; p.Board.CopyTo(board);
        Span<ulong> pieces = stackalloc ulong[13]; p.Pieces.CopyTo(pieces);
        Span<ulong> colors = stackalloc ulong[2]; p.Colors.CopyTo(colors);
        Span<Move> moves = stackalloc Move[256];
        int count = MoveGenerator.Generate(p, moves);
        for (int i = 0; i < count; i++)
        {
            p.MakeMove(moves[i]);
            if (Zobrist.Compute(p) != p.Key) Assert.Fail($"Incremental key diverged after {moves[i]}");
            if (depth > 1) VerifyTree(p, depth - 1);
            p.UnmakeMove();
            if (key != p.Key || occupied != p.Occupied || metadata != (p.SideToMove, p.Castling, p.EnPassant, p.HalfmoveClock, p.FullmoveNumber, p.Ply)
                || !board.SequenceEqual(p.Board) || !pieces.SequenceEqual(p.Pieces) || !colors.SequenceEqual(p.Colors))
                Assert.Fail($"Unmake failed to restore complete state after {moves[i]}");
        }
    }

    [TestMethod]
    public void DivideHasPublishedRootCounts()
    {
        var expected = new Dictionary<string, long> { ["a2a3"] = 380, ["a2a4"] = 420, ["b2b3"] = 420, ["b2b4"] = 421, ["c2c3"] = 420, ["c2c4"] = 441, ["d2d3"] = 539, ["d2d4"] = 560, ["e2e3"] = 599, ["e2e4"] = 600, ["f2f3"] = 380, ["f2f4"] = 401, ["g2g3"] = 420, ["g2g4"] = 421, ["h2h3"] = 380, ["h2h4"] = 420, ["b1a3"] = 400, ["b1c3"] = 440, ["g1f3"] = 440, ["g1h3"] = 400 };
        var actual = Perft.Divide(new Position(), 3);
        Assert.HasCount(20, actual);
        foreach (var (move, nodes) in actual) Assert.AreEqual(expected[move.ToString()], nodes);
    }

    [TestMethod]
    public void RepetitionTracksRealHistoryAndUndo()
    {
        var p = new Position();
        foreach (string text in new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8" }) p.MakeMove(Move.Parse(p, text));
        Assert.IsTrue(p.IsRepetition());
        p.UnmakeMove(); Assert.IsFalse(p.IsRepetition());
    }

    [TestMethod]
    public void EnPassantCannotExposeOwnKing()
    {
        var p = Fen.Parse("8/8/8/r4pPK/8/8/8/4k3 w - f6 0 1");
        Assert.ThrowsExactly<FormatException>(() => Move.Parse(p, "g5f6"));
    }

    [TestMethod]
    public void UnusableEnPassantDoesNotChangeRepetitionIdentity()
    {
        Assert.AreEqual(Fen.Parse("8/8/8/r4pPK/8/8/8/4k3 w - - 0 1").Key,
            Fen.Parse("8/8/8/r4pPK/8/8/8/4k3 w - f6 0 1").Key);
        Assert.AreNotEqual(Fen.Parse("4k3/8/8/5pP1/8/8/8/4K3 w - - 0 1").Key,
            Fen.Parse("4k3/8/8/5pP1/8/8/8/4K3 w - f6 0 1").Key);
    }
}
