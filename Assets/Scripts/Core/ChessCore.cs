// Wizard Chess VR - 체스 규칙 코어
// Unity에 의존하지 않는 순수 C# 코드. 헤드셋 없이도 테스트할 수 있다.
// 좌표: x = 파일(0=a ~ 7=h), y = 랭크(0=1 ~ 7=8). 백은 y=0,1에서 시작.

using System;
using System.Collections.Generic;
using System.Text;

namespace WizardChess.Core
{
    public enum PieceType { None, Pawn, Knight, Bishop, Rook, Queen, King }
    public enum PieceColor { White, Black }
    public enum GameResult { Ongoing, WhiteWins, BlackWins, Draw }

    public static class PieceColorExtensions
    {
        public static PieceColor Opponent(this PieceColor c)
        {
            return c == PieceColor.White ? PieceColor.Black : PieceColor.White;
        }
    }

    public struct Piece
    {
        public readonly PieceType Type;
        public readonly PieceColor Color;

        public Piece(PieceType type, PieceColor color)
        {
            Type = type;
            Color = color;
        }

        public bool IsEmpty { get { return Type == PieceType.None; } }
        public static readonly Piece Empty = new Piece(PieceType.None, PieceColor.White);

        public override string ToString() { return IsEmpty ? "-" : Color + " " + Type; }
    }

    public struct Move
    {
        public readonly int FromX, FromY, ToX, ToY;
        public readonly PieceType Promotion;   // 승진이 아니면 None
        public readonly bool IsCastle;
        public readonly bool IsEnPassant;

        public Move(int fromX, int fromY, int toX, int toY,
                    PieceType promotion = PieceType.None, bool isCastle = false, bool isEnPassant = false)
        {
            FromX = fromX; FromY = fromY; ToX = toX; ToY = toY;
            Promotion = promotion; IsCastle = isCastle; IsEnPassant = isEnPassant;
        }

        /// <summary>UCI 표기 (예: e2e4, e7e8q). Stockfish와 통신할 때 사용.</summary>
        public string ToUci()
        {
            string s = Board.SquareName(FromX, FromY) + Board.SquareName(ToX, ToY);
            if (Promotion != PieceType.None) s += char.ToLower(Board.PieceChar(Promotion));
            return s;
        }

        public override string ToString() { return ToUci(); }
    }

    public class Board
    {
        static readonly int[,] KnightOffsets = { { 1, 2 }, { 2, 1 }, { 2, -1 }, { 1, -2 }, { -1, -2 }, { -2, -1 }, { -2, 1 }, { -1, 2 } };
        static readonly int[,] KingOffsets = { { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 }, { -1, -1 }, { 0, -1 }, { 1, -1 } };
        static readonly int[,] RookDirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        static readonly int[,] BishopDirs = { { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 } };
        static readonly PieceType[] BackRank =
            { PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
              PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook };

        Piece[,] squares = new Piece[8, 8];

        public PieceColor SideToMove { get; private set; }
        public bool WhiteCastleKing { get; private set; }
        public bool WhiteCastleQueen { get; private set; }
        public bool BlackCastleKing { get; private set; }
        public bool BlackCastleQueen { get; private set; }
        public int EnPassantX { get; private set; }   // 앙파상으로 잡을 수 있는 칸, 없으면 -1
        public int EnPassantY { get; private set; }
        public int HalfmoveClock { get; private set; }
        public int FullmoveNumber { get; private set; }

        public Piece this[int x, int y] { get { return squares[x, y]; } }

        Board()
        {
            EnPassantX = -1;
            EnPassantY = -1;
            FullmoveNumber = 1;
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    squares[x, y] = Piece.Empty;
        }

        // ───────────────────────── 생성 ─────────────────────────

