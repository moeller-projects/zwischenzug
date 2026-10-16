using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;

namespace Zwischenzug.Lichess.Tests;

[TestClass]
public sealed class SessionTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task ReconnectSnapshotsReplayInitialSideAndNeverDuplicateSubmittedMoves(int color)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = new FakeClient();
        var settings = Options.Create(new LichessOptions { Username = "bot", SearchDepth = 3 });
        var session = new GameSession(client, settings, NullLogger<GameSession>.Instance);
        Task run = session.RunAsync("g", stop.Token);
        string fen = color == 0 ? Fen.StartPosition : Fen.StartPosition.Replace(" w ", " b ", StringComparison.Ordinal);
        GameEvent Full(string moves) => new() { Type = "gameFull", Id = "g", InitialFen = fen, White = new() { Id = color == 0 ? "bot" : "other" }, Black = new() { Id = color == 1 ? "bot" : "other" }, State = new() { Moves = moves, Status = "started", Wtime = 1000, Btime = 1000 } };
        await client.Games.SendAsync(Full(""), stop.Token);
        string first = await client.Moves.Reader.ReadAsync(stop.Token);
        var position = Fen.Parse(fen); position.MakeMove(Move.Parse(position, first));
        await client.Games.SendAsync(Full(""), stop.Token);
        Assert.AreEqual(1, client.MoveCount);
        await client.Games.SendAsync(new GameEvent { Type = "gameState", Moves = first, Status = "started", Wtime = 900, Btime = 900 }, stop.Token);
        Assert.AreEqual(1, client.MoveCount);
        Move[] replies = LegalMoves(position); string reply = replies[0].ToString(); position.MakeMove(replies[0]);
        string history = first + " " + reply;
        await client.Games.SendAsync(Full(history), stop.Token);
        string second = await client.Moves.Reader.ReadAsync(stop.Token);
        Assert.AreEqual(second, Move.Parse(position, second).ToString());
        await client.Games.SendAsync(new GameEvent { Type = "gameState", Moves = history, Status = "mate" }, stop.Token, waitForAck: false);
        await run.WaitAsync(stop.Token);
        Assert.AreEqual(2, client.MoveCount);
    }

    [TestMethod]
    public async Task FinishedStateCancelsSearchBeforeSubmittingAStaleMove()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var client = new FakeClient();
        var session = new GameSession(client, Options.Create(new LichessOptions { Username = "bot" }), NullLogger<GameSession>.Instance);
        Task run = session.RunAsync("g", stop.Token);
        await client.Games.SendAsync(new GameEvent { Type = "gameFull", Id = "g", InitialFen = "startpos", White = new() { Id = "bot" }, Black = new() { Id = "other" }, State = new() { Status = "started", Wtime = 100000, Btime = 100000 } }, stop.Token);
        await client.Games.SendAsync(new GameEvent { Type = "gameState", Status = "aborted" }, stop.Token, waitForAck: false);
        await run.WaitAsync(stop.Token);
        Assert.AreEqual(0, client.MoveCount);
    }

    [TestMethod]
    public async Task PendingChallengeReservesCapacityAndCancellationReleasesIt()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var client = new FakeClient();
        var runner = new BotRunner(client, Options.Create(new LichessOptions()), NullLogger<BotRunner>.Instance, NullLoggerFactory.Instance);
        Task run = runner.RunAsync(stop.Token);
        BotEvent Challenge(string id) => new() { Type = "challenge", Challenge = new() { Id = id, Variant = new() { Key = "standard" }, TimeControl = new() { Type = "clock" } } };
        await client.Events.SendAsync(Challenge("one"), stop.Token);
        Assert.AreEqual("one", await client.NextAcceptedAsync(stop.Token));
        await client.Events.SendAsync(Challenge("one"), stop.Token);
        await client.Events.SendAsync(Challenge("two"), stop.Token);
        Assert.AreEqual("two:later", await client.NextDeclinedAsync(stop.Token));
        Assert.HasCount(1, client.Accepted);
        await client.Events.SendAsync(new BotEvent { Type = "challengeCanceled", Challenge = new() { Id = "one" } }, stop.Token);
        await client.Events.SendAsync(Challenge("three"), stop.Token);
        Assert.AreEqual("three", await client.NextAcceptedAsync(stop.Token));
        CollectionAssert.AreEqual(new[] { "one", "three" }, client.Accepted);
        stop.Cancel(); await run;
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AmbiguousMoveDeliveryReconcilesSnapshotBeforeRetry(bool serverAccepted)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6)); var client = new FakeClient();
        client.Submit = _ => client.MoveCount == 1 ? Task.FromException(new HttpRequestException("ambiguous fixture response")) : Task.CompletedTask;
        var session = new GameSession(client, Options.Create(new LichessOptions { Username = "bot", SearchDepth = 3 }), NullLogger<GameSession>.Instance);
        Task run = session.RunAsync("g", stop.Token);
        GameEvent Full(string moves) => new() { Type = "gameFull", Id = "g", InitialFen = "startpos", White = new() { Id = "bot" }, Black = new() { Id = "other" }, State = new() { Moves = moves, Status = "started", Wtime = 1000, Btime = 1000 } };
        await client.Games.SendAsync(Full(""), stop.Token);
        string first = await client.Moves.Reader.ReadAsync(stop.Token);
        await client.Reopened.Task.WaitAsync(stop.Token);
        string history = serverAccepted ? first : "";
        await client.Games.SendAsync(Full(history), stop.Token);
        if (!serverAccepted) history = await client.Moves.Reader.ReadAsync(stop.Token);
        await client.Games.SendAsync(new GameEvent { Type = "gameState", Moves = history, Status = "aborted" }, stop.Token, waitForAck: false);
        await run.WaitAsync(stop.Token);
        Assert.AreEqual(serverAccepted ? 1 : 2, client.MoveCount);
    }

    [TestMethod]
    public async Task CanceledChallengeResponseDoesNotStopUnrelatedEventProcessing()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var client = new FakeClient();
        client.Accept = id => id == "bad" ? Task.FromException(new LichessApiException(System.Net.HttpStatusCode.NotFound, "canceled fixture challenge")) : Task.CompletedTask;
        var runner = new BotRunner(client, Options.Create(new LichessOptions()), NullLogger<BotRunner>.Instance, NullLoggerFactory.Instance);
        Task run = runner.RunAsync(stop.Token);
        BotEvent Offer(string id) => new() { Type = "challenge", Challenge = new() { Id = id, Variant = new() { Key = "standard" }, TimeControl = new() { Type = "clock" } } };
        await client.Events.SendAsync(Offer("bad"), stop.Token);
        Assert.AreEqual("bad", await client.NextAcceptedAsync(stop.Token));
        await client.Events.SendAsync(Offer("good"), stop.Token);
        Assert.AreEqual("good", await client.NextAcceptedAsync(stop.Token));
        CollectionAssert.AreEqual(new[] { "bad", "good" }, client.Accepted);
        Assert.HasCount(0, client.Declined);
        stop.Cancel(); await run;
    }

    [TestMethod]
    public async Task OutOfOrderGameStartReleasesOnlyMatchingChallengeReservation()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var client = new FakeClient();
        var runner = new BotRunner(client, Options.Create(new LichessOptions { MaxConcurrentGames = 2 }), NullLogger<BotRunner>.Instance, NullLoggerFactory.Instance);
        Task run = runner.RunAsync(stop.Token);
        BotEvent Offer(string id) => new() { Type = "challenge", Challenge = new() { Id = id, Variant = new() { Key = "standard" }, TimeControl = new() { Type = "clock" } } };
        await client.Events.SendAsync(Offer("a"), stop.Token);
        Assert.AreEqual("a", await client.NextAcceptedAsync(stop.Token));
        await client.Events.SendAsync(Offer("b"), stop.Token);
        Assert.AreEqual("b", await client.NextAcceptedAsync(stop.Token));
        await client.Events.SendAsync(new BotEvent { Type = "gameStart", Game = new() { GameId = "b" } }, stop.Token);
        await client.Events.SendAsync(new BotEvent { Type = "challengeCanceled", Challenge = new() { Id = "a" } }, stop.Token);
        await client.Events.SendAsync(Offer("c"), stop.Token);
        Assert.AreEqual("c", await client.NextAcceptedAsync(stop.Token));
        Assert.AreEqual(1, client.GameStreams);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, client.Accepted);
        Assert.HasCount(0, client.Declined);
        stop.Cancel(); await run;
    }

    [TestMethod]
    public async Task DelayedChallengeResponseCannotBlockAnAcceptedGameStart()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient { Accept=id => id=="slow" ? release.Task : Task.CompletedTask };
        var runner = new BotRunner(client,Options.Create(new LichessOptions { MaxConcurrentGames=2 }),NullLogger<BotRunner>.Instance,NullLoggerFactory.Instance);
        Task run = runner.RunAsync(stop.Token);
        BotEvent Offer(string id) => new() { Type="challenge",Challenge=new() { Id=id,Variant=new() { Key="standard" },TimeControl=new() { Type="clock" } } };
        try
        {
            await client.Events.SendAsync(Offer("accepted"),stop.Token);
            Assert.AreEqual("accepted", await client.NextAcceptedAsync(stop.Token));
            await client.Events.SendAsync(Offer("slow"),stop.Token);
            await client.Events.SendAsync(new BotEvent { Type="gameStart",Game=new() { GameId="accepted" } },stop.Token);
            Assert.AreEqual(1,client.GameStreams);
        }
        finally { release.TrySetResult(); stop.Cancel(); await run; }
    }

    [TestMethod]
    public async Task BackgroundChallengeAuthenticationFailureStopsTheBotVisibly()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var client = new FakeClient { Accept=_ => Task.FromException(new LichessApiException(System.Net.HttpStatusCode.Unauthorized,"fixture authentication failure")) };
        var runner = new BotRunner(client,Options.Create(new LichessOptions()),NullLogger<BotRunner>.Instance,NullLoggerFactory.Instance);
        Task run = runner.RunAsync(stop.Token);
        await client.Events.SendAsync(new BotEvent { Type="challenge",Challenge=new() { Id="bad",Variant=new() { Key="standard" },TimeControl=new() { Type="clock" } } },stop.Token,waitForAck:false);
        await Assert.ThrowsExactlyAsync<LichessApiException>(() => run.WaitAsync(stop.Token));
    }

    private static Move[] LegalMoves(Position p)
    {
        Span<Move> moves = stackalloc Move[256]; int count = MoveGenerator.Generate(p, moves); return moves[..count].ToArray();
    }
    private sealed class Feed<T>
    {
        private readonly Channel<(T Item, TaskCompletionSource Ack)> _items = Channel.CreateUnbounded<(T, TaskCompletionSource)>();
        public async Task SendAsync(T item, CancellationToken ct, bool waitForAck = true)
        {
            var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _items.Writer.WriteAsync((item, ack), ct); if (waitForAck) await ack.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<T> Read([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var (item, ack) in _items.Reader.ReadAllAsync(ct)) { yield return item; ack.TrySetResult(); }
        }
    }
    private sealed class FakeClient : ILichessClient
    {
        public Feed<GameEvent> Games { get; } = new(); public Feed<BotEvent> Events { get; } = new();
        public Channel<string> Moves { get; } = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _accepted = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _declined = Channel.CreateUnbounded<string>();
        private readonly List<string> _acceptedList = [];
        private readonly List<string> _declinedList = [];
        private readonly object _gate = new();
        public string[] Accepted { get { lock (_gate) return [.. _acceptedList]; } }
        public string[] Declined { get { lock (_gate) return [.. _declinedList]; } }
        public int MoveCount;
        public int GameStreams;
        public Func<string, Task>? Submit { get; set; }
        public Func<string, Task>? Accept { get; set; }
        public TaskCompletionSource Reopened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> NextAcceptedAsync(CancellationToken ct) => await _accepted.Reader.ReadAsync(ct);
        public async Task<string> NextDeclinedAsync(CancellationToken ct) => await _declined.Reader.ReadAsync(ct);
        public IAsyncEnumerable<BotEvent> StreamEventsAsync(CancellationToken ct = default) => Events.Read(ct);
        public IAsyncEnumerable<GameEvent> StreamGameAsync(string id, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref GameStreams) > 1) Reopened.TrySetResult();
            return Games.Read(ct);
        }
        public Task AcceptChallengeAsync(string id, CancellationToken ct = default)
        {
            lock (_gate) _acceptedList.Add(id);
            _accepted.Writer.TryWrite(id);
            return Accept?.Invoke(id) ?? Task.CompletedTask;
        }
        public Task DeclineChallengeAsync(string id, string reason, CancellationToken ct = default)
        {
            lock (_gate) _declinedList.Add(id + ":" + reason);
            _declined.Writer.TryWrite(id + ":" + reason);
            return Task.CompletedTask;
        }
        public Task MakeMoveAsync(string id, string move, CancellationToken ct = default) { Interlocked.Increment(ref MoveCount); Moves.Writer.TryWrite(move); return Submit?.Invoke(move) ?? Task.CompletedTask; }
    }
}
