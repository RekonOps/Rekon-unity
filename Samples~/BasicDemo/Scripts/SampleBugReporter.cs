// SampleBugReporter.cs
// Rekon BasicDemo 샘플
//
// Rekon의 핵심 사용법을 보여주는 예제입니다:
//   1) 게임 상태를 평소처럼 Unity 콘솔(Debug.Log)에 남기면 Rekon이 자동으로 캡처합니다.
//      → 웹 리포트의 로그 패널에서, team_pro 리플레이라면 영상/스크린샷과 시간 동기화되어
//        그대로 표시됩니다. (별도 컨텍스트 API 불필요)
//   2) 코드에서 Rekon.Capture("제목") 를 호출하면 그 순간 영상/스크린샷/로그가 자동 수집되어
//      웹 대시보드로 전송됩니다. (Project Settings의 내장 핫키로도 캡처 가능)
//
// 사용 방법:
//   - 이 컴포넌트를 씬의 빈 게임 오브젝트에 추가합니다.
//   - Inspector에서 컴포넌트 우클릭 → "Report Bug Now" 로 즉시 테스트하거나,
//     UI 버튼 OnClick / 게임 코드에서 ReportBug(...) 를 호출합니다.
//   - 웹 리포트의 로그 패널에서 아래 Debug.Log 들이 그대로 보입니다.
//
// 참고: 실제 전송에는 Rekon 설정(Project Settings > Rekon: 라이선스 키 등)이 필요합니다.
//       미설정 시에는 콘솔 로그/캡처 흐름만 동작하고 업로드는 되지 않습니다.

using UnityEngine;
// 네임스페이스(RekonOps.Rekon.Samples)와 클래스명(Rekon)이 겹치므로 별칭으로 참조합니다.
using RekonSdk = RekonOps.Rekon.Rekon;

namespace RekonOps.Rekon.Samples
{
    /// <summary>
    /// "게임 상태를 로그로 남기고 Rekon.Capture로 리포트한다"는 핵심 플로우 예제.
    /// 평소 쓰는 Debug.Log 가 그대로 리포트에 담기므로 별도 컨텍스트 API가 필요 없습니다.
    /// </summary>
    public class SampleBugReporter : MonoBehaviour
    {
        [Header("샘플 게임 상태")]
        [SerializeField] private int _currentLevel = 1;
        [SerializeField] private int _score = 0;
        [SerializeField] private int _playerHp = 100;

        // Inspector에서 컴포넌트 우클릭 → "Report Bug Now" 로 즉시 테스트
        [ContextMenu("Report Bug Now")]
        private void ReportBugFromMenu() => ReportBug("수동 버그 리포트 (샘플)");

        /// <summary>
        /// 현재 게임 상태를 콘솔에 남기고 Rekon 캡처를 트리거합니다.
        /// Debug.Log 들은 Rekon이 자동 캡처 → 웹 리포트 로그 패널에 시간순으로 표시됩니다.
        /// </summary>
        public void ReportBug(string title)
        {
            // 1) 평소처럼 게임 상태를 로그로 남깁니다. (이게 곧 리포트의 컨텍스트가 됩니다)
            Debug.Log($"[Demo] level={_currentLevel}, score={_score}, hp={_playerHp}, " +
                      $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

            // 2) 캡처 트리거 → 영상/스크린샷/로그가 자동 수집되어 웹 대시보드로 전송됩니다.
            RekonSdk.Capture(title);
            Debug.Log($"[Demo] Rekon.Capture(\"{title}\") 호출 — 영상/스크린샷/로그 자동 수집");
        }

        // ── 데모용 상태 변화 (Inspector 컨텍스트 메뉴 / UI 버튼 / 게임 코드에서 호출) ──

        [ContextMenu("Next Level")]
        public void NextLevel()
        {
            _currentLevel++;
            _score = 0;
            Debug.Log($"[Demo] 레벨 {_currentLevel} 시작");
        }

        public void AddScore(int amount)
        {
            _score += amount;
            Debug.Log($"[Demo] 점수 +{amount} → {_score}");
        }

        public void TakeDamage(int damage)
        {
            _playerHp = Mathf.Max(0, _playerHp - damage);
            Debug.LogWarning($"[Demo] 데미지 {damage} → HP {_playerHp}");

            if (_playerHp <= 0)
            {
                // 게임 내 이벤트(사망)에서 자동으로 버그 리포트를 트리거하는 예
                Debug.LogError("[Demo] 플레이어 사망 — 자동 리포트 트리거");
                ReportBug("플레이어 사망");
            }
        }
    }
}
