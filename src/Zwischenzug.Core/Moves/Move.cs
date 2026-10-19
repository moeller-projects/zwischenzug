using System.Globalization;
namespace Zwischenzug.Core;

public readonly record struct Move
{
    /// <summary>Gets the packed 16-bit representation used to copy moves cheaply.</summary>
    public ushort Raw { get; }
    /// <summary>Creates a move from its source, destination, and special-move flag.</summary>
    public Move(int from, int to, int flag = 0) => Raw = (ushort)(from | (to << 6) | (flag << 12));
    /// <summary>Gets the source square (a1 is zero).</summary>
    public int From => Raw & 63;
    /// <summary>Gets the destination square (h8 is 63).</summary>
    public int To => (Raw >> 6) & 63;
    /// <summary>Gets the special-move flag encoded above the two square numbers.</summary>
    public int Flag => Raw >> 12;
    /// <summary>Reports captures, including en passant and capture promotions.</summary>
    public bool IsCapture => Flag is 3 or 4 or >= 12;
    /// <summary>Gets the promoted piece kind, or zero when this is not a promotion.</summary>
    public int Promotion => Flag >= 8 ? (Flag & 3) + 2 : 0;
    /// <summary>Formats the move in UCI coordinate notation.</summary>
    public override string ToString() => Raw == 0 ? "0000" : string.Create(CultureInfo.InvariantCulture, $"{(char)('a' + (From & 7))}{(char)('1' + (From >> 3))}{(char)('a' + (To & 7))}{(char)('1' + (To >> 3))}{(Promotion == 0 ? "" : " nbrq"[Promotion - 1].ToString())}");
    /// <summary>Parses a coordinate move by matching the text against legal moves in the current position.</summary>
    /// <example>
    /// <code>
    /// Position board = new Position();
    /// Move firstMove = Move.Parse(board, "e2e4");
    /// board.MakeMove(firstMove);
    /// </code>
    /// </example>
    public static Move Parse(Position position, string text)
    {
        Span<Move> moves = stackalloc Move[256];
        int count = MoveGenerator.Generate(position, moves);
        if (text.Length is not (4 or 5)) throw new FormatException("Invalid UCI move.");
        for (int i = 0; i < count; i++) if (moves[i].ToString() == text) return moves[i];
        throw new FormatException("Illegal UCI move: " + text);
    }
}

public ref struct MoveList(Span<Move> storage)
{
    /// <summary>Uses caller-owned storage so move-list construction does not allocate.</summary>
    private readonly Span<Move> _Storage = storage;
    /// <summary>Gets the number of moves currently stored.</summary>
    public int Count { get; private set; }
    /// <summary>Gets the populated prefix of the backing span.</summary>
    public readonly Span<Move> Moves => _Storage[..Count];
    /// <summary>Adds a move; callers must ensure the supplied span has capacity.</summary>
    public void Add(Move move) => _Storage[Count++] = move;
}
