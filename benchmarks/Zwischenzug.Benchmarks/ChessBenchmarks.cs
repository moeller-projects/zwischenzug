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
    private Position[] _positions = [];
    private Position _start = null!;

    [GlobalSetup]
    public void Setup()
    {
        _positions = BenchmarkSuite.Positions.Select(Fen.Parse).ToArray();
        _start = new Position();
        Perft.Count(_start, 3);
    }

    [Benchmark]
    public int MoveGeneration()
    {
        Span<Move> moves = stackalloc Move[256];
        int count = 0;
        foreach (var position in _positions) count += MoveGenerator.Generate(position, moves);
        return count;
    }

    [Benchmark]
    public long PerftNodes() => Perft.Count(_start, 5);

    // Includes cold engine/position setup, deliberately matching the CLI regression bench.
    [Benchmark]
    public Task<BenchmarkResult> SearchNodes() => BenchmarkSuite.RunAsync();
}

public sealed class ChessBenchmarkConfig : ManualConfig
{
    public ChessBenchmarkConfig() => AddColumn(new EngineMetricColumn(false), new EngineMetricColumn(true));
}

internal sealed class EngineMetricColumn(bool hitRate) : IColumn
{
    private static readonly Lazy<BenchmarkResult> ReferenceSearch = new(() => BenchmarkSuite.RunAsync().GetAwaiter().GetResult());
    private static readonly Lazy<int> ReferenceMoves = new(() =>
    {
        int count = 0; Span<Move> moves = stackalloc Move[256];
        foreach (string fen in BenchmarkSuite.Positions) count += MoveGenerator.Generate(Fen.Parse(fen), moves);
        return count;
    });

    public string Id => hitRate ? "TtHitRate" : "EngineThroughput";
    public string ColumnName => hitRate ? "TT hit %" : "Throughput/s";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => hitRate ? 1 : 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => hitRate ? "TT hits / probes on the fixed depth-five suite" : "Legal moves/s for movegen; leaf nodes/s for perft; searched nodes/s for search";
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, summary.Style);
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
