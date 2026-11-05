using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;   // XR Interaction Toolkit 3.x (Unity 6 VR 템플릿 기본)
using WizardChess.AI;
using WizardChess.Core;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace WizardChess
{
    /// <summary>
    /// 프로토타입 보드. 회색 큐브 칸과 단순 도형 기물을 자동 생성하고,
    /// VR 포인터(또는 에디터 마우스)로 기물을 골라 옮기면 AI가 응수한다.
    ///
    /// 사용법: 빈 GameObject에 이 컴포넌트를 붙이고 플레이 위치 앞(예: 0, 0.8, 0.6)에 둔다.
    /// 백 진영이 -Z 쪽(플레이어 쪽), 흑 진영이 +Z 쪽에 생성된다.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("보드")]
        [Tooltip("칸 한 변의 길이(m). 0.1이면 보드 전체가 80cm")]
        [SerializeField] float squareSize = 0.1f;
        [SerializeField] Color lightSquareColor = new Color(0.93f, 0.87f, 0.73f);
        [SerializeField] Color darkSquareColor = new Color(0.55f, 0.38f, 0.24f);
        [SerializeField] Color selectedColor = new Color(1f, 0.82f, 0.2f);
        [SerializeField] Color moveTargetColor = new Color(0.45f, 0.85f, 0.45f);

        [Header("기물 모델 (비워두면 회색 도형 사용)")]
        [Tooltip("백 기물 프리팹 6종")]
        [SerializeField] PiecePrefabSet whitePiecePrefabs = new PiecePrefabSet();
        [Tooltip("흑 기물 프리팹 6종. 비워두면 백 프리팹에 흑 재질/색을 입혀 사용")]
        [SerializeField] PiecePrefabSet blackPiecePrefabs = new PiecePrefabSet();
        [Tooltip("흑 프리팹이 없어서 백 프리팹을 재사용할 때 입힐 재질 (비우면 Black Piece Color로 색만 바꿈)")]
        [SerializeField] Material blackMaterialForSharedPrefabs;
        [Tooltip("켜면 킹 높이를 기준으로 모델 크기를 칸 크기에 자동으로 맞춤")]
        [SerializeField] bool autoFitModels = true;
        [Tooltip("자동 맞춤 시 킹 높이 (칸 한 변 대비 배수)")]
        [SerializeField] float kingHeightInSquares = 1.6f;
        [Tooltip("자동 맞춤을 끈 경우 사용할 배율")]
        [SerializeField] float modelScale = 1f;
        [Tooltip("모델이 누워 있거나 엉뚱한 방향을 볼 때 보정 회전 (예: X -90)")]
        [SerializeField] Vector3 modelRotation = Vector3.zero;
        [Tooltip("흑 기물을 180도 돌려서 백 쪽을 바라보게 함 (나이트 방향)")]
        [SerializeField] bool rotateBlackPieces = true;

        [Header("기물 (도형/색)")]
        [SerializeField] Color whitePieceColor = new Color(0.95f, 0.93f, 0.88f);
        [SerializeField] Color blackPieceColor = new Color(0.15f, 0.15f, 0.18f);
        [Tooltip("기물 이동 애니메이션 시간(초)")]
        [SerializeField] float moveDuration = 0.4f;

        [Header("게임")]
        [SerializeField] PieceColor playerColor = PieceColor.White;
        [Tooltip("비워두면 RandomAI를 자동으로 사용")]
        [SerializeField] ChessAIBase ai;
        [SerializeField] float aiThinkDelay = 0.3f;
        [Tooltip("상태 표시용 텍스트 (선택 사항)")]
        [SerializeField] TMPro.TMP_Text statusText;

        [Header("디버그")]
        [Tooltip("에디터에서 Game 뷰 마우스 클릭으로도 조작 가능")]
#pragma warning disable 0414   // 빌드에서는 Update가 빠지므로 미사용 경고가 뜬다
        [SerializeField] bool mouseInputInEditor = true;
#pragma warning restore 0414
        [Tooltip("클릭될 때마다 콘솔에 어느 칸이 눌렸는지 출력")]
        [SerializeField] bool logClicks = true;

        Board board;
        readonly Renderer[,] squareRenderers = new Renderer[8, 8];
        readonly GameObject[,] pieceObjects = new GameObject[8, 8];
        readonly List<Move> selectedMoves = new List<Move>();
        int selectedX = -1, selectedY = -1;
        bool busy;
        bool gameOver;

        public Board CurrentBoard { get { return board; } }

        // ───────────────────────── 초기화 ─────────────────────────

        void Start()
        {
            board = Board.CreateStandard();
            if (ai == null) ai = gameObject.AddComponent<RandomAI>();

            BuildSquares();
            SpawnAllPieces();
            RefreshStatus();

            if (board.SideToMove != playerColor) StartCoroutine(RunAITurn());
        }

        void BuildSquares()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                {
                    var sq = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sq.name = "Square_" + Board.SquareName(x, y);
                    sq.transform.SetParent(transform, false);
                    sq.transform.localPosition = SquareCenter(x, y) + Vector3.down * 0.01f;
                    sq.transform.localScale = new Vector3(squareSize, 0.02f, squareSize);

                    var r = sq.GetComponent<Renderer>();
                    r.material.color = BaseSquareColor(x, y);
                    squareRenderers[x, y] = r;

                    MakeClickable(sq, x, y);
                }
        }

        void SpawnAllPieces()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    if (!board[x, y].IsEmpty)
                        pieceObjects[x, y] = CreatePieceObject(board[x, y], x, y);
        }

        GameObject CreatePieceObject(Piece piece, int x, int y)
        {
            bool reuseWhitePrefab;
            GameObject prefab = FindPrefab(piece, out reuseWhitePrefab);

            Vector3 offset;   // 칸 중심에서 모델 피벗까지의 거리 (바닥 중앙이 칸 중심에 오도록)
            GameObject go = prefab != null
                ? CreateModelPiece(piece, prefab, reuseWhitePrefab, out offset)
                : CreatePrimitivePiece(piece, out offset);

            go.name = piece.Color + "_" + piece.Type;
            go.transform.localPosition = SquareCenter(x, y) + offset;
            MakeClickable(go, x, y).PlacementOffset = offset;
            return go;
        }

        GameObject FindPrefab(Piece piece, out bool reuseWhitePrefab)
        {
            reuseWhitePrefab = false;
            var own = piece.Color == PieceColor.White ? whitePiecePrefabs : blackPiecePrefabs;
            GameObject prefab = own != null ? own.Get(piece.Type) : null;

            if (prefab == null && piece.Color == PieceColor.Black && whitePiecePrefabs != null)
            {
                prefab = whitePiecePrefabs.Get(piece.Type);
                reuseWhitePrefab = prefab != null;
            }
            return prefab;
        }

        GameObject CreateModelPiece(Piece piece, GameObject prefab, bool reuseWhitePrefab, out Vector3 offset)
        {
            var go = Instantiate(prefab, transform);
            float yaw = (piece.Color == PieceColor.Black && rotateBlackPieces) ? 180f : 0f;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(modelRotation);
            go.transform.localScale = prefab.transform.localScale * GetModelScale();

            if (reuseWhitePrefab) ApplyBlackLook(go);
            StripModelComponents(go);
            go.layer = 0;   // Default 레이어 (Ignore Raycast 등이면 레이저가 통과해 버림)
            EnsureCollider(go);

            // 보드 기준으로 모델의 크기를 재서, 바닥 중앙이 칸 중심에 오도록 위치 보정값 계산
            Bounds b = GetBoundsIn(transform, go);
            offset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            return go;
        }

        float cachedAutoScale = -1f;

        /// <summary>킹 모델 높이를 재서 모든 모델에 같은 배율을 적용 (세트 안의 비율 유지).</summary>
        float GetModelScale()
        {
            if (!autoFitModels) return modelScale;
            if (cachedAutoScale > 0f) return cachedAutoScale;

            GameObject reference = whitePiecePrefabs != null ? whitePiecePrefabs.king : null;
            if (reference == null && blackPiecePrefabs != null) reference = blackPiecePrefabs.king;
            if (reference == null)
            {
                Debug.LogWarning("자동 크기 맞춤에는 킹 프리팹이 필요합니다. Model Scale 값을 사용합니다.");
                cachedAutoScale = modelScale;
                return cachedAutoScale;
            }

            var probe = Instantiate(reference, transform);
            probe.transform.localPosition = Vector3.zero;
            probe.transform.localRotation = Quaternion.Euler(modelRotation);
            probe.transform.localScale = reference.transform.localScale;
            float height = GetBoundsIn(transform, probe).size.y;
            DestroyImmediate(probe);

            cachedAutoScale = height > 0.0001f ? kingHeightInSquares * squareSize / height : 1f;
            return cachedAutoScale;
        }

        void ApplyBlackLook(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (blackMaterialForSharedPrefabs != null)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = blackMaterialForSharedPrefabs;
                    r.sharedMaterials = mats;
                }
                else
                {
                    foreach (var m in r.materials) m.color = blackPieceColor;
                }
            }
        }

        /// <summary>
        /// 에셋에 원래 들어 있던 XR 상호작용·물리·콜라이더를 제거한다.
        /// 메쉬 콜라이더는 Mesh가 비어 있거나 Trigger로 되어 있으면 레이저에 안 맞고,
        /// 가느다란 부분은 맞히기도 어렵다. 그래서 지우고 EnsureCollider에서 박스 하나로 새로 만든다.
        /// </summary>
        static void StripModelComponents(GameObject go)
        {
            // Grab Interactable이 Rigidbody를 필요로 하므로 상호작용 → 조인트 → 리지드바디 → 콜라이더 순서로 제거
            foreach (var i in go.GetComponentsInChildren<XRBaseInteractable>(true))
            {
                Debug.Log("[WizardChess] 모델에 있던 " + i.GetType().Name + " 제거: " + i.name);
                DestroyImmediate(i);
            }
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        }

        /// <summary>모델에 콜라이더가 없으면 포인터가 맞을 수 있도록 박스 콜라이더를 씌운다.</summary>
        static void EnsureCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;
            Bounds b = GetBoundsIn(go.transform, go);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
        }

        /// <summary>go의 모든 렌더러를 감싸는 경계 상자를 space 좌표계 기준으로 계산.</summary>
        static Bounds GetBoundsIn(Transform space, GameObject go)
        {
            bool has = false;
            var result = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? wb.min.x : wb.max.x,
                        (i & 2) == 0 ? wb.min.y : wb.max.y,
                        (i & 4) == 0 ? wb.min.z : wb.max.z);
                    Vector3 p = space.InverseTransformPoint(corner);
                    if (!has) { result = new Bounds(p, Vector3.zero); has = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        GameObject CreatePrimitivePiece(Piece piece, out Vector3 offset)
        {
            PrimitiveType shape;
            float width, height;   // 칸 크기 대비 비율
            switch (piece.Type)
            {
                case PieceType.Pawn: shape = PrimitiveType.Cylinder; width = 0.40f; height = 0.50f; break;
                case PieceType.Knight: shape = PrimitiveType.Cube; width = 0.40f; height = 0.70f; break;
                case PieceType.Bishop: shape = PrimitiveType.Capsule; width = 0.45f; height = 0.85f; break;
                case PieceType.Rook: shape = PrimitiveType.Cylinder; width = 0.55f; height = 0.70f; break;
                case PieceType.Queen: shape = PrimitiveType.Capsule; width = 0.55f; height = 1.00f; break;
                default: shape = PrimitiveType.Cylinder; width = 0.55f; height = 1.15f; break;   // King
            }

            var go = GameObject.CreatePrimitive(shape);
            go.transform.SetParent(transform, false);

            // 유니티 기본 실린더·캡슐은 높이가 2, 큐브는 1이라 스케일 계산을 다르게 한다
            float h = height * squareSize, w = width * squareSize;
            go.transform.localScale = shape == PrimitiveType.Cube ? new Vector3(w, h, w) : new Vector3(w, h * 0.5f, w);
            offset = Vector3.up * (h * 0.5f);

            go.GetComponent<Renderer>().material.color =
                piece.Color == PieceColor.White ? whitePieceColor : blackPieceColor;
            return go;
        }

        /// <summary>칸·기물을 VR 포인터와 마우스로 클릭할 수 있게 만든다.</summary>
        BoardCell MakeClickable(GameObject go, int x, int y)
        {
            var cell = go.AddComponent<BoardCell>();
            cell.Init(this, x, y);

            // 콜라이더가 이미 있어야 함: XRSimpleInteractable은 추가되는 순간 자식 콜라이더까지 수집한다
            var interactable = go.AddComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => OnCellClicked(cell.X, cell.Y));
            return cell;
        }

        // ───────────────────────── 입력 ─────────────────────────

