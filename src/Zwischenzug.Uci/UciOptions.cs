namespace Zwischenzug.Uci;

public sealed class UciOptions
{
    /// <summary>Transposition-table size in megabytes.</summary>
    public int Hash { get; private set; } = 16;
    /// <summary>Number of independent search workers.</summary>
    public int Threads { get; private set; } = 1;
    /// <summary>Time reserved for the GUI to receive and play a move.</summary>
    public int MoveOverhead { get; private set; } = 10;
    /// <summary>Number of root alternatives to report.</summary>
    public int MultiPv { get; private set; } = 1;
    /// <summary>Whether info lines include empirical win/draw/loss estimates.</summary>
    public bool ShowWdl { get; private set; }
    /// <summary>Whether the GUI has enabled ponder searches.</summary>
    public bool Ponder { get; private set; }

    /// <summary>Validates and applies one advertised UCI option, returning false for unknown or invalid input.</summary>
    public bool TrySet(string name, string? value)
    {
        if (value is null) return false;
        if (name.Equals("UCI_ShowWDL", StringComparison.OrdinalIgnoreCase))
        {
            if (!bool.TryParse(value, out bool showWdl)) return false;
            ShowWdl = showWdl;
            return true;
        }
        if (name.Equals("Ponder", StringComparison.OrdinalIgnoreCase))
        {
            if (!bool.TryParse(value, out bool ponder)) return false;
            Ponder = ponder;
            return true;
        }
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number)) return false;
        if (name.Equals("Hash", StringComparison.OrdinalIgnoreCase) && number is >= 1 and <= 1024) { Hash = number; return true; }
        if (name.Equals("Threads", StringComparison.OrdinalIgnoreCase) && number is >= 1 and <= 128) { Threads = number; return true; }
        if (name.Equals("Move Overhead", StringComparison.OrdinalIgnoreCase) && number is >= 0 and <= 5000) { MoveOverhead = number; return true; }
        if (name.Equals("MultiPV", StringComparison.OrdinalIgnoreCase) && number is >= 1 and <= 256) { MultiPv = number; return true; }
        return false;
    }
}