        public static Board CreateStandard()
        {
            var b = new Board();
            for (int x = 0; x < 8; x++)
            {
                b.squares[x, 0] = new Piece(BackRank[x], PieceColor.White);
                b.squares[x, 1] = new Piece(PieceType.Pawn, PieceColor.White);
                b.squares[x, 6] = new Piece(PieceType.Pawn, PieceColor.Black);
                b.squares[x, 7] = new Piece(BackRank[x], PieceColor.Black);
            }
            b.WhiteCastleKing = b.WhiteCastleQueen = b.BlackCastleKing = b.BlackCastleQueen = true;
            return b;
        }

        /// <summary>FEN 문자열로 보드를 만든다. 특정 상황(체크메이트 직전 등)을 테스트할 때 유용.</summary>
        public static Board FromFen(string fen)
        {
            var b = new Board();
            string[] parts = fen.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] rows = parts[0].Split('/');
            if (rows.Length != 8) throw new ArgumentException("잘못된 FEN: " + fen);

            for (int r = 0; r < 8; r++)
            {
                int y = 7 - r, x = 0;
                foreach (char ch in rows[r])
                {
                    if (char.IsDigit(ch)) { x += ch - '0'; continue; }
                    var color = char.IsUpper(ch) ? PieceColor.White : PieceColor.Black;
                    b.squares[x, y] = new Piece(TypeFromChar(ch), color);
                    x++;
                }
            }

            b.SideToMove = (parts.Length > 1 && parts[1] == "b") ? PieceColor.Black : PieceColor.White;
            string castling = parts.Length > 2 ? parts[2] : "-";
            b.WhiteCastleKing = castling.Contains("K");
            b.WhiteCastleQueen = castling.Contains("Q");
            b.BlackCastleKing = castling.Contains("k");
            b.BlackCastleQueen = castling.Contains("q");
            if (parts.Length > 3 && parts[3] != "-")
            {
                b.EnPassantX = parts[3][0] - 'a';
                b.EnPassantY = parts[3][1] - '1';
            }
            if (parts.Length > 4) b.HalfmoveClock = int.Parse(parts[4]);
            if (parts.Length > 5) b.FullmoveNumber = int.Parse(parts[5]);
            return b;
        }

        public Board Clone()
        {
            var b = (Board)MemberwiseClone();
            b.squares = (Piece[,])squares.Clone();
            return b;
        }

        // ───────────────────────── 이동 생성 ─────────────────────────

        /// <summary>현재 차례인 쪽의 합법수 전체.</summary>
        public List<Move> GetLegalMoves()
        {
            var pseudo = new List<Move>();
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                {
                    var p = squares[x, y];
                    if (!p.IsEmpty && p.Color == SideToMove) AddPseudoMoves(pseudo, x, y, p);
                }

            var legal = new List<Move>(pseudo.Count);
            foreach (var m in pseudo)
            {
                var copy = Clone();
                copy.MakeMove(m);
                if (!copy.IsInCheck(SideToMove)) legal.Add(m);
            }
            return legal;
        }

        /// <summary>특정 칸에 있는 기물의 합법수. 포인터로 기물을 골랐을 때 이동 가능한 칸 표시에 사용.</summary>
        public List<Move> GetLegalMovesFrom(int x, int y)
        {
            return GetLegalMoves().FindAll(m => m.FromX == x && m.FromY == y);
        }

        void AddPseudoMoves(List<Move> moves, int x, int y, Piece p)
        {
            switch (p.Type)
            {
                case PieceType.Pawn: AddPawnMoves(moves, x, y, p.Color); break;
                case PieceType.Knight: AddStepMoves(moves, x, y, p.Color, KnightOffsets); break;
                case PieceType.Bishop: AddSlideMoves(moves, x, y, p.Color, BishopDirs); break;
                case PieceType.Rook: AddSlideMoves(moves, x, y, p.Color, RookDirs); break;
                case PieceType.Queen:
                    AddSlideMoves(moves, x, y, p.Color, RookDirs);
                    AddSlideMoves(moves, x, y, p.Color, BishopDirs);
                    break;
                case PieceType.King:
                    AddStepMoves(moves, x, y, p.Color, KingOffsets);
                    AddCastlingMoves(moves, x, y, p.Color);
                    break;
            }
        }

