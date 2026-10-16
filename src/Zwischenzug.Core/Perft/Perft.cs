namespace Zwischenzug.Core;

public static class Perft
{
    public static long Count(Position position, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        if (depth == 0) return 1;
        Span<Move> moves = stackalloc Move[256];
        int count = MoveGenerator.Generate(position, moves);
        if (depth == 1) return count;
        long nodes = 0;
        for (int i = 0; i < count; i++)
        {
            position.MakeMove(moves[i]);
            nodes += Count(position, depth - 1);
            position.UnmakeMove();
        }
        return nodes;
    }

    public static IReadOnlyList<(Move Move, long Nodes)> Divide(Position position, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        var result = new List<(Move, long)>();
        Span<Move> moves = stackalloc Move[256];
        int count = MoveGenerator.Generate(position, moves);
        for (int i = 0; i < count; i++)
        {
            position.MakeMove(moves[i]);
            long nodes = Count(position, depth - 1);
            position.UnmakeMove(); result.Add((moves[i], nodes));
        }
        return result;
    }
}
