using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Zwischenzug.Engine.Tests;

[TestClass]
public sealed class BenchmarkSignatureTests
{
    [TestMethod]
    public async Task FixedSuiteMatchesCommittedSearchNodeSignature()
    {
        var result = await BenchmarkSuite.RunAsync();
        Assert.AreEqual(BenchmarkSuite.ExpectedDefaultNodes, result.TotalNodes,
            "Search behavior changed: investigate, then deliberately update the committed signature and README.");
    }
}
