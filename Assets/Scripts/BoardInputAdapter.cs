using System;
using System.Collections.Generic;
using UnityEngine;
using WizardChess.Core;
#if UNITY_EDITOR
using UnityEngine.InputSystem;   // 마우스는 에디터에서만 쓰므로 using도 #if 안에
#endif

namespace WizardChess
{
    /// <summary>
    /// 입력을 GameFlow가 받을 수 있는 수로 바꿔 넘기고, "선택된 기물" 상태를 관리한다.
    /// </summary>
    /// <remarks>
    /// <para>모든 입력은 결국 아래 중 하나로 들어온다.</para>
    /// <para>- HandleSquareClicked(x, y): 클릭 방식 입력 (XR 포인터, 에디터 마우스, 나중에 지휘 패널).
    /// 첫 클릭은 내 기물 선택, 두 번째 클릭은 목적지.</para>
    /// <para>- Select / ClearSelection / TryMoveSelectedTo: 선택을 직접 다루는 입력 (예: 패널에서 지정 → 음성으로 확정).</para>
    /// <para>- GameFlow.TrySubmitPlayerMove: 수 전체를 이미 아는 입력 (걷기 이동 등).</para>
    /// <para>3D 보드 하이라이트는 여기서 BoardView를 직접 불러 그린다(의존 방향: 입력 → BoardView, GameFlow).
    /// BoardView가 모르는 다른 화면(나중에 지휘 패널 등)은 OnSelectionChanged / OnSelectionCleared를 구독한다.</para>
    /// </remarks>
    public class BoardInputAdapter : MonoBehaviour
    {
        // ───────────────────────── 연결 ─────────────────────────

        [SerializeField] GameFlow gameFlow;
        [Tooltip("XR 포인터/마우스 클릭을 받을 3D 보드. 비워두면 같은 오브젝트에서 찾음")]
        [SerializeField] BoardView boardView;

        // ───────────────────────── 디버그 설정 ─────────────────────────

        [Tooltip("에디터에서 Game 뷰 마우스 클릭으로도 조작 가능")]
#pragma warning disable 0414   // 빌드에서는 Update가 빠지므로 미사용 경고가 뜬다
        [SerializeField] bool mouseInputInEditor = true;
#pragma warning restore 0414
        [Tooltip("클릭될 때마다 콘솔에 어느 칸이 눌렸는지 출력")]
        [SerializeField] bool logClicks = true;

        // ───────────────────────── 선택 상태 ─────────────────────────
        // 선택이 있느냐 없느냐로 "다음 클릭이 첫 클릭인지 두 번째 클릭인지"를 판단한다.

        readonly List<Move> selectedMoves = new List<Move>();   // 선택한 기물의 합법수
        int selectedX = -1, selectedY = -1;                     // -1이면 선택 없음

        // ───────────────────────── 이벤트 ─────────────────────────

        /// <summary>기물이 선택됨. 인자: 칸 x, 칸 y, 그 기물의 합법수. (3D 보드 외 화면용)</summary>
        public event Action<int, int, IReadOnlyList<Move>> OnSelectionChanged;
        /// <summary>선택이 해제됨. 선택이 있었을 때만 발생한다. (3D 보드 외 화면용)</summary>
        public event Action OnSelectionCleared;

        // ───────────────────────── 초기화·구독 ─────────────────────────

        void Awake()
        {
            // 인스펙터에서 비워 두면 같은 오브젝트의 컴포넌트를 쓴다.
            if (gameFlow == null) gameFlow = GetComponent<GameFlow>();
            if (boardView == null) boardView = GetComponent<BoardView>();
        }

        void OnEnable()
        {
            BoardCell.Selected += OnCellSelected;
            gameFlow.OnMoveDecided += OnMoveDecided;
        }

        void OnDisable()
        {
            // static 이벤트는 해제하지 않으면 파괴된 뒤에도 계속 불리므로 꼭 뺀다.
            gameFlow.OnMoveDecided -= OnMoveDecided;
            BoardCell.Selected -= OnCellSelected;
        }

#if UNITY_EDITOR
        // 에디터 전용: Game 뷰 마우스 클릭을 레이캐스트로 칸에 연결한다.
        void Update()
        {
            if (!mouseInputInEditor) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                var cell = hit.collider.GetComponentInParent<BoardCell>();   // 모델은 콜라이더가 자식에 있을 수 있음
                if (cell != null && cell.Owner == boardView) HandleSquareClicked(cell.X, cell.Y);
            }
        }
#endif

