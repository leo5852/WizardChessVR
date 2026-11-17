using System;
using System.Collections;
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
        private bool busy = true;
        private bool gameOver;
        // 게임이 끝난 뒤에도 결과를 물어볼 수 있도록 저장해 둔다.
        private GameResult result = GameResult.Ongoing;

        // 수를 보드에 반영하기 전에 끝나기를 기다릴 연출 목록 (RunPresenters에서 실행)
        private readonly List<IMovePresenter> presenters = new List<IMovePresenter>();

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

        private void Start()
        {
            StartCoroutine(BeginGame());
        }

        /// <summary>첫 턴을 시작한다.</summary>
        private IEnumerator BeginGame()
        {
            // 모든 화면의 Start가 끝난 뒤 첫 이벤트가 나가도록 한 프레임 기다린다
            yield return null;
            BeginTurn();
        }

        // ───────────────────────── 연출 등록 ─────────────────────────

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

        // ───────────────────────── 플레이어 입력 ─────────────────────────

        /// <summary>
        /// 플레이어의 수를 제출한다. 출발·도착 칸과 승진 기물만 비교한다.
        /// </summary>
        /// <remarks>
        /// 음성·걷기처럼 직접 만든 Move에는 IsCastle/IsEnPassant 플래그가 없으므로,
        /// 받은 move가 아니라 합법수 목록에서 찾은 수로 진행한다.
        /// </remarks>
        /// <returns>플레이어 차례가 아니거나 합법수가 아니면 false</returns>
        public bool TrySubmitPlayerMove(Move move)
        {
            if (!IsPlayerTurn) return false;
            foreach (var m in GetLegalMoves())
            {
                // 합법수 목록에서 출발 칸, 도착 칸, 프로모션 기물이 같은 수를 찾는다.
                if (m.FromX == move.FromX && m.FromY == move.FromY
                                          && m.ToX == move.ToX && m.ToY == move.ToY
                                          && m.Promotion == move.Promotion)
                {
                    StartCoroutine(RunPlayerMove(m));
                    return true;
                }
            }
            return false;
        }

        // ───────────────────────── 턴 진행 ─────────────────────────

        /// <summary>
        /// 둘 차례인 쪽의 턴을 시작한다: 플레이어면 입력을 풀고, AI면 AI를 실행한다.
        /// </summary>
        /// <remarks>
        /// busy를 푸는 곳은 여기(플레이어 차례)와 CheckGameOver 두 곳뿐이다.
        /// 코루틴을 중첩해서 AI를 기다리지 않는 이유: 플레이어 차례가 시작된 직후 새 수가 들어오면
        /// 바깥 코루틴이 나중에 깨어나 busy를 덮어쓰기 때문이다.
        /// </remarks>
        private void BeginTurn()
        {
            if (SideToMove == PlayerColor)
            {
                busy = false;
                OnTurnStart?.Invoke(SideToMove);
            }
            else
            {
                busy = true;
                OnTurnStart?.Invoke(SideToMove);
                StartCoroutine(RunAITurn());
            }
        }

        /// <summary>플레이어의 수를 진행하고, 게임이 안 끝났으면 다음 턴(AI)을 시작한다.</summary>
        private IEnumerator RunPlayerMove(Move move)
        {
            busy = true;
            yield return ApplyMove(move, true);
            if (!CheckGameOver()) BeginTurn();
        }

        /// <summary>AI가 수를 고르고 진행한 뒤, 게임이 안 끝났으면 다음 턴(플레이어)을 시작한다.</summary>
        private IEnumerator RunAITurn()
        {
            busy = true;
            // 너무 빨리 두면 플레이어가 상황을 못 보므로 잠깐 기다린다.
            yield return new WaitForSeconds(aiThinkDelay);

            // AI에는 복사본을 넘긴다. 엔진이 생각하는 동안 원본 보드가 바뀌지 않게 하기 위해서다.
            var task = ai.ChooseMoveAsync(board.Clone());
            yield return new WaitUntil(() => task.IsCompleted);

            Move? move = task.IsFaulted ? null : task.Result;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            // Stockfish가 실패하거나 없는 환경(Mac 에디터)이면 무작위 수로 대신 둔다.
            if (move == null)
            {
                Debug.LogWarning("AI가 수를 고르지 못해 랜덤 수로 대신 둡니다.");
                move = RandomAI.PickMove(board);
            }

            if (move.HasValue)
            {
                yield return ApplyMove(move.Value, false);
                if (!CheckGameOver()) BeginTurn();
            }
            else
            {
                busy = false;
            }
        }

        /// <summary>
        /// 한 수를 진행한다: 이벤트 → 연출 대기 → 보드 반영 → 이벤트.
        /// </summary>
        /// <remarks>승패 판정과 다음 턴 시작은 부르는 쪽(RunPlayerMove, RunAITurn)이 한다.</remarks>
        /// <param name="move">합법수 목록에서 나온 수</param>
        /// <param name="isPlayerMove">플레이어가 둔 수인지</param>
        private IEnumerator ApplyMove(Move move, bool isPlayerMove)
        {
            // 보드가 바뀌기 전에 연출에 넘길 정보를 모은다. MakeMove 뒤에는 출발 칸과 잡힌 기물을 알 수 없다.
            Piece mover = board[move.FromX, move.FromY];
            int capX = move.ToX;
            int capY = move.IsEnPassant ? move.FromY : move.ToY;
            Piece captured = board[capX, capY];
            var info = new MoveInfo(move,mover, captured, capX, capY, isPlayerMove);   // 연출에 넘길 정보
            
            OnMoveDecided?.Invoke(move);
            if (!captured.IsEmpty) OnCapture?.Invoke(move, captured);
            
            // 등록된 연출이 모두 끝날 때까지 보드를 바꾸지 않는다.
            yield return RunPresenters(info);
            
            // 여기서 보드가 바뀌고 차례도 상대에게 넘어간다.
            board.MakeMove(move);
            if (info.IsPromotion) OnPromotion?.Invoke(move);
            
            // 분리 전과 같은 로그
            Debug.Log("수: " + move.ToUci() + "   FEN: " + board.ToFen());
            OnBoardChanged?.Invoke();
        }

        /// <summary>
        /// 승패를 판정한다. 끝났으면 상태를 정리하고 OnGameOver를 쏜다.
        /// </summary>
        /// <returns>게임이 끝났으면 true</returns>
        private bool CheckGameOver()
        {
            GameResult r = board.GetResult();
            
            if (r != GameResult.Ongoing)
            {
                gameOver = true;
                result = r;
                busy = false;
                OnGameOver?.Invoke(result);
                return true;
            }

            return false;
        }

        // ───────────────────────── 연출 실행 ─────────────────────────

        /// <summary>
        /// 등록된 연출을 모두 동시에 시작하고, 전부 끝날 때까지 기다린다.
        /// </summary>
        /// <remarks>가장 오래 걸리는 연출이 끝날 때 이 코루틴도 끝난다.</remarks>
        private IEnumerator RunPresenters(MoveInfo info)
        {
            // 등록된 연출이 없으면 기다리지 않는다.
            if (presenters.Count == 0) yield break;
            // 연출 도중 누가 등록·해제해도 반복이 깨지지 않도록 복사본으로 돈다.
            IMovePresenter[] snapshot = presenters.ToArray();

            // 1) 전부 먼저 시작해 둔다. 여기서 하나씩 기다리면 연출이 동시가 아니라 차례대로 돈다.
            List<Coroutine> running = new List<Coroutine>();
            foreach (IMovePresenter p in snapshot)
            {
                Coroutine c = StartCoroutine(p.PresentMove(info));
                running.Add(c);
            }
            
            // 2) 하나씩 기다린다. 이미 끝난 연출은 기다리지 않고 바로 지나간다.
            foreach (var c in running) yield return c;
        }
    }
}
