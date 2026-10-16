using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Engine.Tests;

[TestClass]
public sealed class TranspositionTests
{
    [TestMethod]
    public void StoresAndRetrievesEntryAndTracksStatistics()
    {
        var table = new TranspositionTable(1);
        Move move = new(12, 28);

        table.Store(0x1234UL, 7, 42, Bound.Exact, move, 3);

        Assert.IsTrue(table.TryGet(0x1234UL, 3, out TranspositionEntry entry));
        Assert.AreEqual(0x1234UL, entry.Key);
        Assert.AreEqual(7, entry.Depth);
        Assert.AreEqual(42, entry.Score);
        Assert.AreEqual(Bound.Exact, entry.Bound);
        Assert.AreEqual(move, entry.BestMove);
        Assert.IsTrue(table.Hashfull is >= 0 and <= 1000);
        Assert.AreEqual(1L, table.Hits);
    }

    [TestMethod]
    public void RejectsDifferentFullKeyEvenWhenItCollides()
    {
        var table = new TranspositionTable(1);
        const ulong first = 0;
        const ulong colliding = 8192;

        table.Store(first, 10, 1, Bound.Lower, default, 0);
        table.Store(colliding, 10, 2, Bound.Upper, default, 0);

        Assert.IsTrue(table.TryGet(first, 0, out TranspositionEntry firstEntry));
        Assert.AreEqual(1, firstEntry.Score);
        Assert.IsTrue(table.TryGet(colliding, 0, out TranspositionEntry collisionEntry));
        Assert.AreEqual(2, collisionEntry.Score);
    }

    [TestMethod]
    public void PrefersDeepEntriesWhenBucketIsFull()
    {
        var table = new TranspositionTable(1);
        const ulong bucket = 8192;
        for (ulong i = 0; i < 4; i++) table.Store(i * bucket, (int)i + 1, (int)i, Bound.Exact, default, 0);

        table.Store(4 * bucket, 20, 99, Bound.Exact, default, 0);

        Assert.IsTrue(table.TryGet(4 * bucket, 0, out _));
        Assert.IsTrue(table.TryGet(3 * bucket, 0, out _));
        Assert.IsFalse(table.TryGet(0, 0, out _));
    }

    [TestMethod]
    public void NewGenerationResetsStatisticsAndPrefersOldEntriesForReplacement()
    {
        var table = new TranspositionTable(1);
        const ulong bucket = 8192;
        for (ulong i = 0; i < 4; i++) table.Store(i * bucket, (int)i + 1, (int)i, Bound.Exact, default, 0);
        Assert.IsTrue(table.TryGet(0, 0, out _));

        table.NewGeneration();

        Assert.AreEqual(0L, table.Probes);
        Assert.AreEqual(0L, table.Hits);
        table.Store(4 * bucket, 1, 1, Bound.Exact, default, 0);
        Assert.IsTrue(table.TryGet(4 * bucket, 0, out _));
        Assert.IsFalse(table.TryGet(0, 0, out _));
    }

    [TestMethod]
    public void NormalizesMateDistanceAtStoreAndProbe()
    {
        var table = new TranspositionTable(1);

        table.Store(1, 8, 30000 - 5, Bound.Exact, default, 5);
        Assert.IsTrue(table.TryGet(1, 2, out TranspositionEntry positive));
        Assert.AreEqual(30000 - 2, positive.Score);

        table.Store(2, 8, -30000 + 5, Bound.Exact, default, 5);
        Assert.IsTrue(table.TryGet(2, 2, out TranspositionEntry negative));
        Assert.AreEqual(-30000 + 2, negative.Score);
    }

    [TestMethod]
    public void ResizeAndClearDiscardEntries()
    {
        var table = new TranspositionTable(1);
        table.Store(11, 4, 4, Bound.Exact, default, 0);
        table.Resize(2);
        Assert.IsFalse(table.TryGet(11, 0, out _));

        table.Store(12, 4, 4, Bound.Exact, default, 0);
        table.Clear();
        Assert.IsFalse(table.TryGet(12, 0, out _));
        Assert.AreEqual(0, table.Hashfull);
    }

    [TestMethod]
    public void ShallowerSameKeyDoesNotDestroyDeeperCurrentResult()
    {
        var table = new TranspositionTable(1);
        table.Store(99,8,123,Bound.Exact,new Move(1,18),0);
        table.Store(99,2,50,Bound.Lower,new Move(6,21),0);
        Assert.IsTrue(table.TryGet(99,0,out var entry));
        Assert.AreEqual(8,entry.Depth);
        Assert.AreEqual(123,entry.Score);
        Assert.AreEqual(new Move(1,18),entry.BestMove);
    }
}
