using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;
using Zwischenzug.Engine;
using ChessEngine = Zwischenzug.Engine.Engine;

namespace Zwischenzug.Uci.Tests;

[TestClass]
public sealed class UciHostTests
{
    [TestMethod]
    public async Task HandshakeAndReadyTranscriptIsExact()
    {
        var output = new StringWriter();
        var host = new UciHost(new StringReader("uci\nisready\nquit\n"), output, new ChessEngine(), NullLogger<UciHost>.Instance);
        await host.RunAsync();
        string[] expected = ["id name Zwischenzug", "id author MoellerProjects", "option name Hash type spin default 16 min 1 max 1024", "option name Threads type spin default 1 min 1 max 128", "option name MultiPV type spin default 1 min 1 max 256", "option name UCI_ShowWDL type check default false", "option name Ponder type check default false", "option name Move Overhead type spin default 10 min 0 max 5000", "uciok", "readyok"];
        CollectionAssert.AreEqual(expected, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    public async Task TerminalPositionSendsNullBestmove()
    {
        var output = new StringWriter();
        var host = new UciHost(new StringReader("position fen 7k/6Q1/5K2/8/8/8/8/8 b - - 0 1\ngo depth 4\n"), output, new ChessEngine(), NullLogger<UciHost>.Instance);
        await host.RunAsync();
        Assert.AreEqual("bestmove 0000" + Environment.NewLine, output.ToString());
    }

    [TestMethod]
    public async Task StopAndReadyRemainResponsiveDuringInfiniteSearch()
    {
        var input = new ScriptReader(); var output = new LineWriter();
        var host = new UciHost(input, output, new ChessEngine(), NullLogger<UciHost>.Instance);
        Task run = host.RunAsync();
        input.Send("position startpos moves e2e4 e7e5"); input.Send("go infinite");
        await output.Until("info depth 2");
        input.Send("isready"); await output.Until("readyok");
        var watch = Stopwatch.StartNew(); input.Send("stop");
        string line = await output.Until("bestmove ");
        Assert.IsLessThan(100L, watch.ElapsedMilliseconds);
        var p = new Position(); p.MakeMove(Move.Parse(p, "e2e4")); p.MakeMove(Move.Parse(p, "e7e5"));
        Assert.AreEqual(line[9..], Move.Parse(p, line[9..]).ToString());
        input.Send("quit"); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ThreadsOptionRunsConcurrentSearchWorkers()
    {
        if (Environment.ProcessorCount == 1) Assert.Inconclusive("Parallel scheduling requires more than one logical processor.");
        var input = new ScriptReader();
        var output = new LineWriter();
        var evaluator = new ParallelProbeEvaluator();
        var host = new UciHost(input, output, new ChessEngine(evaluator: evaluator), NullLogger<UciHost>.Instance);
        Task run = host.RunAsync();
        input.Send("setoption name Threads value 3");
        input.Send("go depth 4");
        await output.Until("bestmove ");

        Assert.IsGreaterThan(1, evaluator.MaximumConcurrency);
        input.Send("quit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task MultiPvAndWdlOptionsEmitDistinctIndexedLines()
    {
        var input = new ScriptReader();
        var output = new LineWriter();
        var host = new UciHost(input, output, new ChessEngine(), NullLogger<UciHost>.Instance);
        Task run = host.RunAsync();
        input.Send("setoption name MultiPV value 3");
        input.Send("setoption name UCI_ShowWDL value true");
        input.Send("position startpos");
        input.Send("go depth 2");
        await output.Until("bestmove ");

        string[] lines = output.History.Where(line => line.StartsWith("info depth 2 ", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(3, lines);
        var indexes = new HashSet<int>();
        foreach (string line in lines)
        {
            string[] fields = line.Split(' ');
            int multiPv = Array.IndexOf(fields, "multipv");
            int wdl = Array.IndexOf(fields, "wdl");
            Assert.IsGreaterThanOrEqualTo(0, multiPv);
            Assert.IsGreaterThanOrEqualTo(0, wdl);
            Assert.IsTrue(indexes.Add(int.Parse(fields[multiPv + 1], System.Globalization.CultureInfo.InvariantCulture)));
            int win = int.Parse(fields[wdl + 1], System.Globalization.CultureInfo.InvariantCulture);
            int draw = int.Parse(fields[wdl + 2], System.Globalization.CultureInfo.InvariantCulture);
            int loss = int.Parse(fields[wdl + 3], System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(1000, win + draw + loss);
        }

        input.Send("quit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task PonderSearchWithholdsBestmoveUntilPonderhit()
    {
        var input = new ScriptReader();
        var output = new LineWriter();
        var host = new UciHost(input, output, new ChessEngine(), NullLogger<UciHost>.Instance);
        Task run = host.RunAsync();
        input.Send("setoption name Ponder value true");
        input.Send("position startpos");
        input.Send("go ponder depth 1");
        await output.Until("info depth 1 ");

        Assert.IsFalse(await output.TryUntil("bestmove ", TimeSpan.FromMilliseconds(100)));
        input.Send("ponderhit");
        await output.Until("bestmove ");
        input.Send("quit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task MalformedCommandsDoNotCorruptPositionOrStdout()
    {
        var original = Console.Out; var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var host = new UciHost(new StringReader("uci\nsetoption name Hash value rubbish\nposition fen broken\nposition startpos moves e2e5\nposition startpos\ngo depth 1\n"), Console.Out, new ChessEngine(), NullLogger<UciHost>.Instance);
            await host.RunAsync();
            foreach (string line in output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                Assert.IsTrue(line.StartsWith("id ", StringComparison.Ordinal) || line.StartsWith("option ", StringComparison.Ordinal) || line is "uciok" || line.StartsWith("info ", StringComparison.Ordinal) || line.StartsWith("bestmove ", StringComparison.Ordinal), line);
            Assert.IsTrue(output.ToString().Contains("bestmove ", StringComparison.Ordinal));
        }
        finally { Console.SetOut(original); }
    }

    [TestMethod]
    public async Task BackgroundSearchFaultTerminatesWithoutWaitingForAnotherCommand()
    {
        var input = new ScriptReader(); var output = new StringWriter();
        var host = new UciHost(input, output, new ChessEngine(evaluator: new BrokenEvaluator()), NullLogger<UciHost>.Instance);
        Task run = host.RunAsync();
        input.Send("go infinite");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(string.Empty, output.ToString());
    }

    private sealed class BrokenEvaluator : IEvaluator
    {
        public int Evaluate(in Position pos, int ply) => throw new InvalidOperationException("Injected evaluator failure");
    }

    private sealed class ScriptReader : TextReader
    {
        private readonly Channel<string> _Lines = Channel.CreateUnbounded<string>();
        public void Send(string line) => _Lines.Writer.TryWrite(line);
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => await _Lines.Reader.ReadAsync(cancellationToken);
    }

    private sealed class LineWriter : TextWriter
    {
        private readonly Channel<string> _Lines = Channel.CreateUnbounded<string>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _History = new();
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public string[] History => _History.ToArray();
        public override void WriteLine(string? value)
        {
            string line = value ?? "";
            _History.Enqueue(line);
            _Lines.Writer.TryWrite(line);
        }
        public async Task<string> Until(string prefix)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true) { string line = await _Lines.Reader.ReadAsync(timeout.Token); if (line.StartsWith(prefix, StringComparison.Ordinal)) return line; }
        }
        public async Task<bool> TryUntil(string prefix, TimeSpan duration)
        {
            using var timeout = new CancellationTokenSource(duration);
            try
            {
                while (true) { string line = await _Lines.Reader.ReadAsync(timeout.Token); if (line.StartsWith(prefix, StringComparison.Ordinal)) return true; }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return false; }
        }
    }
    private sealed class ParallelProbeEvaluator(ParallelProbeState? state = null) : IEvaluator, IWorkerEvaluatorFactory
    {
        private readonly ParallelProbeState _State = state ?? new ParallelProbeState();

        public int Evaluate(in Position pos, int ply)
        {
            int active = Interlocked.Increment(ref _State.Active);
            int current;
            while (active > (current = Volatile.Read(ref _State.Maximum)) &&
                   Interlocked.CompareExchange(ref _State.Maximum, active, current) != current) { }
            Thread.SpinWait(5_000);
            Interlocked.Decrement(ref _State.Active);
            return 0;
        }

        public IEvaluator CreateWorkerEvaluator() => new ParallelProbeEvaluator(_State);
        public int MaximumConcurrency => Volatile.Read(ref _State.Maximum);
    }

    private sealed class ParallelProbeState
    {
        public int Active;
        public int Maximum;
    }

}
