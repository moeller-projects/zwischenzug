using System.Numerics;
namespace Zwischenzug.Core;

public static class MoveGenerator
{
    public static int Generate(Position p, Span<Move> output, bool capturesOnly = false)
    {
        Span<Move> pseudo = stackalloc Move[256];
        int n = Pseudo(p, pseudo, capturesOnly), count = 0, side = p.SideToMove;
        // Only king moves, en passant and pinned pieces need the expensive make/check filter.
        int king = p.KingSquare(side);
        bool inCheck = Attacks.InCheck(p, side);
        ulong pinned = Pinned(p, side, king);
        for (int i = 0; i < n; i++)
        {
            Move move = pseudo[i];
            if (!inCheck && move.From != king && move.Flag != 3 && (pinned & (1UL << move.From)) == 0)
            { output[count++] = move; continue; }
            p.MakeMove(move);
            bool legal = !Attacks.InCheck(p, side);
            p.UnmakeMove();
            if (legal) output[count++] = move;
        }
        return count;
    }

    private static ulong Pinned(Position p, int side, int king)
    {
        ulong result = 0;
        ReadOnlySpan<int> df = [0,0,1,-1,1,-1,1,-1];
        ReadOnlySpan<int> dr = [1,-1,0,0,1,1,-1,-1];
        for (int direction = 0; direction < 8; direction++)
        {
            int candidate = -1, f = (king & 7) + df[direction], r = (king >> 3) + dr[direction];
            while ((uint)f < 8 && (uint)r < 8)
            {
                int sq = r * 8 + f, piece = p.Board[sq];
                if (piece != 0)
                {
                    if ((piece - 1) / 6 == side) { if (candidate >= 0) break; candidate = sq; }
                    else
                    {
                        int type = (piece - 1) % 6 + 1;
                        if (candidate >= 0 && (type == 5 || (direction < 4 ? type == 4 : type == 3))) result |= 1UL << candidate;
                        break;
                    }
                }
                f += df[direction]; r += dr[direction];
            }
        }
        return result;
    }

    private static int Pseudo(Position p, Span<Move> output, bool capturesOnly)
    {
        int count = 0, side = p.SideToMove, offset = side * 6;
        ulong own = p.Colors[side], enemy = p.Colors[side ^ 1];
        ulong pawns = p.Pieces[1 + offset];
        while (pawns != 0)
        {
            int from = BitOperations.TrailingZeroCount(pawns); pawns &= pawns - 1;
            int step = side == 0 ? 8 : -8, to = from + step;
            bool promotion = (from >> 3) == (side == 0 ? 6 : 1);
            if ((uint)to < 64 && p.Board[to] == 0 && (!capturesOnly || promotion))
            {
                if (promotion) for (int flag = 8; flag < 12; flag++) output[count++] = new(from,to,flag);
                else
                {
                    output[count++] = new(from,to);
                    if ((from >> 3) == (side == 0 ? 1 : 6) && p.Board[to + step] == 0) output[count++] = new(from,to + step,1);
                }
            }
            ulong targets = Attacks.Pawn(from,side) & enemy & ~p.Pieces[6 + (side ^ 1) * 6];
            while (targets != 0)
            {
                to = BitOperations.TrailingZeroCount(targets); targets &= targets - 1;
                if (promotion) for (int flag = 12; flag < 16; flag++) output[count++] = new(from,to,flag);
                else output[count++] = new(from,to,4);
            }
            if (p.EnPassant >= 0 && (Attacks.Pawn(from,side) & (1UL << p.EnPassant)) != 0 && p.Board[p.EnPassant - step] == 1 + (side ^ 1) * 6) output[count++] = new(from,p.EnPassant,3);
        }
        for (int type = 2; type <= 6; type++)
        {
            ulong pieces = p.Pieces[type + offset];
            while (pieces != 0)
            {
                int from = BitOperations.TrailingZeroCount(pieces); pieces &= pieces - 1;
                ulong targets = type switch { 2 => Attacks.Knight(from), 3 => Attacks.Bishop(from,p.Occupied), 4 => Attacks.Rook(from,p.Occupied), 5 => Attacks.Queen(from,p.Occupied), _ => Attacks.King(from) };
                targets &= ~own & ~p.Pieces[6 + (side ^ 1) * 6];
                if (capturesOnly) targets &= enemy;
                while (targets != 0)
                {
                    int to = BitOperations.TrailingZeroCount(targets); targets &= targets - 1;
                    output[count++] = new(from,to,p.Board[to] == 0 ? 0 : 4);
                }
            }
        }
        if (!capturesOnly)
        {
            int k = side == 0 ? 4 : 60;
            if (p.Board[k] == 6 + offset && !Attacks.IsSquareAttacked(p,k,side ^ 1))
            {
                if ((p.Castling & (1 << (side * 2))) != 0 && p.Board[k+3] == 4+offset && p.Board[k+1] == 0 && p.Board[k+2] == 0 && !Attacks.IsSquareAttacked(p,k+1,side ^ 1) && !Attacks.IsSquareAttacked(p,k+2,side ^ 1)) output[count++] = new(k,k+2,2);
                if ((p.Castling & (2 << (side * 2))) != 0 && p.Board[k-4] == 4+offset && p.Board[k-1] == 0 && p.Board[k-2] == 0 && p.Board[k-3] == 0 && !Attacks.IsSquareAttacked(p,k-1,side ^ 1) && !Attacks.IsSquareAttacked(p,k-2,side ^ 1)) output[count++] = new(k,k-2,2);
            }
        }
        return count;
    }
}