#if UNITY_EDITOR
        void Update()
        {
            if (!mouseInputInEditor) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 20f))
            {
                var cell = hit.collider.GetComponentInParent<BoardCell>();   // 모델은 콜라이더가 자식에 있을 수 있음
                if (cell != null && cell.Owner == this) OnCellClicked(cell.X, cell.Y);
            }
        }
#endif

        /// <summary>
        /// 모든 입력(포인터, 마우스, 나중에 음성·걷기)이 결국 이 함수로 모인다.
        /// 첫 클릭: 내 기물 선택 / 두 번째 클릭: 이동할 칸 선택.
        /// </summary>
        public void OnCellClicked(int x, int y)
        {
            if (logClicks)
                Debug.Log("[WizardChess] 클릭: " + Board.SquareName(x, y) + " (" + board[x, y] + ")"
                          + (busy ? "  → 무시됨: 애니메이션/AI 진행 중" : "")
                          + (gameOver ? "  → 무시됨: 게임 종료" : "")
                          + (board.SideToMove != playerColor ? "  → 무시됨: 내 차례 아님" : ""));

            if (busy || gameOver || board.SideToMove != playerColor) return;

            if (selectedX >= 0)
            {
                Move? chosen = null;
                foreach (var m in selectedMoves)
                {
                    if (m.ToX != x || m.ToY != y) continue;
                    // 승진은 프로토타입에서는 퀸으로 자동 선택
                    if (chosen == null || m.Promotion == PieceType.Queen) chosen = m;
                }
                if (chosen.HasValue)
                {
                    ClearSelection();
                    StartCoroutine(RunPlayerMove(chosen.Value));
                    return;
                }
            }

            var piece = board[x, y];
            if (!piece.IsEmpty && piece.Color == playerColor) Select(x, y);
            else ClearSelection();
        }

        /// <summary>외부(음성 명령 등)에서 수를 직접 둘 때 사용. 합법수가 아니면 false.</summary>
        public bool TrySubmitPlayerMove(Move move)
        {
            if (busy || gameOver || board.SideToMove != playerColor) return false;
            foreach (var m in board.GetLegalMoves())
            {
                if (m.FromX == move.FromX && m.FromY == move.FromY && m.ToX == move.ToX && m.ToY == move.ToY
                    && m.Promotion == move.Promotion)
                {
                    ClearSelection();
                    StartCoroutine(RunPlayerMove(m));
                    return true;
                }
            }
            return false;
        }

        void Select(int x, int y)
        {
            ClearSelection();
            selectedX = x;
            selectedY = y;
            selectedMoves.AddRange(board.GetLegalMovesFrom(x, y));

            squareRenderers[x, y].material.color = selectedColor;
            foreach (var m in selectedMoves) squareRenderers[m.ToX, m.ToY].material.color = moveTargetColor;
        }

        void ClearSelection()
        {
            if (selectedX >= 0)
            {
                squareRenderers[selectedX, selectedY].material.color = BaseSquareColor(selectedX, selectedY);
                foreach (var m in selectedMoves)
                    squareRenderers[m.ToX, m.ToY].material.color = BaseSquareColor(m.ToX, m.ToY);
            }
            selectedX = selectedY = -1;
            selectedMoves.Clear();
        }

        // ───────────────────────── 턴 진행 ─────────────────────────

        IEnumerator RunPlayerMove(Move move)
        {
            busy = true;
            yield return ApplyMoveAnimated(move);
            if (!CheckGameOver()) yield return RunAITurn();
            busy = false;
        }

        IEnumerator RunAITurn()
        {
            busy = true;
            SetStatus("AI가 생각 중...");
            yield return new WaitForSeconds(aiThinkDelay);

            var task = ai.ChooseMoveAsync(board.Clone());
            yield return new WaitUntil(() => task.IsCompleted);

            Move? move = task.IsFaulted ? null : task.Result;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            if (move == null)
            {
                Debug.LogWarning("AI가 수를 고르지 못해 랜덤 수로 대신 둡니다.");
                move = RandomAI.PickMove(board);
            }

            if (move.HasValue)
            {
                yield return ApplyMoveAnimated(move.Value);
                CheckGameOver();
            }
            busy = false;
        }

        IEnumerator ApplyMoveAnimated(Move m)
        {
            var mover = pieceObjects[m.FromX, m.FromY];
            int capX = m.ToX, capY = m.IsEnPassant ? m.FromY : m.ToY;
            var captured = pieceObjects[capX, capY];

            pieceObjects[m.FromX, m.FromY] = null;
            pieceObjects[capX, capY] = null;

            yield return AnimatePiece(mover, m.ToX, m.ToY);

            // TODO: 나중에 "처치 제스처" 연출이 들어갈 자리
            if (captured != null) Destroy(captured);
            pieceObjects[m.ToX, m.ToY] = mover;

            if (m.IsCastle)
            {
                int rank = m.FromY;
                int rookFrom = m.ToX == 6 ? 7 : 0, rookTo = m.ToX == 6 ? 5 : 3;
                var rook = pieceObjects[rookFrom, rank];
                pieceObjects[rookFrom, rank] = null;
                yield return AnimatePiece(rook, rookTo, rank);
                pieceObjects[rookTo, rank] = rook;
            }

            board.MakeMove(m);

            if (m.Promotion != PieceType.None)
            {
                Destroy(mover);
                pieceObjects[m.ToX, m.ToY] = CreatePieceObject(board[m.ToX, m.ToY], m.ToX, m.ToY);
            }

            Debug.Log("수: " + m.ToUci() + "   FEN: " + board.ToFen());
            RefreshStatus();
        }

        IEnumerator AnimatePiece(GameObject piece, int toX, int toY)
        {
            var cell = piece.GetComponent<BoardCell>();
            Vector3 start = piece.transform.localPosition;
            Vector3 end = SquareCenter(toX, toY) + cell.PlacementOffset;

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / moveDuration);
                Vector3 p = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                p.y += Mathf.Sin(t * Mathf.PI) * squareSize * 0.5f;   // 살짝 들어 올렸다 내려놓기
                piece.transform.localPosition = p;
                yield return null;
            }
            cell.SetSquare(toX, toY);
        }

        bool CheckGameOver()
        {
            var result = board.GetResult();
            if (result == GameResult.Ongoing) return false;

            gameOver = true;
            switch (result)
            {
                case GameResult.WhiteWins: SetStatus("체크메이트! 백 승리"); break;
                case GameResult.BlackWins: SetStatus("체크메이트! 흑 승리"); break;
                default: SetStatus("무승부"); break;
            }
            return true;
        }

        // ───────────────────────── 유틸 ─────────────────────────

        void RefreshStatus()
        {
            string side = board.SideToMove == PieceColor.White ? "백" : "흑";
            string who = board.SideToMove == playerColor ? "당신" : "AI";
            string check = board.IsInCheck(board.SideToMove) ? "  체크!" : "";
            SetStatus(side + " 차례 (" + who + ")" + check);
        }

        void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
            Debug.Log("[WizardChess] " + message);
        }

        Vector3 SquareCenter(int x, int y)
        {
            return new Vector3((x - 3.5f) * squareSize, 0f, (y - 3.5f) * squareSize);
        }

        Color BaseSquareColor(int x, int y)
        {
            return (x + y) % 2 == 0 ? darkSquareColor : lightSquareColor;   // a1은 어두운 칸
        }
    }
}