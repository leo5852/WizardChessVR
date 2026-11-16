using WizardChess.Core;

namespace WizardChess
{
    /// <summary>
    /// 연출(IMovePresenter)에 넘겨주는 이번 수의 정보.
    /// </summary>
    /// <remarks>
    /// 수가 보드에 반영되기 전에 GameFlow가 만든다.
    /// 그래서 모든 값은 수를 두기 "전" 보드를 기준으로 한다.
    /// </remarks>
    public struct MoveInfo
    {
        /// <summary>이번 수</summary>
        public Move Move { get; }
        /// <summary>움직이는 기물(승진 전)</summary>
        public Piece Mover { get; }

        /// <summary>잡힌 기물, 없으면 Piece.Empty</summary>
        public Piece Captured { get; }
        /// <summary>잡힌 기물이 있던 칸의 x(파일, 0=a)</summary>
        /// <remarks>앙파상 등 특수규칙을 고려해 Move의 정보와 별개로 저장한다.</remarks>
        public int CaptureX { get; }
        /// <summary>잡힌 기물이 있던 칸의 y(랭크, 0=1). 앙파상이면 Move.ToY가 아니라 Move.FromY와 같다.</summary>
        public int CaptureY { get; }

        /// <summary>플레이어가 둔 수인지</summary>
        public bool IsPlayerMove { get; }

        /// <summary>기물을 잡는 수인지</summary>
        public bool IsCapture => !Captured.IsEmpty;
        /// <summary>승진하는 수인지</summary>
        public bool IsPromotion => Move.Promotion != PieceType.None;
        /// <summary>이번 수를 둔 진영(백/흑).</summary>
        public PieceColor Side => Mover.Color;

        /// <param name="move">이번 수</param>
        /// <param name="mover">움직이는 기물(승진 전)</param>
        /// <param name="captured">잡힌 기물, 없으면 Piece.Empty</param>
        /// <param name="captureX">잡힌 기물이 있던 칸의 x</param>
        /// <param name="captureY">잡힌 기물이 있던 칸의 y</param>
        /// <param name="isPlayerMove">플레이어가 둔 수인지</param>
        public MoveInfo(Move move, Piece mover, Piece captured, int captureX, int captureY, bool isPlayerMove)
        {
            Move = move;
            Mover = mover;
            Captured = captured;
            CaptureX = captureX;
            CaptureY = captureY;
            IsPlayerMove = isPlayerMove;
        }
    }
}
