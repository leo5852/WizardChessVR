using System;
using System.Collections.Generic;
using UnityEngine;
using WizardChess.AI;
using WizardChess.Core;

namespace WizardChess
{
    /// <summary>
    /// 게임 상태를 소유하고 턴을 진행한다: Board, 누구 차례인지, busy / 게임 종료, AI 호출, 승패 판정.
    /// </summary>
    /// <remarks>
    /// <para>화면 쪽(3D 보드, 지휘 패널 등)은 Board를 직접 바꾸지 않는다.
    /// 이 클래스의 조회 API로 읽고, 이벤트를 구독하고, 수는 TrySubmitPlayerMove로 낸다.</para>
    /// <para>한 수의 이벤트 순서: OnMoveDecided → OnCapture(캡처일 때만) → 연출 대기 → 보드 반영
    /// → OnPromotion(승진일 때만) → OnBoardChanged → OnGameOver 또는 다음 차례의 OnTurnStart.</para>
    /// </remarks>
    public class GameFlow : MonoBehaviour
    {
        // ───────────────────────── 설정 (인스펙터) ─────────────────────────

        [SerializeField] PieceColor playerColor = PieceColor.White;
        [Tooltip("비워두면 RandomAI를 자동으로 사용")]
        [SerializeField] ChessAIBase ai;
        [SerializeField] float aiThinkDelay = 0.3f;

        // ───────────────────────── 게임 상태 ─────────────────────────

        // 이 게임의 유일한 보드. GameFlow만 MakeMove를 호출한다.
        private Board board;
        // 수를 연출하는 중이거나 AI가 생각 중이면 true. 이때는 플레이어 입력을 받지 않는다.
        private bool busy;
        private bool gameOver;
        // 게임이 끝난 뒤에도 결과를 물어볼 수 있도록 저장해 둔다.
        private GameResult result = GameResult.Ongoing;

        // ───────────────────────── 읽기 전용 상태 ─────────────────────────

        /// <summary>플레이어가 맡은 진영</summary>
        public PieceColor PlayerColor => playerColor;
        /// <summary>지금 둘 차례인 진영</summary>
        public PieceColor SideToMove => board.SideToMove;
        /// <summary>수를 연출하는 중이거나 AI가 생각 중이면 true</summary>
        public bool IsBusy => busy;
        /// <summary>게임이 끝났는지</summary>
        public bool IsGameOver => gameOver;
        /// <summary>게임 결과. 진행 중이면 Ongoing.</summary>
        public GameResult Result => result;
        /// <summary>지금 플레이어가 수를 낼 수 있으면 true</summary>
        public bool IsPlayerTurn => !busy && !gameOver && board.SideToMove == playerColor;

        // ───────────────────────── 보드 조회 ─────────────────────────
        // Board는 MakeMove가 public이라 통째로 내주지 않고, 필요한 조회만 열어 둔다.

        /// <summary>(x, y) 칸의 기물. 빈 칸이면 Piece.Empty.</summary>
        public Piece GetPiece(int x, int y) => board[x, y];
        /// <summary>color 진영의 킹이 체크 상태인지</summary>
        public bool IsInCheck(PieceColor color) => board.IsInCheck(color);
        /// <summary>현재 보드의 FEN 문자열</summary>
        public string ToFen() => board.ToFen();
        /// <summary>둘 차례인 진영의 합법수 전체. 부를 때마다 새로 계산한다.</summary>
        public IReadOnlyList<Move> GetLegalMoves() => board.GetLegalMoves();
        /// <summary>(x, y)에 있는 기물의 합법수. 그 기물의 차례가 아니면 비어 있다.</summary>
        public IReadOnlyList<Move> GetLegalMovesFrom(int x, int y) => board.GetLegalMovesFrom(x, y);
        /// <summary>현재 보드의 복사본. 수정해도 게임에는 영향이 없다.</summary>
        public Board GetBoardSnapshot() => board.Clone();

        // ───────────────────────── 이벤트 ─────────────────────────

        /// <summary>한 쪽의 차례가 시작됨 (게임 시작 시에도 한 번 발생). 인자는 둘 차례인 진영.</summary>
        public event Action<PieceColor> OnTurnStart;
        /// <summary>수가 정해짐 (플레이어 또는 AI). 보드는 아직 수를 두기 전 상태.</summary>
        public event Action<Move> OnMoveDecided;
        /// <summary>정해진 수가 기물을 잡음. 인자는 그 수와 잡힌 기물. OnMoveDecided 직후, 연출 전에 발생.</summary>
        public event Action<Move, Piece> OnCapture;
        /// <summary>승진이 적용됨. 보드에는 이미 승진한 기물이 있다.</summary>
        public event Action<Move> OnPromotion;
        /// <summary>수가 보드에 반영됨. 화면은 여기서 보드를 다시 읽으면 된다.</summary>
        public event Action OnBoardChanged;
        /// <summary>게임이 끝남. 인자는 결과.</summary>
        public event Action<GameResult> OnGameOver;

        // ───────────────────────── 연출 등록 ─────────────────────────

        // 수를 보드에 반영하기 전에 끝나기를 기다릴 연출 목록 (5단계에서 사용)
        private readonly List<IMovePresenter> presenters = new List<IMovePresenter>();

        // ───────────────────────── 초기화 ─────────────────────────

        private void Awake()
        {
            // 화면 쪽이 자기 Start에서 보드를 읽을 수 있도록 Start가 아니라 Awake에서 만든다.
            board = Board.CreateStandard();
            if (ai == null)
            {
                // ChessAIBase는 MonoBehaviour라 new 대신 컴포넌트로 붙인다.
                ai = gameObject.AddComponent<RandomAI>();
            }
        }

        /// <summary>
        /// 수가 보드에 반영되기 전에 기다릴 연출을 등록한다. 같은 연출은 한 번만 들어간다.
        /// </summary>
        /// <remarks>보통 연출 쪽의 OnEnable에서 부른다.</remarks>
        public void AddPresenter(IMovePresenter presenter)
        {
            if (presenter != null && !presenters.Contains(presenter)) presenters.Add(presenter);
        }

        /// <summary>
        /// 등록한 연출을 뺀다.
        /// </summary>
        /// <remarks>보통 연출 쪽의 OnDisable에서 부른다. 빼먹으면 파괴된 오브젝트가 목록에 남는다.</remarks>
        public void RemovePresenter(IMovePresenter presenter)
        {
            presenters.Remove(presenter);
        }
    }
}
