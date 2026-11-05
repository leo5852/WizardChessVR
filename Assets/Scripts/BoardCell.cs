using UnityEngine;

namespace WizardChess
{
    /// <summary>
    /// 칸 또는 기물 오브젝트에 붙어서 "보드의 몇 번째 칸인지"를 기억한다.
    /// 포인터나 마우스로 클릭되면 BoardView에 칸 좌표를 전달하는 용도.
    /// </summary>
    public class BoardCell : MonoBehaviour
    {
        public BoardView Owner { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }

        /// <summary>기물 전용: 칸 중심에서 오브젝트 피벗까지의 보정값. 모델마다 피벗 위치가 달라서 필요.</summary>
        public Vector3 PlacementOffset { get; set; }

        public void Init(BoardView owner, int x, int y)
        {
            Owner = owner;
            SetSquare(x, y);
        }

        public void SetSquare(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}