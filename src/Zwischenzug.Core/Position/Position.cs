namespace Zwischenzug.Core;

using System.Numerics;

/// <summary>The complete mutable chess position. Board and bitboards are maintained together.</summary>
public sealed class Position
{
    public const int White = 0, Black = 1;
    public const int Empty = 0;
    public int[] Board { get; } = new int[64];
    public ulong[] Pieces { get; } = new ulong[13];
    public ulong[] Colors { get; } = new ulong[2];
    public ulong Occupied { get; internal set; }
    public int SideToMove { get; internal set; }
    public int Castling { get; internal set; }
    public int EnPassant { get; internal set; }
    public int HalfmoveClock { get; internal set; }
    public int FullmoveNumber { get; internal set; }
    public ulong Key { get; internal set; }
    public int Ply { get; internal set; }

    private const int MaxPly = 1024;
    private readonly Undo[] _Undo = new Undo[MaxPly];
    private readonly ulong[] _History = new ulong[MaxPly + 1];
    private readonly bool[] _RealHistory = new bool[MaxPly + 1];
    private int _HistoryLength;

    private struct Undo
    {
        public Move Move;
        public int Captured;
        public int CapturedSquare;
        public int Castling, EnPassant, Halfmove, Fullmove, Side;
        public ulong Key;
        public bool Null;
    }

    public Position() => Fen.Initialize(this,Fen.StartPosition);

    internal Position(bool empty) { }

    internal void InitializeHistory() { Ply = 0; _HistoryLength = 1; _History[0] = Key; _RealHistory[0] = true; }

    internal void PutPiece(int square, int piece)
    {
        if ((uint)square >= 64 || (uint)piece >= 13) throw new ArgumentOutOfRangeException();
        int old = Board[square];
        if (old != 0) RemovePiece(square, old);
        if (piece == 0) return;
        Board[square] = piece; ulong bit = 1UL << square; Pieces[piece] |= bit;
        Key ^= Zobrist.PieceValue(piece, square);
        int color = piece > 6 ? Black : White; Colors[color] |= bit; Occupied |= bit;
    }
    private void RemovePiece(int square, int piece)
    {
        ulong bit = 1UL << square; Board[square] = 0; Pieces[piece] &= ~bit;
        Key ^= Zobrist.PieceValue(piece, square);
        Colors[piece > 6 ? Black : White] &= ~bit; Occupied &= ~bit;
    }
    private static int TypeOf(int piece) => piece > 6 ? piece - 6 : piece;

    public int KingSquare(int color)
    {
        ulong kings = Pieces[color == White ? 6 : 12];
        if (kings == 0) return -1;
        return BitOperations.TrailingZeroCount(kings);
    }

    public void MakeMove(Move move)
    {
        if ((uint)Ply >= MaxPly) throw new InvalidOperationException("Position ply limit exceeded.");
        int from = move.From, to = move.To, moving = Board[from];
        if ((uint)from >= 64 || (uint)to >= 64 || moving == 0) throw new ArgumentException("Move source is empty.", nameof(move));
        var u = new Undo { Move = move, Captured = 0, CapturedSquare = to, Castling = Castling, EnPassant = EnPassant, Halfmove = HalfmoveClock, Fullmove = FullmoveNumber, Side = SideToMove, Key = Key };
        int flag = move.Flag;
        if (from == to) throw new ArgumentException("Invalid move.", nameof(move));
        Key ^= Zobrist.CastleValue(Castling); if (EnPassant >= 0 && Zobrist.HasEp(this)) Key ^= Zobrist.EpValue(EnPassant);
        if (flag == 2)
        { // castle rook follows king
            int rookFrom = to > from ? (from / 8) * 8 + 7 : (from / 8) * 8;
            int rookTo = to > from ? to - 1 : to + 1;
            MovePiece(from, to, moving); MovePiece(rookFrom, rookTo, Board[rookFrom]);
        }
        else
        {
            if (flag == 3) u.CapturedSquare = SideToMove == White ? to - 8 : to + 8;
            u.Captured = Board[u.CapturedSquare];
            if (u.Captured != 0) RemovePiece(u.CapturedSquare, u.Captured);
            RemovePiece(from, moving);
            int placed = moving;
            if (flag >= 8) { int type = (flag & 3) + 2; placed = SideToMove == White ? type : type + 6; }
            PutPiece(to, placed);
        }
        UpdateCastling(from, to, moving);
        EnPassant = -1;
        if (TypeOf(moving) == 1 && Math.Abs(to - from) == 16) EnPassant = (from + to) / 2;
        HalfmoveClock = (TypeOf(moving) == 1 || u.Captured != 0) ? 0 : HalfmoveClock + 1;
        if (SideToMove == Black) FullmoveNumber++;
        SideToMove ^= 1; Ply++;
        Key ^= Zobrist.SideValue; Key ^= Zobrist.CastleValue(Castling); if (EnPassant >= 0 && Zobrist.HasEp(this)) Key ^= Zobrist.EpValue(EnPassant);
        _Undo[Ply - 1] = u; PushHistory(Key, true);
    }

