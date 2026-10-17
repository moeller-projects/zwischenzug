namespace Zwischenzug.Lichess;

public sealed class LichessOptions
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public Uri BaseAddress { get; set; } = new("https://lichess.org/");
    public int MaxConcurrentGames { get; set; } = 1;
    public bool AllowRated { get; set; } = true;
    public bool AllowCasual { get; set; } = true;
    public string[] AllowedTimeControls { get; set; } = ["clock"];
    public int SearchDepth { get; set; } = 64;
    public int MoveOverhead { get; set; } = 50;

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
