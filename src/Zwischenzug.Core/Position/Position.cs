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

    /// <summary>Creates the standard chess starting position.</summary>
    public Position() => Fen.Initialize(this,Fen.StartPosition);

    /// <summary>Creates an empty position for trusted parsers and internal cloning.</summary>
    internal Position(bool empty) { }

    /// <summary>Starts repetition history with the position's already-computed key.</summary>
    internal void InitializeHistory() { Ply = 0; _HistoryLength = 1; _History[0] = Key; _RealHistory[0] = true; }

    /// <summary>Copies board and real-game history so a search worker can mutate its own position.</summary>
    internal Position CloneForSearch()
    {
        var clone = new Position(true)
        {
            Occupied = Occupied,
            SideToMove = SideToMove,
            Castling = Castling,
            EnPassant = EnPassant,
            HalfmoveClock = HalfmoveClock,
            FullmoveNumber = FullmoveNumber,
            Key = Key,
            Ply = Ply
        };
        Array.Copy(Board, clone.Board, Board.Length);
        Array.Copy(Pieces, clone.Pieces, Pieces.Length);
        Array.Copy(Colors, clone.Colors, Colors.Length);
        Array.Copy(_Undo, clone._Undo, _Undo.Length);
        Array.Copy(_History, clone._History, _History.Length);
        Array.Copy(_RealHistory, clone._RealHistory, _RealHistory.Length);
        clone._HistoryLength = _HistoryLength;
        return clone;
    }

    /// <summary>Places a piece and updates every derived board representation and the position key.</summary>
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
    /// <summary>Removes a piece from the mailbox, bitboards, occupancy, and incremental key.</summary>
    private void RemovePiece(int square, int piece)
    {
        ulong bit = 1UL << square; Board[square] = 0; Pieces[piece] &= ~bit;
        Key ^= Zobrist.PieceValue(piece, square);
        Colors[piece > 6 ? Black : White] &= ~bit; Occupied &= ~bit;
    }
    /// <summary>Returns the piece kind (pawn through king), ignoring its color offset.</summary>
    private static int TypeOf(int piece) => piece > 6 ? piece - 6 : piece;

    /// <summary>Finds a side's king square, or -1 when a malformed internal position has no king.</summary>
    public int KingSquare(int color)
    {
        ulong kings = Pieces[color == White ? 6 : 12];
        if (kings == 0) return -1;
        return BitOperations.TrailingZeroCount(kings);
    }

    /// <summary>Applies a move and records enough state for exact undo.</summary>
    /// <remarks>Ordinary callers pass generated legal moves; move generation also uses this temporarily on pseudo-legal candidates. All board views, rights, clocks, key, and history must stay synchronized.</remarks>
    /// <example>
    /// <code>
    /// Position board = new Position();
    /// Move move = Move.Parse(board, "e2e4");
    /// board.MakeMove(move);
    /// board.UnmakeMove(); // Restores the starting position and its history.
    /// </code>
    /// </example>
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

    /// <summary>Moves one piece while maintaining board representations and the incremental key.</summary>
    private void MovePiece(int from, int to, int piece) { RemovePiece(from, piece); PutPiece(to, piece); }
    /// <summary>Clears castling rights when a king or home rook moves or the home rook square is touched.</summary>
    private void UpdateCastling(int from, int to, int moving)
    {
        if (TypeOf(moving) == 6) Castling &= SideToMove == White ? ~3 : ~12;
        if (from == 0 || to == 0) Castling &= ~2; if (from == 7 || to == 7) Castling &= ~1;
        if (from == 56 || to == 56) Castling &= ~8; if (from == 63 || to == 63) Castling &= ~4;
    }

    /// <summary>Restores the exact state saved by the most recent real or null move.</summary>
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

    /// <summary>Passes the turn for search pruning without adding a real game move to repetition history.</summary>
    public void MakeNullMove()
    {
        if ((uint)Ply >= MaxPly) throw new InvalidOperationException("Position ply limit exceeded.");
        _Undo[Ply] = new Undo { Null = true, Side = SideToMove, Castling = Castling, EnPassant = EnPassant, Halfmove = HalfmoveClock, Fullmove = FullmoveNumber, Key = Key };
        if (EnPassant >= 0 && Zobrist.HasEp(this)) Key ^= Zobrist.EpValue(EnPassant);
        EnPassant = -1; SideToMove ^= 1; Key ^= Zobrist.SideValue; Ply++; PushHistory(Key, false);
    }
    /// <summary>Returns true when the current key has occurred at least <paramref name="count"/> times in real history.</summary>
    public bool IsRepetition(int count = 3)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
        if (_HistoryLength == 0 || !_RealHistory[_HistoryLength - 1]) return false;
        int found = 1;
        for (int i = _HistoryLength - 2; i >= 0 && _RealHistory[i]; i--) if (_History[i] == Key && ++found >= count) return true;
        return false;
    }
    /// <summary>Adds a key and marks whether it came from an actual game move or a search-only null move.</summary>
    private void PushHistory(ulong key, bool real) { _History[_HistoryLength++] = key; _RealHistory[_HistoryLength - 1] = real; }
    /// <summary>Removes the latest history entry when a move is undone.</summary>
    private void PopHistory() { if (_HistoryLength > 1) _HistoryLength--; }
    /// <summary>Checks the standard insufficient-material cases that cannot produce checkmate.</summary>
    public bool HasInsufficientMaterial()
    {
        if ((Pieces[1] | Pieces[7] | Pieces[4] | Pieces[10] | Pieces[5] | Pieces[11]) != 0) return false;
        ulong bishops = Pieces[3] | Pieces[9], knights = Pieces[2] | Pieces[8];
        if (BitOperations.PopCount(bishops | knights) <= 1) return true;
        const ulong darkSquares = 0xAA55AA55AA55AA55UL;
        return knights == 0 && ((bishops & darkSquares) == 0 || (bishops & ~darkSquares) == 0);
    }
}
