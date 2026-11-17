using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;   // XR Interaction Toolkit 3.x (Unity 6 VR 템플릿 기본)
using WizardChess.Core;

namespace WizardChess
{
    /// <summary>
    /// 3D 보드 화면 표시만 담당한다. 칸·기물 생성, 이동 애니메이션, 선택 하이라이트, 상태 텍스트를 처리한다.
    /// </summary>
    /// <remarks>
    /// <para>게임 상태는 바꾸지 않는다. 보드는 GameFlow에서 읽고, 수는 GameFlow 이벤트와
    /// IMovePresenter.PresentMove로 받는다.</para>
    /// <para>의존 방향: BoardView는 GameFlow만 안다. 입력(BoardInputAdapter)은 BoardView를 알고,
    /// 선택 하이라이트는 입력 쪽이 ShowSelection / ClearHighlights를 직접 불러서 그린다.</para>
    /// <para>사용법: GameFlow, BoardInputAdapter와 같은 GameObject에 붙이고 플레이 위치 앞(예: 0, 0.8, 0.6)에 둔다.
    /// 백 진영이 -Z 쪽(플레이어 쪽), 흑 진영이 +Z 쪽에 생성된다.</para>
    /// </remarks>
    public class BoardView : MonoBehaviour, IMovePresenter
    {
        [Header("연결 (비워두면 같은 오브젝트에서 찾음)")]
        [SerializeField] GameFlow gameFlow;

        [Header("보드")]
        [Tooltip("칸 한 변의 길이(m). 0.1이면 보드 전체가 80cm")]
        [SerializeField] float squareSize = 0.1f;
        [SerializeField] Color lightSquareColor = new Color(0.93f, 0.87f, 0.73f);
        [SerializeField] Color darkSquareColor = new Color(0.55f, 0.38f, 0.24f);
        [SerializeField] Color selectedColor = new Color(1f, 0.82f, 0.2f);
        [SerializeField] Color moveTargetColor = new Color(0.45f, 0.85f, 0.45f);
        [Tooltip("칸과 대체 도형 기물에 쓸 머티리얼. 비워두면 Resources/WizardChess/BoardPrimitive를 쓴다.\n" +
                 "CreatePrimitive의 기본 머티리얼은 빌드에서 셰이더가 빠져 마젠타로 깨지거나 안 보일 수 있다")]
        [SerializeField] Material primitiveMaterial;

        [Header("플레이어 이동")]
        [Tooltip("켜면 XR Origin의 CharacterController가 기물에 막히지 않고 통과한다 (탑승 구현 전 자유 이동용)")]
        [SerializeField] bool rigPassesThroughPieces = true;

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

        [Header("상태 표시")]
        [Tooltip("상태 표시용 텍스트 (선택 사항)")]
        [SerializeField] TMPro.TMP_Text statusText;

        CharacterController rigController;   // XR Origin의 이동 충돌체 (없으면 null)
        readonly Renderer[,] squareRenderers = new Renderer[8, 8];
        readonly GameObject[,] pieceObjects = new GameObject[8, 8];
        // 선택 하이라이트로 색을 바꾼 칸 목록 (원래 색으로 되돌리기 위해 보관)
        readonly List<Vector2Int> highlighted = new List<Vector2Int>();

        // ───────────────────────── 초기화·구독 ─────────────────────────

        void Awake()
        {
            // 인스펙터에서 비워 두면 같은 오브젝트의 컴포넌트를 쓴다.
            if (gameFlow == null) gameFlow = GetComponent<GameFlow>();
            if (gameFlow == null) Debug.LogError("[WizardChess] BoardView: GameFlow를 찾을 수 없습니다.", this);
        }

        void OnEnable()
        {
            if (gameFlow != null)
            {
                gameFlow.AddPresenter(this);   // 이동 애니메이션이 끝날 때까지 GameFlow가 기다리게 한다
                gameFlow.OnTurnStart += HandleTurnStart;
                gameFlow.OnPromotion += HandlePromotion;
                gameFlow.OnBoardChanged += RefreshStatus;
                gameFlow.OnGameOver += HandleGameOver;
            }
        }

