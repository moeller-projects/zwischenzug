using Microsoft.Extensions.Options;

namespace Zwischenzug.Lichess;

public sealed class ChallengePolicy(IOptions<LichessOptions> options)
{
    private readonly LichessOptions _options = options.Value;

    public string? DeclineReason(Challenge challenge)
    {
        if (!string.Equals(challenge.Variant?.Key, "standard", StringComparison.OrdinalIgnoreCase))
            return "variant";

        string type = challenge.TimeControl?.Type ?? string.Empty;
        string[] allowed = _options.AllowedTimeControls ?? Array.Empty<string>();
        bool timeAllowed = false;
        foreach (string value in allowed)
            if (string.Equals(value, type, StringComparison.OrdinalIgnoreCase)) { timeAllowed = true; break; }
        if (!timeAllowed) return "timeControl";
        if (challenge.Rated && !_options.AllowRated) return "rated";
        if (!challenge.Rated && !_options.AllowCasual) return "casual";
        return null;
    }
}
