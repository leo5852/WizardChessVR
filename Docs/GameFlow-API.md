# GameFlow 이벤트·API (feature/gameflow)

ChessBoard 오브젝트 = `GameFlow` + `BoardView` + `BoardInputAdapter` + `StockfishAI`

## 원칙
- 의존 방향: `BoardInputAdapter` → `BoardView`, `GameFlow` / `BoardView` → `GameFlow` / `GameFlow` → 없음
- Board는 GameFlow만 바꾼다. 화면 코드는 읽기만 하고, 수는 항상 `GameFlow.TrySubmitPlayerMove`로 둔다.
- 이벤트 리스너나 연출(PresentMove) 안에서 예외가 나면 턴 진행이 멈출 수 있다. 예외를 밖으로 던지지 않게 작성한다.

## 한 수의 이벤트 순서
`OnMoveDecided(Move)` → `OnCapture(Move, Piece)`(캡처일 때만) → **IMovePresenter.PresentMove 대기** → 보드 반영 → `OnPromotion(Move)`(승진일 때만) → `OnBoardChanged()` → `OnGameOver(GameResult)` 또는 `OnTurnStart(PieceColor)`

- 게임 시작 시 첫 프레임 다음에 `OnTurnStart`가 한 번 발생
- PresentMove 동안 보드는 **이동 전** 상태

## 연출 기다리게 하기 (개발 1: 캡처 연출)
```csharp
public class CaptureCinematic : MonoBehaviour, IMovePresenter {
    [SerializeField] GameFlow flow;
    void OnEnable()  { flow.AddPresenter(this); }
    void OnDisable() { flow.RemovePresenter(this); }
    public IEnumerator PresentMove(MoveInfo info) {
        if (!info.IsCapture) yield break;
        yield return PlayCapture(info);   // 이 연출이 끝날 때까지 GameFlow가 기다린다
    }
}
```
- 등록된 presenter는 동시에 실행되고, 모두 끝나야 턴이 넘어간다(BoardView 이동 애니메이션도 presenter 중 하나).
- `MoveInfo`: Move, Mover, Captured, CaptureX/Y(앙파상이면 Move.To와 다름), IsPlayerMove, IsCapture, IsPromotion, Side

## GameFlow 읽기 API
`PlayerColor`, `SideToMove`, `IsBusy`, `IsGameOver`, `Result`, `IsPlayerTurn`, `GetPiece(x,y)`, `IsInCheck(color)`, `ToFen()`, `GetBoardSnapshot()`(복사본), `GetLegalMoves()`, `GetLegalMovesFrom(x,y)`

## 수 두기
- `GameFlow.TrySubmitPlayerMove(Move)` → 합법수이고 내 차례면 true. from/to/promotion만 비교하므로 직접 만든 Move도 된다(걷기 이동, 음성).
- 클릭 방식 입력은 `BoardInputAdapter`:
  - `HandleSquareClicked(x,y)`: 첫 클릭 선택, 두 번째 클릭 이동(지휘 패널 클릭도 여기로)
  - `Select(x,y)` / `ClearSelection()` / `TryMoveSelectedTo(x,y)`: 패널 지정 → 음성 영창 확정용
  - 3D 보드 하이라이트는 어댑터가 `BoardView.ShowSelection` / `ClearHighlights`를 직접 부른다
  - 이벤트 `OnSelectionChanged(x, y, moves)`, `OnSelectionCleared()` → BoardView가 모르는 다른 화면(지휘 패널 등)용
- XR 포인터: `BoardCell.Selected`(static) → 어댑터가 `Owner`로 걸러서 처리
- 에디터 테스트: XR Interaction Simulator 레이로 클릭한다. 마우스 입력(`BoardInputAdapter.mouseInputInEditor`)은 시뮬레이터와 겹쳐 클릭이 두 번 처리되므로 씬에서 꺼 두었다.

## 아직 안 된 것
- 프로모션 선택 UI(현재는 퀸 자동, `BoardInputAdapter.TryMoveSelectedTo`의 TODO)