        // ───────────────────────── 공개 API ─────────────────────────

        /// <summary>
        /// 클릭 방식 입력 (포인터, 마우스, 나중에 지휘 패널).
        /// 선택이 있고 합법 목적지면 수를 내고, 내 기물이면 선택하고, 그 외에는 선택을 푼다.
        /// </summary>
        public void HandleSquareClicked(int x, int y)
        {
            Piece piece = gameFlow.GetPiece(x, y);
            // 분리 전과 같은 클릭 로그
            if (logClicks)
                Debug.Log("[WizardChess] 클릭: " + Board.SquareName(x, y) + " (" + piece + ")"
                          + (gameFlow.IsBusy ? "  → 무시됨: 애니메이션/AI 진행 중" : "")
                          + (gameFlow.IsGameOver ? "  → 무시됨: 게임 종료" : "")
                          + (gameFlow.SideToMove != gameFlow.PlayerColor ? "  → 무시됨: 내 차례 아님" : ""));

            if (!gameFlow.IsPlayerTurn) return;

            // 두 번째 클릭: 선택한 기물의 목적지면 수를 낸다.
            if (TryMoveSelectedTo(x, y)) return;

            // 첫 클릭(또는 다른 내 기물로 갈아타기)
            if (!piece.IsEmpty && piece.Color == gameFlow.PlayerColor)
            {
                Select(x, y);
            }
            else
            {
                ClearSelection();
            }
        }

        /// <summary>선택한 기물을 (x, y)로 옮기는 수를 제출. 실패하면 false.</summary>
        /// <remarks>받아들여지면 GameFlow의 OnMoveDecided에서 선택이 풀린다.</remarks>
        public bool TryMoveSelectedTo(int x, int y)
        {
            if (selectedX == -1 && selectedY == -1) return false;

            Move? move = null;
            
            foreach (Move m in selectedMoves)
            {
                if (m.ToX == x && m.ToY == y && m.Promotion is PieceType.None or PieceType.Queen)
                {
                    // TODO: 승진 기물을 플레이어가 고르게 하기. 지금은 퀸 자동 선택.
                    move = m;
                    break;
                }
            }
            
            if (!move.HasValue) return false;

            return gameFlow.TrySubmitPlayerMove(move.Value);
        }

        /// <summary>(x, y)의 기물을 선택하고 합법수를 알린다.</summary>
        public void Select(int x, int y)
        {
            selectedMoves.Clear();
            selectedX = x;
            selectedY = y;

            selectedMoves.AddRange(gameFlow.GetLegalMovesFrom(x, y));

            if (boardView != null) boardView.ShowSelection(x, y, selectedMoves);
            OnSelectionChanged?.Invoke(x, y, selectedMoves);
        }

        /// <summary>선택을 푼다.</summary>
        public void ClearSelection()
        {
            bool wasSelected = selectedX != -1 && selectedY != -1;
            selectedX = -1;
            selectedY = -1;
            selectedMoves.Clear();

            // 선택이 없었는데 매번 쏘면 화면이 쓸데없이 다시 칠하므로, 있었을 때만 알린다.
            if (wasSelected)
            {
                if (boardView != null) boardView.ClearHighlights();
                OnSelectionCleared?.Invoke();
            }
        }

        // ───────────────────────── 이벤트 처리 ─────────────────────────

        // XR 포인터로 칸이 선택됨. 다른 보드의 칸이면 무시한다.
        void OnCellSelected(BoardCell cell)
        {
            if (cell.Owner == boardView)
            {
                HandleSquareClicked(cell.X, cell.Y);
            }
        }

        // 누가 수를 냈든(클릭, 음성, 걷기) 수가 정해졌으니 선택을 푼다.
        void OnMoveDecided(Move move)
        {
            ClearSelection();
        }
    }
}
