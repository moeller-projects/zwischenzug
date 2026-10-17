using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;

namespace Zwischenzug.Lichess.Tests;

[TestClass]
public sealed class FixtureIntegrationTests
{
    [TestMethod]
    public async Task HttpFixturesAcceptDeclinePlayDeduplicateAndFinishWithoutLeakingToken()
    {
        // Deliberately fake sentinel; never use an actual credential in a fixture.
        const string fakeCredential = "fixture-only-not-a-real-credential";
        var settings = Options.Create(new LichessOptions { Token = fakeCredential, Username = "zwischenzug", SearchDepth = 3 });
        var logs = new CaptureLogs(); using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs));
        using var handler = new FixtureHandler(); using var http = new HttpClient(handler) { BaseAddress = new Uri("https://lichess.org/") };
        var client = new LichessClient(http, settings, loggerFactory.CreateLogger<LichessClient>());
        var runner = new BotRunner(client, settings, loggerFactory.CreateLogger<BotRunner>(), loggerFactory);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = runner.RunAsync(stop.Token);
        handler.Events.Send(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "events.ndjson"), stop.Token));
        handler.Game.Send(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "game.ndjson"), stop.Token));
        string move = await handler.Move.Task.WaitAsync(stop.Token);
        await handler.Accepted.Task.WaitAsync(stop.Token);
        await handler.Declined.Task.WaitAsync(stop.Token);
        Assert.AreEqual(move, Move.Parse(new Position(), move).ToString());
        Assert.IsTrue(handler.Requests.Contains("/api/challenge/fixture-game/accept"));
        Assert.AreEqual("reason=variant", handler.Declines["/api/challenge/nonstandard/decline"]);
        handler.Game.Send("{\"type\":\"gameState\",\"moves\":\"" + move + "\",\"status\":\"started\",\"wtime\":900,\"btime\":1000,\"winc\":10,\"binc\":10}\n");
        handler.Game.Send("{\"type\":\"gameState\",\"moves\":\"" + move + "\",\"status\":\"mate\",\"wtime\":900,\"btime\":1000}\n");
        handler.Events.Send("{\"type\":\"gameFinish\",\"game\":{\"id\":\"fixture-game\"}}\n");
        stop.Cancel();
        try { await running; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Assert.AreEqual(1, handler.MoveCount);
        Assert.IsFalse(string.Join('\n', logs.Messages).Contains(fakeCredential, StringComparison.Ordinal));
        Assert.IsTrue(handler.SawAuthorization);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public PushStream Events { get; } = new();
        public PushStream Game { get; } = new();
        public TaskCompletionSource<string> Move { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<string> Requests { get; } = [];
        public TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Declined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, string> Declines { get; } = new();
        public int MoveCount;
        public bool SawAuthorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SawAuthorization |= request.Headers.Authorization?.Scheme == "Bearer";
            string path = request.RequestUri!.AbsolutePath; Requests.Add(path);
            if (path == "/api/stream/event") return new(HttpStatusCode.OK) { Content = new StreamContent(Events) };
            if (path == "/api/challenge/fixture-game/accept") Accepted.TrySetResult();
            if (path == "/api/bot/game/stream/fixture-game") return new(HttpStatusCode.OK) { Content = new StreamContent(Game) };
            if (path.EndsWith("/decline", StringComparison.Ordinal)) { Declines[path] = await request.Content!.ReadAsStringAsync(ct); Declined.TrySetResult(); }
            if (path.Contains("/move/", StringComparison.Ordinal)) { Interlocked.Increment(ref MoveCount); Move.TrySetResult(path[(path.LastIndexOf('/') + 1)..]); }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class PushStream : Stream
    {
        private readonly Channel<byte[]> _Chunks = Channel.CreateUnbounded<byte[]>();
        private byte[]? _Current; private int _Offset;
        public void Send(string text) => _Chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_Current is null || _Offset == _Current.Length) { _Current = await _Chunks.Reader.ReadAsync(ct); _Offset = 0; }
            int count = Math.Min(buffer.Length, _Current.Length - _Offset);
            _Current.AsMemory(_Offset, count).CopyTo(buffer); _Offset += count; return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
