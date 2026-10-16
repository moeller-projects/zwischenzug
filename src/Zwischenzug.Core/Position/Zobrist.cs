namespace Zwischenzug.Core;

internal static class Zobrist
{
    private static readonly ulong[,] Piece = new ulong[13, 64];
    private static readonly ulong[] Castle = new ulong[16];
    private static readonly ulong[] Ep = new ulong[64];
    private static readonly ulong Side;
    static Zobrist()
    {
        ulong x = 0x9E3779B97F4A7C15UL;
        for (int p = 0; p < 13; p++) for (int s = 0; s < 64; s++) Piece[p, s] = Next(ref x);
        for (int i = 0; i < 16; i++) Castle[i] = Next(ref x);
        for (int i = 0; i < 64; i++) Ep[i] = Next(ref x);
        Side = Next(ref x);
    }
    private static ulong Next(ref ulong x) { x += 0x9E3779B97F4A7C15UL; ulong z = x; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
    internal static ulong CastleValue(int rights) => Castle[rights];
    internal static ulong EpValue(int square) => Ep[square];
    internal static ulong PieceValue(int piece, int square) => Piece[piece, square];
    internal static ulong SideValue => Side;
    internal static ulong Compute(Position position)
    {
        ulong key = Castle[position.Castling];
        for (int sq = 0; sq < 64; sq++) { int p = position.Board[sq]; if (p != 0) key ^= Piece[p, sq]; }
        if (position.SideToMove == Position.Black) key ^= Side;
        if (position.EnPassant >= 0 && HasEp(position)) key ^= Ep[position.EnPassant];
        return key;
    }
    internal static bool HasEp(Position p)
    {
        int target = p.EnPassant;
        if (target < 0 || p.Board[target] != 0) return false;
        int side = p.SideToMove, enemy = side ^ 1, offset = enemy * 6;
        int captured = target + (side == 0 ? -8 : 8);
        if ((uint)captured >= 64 || p.Board[captured] != 1 + offset) return false;
        ulong candidates = Attacks.Pawn(target, enemy) & p.Pieces[1 + side * 6];
        int king = p.KingSquare(side);
        while (candidates != 0)
        {
            int from = System.Numerics.BitOperations.TrailingZeroCount(candidates);
            candidates &= candidates - 1;
            ulong occupied = (p.Occupied & ~(1UL << from) & ~(1UL << captured)) | (1UL << target);
            if ((Attacks.Pawn(king, side) & p.Pieces[1 + offset] & ~(1UL << captured)) != 0) continue;
            if ((Attacks.Knight(king) & p.Pieces[2 + offset]) != 0) continue;
            if ((Attacks.King(king) & p.Pieces[6 + offset]) != 0) continue;
            if ((Attacks.Bishop(king, occupied) & (p.Pieces[3 + offset] | p.Pieces[5 + offset])) != 0) continue;
            if ((Attacks.Rook(king, occupied) & (p.Pieces[4 + offset] | p.Pieces[5 + offset])) != 0) continue;
            return true;
        }
        return false;
    }
}
