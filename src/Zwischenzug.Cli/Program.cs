using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zwischenzug.Core;
using Zwischenzug.Engine;
using Zwischenzug.Uci;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<Engine>();
builder.Services.AddSingleton(sp => new UciHost(
    Console.In,
    Console.Out,
    sp.GetRequiredService<Engine>(),
    sp.GetRequiredService<ILogger<UciHost>>()));

using IHost host = builder.Build();

var uci = new Command("uci", "Run the UCI protocol on stdin/stdout.");
uci.SetAction(async (_, cancellationToken) => await host.Services.GetRequiredService<UciHost>().RunAsync(cancellationToken));

var perft = new Command("perft", "Count legal move sequences.");
var perftDepth = new Argument<int>("depth");
var fenOption = new Option<string?>("--fen") { Description = "Position in Forsyth-Edwards Notation." };
var divideOption = new Option<bool>("--divide") { Description = "Print counts for each root move." };
perft.Arguments.Add(perftDepth);
perft.Options.Add(fenOption);
perft.Options.Add(divideOption);
perft.SetAction(parseResult =>
{
    int depth = parseResult.GetValue(perftDepth);
    string fen = parseResult.GetValue(fenOption) ?? Fen.StartPosition;
    var position = Fen.Parse(fen);
    long nodes;
    if (parseResult.GetValue(divideOption))
    {
        foreach (var (move, count) in Perft.Divide(position, depth))
            Console.WriteLine(FormattableString.Invariant($"{move} {count}"));
        nodes = Perft.Count(position, depth);
    }
    else
    {
        nodes = Perft.Count(position, depth);
    }
    Console.WriteLine(FormattableString.Invariant($"nodes {nodes}"));
});

var bench = new Command("bench", "Run the fixed deterministic search benchmark.");
var benchDepth = new Argument<int>("depth") { DefaultValueFactory = _ => BenchmarkSuite.DefaultDepth };
bench.Arguments.Add(benchDepth);
bench.SetAction(async parseResult =>
{
    BenchmarkResult result = await BenchmarkSuite.RunAsync(parseResult.GetValue(benchDepth)).ConfigureAwait(false);
    Console.WriteLine(FormattableString.Invariant($"nodes {result.TotalNodes} time {result.ElapsedMilliseconds} ms nps {result.Nps} ttprobes {result.TtProbes} tthits {result.TtHits}"));
});

var root = new RootCommand("Zwischenzug chess engine");
root.Subcommands.Add(uci);
root.Subcommands.Add(perft);
root.Subcommands.Add(bench);
root.SetAction(async (_, cancellationToken) => await host.Services.GetRequiredService<UciHost>().RunAsync(cancellationToken));

return await root.Parse(args).InvokeAsync();
