using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zwischenzug.Lichess;

/// <summary>Token-safe API failure carrying an HTTP status without response-body content.</summary>
public sealed class LichessApiException(HttpStatusCode statusCode, string message) : HttpRequestException(message, null, statusCode);

public sealed class LichessClient : ILichessClient
{
    private readonly HttpClient _Http;
    private readonly LichessOptions _Options;
    private readonly TimeProvider _Time;

    /// <summary>Creates a typed API client and validates its endpoint and credentials once.</summary>
    public LichessClient(HttpClient httpClient, IOptions<LichessOptions> options, ILogger<LichessClient> logger, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _Http = httpClient; _Options = options.Value; _Options.Validate();
        _Time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Opens the account event stream and reconnects after transient transport failures.</summary>
    public IAsyncEnumerable<BotEvent> StreamEventsAsync(CancellationToken ct = default) => StreamAsync("/api/stream/event", LichessJsonContext.Default.BotEvent, ct);
    /// <summary>Opens a game NDJSON stream for one escaped game identifier.</summary>
    public IAsyncEnumerable<GameEvent> StreamGameAsync(string gameId, CancellationToken ct = default) => StreamAsync($"/api/bot/game/stream/{Escape(gameId)}", LichessJsonContext.Default.GameEvent, ct);
    /// <summary>Accepts the challenge identified by its exact Lichess ID.</summary>
    public Task AcceptChallengeAsync(string id, CancellationToken ct = default) => PostAsync($"/api/challenge/{Escape(id)}/accept", null, ct);
    /// <summary>Declines one challenge with the supplied API reason code.</summary>
    public Task DeclineChallengeAsync(string id, string reason, CancellationToken ct = default) => PostAsync($"/api/challenge/{Escape(id)}/decline", reason, ct);
    /// <summary>Submits one UCI move for a game; ambiguous failures are returned for session reconciliation.</summary>
    public Task MakeMoveAsync(string gameId, string move, CancellationToken ct = default) => PostAsync($"/api/bot/game/{Escape(gameId)}/move/{Escape(move)}", null, ct);

    /// <summary>Reads and deserializes newline-delimited events, reconnecting with backoff until cancellation.</summary>
    private async IAsyncEnumerable<T> StreamAsync<T>(string path, JsonTypeInfo<T> json, [EnumeratorCancellation] CancellationToken ct)
    {
        int failures = 0;
        while (true)
        {
            bool delivered = false;
            using (var response = await OpenStreamAsync(path, ct).ConfigureAwait(false))
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                while (true)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync(ct).ConfigureAwait(false); }
                    catch (IOException) when (!ct.IsCancellationRequested) { break; }
                    catch (HttpRequestException) when (!ct.IsCancellationRequested) { break; }
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    delivered = true;
                    yield return JsonSerializer.Deserialize(line, json) ?? throw new JsonException("Lichess returned a null event.");
                }
            }
            failures = delivered ? 1 : failures + 1;
            await DelayAsync(Backoff(failures), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Retries transient stream-opening failures, but surfaces permanent HTTP errors.</summary>
    private async Task<HttpResponseMessage> OpenStreamAsync(string path, CancellationToken ct)
    {
        int failures = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            try
            {
                using var request = Request(HttpMethod.Get, path);
                response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (!ct.IsCancellationRequested)
            {
                await DelayAsync(Backoff(++failures), ct).ConfigureAwait(false); continue;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                response.Dispose(); await DelayAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false); continue;
            }
            if ((int)response.StatusCode >= 500)
            {
                response.Dispose(); await DelayAsync(Backoff(++failures), ct).ConfigureAwait(false); continue;
            }
            try { EnsureSuccess(response); return response; }
            catch { response.Dispose(); throw; }
        }
    }

    /// <summary>Sends a POST and retries only explicit rate limiting; other failures may have ambiguous outcomes.</summary>
    private async Task PostAsync(string path, string? reason, CancellationToken ct)
    {
        while (true)
        {
            using var request = Request(HttpMethod.Post, path);
            if (reason is not null) request.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("reason", reason)]);
            using var response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await DelayAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false); continue;
            }
            // No retry on an ambiguous move failure: the stream is authoritative.
            EnsureSuccess(response); return;
        }
    }

    /// <summary>Creates a request with a per-request bearer header to avoid shared mutable auth state.</summary>
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_Options.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _Options.Token);
        return request;
    }
    /// <summary>Validates and URL-escapes an identifier before placing it in a path segment.</summary>
    private static string Escape(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value); return Uri.EscapeDataString(value); }
    /// <summary>Waits with the injected clock so rate-limit and backoff tests can advance time deterministically.</summary>
    private Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, _Time, ct);
    /// <summary>Returns exponential reconnect delay capped at 30 seconds.</summary>
    private static TimeSpan Backoff(int failures) => TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Clamp(failures - 1, 0, 5)));
    /// <summary>Throws a token-safe exception for non-success responses without reading sensitive response content.</summary>
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "Lichess authentication failed; verify bot:play permission and account setup."
            : $"Lichess request failed with HTTP {(int)response.StatusCode}.";
        throw new LichessApiException(response.StatusCode, message);
    }
}
