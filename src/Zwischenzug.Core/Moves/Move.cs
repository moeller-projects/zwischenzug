using System.Globalization;
namespace Zwischenzug.Core;

public readonly record struct Move
{
    public ushort Raw { get; }
    public Move(int from, int to, int flag = 0) => Raw = (ushort)(from | (to << 6) | (flag << 12));
    public int From => Raw & 63;
    public int To => (Raw >> 6) & 63;
    public int Flag => Raw >> 12;
    public bool IsCapture => Flag is 3 or 4 or >= 12;
    public int Promotion => Flag >= 8 ? (Flag & 3) + 2 : 0;
    public override string ToString() => Raw == 0 ? "0000" : string.Create(CultureInfo.InvariantCulture, $"{(char)('a' + (From & 7))}{(char)('1' + (From >> 3))}{(char)('a' + (To & 7))}{(char)('1' + (To >> 3))}{(Promotion == 0 ? "" : " nbrq"[Promotion - 1].ToString())}");
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
    private readonly Span<Move> _Storage = storage;
    public int Count { get; private set; }
    public readonly Span<Move> Moves => _Storage[..Count];
    public void Add(Move move) => _Storage[Count++] = move;
}
