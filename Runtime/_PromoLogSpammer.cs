// =============================================================================
// ⚠️ 임시 파일 — 홍보 영상 촬영 전용. 촬영 후 즉시 삭제(롤백)할 것.
//    이 파일(.cs + .meta) 하나만 지우면 원복 완료. 커밋 금지.
//
//    동작: 플레이 모드 진입 시 자동 생성되어, 매 0.6초마다
//          로그 종류별(일반/경고/에러)로 각 1개씩 테스트 로그를 출력한다.
// =============================================================================

using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// [임시/홍보용] 플레이 모드에서 종류별 테스트 로그를 0.6초 간격으로 출력.
    /// Rekon 로그 캡처/리플레이 데모 영상 촬영용. 촬영 후 파일 삭제로 롤백.
    /// </summary>
    public class _PromoLogSpammer : MonoBehaviour
    {
        // 출력 간격(초) — 각 로그 종류가 이 간격마다 1개씩 출력된다.
        private const float IntervalSeconds = 0.6f;

        // 홍보 영상에 자연스럽게 보이도록 한 실제 게임 로그 느낌의 메시지 풀.
        private static readonly string[] InfoMessages =
        {
            "플레이어 스폰 완료 (team=Blue, spawn=A2).",
            "무기 교체: Rifle (탄약 30/30).",
            "헤드샷! 적 처치 (Enemy_07, dist=42m).",
            "재장전 완료 (Rifle 30/30).",
            "거점 점령 진행 중 (B 거점 64%).",
        };

        private static readonly string[] WarningMessages =
        {
            "탄약 부족 (Rifle 5/30) — 재장전 권장.",
            "체력 위험 (HP 18/100).",
            "수류탄 쿨다운 중 (3.2s 남음).",
            "적 다수 감지 (반경 20m 내 4명).",
            "핑 상승 감지 (48ms → 130ms).",
        };

        private static readonly string[] ErrorMessages =
        {
            "히트박스 동기화 실패 (target=Enemy_03).",
            "탄도 레이캐스트 충돌 누락 (frame=5821).",
            "리스폰 타이머 예외 (값=NaN).",
            "넷코드 패킷 손실 — 위치 보정 실패.",
            "무기 프리팹 로드 실패: Weapons/Sniper.",
        };

        private int _tick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            var go = new GameObject("[Rekon Demo Log Spammer]");
            DontDestroyOnLoad(go);
            go.AddComponent<_PromoLogSpammer>();
        }

        private void Start()
        {
            // 진입 즉시 한 번 + 이후 0.6초 간격 반복.
            InvokeRepeating(nameof(Emit), 0f, IntervalSeconds);
        }

        private void Emit()
        {
            int i = _tick;
            Debug.Log($"[Rekon Demo] #{i} {InfoMessages[i % InfoMessages.Length]}");
            Debug.LogWarning($"[Rekon Demo] #{i} {WarningMessages[i % WarningMessages.Length]}");
            Debug.LogError($"[Rekon Demo] #{i} {ErrorMessages[i % ErrorMessages.Length]}");
            _tick++;
        }
    }
}
