using System.Numerics;
using System.Runtime.Intrinsics.X86;

namespace Zwischenzug.Core;

/// <summary>Chess attack generation. Squares are numbered a1 (0) through h8 (63).</summary>
public static class Attacks
{
    private static readonly ulong[] KnightAttacks = new ulong[64];
    private static readonly ulong[] KingAttacks = new ulong[64];
    private static readonly ulong[] WhitePawnAttacks = new ulong[64];
    private static readonly ulong[] BlackPawnAttacks = new ulong[64];
    private static readonly ulong[] BishopMasks = new ulong[64];
    private static readonly ulong[] RookMasks = new ulong[64];
    private static readonly int[] BishopOffsets = new int[64];
    private static readonly int[] RookOffsets = new int[64];
    private static readonly ulong[] BishopTable;
    private static readonly ulong[] RookTable;

    static Attacks()
    {
        // Sliding tables enumerate only blockers that can change a ray before its edge; this keeps lookup compact.
        int bishopSize = 0;
        int rookSize = 0;
        for (int square = 0; square < 64; square++)
        {
            KnightAttacks[square] = Leaper(square, KnightDeltas);
            KingAttacks[square] = Leaper(square, KingDeltas);
            WhitePawnAttacks[square] = PawnAttacks(square, 0);
            BlackPawnAttacks[square] = PawnAttacks(square, 1);
            BishopMasks[square] = SlidingMask(square, BishopDirections);
            RookMasks[square] = SlidingMask(square, RookDirections);
            BishopOffsets[square] = bishopSize;
            RookOffsets[square] = rookSize;
            bishopSize += 1 << BitOperations.PopCount(BishopMasks[square]);
            rookSize += 1 << BitOperations.PopCount(RookMasks[square]);
        }

        BishopTable = new ulong[bishopSize];
        RookTable = new ulong[rookSize];
        for (int square = 0; square < 64; square++)
        {
            FillTable(square, BishopMasks[square], BishopOffsets[square], BishopTable, BishopDirections);
            FillTable(square, RookMasks[square], RookOffsets[square], RookTable, RookDirections);
        }
    }

    /// <summary>Returns the precomputed squares attacked by a knight on the given square.</summary>
    public static ulong Knight(int square) => KnightAttacks[square];
    /// <summary>Returns the precomputed adjacent squares attacked by a king.</summary>
    public static ulong King(int square) => KingAttacks[square];
    /// <summary>Returns pawn attacks for color 0 (White) or 1 (Black); color does not mean side-to-move.</summary>
    public static ulong Pawn(int square, int color) => color == 0 ? WhitePawnAttacks[square] : BlackPawnAttacks[square];
    /// <summary>Returns bishop rays, stopping on the first occupied square in each diagonal.</summary>
    public static ulong Bishop(int square, ulong occupied) => BishopTable[BishopOffsets[square] + Index(occupied & BishopMasks[square], BishopMasks[square])];
    /// <summary>Returns rook rays, stopping on the first occupied square in each rank or file.</summary>
    public static ulong Rook(int square, ulong occupied) => RookTable[RookOffsets[square] + Index(occupied & RookMasks[square], RookMasks[square])];
    /// <summary>Combines the diagonal and rank/file rays used by a queen.</summary>
    public static ulong Queen(int square, ulong occupied) => Bishop(square, occupied) | Rook(square, occupied);

    /// <summary>Tests whether a square is attacked by a color, regardless of whether that attack is a legal move.</summary>
    public static bool IsSquareAttacked(Position position, int square, int byColor)
    {
        int offset = byColor * 6;
        return (Pawn(square, byColor ^ 1) & position.Pieces[1 + offset]) != 0
            || (Knight(square) & position.Pieces[2 + offset]) != 0
            || (King(square) & position.Pieces[6 + offset]) != 0
            || (Bishop(square, position.Occupied) & (position.Pieces[3 + offset] | position.Pieces[5 + offset])) != 0
            || (Rook(square, position.Occupied) & (position.Pieces[4 + offset] | position.Pieces[5 + offset])) != 0;
    }

    /// <summary>Tests whether the selected side's king is attacked.</summary>
    public static bool InCheck(Position position, int color) =>
        IsSquareAttacked(position, position.KingSquare(color), color ^ 1);