        void OnDisable()
        {
            if (gameFlow != null)
            {
                gameFlow.RemovePresenter(this);
                gameFlow.OnTurnStart -= HandleTurnStart;
                gameFlow.OnPromotion -= HandlePromotion;
                gameFlow.OnBoardChanged -= RefreshStatus;
                gameFlow.OnGameOver -= HandleGameOver;
            }
        }

        void Start()
        {
            if (gameFlow == null) return;

            if (primitiveMaterial == null) primitiveMaterial = Resources.Load<Material>("WizardChess/BoardPrimitive");
            if (primitiveMaterial == null) Debug.LogWarning("[WizardChess] BoardPrimitive 머티리얼을 찾지 못해 기본 머티리얼을 사용합니다");

            rigController = FindAnyObjectByType<CharacterController>();
            if (rigPassesThroughPieces) LetPointersHitTriggers();

            BuildSquares();
            SpawnAllPieces();

            if (rigPassesThroughPieces)
            {
                int triggers = 0;
                foreach (var po in pieceObjects)
                    if (po != null) foreach (var c in po.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) triggers++;
                Debug.Log("[WizardChess] 기물 통과 모드: 트리거 콜라이더 " + triggers + "개, CharacterController = "
                    + (rigController != null ? rigController.name : "없음"));
            }
            RefreshStatus();
            // AI가 먼저 두는 경우의 첫 턴 시작은 GameFlow가 한다.
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
                    ApplyPrimitiveMaterial(r);
                    r.material.color = BaseSquareColor(x, y);   // 칸마다 머티리얼 인스턴스를 만들어 색만 바꾼다
                    squareRenderers[x, y] = r;

                    MakeClickable(sq, x, y);
                }
        }

        void SpawnAllPieces()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    if (!gameFlow.GetPiece(x, y).IsEmpty)
                        pieceObjects[x, y] = CreatePieceObject(gameFlow.GetPiece(x, y), x, y);
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
            IgnoreRigCollision(go);
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

