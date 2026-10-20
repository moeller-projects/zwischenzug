using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(ChessBenchmarkConfig))]
public class ChessBenchmarks
{
    private Position[] _Positions = [];
    private Position _Start = null!;

    [GlobalSetup]
    /// <summary>Parses fixed positions and warms core attack tables before measured iterations.</summary>
    public void Setup()
    {
        _Positions = BenchmarkSuite.Positions.Select(Fen.Parse).ToArray();
        _Start = new Position();
        Perft.Count(_Start, 3);
    }

    [Benchmark]
    /// <summary>Measures legal move generation across the fixed suite using reused stack storage.</summary>
    public int MoveGeneration()
    {
        Span<Move> moves = stackalloc Move[256];
        int count = 0;
        foreach (var position in _Positions) count += MoveGenerator.Generate(position, moves);
        return count;
    }

    [Benchmark]
    /// <summary>Measures the known start-position perft-five traversal.</summary>
    public long PerftNodes() => Perft.Count(_Start, 5);

    // Includes cold engine/position setup, deliberately matching the CLI regression bench.
    [Benchmark]
    /// <summary>Measures the full fixed search suite, including engine and position setup.</summary>
    public Task<BenchmarkResult> SearchNodes() => BenchmarkSuite.RunAsync();
}

public sealed class ChessBenchmarkConfig : ManualConfig
{
    /// <summary>Adds throughput and transposition hit-rate columns to BenchmarkDotNet output.</summary>
    public ChessBenchmarkConfig() => AddColumn(new EngineMetricColumn(false), new EngineMetricColumn(true));
}

/// <summary>Displays benchmark throughput or fixed-suite transposition-table hit rate.</summary>
internal sealed class EngineMetricColumn(bool hitRate) : IColumn
{
    private static readonly Lazy<BenchmarkResult> ReferenceSearch = new(() => BenchmarkSuite.RunAsync().GetAwaiter().GetResult());
    private static readonly Lazy<int> ReferenceMoves = new(() =>
    {
        int count = 0; Span<Move> moves = stackalloc Move[256];
        foreach (string fen in BenchmarkSuite.Positions) count += MoveGenerator.Generate(Fen.Parse(fen), moves);
        return count;
    });

    /// <summary>Gets a stable column identifier for the selected supplemental measurement.</summary>
    public string Id => hitRate ? "TtHitRate" : "EngineThroughput";
    public string ColumnName => hitRate ? "TT hit %" : "Throughput/s";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => hitRate ? 1 : 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => hitRate ? "TT hits / probes on the fixed depth-five suite" : "Legal moves/s for movegen; leaf nodes/s for perft; searched nodes/s for search";
    /// <summary>Keeps custom measurements out of BenchmarkDotNet's default column set.</summary>
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    /// <summary>Always makes these columns available; inapplicable rows display a dash.</summary>
    public bool IsAvailable(Summary summary) => true;
    /// <summary>Delegates to the style-aware formatter with the summary's selected style.</summary>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, summary.Style);
    /// <summary>Computes the supplemental rate using each benchmark workload's matching reference units.</summary>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        string method = benchmarkCase.Descriptor.WorkloadMethod.Name;
        if (hitRate)
        {
            if (method != nameof(ChessBenchmarks.SearchNodes)) return "-";
            var reference = ReferenceSearch.Value;
            return (reference.TtProbes == 0 ? 0 : 100.0 * reference.TtHits / reference.TtProbes).ToString("F2", CultureInfo.InvariantCulture);
        }
        double? mean = summary[benchmarkCase]?.ResultStatistics?.Mean;
        if (mean is null or <= 0) return "-";
        long units = method switch
        {
            nameof(ChessBenchmarks.MoveGeneration) => ReferenceMoves.Value,
            nameof(ChessBenchmarks.PerftNodes) => 4865609,
            nameof(ChessBenchmarks.SearchNodes) => ReferenceSearch.Value.TotalNodes,
            _ => 0
        };
        return (units * 1_000_000_000.0 / mean.Value).ToString("F0", CultureInfo.InvariantCulture);
    }
}
