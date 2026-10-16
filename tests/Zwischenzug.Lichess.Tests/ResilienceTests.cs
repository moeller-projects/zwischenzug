using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Zwischenzug.Lichess.Tests;

[TestClass]
public sealed class ResilienceTests
{
    [TestMethod]
    public async Task DroppedStreamReconnectsAndRateLimitWaitsSixtySeconds()
    {
        int calls = 0; var time = new ImmediateTime();
        using var handler = new Handler(_ => ++calls switch
        {
            1 => new(HttpStatusCode.OK) { Content = new StreamContent(new DroppedStream("{\"type\":\"gameStart\",\"game\":{\"id\":\"g\"}}\n")) },
            2 => throw new HttpRequestException("Fixture connection dropped"),
            3 => new(HttpStatusCode.TooManyRequests),
            _ => new(HttpStatusCode.OK) { Content = new StringContent("\n{\"type\":\"gameFinish\",\"game\":{\"id\":\"g\"}}\n") }
        });
        using var http = new HttpClient(handler);
        var client = Client(http, time);
        await using var events = client.StreamEventsAsync().GetAsyncEnumerator();
        Assert.IsTrue(await events.MoveNextAsync()); Assert.AreEqual("gameStart", events.Current.Type);
        Assert.IsTrue(await events.MoveNextAsync()); Assert.AreEqual("gameFinish", events.Current.Type);
        Assert.AreEqual(4, calls);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60) }, time.Delays.ToArray());
    }

    [TestMethod]
    public async Task ExplicitPostRateLimitRetriesButTransportFailureDoesNot()
    {
        int calls = 0; var time = new ImmediateTime();
        using var handler = new Handler(_ => ++calls == 1 ? new(HttpStatusCode.TooManyRequests) : new(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        await Client(http, time).MakeMoveAsync("g", "e2e4");
        Assert.AreEqual(2, calls); CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(60) }, time.Delays.ToArray());
        int failures = 0;
        using var broken = new HttpClient(new Handler(_ => { failures++; throw new HttpRequestException("ambiguous move delivery"); }));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => Client(broken, time).MakeMoveAsync("g", "e2e4"));
        Assert.AreEqual(1, failures);
    }

    [TestMethod]
    [DataRow("chess960", "clock", false, true, true, "variant")]
    [DataRow("standard", "correspondence", false, true, true, "timeControl")]
    [DataRow("standard", "clock", true, false, true, "rated")]
    [DataRow("standard", "clock", false, true, false, "casual")]
    public void PolicyUsesMatchingLichessDeclineCodes(string variant, string time, bool rated, bool allowRated, bool allowCasual, string expected)
    {
        var policy = new ChallengePolicy(Options.Create(new LichessOptions { AllowRated = allowRated, AllowCasual = allowCasual }));
        Assert.AreEqual(expected, policy.DeclineReason(new Challenge { Rated = rated, Variant = new() { Key = variant }, TimeControl = new() { Type = time } }));
    }

    private static LichessClient Client(HttpClient http, TimeProvider time) => new(http,
        Options.Create(new LichessOptions { Token = "fixture-only-fake-credential", Username = "bot" }), NullLogger<LichessClient>.Instance, time);

    private sealed class ImmediateTime : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(request));
    }
    private sealed class DroppedStream(string text) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Position == Length ? ValueTask.FromException<int>(new IOException("Fixture truncated stream")) : base.ReadAsync(buffer, ct);
    }
}
