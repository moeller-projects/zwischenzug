namespace Zwischenzug.Lichess;

public sealed class LichessOptions
{
    /// <summary>Bearer token; load through a secret store or environment variable, never source control.</summary>
    public string Token { get; set; } = string.Empty;
    /// <summary>Account identity used to recognize the bot's side in full game snapshots.</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>API origin; HTTPS is required except for loopback test servers.</summary>
    public Uri BaseAddress { get; set; } = new("https://lichess.org/");
    /// <summary>Maximum active games plus accepted challenges with unresolved starts.</summary>
    public int MaxConcurrentGames { get; set; } = 1;
    /// <summary>Whether rated challenges may be accepted.</summary>
    public bool AllowRated { get; set; } = true;
    /// <summary>Whether casual challenges may be accepted.</summary>
    public bool AllowCasual { get; set; } = true;
    /// <summary>Accepted API time-control categories; default permits clock games.</summary>
    public string[] AllowedTimeControls { get; set; } = ["clock"];
    /// <summary>Requested maximum depth for each game search, bounded by engine capacity.</summary>
    public int SearchDepth { get; set; } = 64;
    /// <summary>Milliseconds reserved for move delivery and server response.</summary>
    public int MoveOverhead { get; set; } = 50;

    /// <summary>Checks credentials, transport scheme, and configured limits before a bot starts.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException("Lichess:Token and Lichess:Username are required.");
        if (!BaseAddress.IsAbsoluteUri || BaseAddress.Scheme != Uri.UriSchemeHttps && !(BaseAddress.IsLoopback && BaseAddress.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("Lichess:BaseAddress must use HTTPS (HTTP is allowed only for loopback fixtures).");
        if (MaxConcurrentGames is < 1 or > 32 || SearchDepth is < 1 or > 94 || MoveOverhead is < 0 or > 5000)
            throw new InvalidOperationException("Invalid Lichess game, depth, or overhead limits.");
        if (!string.IsNullOrEmpty(BaseAddress.UserInfo) || !string.IsNullOrEmpty(BaseAddress.Query) || !string.IsNullOrEmpty(BaseAddress.Fragment))
            throw new InvalidOperationException("Lichess:BaseAddress must not contain credentials, a query, or a fragment.");
        if (AllowedTimeControls.Length == 0)
            throw new InvalidOperationException("At least one Lichess time control must be allowed.");
    }
}
