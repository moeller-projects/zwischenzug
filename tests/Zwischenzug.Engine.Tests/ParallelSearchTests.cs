using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;

namespace Zwischenzug.Engine.Tests;

[TestClass]
public sealed class ParallelSearchTests
{
    [TestMethod]
    public async Task ParallelWorkersPreserveThreefoldHistory()
    {
        var engine = new Engine();
        engine.SetPosition(
            "4k3/8/8/8/8/8/8/Q3K3 w - - 0 1",
            ["e1d1", "e8d8", "d1e1", "d8e8", "e1d1", "e8d8", "d1e1", "d8e8"]);

        SearchResult result = await engine.SearchAsync(new SearchLimits { Depth = 4, Threads = 3 });

        Assert.AreEqual(0, result.Score);
        Assert.AreEqual(result.BestMove, Move.Parse(Fen.Parse("4k3/8/8/8/8/8/8/Q3K3 w - - 0 1"), result.BestMove.ToString()));
    }

    [TestMethod]
    public async Task CancellationJoinsEveryWorkerBeforePositionMutation()
    {
        var engine = new Engine();
        using var cancellation = new CancellationTokenSource();
        Task<SearchResult> search = engine.SearchAsync(new SearchLimits { Infinite = true, Threads = 4 }, ct: cancellation.Token);
        cancellation.CancelAfter(40);

        SearchResult result = await search.WaitAsync(TimeSpan.FromSeconds(5));
        engine.SetPosition("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        Assert.AreEqual(result.BestMove, Move.Parse(new Position(), result.BestMove.ToString()));
        Assert.IsGreaterThanOrEqualTo(1, result.Depth);
    }
}
