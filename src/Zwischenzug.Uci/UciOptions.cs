namespace Zwischenzug.Uci;

public sealed class UciOptions
{
    public int Hash { get; private set; } = 16;
    public int Threads { get; private set; } = 1;
    public int MoveOverhead { get; private set; } = 10;

    public bool TrySet(string name, string? value)
    {
        if (value is null || !int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number)) return false;
        if (name.Equals("Hash", StringComparison.OrdinalIgnoreCase) && number is >= 1 and <= 1024) { Hash = number; return true; }
        if (name.Equals("Threads", StringComparison.OrdinalIgnoreCase) && number is >= 1 and <= 128) { Threads = number; return true; }
        if (name.Equals("Move Overhead", StringComparison.OrdinalIgnoreCase) && number is >= 0 and <= 5000) { MoveOverhead = number; return true; }
        return false;
    }
}
