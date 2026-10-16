using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;

namespace Zwischenzug.Engine.Tests;

[TestClass]
public sealed class SearchTests
{
    [TestMethod]
    public async Task RepeatedFreshSearchesAreDeterministicAndLegal()
    {
        var engine = new Engine();
        var first = await engine.SearchAsync(new SearchLimits { Depth = 4 });
        engine.NewGame(); engine.SetPosition(Fen.StartPosition);
        var second = await engine.SearchAsync(new SearchLimits { Depth = 4 });
        Assert.AreEqual(first.BestMove, second.BestMove); Assert.AreEqual(first.Nodes, second.Nodes);
        Assert.AreEqual(first.BestMove, Move.Parse(new Position(),first.BestMove.ToString()));
        Assert.IsGreaterThan(0L, first.Statistics.QuiescenceNodes);
        Assert.IsGreaterThan(0L, first.Statistics.KillerUpdates);
        Assert.IsGreaterThan(0L, first.Statistics.HistoryUpdates);
        Assert.IsGreaterThan(0L, first.Statistics.TtFirstMoves);
    }

    [TestMethod]
    [DataRow("7k/6Q1/5K2/8/8/8/8/8 b - - 0 1", -30000)]
    [DataRow("7k/5K2/6Q1/8/8/8/8/8 b - - 0 1", 0)]
    public async Task TerminalRootsReturnNullMove(string fen,int score)
    {
        var engine = new Engine(); engine.SetPosition(fen);
        var result = await engine.SearchAsync(new SearchLimits { Depth=4 });
        Assert.AreEqual(default, result.BestMove); Assert.AreEqual(score,result.Score);
    }

    [TestMethod]
    public async Task ImmediateCancellationStillCompletesFirstIteration()
    {
        var engine = new Engine(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var result = await engine.SearchAsync(new SearchLimits { Infinite=true },ct:cancel.Token);
        Assert.AreEqual(1,result.Depth);
        Assert.AreEqual(result.BestMove,Move.Parse(new Position(),result.BestMove.ToString()));
    }

    [TestMethod]
    public async Task TimeAndNodeLimitsStopSearch()
    {
        foreach (var limits in new[] { new SearchLimits { MoveTime=80 }, new SearchLimits { WhiteTime=1000,BlackTime=1000 }, new SearchLimits { Nodes=2000 } })
        {
            var engine = new Engine(); var watch = Stopwatch.StartNew();
            var result = await engine.SearchAsync(limits);
            Assert.IsLessThan(200L,watch.ElapsedMilliseconds);
            Assert.AreEqual(result.BestMove,Move.Parse(new Position(),result.BestMove.ToString()));
            if (limits.Nodes != long.MaxValue) Assert.IsLessThanOrEqualTo(limits.Nodes,result.Nodes);
        }
    }

    [TestMethod]
    public async Task HangingQueenIsResolvedByQuiescence()
    {
        var engine = new Engine(); engine.SetPosition("4k3/8/8/8/3q4/8/3R4/4K3 w - - 0 1");
        var result = await engine.SearchAsync(new SearchLimits { Depth=1 });
        Assert.AreEqual("d2d4",result.BestMove.ToString());
        Assert.IsGreaterThan(0L,result.Statistics.QuiescenceNodes);
    }

    [TestMethod]
    public async Task DrawRulesRespectMatePrecedence()
    {
        var engine = new Engine();
        engine.SetPosition("7k/6Q1/5K2/8/8/8/8/8 b - - 100 1");
        Assert.AreEqual(-30000,(await engine.SearchAsync(new SearchLimits { Depth=2 })).Score);
        engine.SetPosition("4k3/8/8/8/8/8/8/4K3 w - - 100 1");
        Assert.AreEqual(0,(await engine.SearchAsync(new SearchLimits { Depth=2 })).Score);
    }

    [TestMethod]
    public async Task WarmTablePreservesLegalPrincipalVariation()
    {
        var engine = new Engine();
        await engine.SearchAsync(new SearchLimits { Depth=4 });
        var result = await engine.SearchAsync(new SearchLimits { Depth=4 });
        Assert.IsGreaterThanOrEqualTo(result.Depth,result.Pv.Length);
        var position = new Position();
        foreach (var move in result.Pv) position.MakeMove(Move.Parse(position,move.ToString()));
    }

    [TestMethod]
    public async Task FiftyMoveContextDoesNotPoisonFreshPositionScores()
    {
        const string board = "7k/8/8/8/8/8/R7/4K3 w - - ";
        var warm = new Engine(); warm.SetPosition(board+"98 1");
        await warm.SearchAsync(new SearchLimits { Depth=4 });
        warm.SetPosition(board+"0 1");
        var actual = await warm.SearchAsync(new SearchLimits { Depth=4 });
        var fresh = new Engine(); fresh.SetPosition(board+"0 1");
        var expected = await fresh.SearchAsync(new SearchLimits { Depth=4 });
        Assert.AreEqual(expected.Score,actual.Score);
    }

    public static IEnumerable<object[]> Mates()
    {
        // Twenty legal, distinct mating positions. Expected mate lengths are verified by search.
        string[] positions = [
            "7k/5Q2/6K1/8/8/8/8/8 w - - 0 1",
            "k7/2Q5/1K6/8/8/8/8/8 w - - 0 1",
            "8/8/8/8/8/6K1/5Q2/7k w - - 0 1",
            "8/8/8/8/8/1K6/2Q5/k7 w - - 0 1",
            "6k1/5ppp/8/8/8/8/5PPP/4R1K1 w - - 0 1",
            "1k6/ppp5/8/8/8/8/PPP5/1K1R4 w - - 0 1",
            "4r1k1/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1",
            "1k1r4/ppp5/8/8/8/8/PPP5/1K6 b - - 0 1",
            "7k/8/8/5K2/8/8/8/2Q5 w - - 0 1",
            "7k/8/8/5K2/8/8/2Q5/8 w - - 0 1",
            "7k/8/8/2Q2K2/8/8/8/8 w - - 0 1",
            "7k/8/5K2/8/8/8/8/2Q5 w - - 0 1",
            "7k/8/5K2/8/8/8/2Q5/8 w - - 0 1",
            "7k/8/5K2/8/8/2Q5/8/8 w - - 0 1",
            // Classic Chess Problems: https://talkchess.com/viewtopic.php?t=78478
            "6R1/2K5/8/8/5p2/6p1/1Q3Nk1/8 w - - 0 1",
            "8/P3P3/p7/Pk6/4N3/1K6/8/8 w - - 0 1",
            "8/7p/8/7k/5K2/8/6R1/8 w - - 0 1",
            "8/6P1/8/1K6/2R5/8/1k6/8 w - - 0 1",
            "4K3/5P2/2N1k3/8/4P3/3R4/6P1/8 w - - 0 1",
            "8/8/1Q3K2/3pN3/4kp2/8/8/8 w - - 0 1"
        ];
        for (int i=0;i<positions.Length;i++) yield return [positions[i],i<8 ? 29999 : i<14 ? 29997 : 29995];
    }

    [TestMethod, DynamicData(nameof(Mates))]
    public async Task FindsForcedMateWithinThreeMoves(string fen,int expectedScore)
    {
        var engine = new Engine(); engine.SetPosition(fen);
        var result = await engine.SearchAsync(new SearchLimits { Depth=6 });
        Assert.AreEqual(expectedScore,result.Score,fen);
        Assert.AreEqual(result.BestMove,Move.Parse(Fen.Parse(fen),result.BestMove.ToString()));
    }
}
