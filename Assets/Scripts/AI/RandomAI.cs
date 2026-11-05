using System.Threading.Tasks;
using WizardChess.Core;

namespace WizardChess.AI
{
    /// <summary>
    /// 프로토타입용 AI. 합법수 중 무작위로 두되, 잡을 수 있으면 70% 확률로 잡는다.
    /// Stockfish를 연결하기 전이나 Stockfish가 실패했을 때 대신 사용된다.
    /// </summary>
    public class RandomAI : ChessAIBase
    {
        static readonly System.Random Rng = new System.Random();

        public override Task<Move?> ChooseMoveAsync(Board board)
        {
            return Task.FromResult(PickMove(board));
        }

        public static Move? PickMove(Board board)
        {
            var moves = board.GetLegalMoves();
            if (moves.Count == 0) return null;

            var captures = moves.FindAll(m => m.IsEnPassant || !board[m.ToX, m.ToY].IsEmpty);
            var pool = (captures.Count > 0 && Rng.NextDouble() < 0.7) ? captures : moves;
            return pool[Rng.Next(pool.Count)];
        }
    }
}
