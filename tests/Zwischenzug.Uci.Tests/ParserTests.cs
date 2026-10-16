using Zwischenzug.Core;
using Zwischenzug.Engine;
using Zwischenzug.Uci;

namespace Zwischenzug.Uci.Tests;

[TestClass]
public sealed class ParserTests
{
    [TestMethod]
    public void ParseGoReadsAllLimitsInvariantly()
    {
        var command = UciCommandParser.Parse("  go   depth 7 nodes 123 movetime 50 wtime 1000 btime 2000 winc 10 binc 20 movestogo 12 infinite ") as UciCommand.Go;
        Assert.IsNotNull(command);
        Assert.AreEqual(7, command.Limits.Depth);
        Assert.AreEqual(123, command.Limits.Nodes);
        Assert.AreEqual(50, command.Limits.MoveTime);
        Assert.AreEqual(1000, command.Limits.WhiteTime);
        Assert.AreEqual(2000, command.Limits.BlackTime);
        Assert.AreEqual(10, command.Limits.WhiteIncrement);
        Assert.AreEqual(20, command.Limits.BlackIncrement);
        Assert.AreEqual(12, command.Limits.MovesToGo);
        Assert.IsTrue(command.Limits.Infinite);
    }

    [TestMethod]
    public void ParseMalformedNumericArgumentReturnsNull()
    {
        Assert.IsNull(UciCommandParser.Parse("go depth nope"));
        Assert.IsNull(UciCommandParser.Parse("go nodes -1"));
        Assert.IsNull(UciCommandParser.Parse("go movetime 20ms"));
    }

    [TestMethod]
    public void ParsePositionSupportsFourFieldFenAndMoves()
    {
        var command = UciCommandParser.Parse("position fen 8/8/8/8/8/8/4K3/4k3 w - - moves e2e3") as UciCommand.Position;
        Assert.IsNotNull(command);
        Assert.AreEqual("8/8/8/8/8/8/4K3/4k3 w - -", command.Fen);
        CollectionAssert.AreEqual(new[] { "e2e3" }, command.Moves);
    }

    [TestMethod]
    public void FormatInfoUsesSignedMateDistanceAndInvariantProtocolFields()
    {
        var result = new SearchResult(new Move(0, 8), 29999, 5, 6, 1000, 100, 42, [new Move(0, 8)], default);
        string info = UciResponseFormatter.Info(result);
        StringAssert.Contains(info, "score mate 1");
        StringAssert.Contains(info, "nps 10000");
        StringAssert.Contains(info, "pv a1a2");
    }

}
