namespace Zwischenzug.Core;

using System.Globalization;

public static class Fen
{
    public const string StartPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public static Position Parse(string fen)
    {
        var position = new Position(true);
        Initialize(position,fen);
        return position;
    }

    // Called only for a freshly allocated position, never over an existing game history.
    internal static void Initialize(Position p,string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) throw new FormatException("FEN is empty.");
        string[] f = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length is < 4 or > 6) throw new FormatException("FEN requires four to six fields.");
        int rank = 7, file = 0;
        foreach (char c in f[0])
        {
            if (c == '/') { if (file != 8 || --rank < 0) throw new FormatException("Invalid board layout."); file = 0; continue; }
            if (char.IsDigit(c)) { int n = c - '0'; if (n is < 1 or > 8 || file + n > 8) throw new FormatException("Invalid board layout."); file += n; continue; }
            int piece = Piece(c); if (piece == 0 || file >= 8) throw new FormatException("Invalid piece."); p.PutPiece(rank * 8 + file++, piece);
        }
        if (rank != 0 || file != 8) throw new FormatException("Invalid board layout.");
        if (p.KingSquare(Position.White) < 0 || p.KingSquare(Position.Black) < 0 || Count(p, 6) != 1 || Count(p, 12) != 1) throw new FormatException("FEN must contain one king per side.");
        p.SideToMove = f[1] switch { "w" => Position.White, "b" => Position.Black, _ => throw new FormatException("Invalid side.") };
        p.Castling = 0; if (f[2] != "-") foreach (char c in f[2]) p.Castling |= c switch { 'K' => 1, 'Q' => 2, 'k' => 4, 'q' => 8, _ => throw new FormatException("Invalid castling rights.") };
        if ((p.Castling & 1) != 0 && (p.Board[4] != 6 || p.Board[7] != 4) || (p.Castling & 2) != 0 && (p.Board[4] != 6 || p.Board[0] != 4) || (p.Castling & 4) != 0 && (p.Board[60] != 12 || p.Board[63] != 10) || (p.Castling & 8) != 0 && (p.Board[60] != 12 || p.Board[56] != 10)) throw new FormatException("Castling rights do not match pieces.");
        p.EnPassant = f[3] == "-" ? -1 : ParseSquare(f[3]);
        if (p.EnPassant >= 0 && p.Board[p.EnPassant] != 0) throw new FormatException("En-passant square is occupied.");
        if (p.EnPassant >= 0 && ((p.EnPassant / 8 is not (2 or 5)))) throw new FormatException("Invalid en-passant rank.");
        p.HalfmoveClock = f.Length >= 5 ? ParseNonNegative(f[4], "halfmove") : 0;
        p.FullmoveNumber = f.Length >= 6 ? ParsePositive(f[5], "fullmove") : 1;
        for (int sq = 0; sq < 64; sq++) if ((p.Board[sq] is 1 or 7) && (sq / 8 is 0 or 7)) throw new FormatException("Pawn on back rank.");
        p.Key = Zobrist.Compute(p);
        p.InitializeHistory();
    }
    public static string Format(Position p)
    {
        Span<char> board = stackalloc char[80]; int n = 0;
        for (int rank = 7; rank >= 0; rank--) { int empty = 0; for (int file = 0; file < 8; file++) { int piece = p.Board[rank * 8 + file]; if (piece == 0) empty++; else { if (empty > 0) board[n++] = (char)('0' + empty); empty = 0; } if (piece != 0) board[n++] = PieceChar(piece); } if (empty > 0) board[n++] = (char)('0' + empty); if (rank != 0) board[n++] = '/'; }
        string rights = p.Castling == 0 ? "-" : string.Concat((p.Castling & 1) != 0 ? "K" : "", (p.Castling & 2) != 0 ? "Q" : "", (p.Castling & 4) != 0 ? "k" : "", (p.Castling & 8) != 0 ? "q" : "");
        string ep = p.EnPassant < 0 ? "-" : $"{(char)('a' + (p.EnPassant & 7))}{1 + p.EnPassant / 8}";
        return new string(board[..n]) + $" {(p.SideToMove == Position.White ? "w" : "b")} {rights} {ep} {p.HalfmoveClock.ToString(CultureInfo.InvariantCulture)} {p.FullmoveNumber.ToString(CultureInfo.InvariantCulture)}";
    }
    private static int Count(Position p, int piece) { int n = 0; for (int i = 0; i < 64; i++) if (p.Board[i] == piece) n++; return n; }
    private static int ParseSquare(string s) { if (s.Length != 2 || s[0] is < 'a' or > 'h' || s[1] is < '1' or > '8') throw new FormatException("Invalid square."); return (s[1] - '1') * 8 + s[0] - 'a'; }
    private static int ParseNonNegative(string s, string name) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 0 ? n : throw new FormatException($"Invalid {name}.");
    private static int ParsePositive(string s, string name) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : throw new FormatException($"Invalid {name}.");
    private static int Piece(char c) => c switch { 'P' => 1, 'N' => 2, 'B' => 3, 'R' => 4, 'Q' => 5, 'K' => 6, 'p' => 7, 'n' => 8, 'b' => 9, 'r' => 10, 'q' => 11, 'k' => 12, _ => 0 };
    private static char PieceChar(int p) => p switch { 1 => 'P', 2 => 'N', 3 => 'B', 4 => 'R', 5 => 'Q', 6 => 'K', 7 => 'p', 8 => 'n', 9 => 'b', 10 => 'r', 11 => 'q', 12 => 'k', _ => '?' };
}