            ApplyPrimitiveMaterial(go.GetComponent<Renderer>());
            go.GetComponent<Renderer>().material.color =
                piece.Color == PieceColor.White ? whitePieceColor : blackPieceColor;
            return go;
        }

        /// <summary>
        /// 기물 콜라이더를 트리거로 바꿔 플레이어가 통과하게 한다.
        /// CharacterController는 트리거와 충돌하지 않으므로 스틱 이동·실제 걷기 모두 막히지 않는다.
        /// 포인터는 LetPointersHitTriggers에서 트리거도 맞도록 바꿔 두었다.
        /// </summary>
        void IgnoreRigCollision(GameObject go)
        {
            if (!rigPassesThroughPieces) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                c.isTrigger = true;
                if (rigController != null) Physics.IgnoreCollision(rigController, c, true);   // 혹시 모를 경우를 위한 이중 처리
            }
        }

        /// <summary>
        /// 스타터 리그의 Near-Far 포인터는 기본값이 트리거 무시라, 트리거가 된 기물에도 맞도록 바꾼다.
        /// 텔레포트 레이는 건드리지 않아서 기물을 지나 바닥으로 텔레포트할 수 있다.
        /// </summary>
        static void LetPointersHitTriggers()
        {
            foreach (var caster in FindObjectsByType<CurveInteractionCaster>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                caster.raycastTriggerInteraction = QueryTriggerInteraction.Collide;
        }

        /// <summary>빌드에 포함되는 에셋 머티리얼로 바꾼다 (지정된 게 없으면 기본 머티리얼 유지).</summary>
        void ApplyPrimitiveMaterial(Renderer r)
        {
            if (primitiveMaterial != null) r.sharedMaterial = primitiveMaterial;
        }

        /// <summary>칸·기물을 VR 포인터와 마우스로 클릭할 수 있게 만든다.</summary>
        BoardCell MakeClickable(GameObject go, int x, int y)
        {
            var cell = go.AddComponent<BoardCell>();
            cell.Init(this, x, y);

            // 콜라이더가 이미 있어야 함: XRSimpleInteractable은 추가되는 순간 자식 콜라이더까지 수집한다
            var interactable = go.AddComponent<XRSimpleInteractable>();
            cell.BindInteractable(interactable);   // 클릭 처리는 BoardInputAdapter가 맡는다
            return cell;
        }

        // ───────────────────────── 선택 하이라이트 ─────────────────────────

        /// <summary>선택한 칸은 노란색, 갈 수 있는 칸은 초록색으로 칠한다. 이전 하이라이트는 지운다.</summary>
        /// <remarks>입력 쪽(BoardInputAdapter)이 기물을 선택할 때 부른다.</remarks>
        public void ShowSelection(int x, int y, IReadOnlyList<Move> moves)
        {
            ClearHighlights();
            Tint(x, y, selectedColor);
            foreach (var m in moves) Tint(m.ToX, m.ToY, moveTargetColor);
        }

        void Tint(int x, int y, Color color)
        {
            squareRenderers[x, y].material.color = color;
            highlighted.Add(new Vector2Int(x, y));
        }

        /// <summary>하이라이트한 칸을 원래 색으로 되돌린다.</summary>
        public void ClearHighlights()
        {
            foreach (var p in highlighted)
                squareRenderers[p.x, p.y].material.color = BaseSquareColor(p.x, p.y);
            highlighted.Clear();
        }

        // ───────────────────────── 이동 연출 ─────────────────────────

        /// <summary>
        /// 수가 보드에 반영되기 전에 GameFlow가 호출한다. 이 코루틴이 끝날 때까지 GameFlow가 기다린다.
        /// </summary>
        /// <remarks>캡처 연출(개발 1)은 별도 IMovePresenter로 붙인다.</remarks>
        public IEnumerator PresentMove(MoveInfo info)
        {
            Move m = info.Move;
            var mover = pieceObjects[m.FromX, m.FromY];
            var captured = pieceObjects[info.CaptureX, info.CaptureY];

            pieceObjects[m.FromX, m.FromY] = null;
            pieceObjects[info.CaptureX, info.CaptureY] = null;

            yield return AnimatePiece(mover, m.ToX, m.ToY);

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
            // 보드 반영, 승진 모델 교체, FEN 로그, 상태 텍스트는 GameFlow 이벤트를 받아 처리한다.
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

        // 승진: 이 시점에 보드에는 이미 승진한 기물이 있으므로, 그 기물로 모델을 새로 만든다.
        void HandlePromotion(Move m)
        {
            var old = pieceObjects[m.ToX, m.ToY];
            if (old != null) Destroy(old);
            pieceObjects[m.ToX, m.ToY] = CreatePieceObject(gameFlow.GetPiece(m.ToX, m.ToY), m.ToX, m.ToY);
        }

        // ───────────────────────── 상태 텍스트 ─────────────────────────

        void HandleTurnStart(PieceColor side)
        {
            // 플레이어 차례 표시는 매 수 뒤 RefreshStatus(OnBoardChanged)가 이미 해 준다.
            if (side != gameFlow.PlayerColor) SetStatus("AI가 생각 중...");
        }

        void HandleGameOver(GameResult result)
        {
            switch (result)
            {
                case GameResult.WhiteWins: SetStatus("체크메이트! 백 승리"); break;
                case GameResult.BlackWins: SetStatus("체크메이트! 흑 승리"); break;
                default: SetStatus("무승부"); break;
            }
        }

        void RefreshStatus()
        {
            PieceColor toMove = gameFlow.SideToMove;
            string side = toMove == PieceColor.White ? "백" : "흑";
            string who = toMove == gameFlow.PlayerColor ? "당신" : "AI";
            string check = gameFlow.IsInCheck(toMove) ? "  체크!" : "";
            SetStatus(side + " 차례 (" + who + ")" + check);
        }

        void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
            Debug.Log("[WizardChess] " + message);
        }

        // ───────────────────────── 유틸 ─────────────────────────

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