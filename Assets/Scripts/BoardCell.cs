using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace WizardChess
{
    /// <summary>
    /// 칸 또는 기물 오브젝트에 붙어서 "보드의 몇 번째 칸인지"를 기억한다.
    /// XR 포인터로 선택되면 Selected 이벤트로 알린다. 에디터 마우스 입력은 레이캐스트로 이 컴포넌트를 찾아 좌표만 읽는다.
    /// </summary>
    public class BoardCell : MonoBehaviour
    {
        public BoardView Owner { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }

        /// <summary>기물 전용: 칸 중심에서 오브젝트 피벗까지의 보정값. 모델마다 피벗 위치가 달라서 필요.</summary>
        public Vector3 PlacementOffset { get; set; }

        /// <summary>XR 포인터로 어떤 칸이든 선택되면 발생. 어느 보드인지는 Owner로 구분.</summary>
        public static event Action<BoardCell> Selected;

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

        /// <summary>이 Interactable이 포인터로 선택되면 Selected 이벤트를 쏘도록 연결한다.</summary>
        public void BindInteractable(XRSimpleInteractable interactable)
        {
            interactable.selectEntered.AddListener(_ =>
            {
                Selected?.Invoke(this);
            });
        }
    }
}