        void AddPawnMoves(List<Move> moves, int x, int y, PieceColor c)
        {
            int dir = c == PieceColor.White ? 1 : -1;
            int startRank = c == PieceColor.White ? 1 : 6;
            int lastRank = c == PieceColor.White ? 7 : 0;
            int ny = y + dir;
            if (!InBounds(x, ny)) return;

            if (squares[x, ny].IsEmpty)
            {
                AddPawnMove(moves, x, y, x, ny, lastRank);
                if (y == startRank && squares[x, y + 2 * dir].IsEmpty)
                    moves.Add(new Move(x, y, x, y + 2 * dir));
            }

            for (int dx = -1; dx <= 1; dx += 2)
            {
                int nx = x + dx;
                if (!InBounds(nx, ny)) continue;
                var target = squares[nx, ny];
                if (!target.IsEmpty && target.Color != c)
                    AddPawnMove(moves, x, y, nx, ny, lastRank);
                else if (nx == EnPassantX && ny == EnPassantY)
                    moves.Add(new Move(x, y, nx, ny, PieceType.None, false, true));
            }
        }

        static void AddPawnMove(List<Move> moves, int fx, int fy, int tx, int ty, int lastRank)
        {
            if (ty == lastRank)
            {
                moves.Add(new Move(fx, fy, tx, ty, PieceType.Queen));
                moves.Add(new Move(fx, fy, tx, ty, PieceType.Rook));
                moves.Add(new Move(fx, fy, tx, ty, PieceType.Bishop));
                moves.Add(new Move(fx, fy, tx, ty, PieceType.Knight));
            }
            else moves.Add(new Move(fx, fy, tx, ty));
        }

