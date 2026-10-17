using System.Numerics;
using Zwischenzug.Core;

namespace Zwischenzug.Engine;

/// <summary>
/// Allocation-free static exchange evaluation for an initially legal move. Recaptures
/// are filtered for king safety, including pinned attackers and king recaptures.
/// Uses least-valuable-attacker exchange ordering with dynamically revealed x-rays.
/// </summary>
public static class StaticExchange
{
    private static ReadOnlySpan<int> Values => [0, 100, 320, 330, 500, 900, 20_000];

    public static int Evaluate(Position position, Move move)
    {
        int from = move.From, to = move.To, side = position.SideToMove;
        int moving = position.Board[from];
        if (moving == 0) return 0;
        int movingType = moving > 6 ? moving - 6 : moving;
        int capturedSquare = move.Flag == 3 ? to + (side == Position.White ? -8 : 8) : to;
        int captured = (uint)capturedSquare < 64 ? position.Board[capturedSquare] : 0;
        int promoted = move.Promotion;
        int firstGain = Value(captured) + (promoted == 0 ? 0 : Values[promoted] - Values[movingType]);
        if (firstGain == 0 && promoted == 0) return 0;

        ulong occupancy = (position.Occupied & ~(1UL << from) & ~(1UL << capturedSquare)) | (1UL << to);
        Span<int> gains = stackalloc int[32];
        gains[0] = firstGain;
        int depth = 0, occupantValue = promoted == 0 ? Values[movingType] : Values[promoted];
        if (movingType == 6) return firstGain;
        int turn = side ^ 1;
        while (depth < gains.Length - 1)
        {
            int attackerSquare = LeastValuableAttacker(position, occupancy, to, turn, out int attackerType);
            if (attackerSquare < 0) break;
            occupancy &= ~(1UL << attackerSquare);
            depth++;
            bool promotes = attackerType == 1 && (to >> 3) is 0 or 7;
            gains[depth] = occupantValue + (promotes ? Values[5] - Values[1] : 0) - gains[depth - 1];
            occupantValue = promotes ? Values[5] : Values[attackerType];
            turn ^= 1;
            if (attackerType == 6) break;
        }
        while (depth > 0)
        {
            depth--;
            gains[depth] = -Math.Max(-gains[depth], gains[depth + 1]);
        }
        return gains[0];

    }
    private static int LeastValuableAttacker(Position p, ulong occupancy, int target, int side, out int type)
    {
        int offset = side * 6;
        ulong attackers = (Attacks.Pawn(target, side ^ 1) & p.Pieces[1 + offset])
            | (Attacks.Knight(target) & p.Pieces[2 + offset])
            | (Attacks.Bishop(target, occupancy) & (p.Pieces[3 + offset] | p.Pieces[5 + offset]))
            | (Attacks.Rook(target, occupancy) & (p.Pieces[4 + offset] | p.Pieces[5 + offset]))
            | (Attacks.King(target) & p.Pieces[6 + offset]);
        attackers &= occupancy & ~(1UL << target);
        for (type = 1; type <= 6; type++)
        {
            ulong candidates = attackers & p.Pieces[type + offset];
            while (candidates != 0)
            {
                int square = BitOperations.TrailingZeroCount(candidates);
                candidates &= candidates - 1;
                int king = type == 6 ? target : p.KingSquare(side);
                ulong after = occupancy & ~(1UL << square);
                if (!Attacked(p, king, side ^ 1, after, target)) return square;
            }
        }
        type = 0; return -1;
    }

    private static bool Attacked(Position p, int square, int side, ulong occupied, int exchangeSquare)
    {
        int offset = side * 6;
        ulong active = occupied & ~(1UL << exchangeSquare);
        return (Attacks.Pawn(square, side ^ 1) & p.Pieces[1 + offset] & active) != 0
            || (Attacks.Knight(square) & p.Pieces[2 + offset] & active) != 0
            || (Attacks.King(square) & p.Pieces[6 + offset] & active) != 0
            || (Attacks.Bishop(square, occupied) & (p.Pieces[3 + offset] | p.Pieces[5 + offset]) & active) != 0
            || (Attacks.Rook(square, occupied) & (p.Pieces[4 + offset] | p.Pieces[5 + offset]) & active) != 0;
    }

    private static int Value(int piece) => piece == 0 ? 0 : Values[piece > 6 ? piece - 6 : piece];
}
