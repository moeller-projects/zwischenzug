using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zwischenzug.Lichess;

namespace Zwischenzug.Lichess.Tests;

[TestClass]
public sealed class ClientTests
{
    [TestMethod]
    public async Task StreamSkipsKeepAliveLinesAndDeserializesEvents()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("\n{\"type\":\"challenge\",\"challenge\":{\"id\":\"c1\",\"rated\":false}}\n")
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await using var enumerator = client.StreamEventsAsync().GetAsyncEnumerator();
        Assert.IsTrue(await enumerator.MoveNextAsync());
        Assert.AreEqual("challenge", enumerator.Current.Type);
        Assert.AreEqual("c1", enumerator.Current.Challenge!.Id);
    }

    [TestMethod]
    public async Task DeclineChallengeUsesFormAndEscapedPath()
    {
        HttpRequestMessage? captured = null;
        Task<string>? body = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.DeclineChallengeAsync("a/b", "standard");

        Assert.IsNotNull(captured);
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        StringAssert.Contains(captured.RequestUri!.AbsolutePath, "/api/challenge/a%2Fb/decline");
        Assert.AreEqual("Bearer test-token", captured.Headers.Authorization!.ToString());
        Assert.AreEqual("reason=standard", await body!);
    }

    [TestMethod]
    public async Task UnauthorizedResponseDoesNotExposeTokenInException()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<LichessApiException>(() => client.AcceptChallengeAsync("c1"));

        Assert.IsFalse(exception.Message.Contains("test-token", StringComparison.Ordinal));
    }

    private static LichessClient CreateClient(HttpClient httpClient) =>
        new(httpClient, Options.Create(new LichessOptions
        {
            Token = "test-token",
            Username = "bot",
            BaseAddress = new Uri("http://localhost/")
        }), NullLogger<LichessClient>.Instance);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
