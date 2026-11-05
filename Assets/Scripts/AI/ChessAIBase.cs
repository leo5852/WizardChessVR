using System.Threading.Tasks;
using UnityEngine;
using WizardChess.Core;

namespace WizardChess.AI
{
    /// <summary>
    /// AI 공통 인터페이스. 랜덤 AI, Stockfish AI 등을 이 클래스를 상속해서 만든다.
    /// BoardView는 이 타입만 알기 때문에 AI를 바꿔도 다른 코드는 수정할 필요가 없다.
    /// </summary>
    public abstract class ChessAIBase : MonoBehaviour
    {
        /// <summary>둘 수를 고른다. 실패하면 null을 돌려준다.</summary>
        public abstract Task<Move?> ChooseMoveAsync(Board board);
    }
}
