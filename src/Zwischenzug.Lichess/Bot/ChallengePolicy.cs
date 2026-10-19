using Microsoft.Extensions.Options;

namespace Zwischenzug.Lichess;

public sealed class ChallengePolicy(IOptions<LichessOptions> options)
{
    private readonly LichessOptions _Options = options.Value;

    /// <summary>Returns the Lichess decline code for a rejected challenge, or null when policy allows it.</summary>
    public string? DeclineReason(Challenge challenge)
    {
        if (!string.Equals(challenge.Variant?.Key, "standard", StringComparison.OrdinalIgnoreCase))
            return "variant";

        string type = challenge.TimeControl?.Type ?? string.Empty;
        string[] allowed = _Options.AllowedTimeControls ?? Array.Empty<string>();
        bool timeAllowed = false;
        foreach (string value in allowed)
            if (string.Equals(value, type, StringComparison.OrdinalIgnoreCase)) { timeAllowed = true; break; }
        if (!timeAllowed) return "timeControl";
        if (challenge.Rated && !_Options.AllowRated) return "rated";
        if (!challenge.Rated && !_Options.AllowCasual) return "casual";
        return null;
    }
}