    private static readonly (int dr, int df)[] KnightDeltas =
    {
        (2, 1), (2, -1), (-2, 1), (-2, -1),
        (1, 2), (1, -2), (-1, 2), (-1, -2)
    };
    private static readonly (int dr, int df)[] KingDeltas =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)
    };
    private static readonly (int dr, int df)[] BishopDirections = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly (int dr, int df)[] RookDirections = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Builds an attack mask for a piece with fixed rank/file offsets.</summary>
    private static ulong Leaper(int square, (int dr, int df)[] deltas)
    {
        int rank = square >> 3;
        int file = square & 7;
        ulong attacks = 0;
        foreach (var (dr, df) in deltas)
        {
            int r = rank + dr;
            int f = file + df;
            if ((uint)r < 8 && (uint)f < 8)
                attacks |= 1UL << (r * 8 + f);
        }
        return attacks;
    }

    /// <summary>Builds one pawn's diagonal attack mask, respecting the board edge.</summary>
    private static ulong PawnAttacks(int square, int color)
    {
        int rank = square >> 3;
        int file = square & 7;
        int nextRank = rank + (color == 0 ? 1 : -1);
        if ((uint)nextRank >= 8) return 0;
        ulong attacks = 0;
        if (file != 0) attacks |= 1UL << (nextRank * 8 + file - 1);
        if (file != 7) attacks |= 1UL << (nextRank * 8 + file + 1);
        return attacks;
    }

    /// <summary>Builds the relevant occupancy mask; edge blockers need not be indexed because rays end there.</summary>
    private static ulong SlidingMask(int square, (int dr, int df)[] directions)
    {
        int rank = square >> 3;
        int file = square & 7;
        ulong mask = 0;
        foreach (var (dr, df) in directions)
        {
            int r = rank + dr;
            int f = file + df;
            while ((uint)r < 8 && (uint)f < 8)
            {
                if ((uint)(r + dr) >= 8 || (uint)(f + df) >= 8) break;
                mask |= 1UL << (r * 8 + f);
                r += dr;
                f += df;
            }
        }
        return mask;
    }

    /// <summary>Precomputes sliding attacks for every blocker subset of one square's relevant mask.</summary>
    private static void FillTable(int square, ulong mask, int offset, ulong[] table, (int dr, int df)[] directions)
    {
        int count = BitOperations.PopCount(mask);
        int entries = 1 << count;
        for (int subset = 0; subset < entries; subset++)
        {
            ulong occupied = ExpandSubset(mask, subset);
            table[offset + subset] = SlidingAttacks(square, occupied, directions);
        }
    }

    /// <summary>Expands a compact integer into board bits selected by a mask.</summary>
    private static ulong ExpandSubset(ulong mask, int subset)
    {
        ulong result = 0;
        while (mask != 0)
        {
            ulong bit = mask & (0UL - mask);
            if ((subset & 1) != 0) result |= bit;
            subset >>= 1;
            mask ^= bit;
        }
        return result;
    }

    /// <summary>Walks each sliding direction and includes the first blocker square before stopping.</summary>
    private static ulong SlidingAttacks(int square, ulong occupied, (int dr, int df)[] directions)
    {
        int rank = square >> 3;
        int file = square & 7;
        ulong attacks = 0;
        foreach (var (dr, df) in directions)
        {
            int r = rank + dr;
            int f = file + df;
            while ((uint)r < 8 && (uint)f < 8)
            {
                ulong bit = 1UL << (r * 8 + f);
                attacks |= bit;
                if ((occupied & bit) != 0) break;
                r += dr;
                f += df;
            }
        }
        return attacks;
    }

    /// <summary>Maps masked occupancy to the attack table index, using BMI2 when available.</summary>
    private static int Index(ulong occupancy, ulong mask)
    {
        if (Bmi2.X64.IsSupported)
            return (int)Bmi2.X64.ParallelBitExtract(occupancy, mask);
        int result = 0;
        int bit = 0;
        while (mask != 0)
        {
            ulong lowest = mask & (0UL - mask);
            if ((occupancy & lowest) != 0) result |= 1 << bit;
            bit++;
            mask ^= lowest;
        }
        return result;
    }
}