        void AddStepMoves(List<Move> moves, int x, int y, PieceColor c, int[,] offsets)
        {
            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                int nx = x + offsets[i, 0], ny = y + offsets[i, 1];
                if (!InBounds(nx, ny)) continue;
                var t = squares[nx, ny];
                if (t.IsEmpty || t.Color != c) moves.Add(new Move(x, y, nx, ny));
            }
        }

        void AddSlideMoves(List<Move> moves, int x, int y, PieceColor c, int[,] dirs)
        {
            for (int i = 0; i < dirs.GetLength(0); i++)
            {
                int nx = x + dirs[i, 0], ny = y + dirs[i, 1];
                while (InBounds(nx, ny))
                {
                    var t = squares[nx, ny];
                    if (t.IsEmpty) moves.Add(new Move(x, y, nx, ny));
                    else
                    {
                        if (t.Color != c) moves.Add(new Move(x, y, nx, ny));
                        break;
                    }
                    nx += dirs[i, 0]; ny += dirs[i, 1];
                }
            }
        }

        void AddCastlingMoves(List<Move> moves, int x, int y, PieceColor c)
        {
            int rank = c == PieceColor.White ? 0 : 7;
            if (x != 4 || y != rank) return;
            bool canKing = c == PieceColor.White ? WhiteCastleKing : BlackCastleKing;
            bool canQueen = c == PieceColor.White ? WhiteCastleQueen : BlackCastleQueen;
            if (!canKing && !canQueen) return;

            var opp = c.Opponent();
            if (IsSquareAttacked(4, rank, opp)) return;   // 체크 상태에서는 캐슬링 불가

            if (canKing && squares[5, rank].IsEmpty && squares[6, rank].IsEmpty
                && IsPiece(7, rank, PieceType.Rook, c)
                && !IsSquareAttacked(5, rank, opp) && !IsSquareAttacked(6, rank, opp))
                moves.Add(new Move(4, rank, 6, rank, PieceType.None, true));

            if (canQueen && squares[1, rank].IsEmpty && squares[2, rank].IsEmpty && squares[3, rank].IsEmpty
                && IsPiece(0, rank, PieceType.Rook, c)
                && !IsSquareAttacked(3, rank, opp) && !IsSquareAttacked(2, rank, opp))
                moves.Add(new Move(4, rank, 2, rank, PieceType.None, true));
        }

        // ───────────────────────── 공격 / 체크 ─────────────────────────

        public bool IsSquareAttacked(int x, int y, PieceColor by)
        {
            // 폰: by 색의 폰은 앞쪽 대각선을 공격하므로, 공격하는 폰은 (x±1, y-dir)에 있다
            int dir = by == PieceColor.White ? 1 : -1;
            if (IsPiece(x - 1, y - dir, PieceType.Pawn, by) || IsPiece(x + 1, y - dir, PieceType.Pawn, by)) return true;

            for (int i = 0; i < 8; i++)
            {
                if (IsPiece(x + KnightOffsets[i, 0], y + KnightOffsets[i, 1], PieceType.Knight, by)) return true;
                if (IsPiece(x + KingOffsets[i, 0], y + KingOffsets[i, 1], PieceType.King, by)) return true;
            }

            if (SlideAttack(x, y, by, RookDirs, PieceType.Rook)) return true;
            if (SlideAttack(x, y, by, BishopDirs, PieceType.Bishop)) return true;
            return false;
        }

        bool SlideAttack(int x, int y, PieceColor by, int[,] dirs, PieceType slider)
        {
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dirs[i, 0], ny = y + dirs[i, 1];
                while (InBounds(nx, ny))
                {
                    var t = squares[nx, ny];
                    if (!t.IsEmpty)
                    {
                        if (t.Color == by && (t.Type == slider || t.Type == PieceType.Queen)) return true;
                        break;
                    }
                    nx += dirs[i, 0]; ny += dirs[i, 1];
                }
            }
            return false;
        }

        public bool IsInCheck(PieceColor c)
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    if (IsPiece(x, y, PieceType.King, c)) return IsSquareAttacked(x, y, c.Opponent());
            return false;
        }

        // ───────────────────────── 수 두기 ─────────────────────────

        /// <summary>수를 보드에 적용한다. 합법성은 검사하지 않으므로 GetLegalMoves로 얻은 수만 넣을 것.</summary>
        public void MakeMove(Move m)
        {
            var mover = squares[m.FromX, m.FromY];
            bool isCapture = !squares[m.ToX, m.ToY].IsEmpty || m.IsEnPassant;

            squares[m.ToX, m.ToY] = m.Promotion != PieceType.None ? new Piece(m.Promotion, mover.Color) : mover;
            squares[m.FromX, m.FromY] = Piece.Empty;

            if (m.IsEnPassant) squares[m.ToX, m.FromY] = Piece.Empty;

            if (m.IsCastle)
            {
                int rank = m.FromY;
                if (m.ToX == 6) { squares[5, rank] = squares[7, rank]; squares[7, rank] = Piece.Empty; }
                else { squares[3, rank] = squares[0, rank]; squares[0, rank] = Piece.Empty; }
            }

            // 캐슬링 권리 갱신
            if (mover.Type == PieceType.King)
            {
                if (mover.Color == PieceColor.White) WhiteCastleKing = WhiteCastleQueen = false;
                else BlackCastleKing = BlackCastleQueen = false;
            }
            ClearCastlingIfRookSquare(m.FromX, m.FromY);
            ClearCastlingIfRookSquare(m.ToX, m.ToY);

            // 앙파상 대상 칸 갱신
            EnPassantX = EnPassantY = -1;
            if (mover.Type == PieceType.Pawn && Math.Abs(m.ToY - m.FromY) == 2)
            {
                EnPassantX = m.FromX;
                EnPassantY = (m.FromY + m.ToY) / 2;
            }

            HalfmoveClock = (mover.Type == PieceType.Pawn || isCapture) ? 0 : HalfmoveClock + 1;
            if (SideToMove == PieceColor.Black) FullmoveNumber++;
            SideToMove = SideToMove.Opponent();
        }

        void ClearCastlingIfRookSquare(int x, int y)
        {
            if (x == 0 && y == 0) WhiteCastleQueen = false;
            if (x == 7 && y == 0) WhiteCastleKing = false;
            if (x == 0 && y == 7) BlackCastleQueen = false;
            if (x == 7 && y == 7) BlackCastleKing = false;
        }

        // ───────────────────────── 게임 결과 ─────────────────────────

        public GameResult GetResult()
        {
            if (GetLegalMoves().Count == 0)
            {
                if (!IsInCheck(SideToMove)) return GameResult.Draw;   // 스테일메이트
                return SideToMove == PieceColor.White ? GameResult.BlackWins : GameResult.WhiteWins;
            }
            if (HalfmoveClock >= 100) return GameResult.Draw;          // 50수 규칙
            return GameResult.Ongoing;
        }

        // ───────────────────────── 표기 변환 ─────────────────────────

        public string ToFen()
        {
            var sb = new StringBuilder();
            for (int y = 7; y >= 0; y--)
            {
                int empty = 0;
                for (int x = 0; x < 8; x++)
                {
                    var p = squares[x, y];
                    if (p.IsEmpty) { empty++; continue; }
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    char ch = PieceChar(p.Type);
                    sb.Append(p.Color == PieceColor.White ? ch : char.ToLower(ch));
                }
                if (empty > 0) sb.Append(empty);
                if (y > 0) sb.Append('/');
            }

            sb.Append(SideToMove == PieceColor.White ? " w " : " b ");
            string castling = (WhiteCastleKing ? "K" : "") + (WhiteCastleQueen ? "Q" : "")
                            + (BlackCastleKing ? "k" : "") + (BlackCastleQueen ? "q" : "");
            sb.Append(castling.Length > 0 ? castling : "-");
            sb.Append(' ');
            sb.Append(EnPassantX >= 0 ? SquareName(EnPassantX, EnPassantY) : "-");
            sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
            return sb.ToString();
        }

        /// <summary>UCI 문자열(예: "e2e4", "e7e8q")을 현재 보드의 합법수로 변환한다.</summary>
        public bool TryParseUci(string uci, out Move move)
        {
            move = default(Move);
            if (string.IsNullOrEmpty(uci) || uci.Length < 4) return false;
            int fx = uci[0] - 'a', fy = uci[1] - '1', tx = uci[2] - 'a', ty = uci[3] - '1';
            var promo = uci.Length >= 5 ? TypeFromChar(uci[4]) : PieceType.None;

            foreach (var m in GetLegalMoves())
            {
                if (m.FromX == fx && m.FromY == fy && m.ToX == tx && m.ToY == ty && m.Promotion == promo)
                {
                    move = m;
                    return true;
                }
            }
            return false;
        }

        // ───────────────────────── 유틸 ─────────────────────────

        public static bool InBounds(int x, int y) { return x >= 0 && x < 8 && y >= 0 && y < 8; }

        public static string SquareName(int x, int y) { return "" + (char)('a' + x) + (char)('1' + y); }

        public static char PieceChar(PieceType t)
        {
            switch (t)
            {
                case PieceType.Pawn: return 'P';
                case PieceType.Knight: return 'N';
                case PieceType.Bishop: return 'B';
                case PieceType.Rook: return 'R';
                case PieceType.Queen: return 'Q';
                case PieceType.King: return 'K';
                default: return '?';
            }
        }

        static PieceType TypeFromChar(char ch)
        {
            switch (char.ToLower(ch))
            {
                case 'p': return PieceType.Pawn;
                case 'n': return PieceType.Knight;
                case 'b': return PieceType.Bishop;
                case 'r': return PieceType.Rook;
                case 'q': return PieceType.Queen;
                case 'k': return PieceType.King;
                default: return PieceType.None;
            }
        }

        bool IsPiece(int x, int y, PieceType type, PieceColor color)
        {
            if (!InBounds(x, y)) return false;
            var p = squares[x, y];
            return p.Type == type && p.Color == color;
        }
    }
}