    private void MovePiece(int from, int to, int piece) { RemovePiece(from, piece); PutPiece(to, piece); }
    private void UpdateCastling(int from, int to, int moving)
    {
        if (TypeOf(moving) == 6) Castling &= SideToMove == White ? ~3 : ~12;
        if (from == 0 || to == 0) Castling &= ~2; if (from == 7 || to == 7) Castling &= ~1;
        if (from == 56 || to == 56) Castling &= ~8; if (from == 63 || to == 63) Castling &= ~4;
    }

    public void UnmakeMove()
    {
        if (Ply == 0) throw new InvalidOperationException("No move to unmake.");
        var u = _Undo[Ply - 1];
        if (u.Null) { SideToMove = u.Side; EnPassant = u.EnPassant; HalfmoveClock = u.Halfmove; FullmoveNumber = u.Fullmove; Key = u.Key; Ply--; PopHistory(); return; }
        int from = u.Move.From, to = u.Move.To, moving = Board[to];
        if (u.Move.Flag == 2) { int rookFrom = to > from ? (from / 8) * 8 + 7 : (from / 8) * 8; int rookTo = to > from ? to - 1 : to + 1; MovePiece(to, from, moving); MovePiece(rookTo, rookFrom, Board[rookTo]); }
        else { RemovePiece(to, moving); PutPiece(from, u.Move.Flag >= 8 ? (u.Side == White ? 1 : 7) : moving); if (u.Captured != 0) PutPiece(u.CapturedSquare, u.Captured); }
        SideToMove = u.Side; Castling = u.Castling; EnPassant = u.EnPassant; HalfmoveClock = u.Halfmove; FullmoveNumber = u.Fullmove; Key = u.Key; Ply--; PopHistory();
    }

    public void MakeNullMove()
    {
        if ((uint)Ply >= MaxPly) throw new InvalidOperationException("Position ply limit exceeded.");
        _Undo[Ply] = new Undo { Null = true, Side = SideToMove, Castling = Castling, EnPassant = EnPassant, Halfmove = HalfmoveClock, Fullmove = FullmoveNumber, Key = Key };
        if (EnPassant >= 0 && Zobrist.HasEp(this)) Key ^= Zobrist.EpValue(EnPassant);
        EnPassant = -1; SideToMove ^= 1; Key ^= Zobrist.SideValue; Ply++; PushHistory(Key, false);
    }
    public bool IsRepetition(int count = 3)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
        if (_HistoryLength == 0 || !_RealHistory[_HistoryLength - 1]) return false;
        int found = 1;
        for (int i = _HistoryLength - 2; i >= 0 && _RealHistory[i]; i--) if (_History[i] == Key && ++found >= count) return true;
        return false;
    }
    private void PushHistory(ulong key, bool real) { _History[_HistoryLength++] = key; _RealHistory[_HistoryLength - 1] = real; }
    private void PopHistory() { if (_HistoryLength > 1) _HistoryLength--; }
    public bool HasInsufficientMaterial()
    {
        if ((Pieces[1] | Pieces[7] | Pieces[4] | Pieces[10] | Pieces[5] | Pieces[11]) != 0) return false;
        ulong bishops = Pieces[3] | Pieces[9], knights = Pieces[2] | Pieces[8];
        if (BitOperations.PopCount(bishops | knights) <= 1) return true;
        const ulong darkSquares = 0xAA55AA55AA55AA55UL;
        return knights == 0 && ((bishops & darkSquares) == 0 || (bishops & ~darkSquares) == 0);
    }
}
