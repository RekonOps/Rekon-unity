using System.Threading.Tasks;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 시스템/애플리케이션 상태 스냅샷 수집 인터페이스.
    /// 테스트용 목(Mock) 또는 확장 구현체로 교체 가능합니다.
    /// </summary>
    public interface IStateSnapshotCollector
    {
        /// <summary>
        /// 현재 시점의 시스템/앱 상태를 수집하여 StateSnapshot을 반환합니다.
        /// Unity API 접근이 필요하므로 메인 스레드에서 호출해야 합니다.
        /// </summary>
        /// <returns>수집된 상태 스냅샷</returns>
        Task<StateSnapshot> CollectAsync();
    }
}
