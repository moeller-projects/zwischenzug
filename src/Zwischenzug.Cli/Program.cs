using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zwischenzug.Core;
using Zwischenzug.Engine;
using Zwischenzug.Uci;
using Zwischenzug.Lichess;

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
builder.Services.AddSingleton<IOptions<LichessOptions>>(_ => Options.Create(ReadLichessOptions(builder.Configuration)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<ILichessClient, LichessClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<BotRunner>();

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
var lichess = new Command("lichess", "Play using the Lichess Bot API.");
lichess.SetAction(async (_, ct) =>
{
    host.Services.GetRequiredService<IOptions<LichessOptions>>().Value.Validate();
    await host.Services.GetRequiredService<BotRunner>().RunAsync(ct).ConfigureAwait(false);
});


var root = new RootCommand("Zwischenzug chess engine");
root.Subcommands.Add(uci);
root.Subcommands.Add(perft);
root.Subcommands.Add(bench);
root.Subcommands.Add(lichess);
root.SetAction(async (_, cancellationToken) => await host.Services.GetRequiredService<UciHost>().RunAsync(cancellationToken));

return await root.Parse(args).InvokeAsync();

static LichessOptions ReadLichessOptions(IConfiguration configuration)
{
    int Number(string key, int fallback) => configuration["Lichess:" + key] is not string value ? fallback :
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number :
        throw new InvalidOperationException("Invalid integer Lichess setting: " + key);
    bool Toggle(string key, bool fallback) => configuration["Lichess:" + key] is not string value ? fallback :
        bool.TryParse(value, out bool enabled) ? enabled : throw new InvalidOperationException("Invalid boolean Lichess setting: " + key);
    string[] allowed = configuration.GetSection("Lichess:AllowedTimeControls").GetChildren()
        .Select(child => child.Value ?? "").ToArray();
    return new LichessOptions
    {
        Token = configuration["Lichess:Token"] ?? "",
        Username = configuration["Lichess:Username"] ?? "",
        BaseAddress = new Uri(configuration["Lichess:BaseAddress"] ?? "https://lichess.org/"),
        MaxConcurrentGames = Number("MaxConcurrentGames", 1),
        SearchDepth = Number("SearchDepth", 64),
        MoveOverhead = Number("MoveOverhead", 50),
        AllowRated = Toggle("AllowRated", true),
        AllowCasual = Toggle("AllowCasual", true),
        AllowedTimeControls = allowed.Length == 0 ? ["clock"] : allowed
    };
}
