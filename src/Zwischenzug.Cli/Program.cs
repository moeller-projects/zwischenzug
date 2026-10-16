using System.Diagnostics;
using System.Globalization;
using Zwischenzug.Core;

if (args.Length >= 2 && args[0] == "perft")
{
    int depth = int.Parse(args[1], CultureInfo.InvariantCulture);
    string fen = args.Length >= 4 && args[2] == "--fen" ? args[3] : Fen.StartPosition;
    var position = Fen.Parse(fen);
    Perft.Count(position, Math.Min(depth, 3)); // Warm static tables and tiered code.
    var watch = Stopwatch.StartNew();
    long nodes = Perft.Count(position, depth);
    watch.Stop();
    Console.WriteLine(FormattableString.Invariant($"nodes {nodes} time {watch.Elapsed.TotalMilliseconds:F2} ms nps {(long)(nodes / Math.Max(watch.Elapsed.TotalSeconds, 0.001))}"));
    return;
}
Console.Error.WriteLine("Usage: perft <depth> [--fen <fen>]");
