using System.Collections;

namespace WizardChess
{
    /// <summary>
    /// 한 수를 화면에 보여주는 연출(기물 이동 애니메이션, 캡처 연출 등).
    /// 구현하면 GameFlow가 이 연출이 끝날 때까지 기다린 뒤 수를 보드에 반영하고 턴을 넘긴다.
    /// </summary>
    /// <remarks>
    /// <para>PresentMove가 도는 동안 보드는 아직 수를 두기 전 상태다.</para>
    /// <para>GameFlow.AddPresenter로 등록하고(보통 OnEnable), RemovePresenter로 해제한다(OnDisable).</para>
    /// <para>등록된 연출은 모두 동시에 시작되고, 전부 끝나야 다음 단계로 넘어간다.</para>
    /// </remarks>
    public interface IMovePresenter
    {
        /// <summary>
        /// 연출 하나를 진행한다.
        /// </summary>
        /// <remarks>
        /// 연출이 끝날 때까지 턴을 멈춰 두기 위해 코루틴을 돌려준다.
        /// 보여줄 게 없는 수라면 바로 yield break 하면 된다.
        /// </remarks>
        /// <param name="info">보여줄 수의 정보.</param>
        /// <returns>연출이 끝나면 종료되는 코루틴.</returns>
        IEnumerator PresentMove(MoveInfo info);
    }
}
