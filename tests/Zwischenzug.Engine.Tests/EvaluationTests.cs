using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Engine.Tests;

[TestClass]
public sealed class EvaluationTests
{
    [TestMethod]
    public void MaterialIsSideToMovePerspectiveAndBlackMirrorsWhite()
    {
        var evaluator = new MaterialPstEvaluator();
        var white = Fen.Parse("4k3/8/8/8/8/8/4Q3/4K3 w - - 0 1");
        var black = Fen.Parse("4k3/8/8/8/8/8/4Q3/4K3 b - - 0 1");
        Assert.IsGreaterThan(0, evaluator.Evaluate(white, 0));
        Assert.IsLessThan(0, evaluator.Evaluate(black, 0));
        Assert.AreEqual(evaluator.Evaluate(white, 0), -evaluator.Evaluate(black, 0));
    }

    [TestMethod]
    public void InsufficientMaterialIsDrawAndPstIsVerticallySymmetric()
    {
        var evaluator = new MaterialPstEvaluator();
        Assert.AreEqual(0, evaluator.Evaluate(Fen.Parse("8/8/8/8/8/8/N7/4K2k w - - 0 1"), 0));
        var white = Fen.Parse("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1");
        var mirrored = Fen.Parse("4K3/4p3/8/8/8/8/8/4k3 b - - 0 1");
        Assert.AreEqual(evaluator.Evaluate(white, 0), evaluator.Evaluate(mirrored, 0));
    }

    [TestMethod]
    public void CustomParametersControlMaterialAndPhase()
    {
        var parameters = new EvalParams(materialValues: [0, 100, 300, 320, 500, 900, 0], phaseWeights: [0, 0, 0, 0, 0, 0, 0], middleGamePst: new int[384], endGamePst: new int[384]);
        var evaluator = new MaterialPstEvaluator(parameters);
        Assert.AreEqual(100, evaluator.Evaluate(Fen.Parse("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1"), 0));
    }

    [TestMethod]
    public void StaticExchangeScoresWinningAndLosingCaptures()
    {
        var winning = Fen.Parse("4k3/8/8/3p4/4Q3/8/8/4K3 w - - 0 1");
        Assert.IsGreaterThan(0, StaticExchange.Evaluate(winning, Move.Parse(winning, "e4d5")));
        var losing = Fen.Parse("4k3/8/2b5/3r4/4Q3/8/8/4K3 w - - 0 1");
        Assert.IsLessThan(0, StaticExchange.Evaluate(losing, Move.Parse(losing, "e4d5")));
    }

    [TestMethod]
    public void PinnedDefenderCannotRecaptureWinningQueenCapture()
    {
        var p = Fen.Parse("4k3/4n3/6p1/5Q2/8/8/8/K3R3 w - - 0 1");
        Assert.AreEqual(100, StaticExchange.Evaluate(p, Move.Parse(p, "f5g6")));
    }

    [TestMethod]
    public void TaperedPhaseNormalizesAgainstConfiguredWeights()
    {
        var middle = new int[384]; middle[65] = 120;
        var evaluator = new MaterialPstEvaluator(new EvalParams(new int[7], [0, 0, 10, 0, 0, 0, 0], middle, new int[384]));
        var p = Fen.Parse("4k3/8/8/8/8/8/P7/1N2K3 w - - 0 1");
        Assert.AreEqual(30, evaluator.Evaluate(p, 0));
    }
}
