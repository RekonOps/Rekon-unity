using System;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 토큰 갱신 최종 실패 시 사용자 재인증을 유도하는 핸들러.
    /// OnReAuthRequired 이벤트를 통해 UI 레이어에서 재인증 다이얼로그를 표시할 수 있습니다.
    /// </summary>
    public class ReAuthHandler
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 재인증이 필요할 때 발생하는 이벤트.
        /// UI 레이어에서 구독하여 재인증 다이얼로그를 표시합니다.
        /// </summary>
        public event Action<ReAuthEventArgs> OnReAuthRequired;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly SessionTokenStore _tokenStore;
        private bool _isReAuthPending;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ReAuthHandler를 초기화합니다.
        /// </summary>
        /// <param name="tokenStore">세션 토큰 저장소 (재인증 시 초기화)</param>
        public ReAuthHandler(SessionTokenStore tokenStore)
        {
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 재인증이 현재 대기 중인지 여부
        /// </summary>
        public bool IsReAuthPending => _isReAuthPending;

        /// <summary>
        /// 재인증 이벤트를 발생시킵니다.
        /// 세션 토큰을 삭제하고 UI에 재인증 다이얼로그 표시를 요청합니다.
        /// </summary>
        /// <param name="reason">재인증이 필요한 이유 (UI에 표시)</param>
        public async Task TriggerReAuthAsync(string reason)
        {
            if (_isReAuthPending)
            {
                Debug.LogWarning("[ReAuthHandler] 이미 재인증 대기 중입니다. 중복 트리거 무시.");
                return;
            }

            Debug.LogWarning($"[ReAuthHandler] 재인증 필요. 이유: {reason}");

            // 세션 토큰 삭제
            try
            {
                _tokenStore.Clear();
                Debug.Log("[ReAuthHandler] 세션 토큰 삭제 완료");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ReAuthHandler] 세션 토큰 삭제 실패: {ex.Message}");
            }

            _isReAuthPending = true;

            // 이벤트 발생 (메인 스레드에서 실행되도록 처리)
            var args = new ReAuthEventArgs(reason);

            try
            {
                OnReAuthRequired?.Invoke(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ReAuthHandler] OnReAuthRequired 이벤트 핸들러 오류: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 재인증 완료를 알립니다. (재인증 성공 후 호출)
        /// </summary>
        public void NotifyReAuthCompleted()
        {
            _isReAuthPending = false;
            Debug.Log("[ReAuthHandler] 재인증 완료");
        }

        /// <summary>
        /// 재인증을 취소합니다. (사용자가 다이얼로그를 닫은 경우)
        /// </summary>
        public void NotifyReAuthCancelled()
        {
            _isReAuthPending = false;
            Debug.Log("[ReAuthHandler] 재인증 취소됨");
        }
    }

    // ─── 이벤트 인자 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// OnReAuthRequired 이벤트 인자
    /// </summary>
    public class ReAuthEventArgs : EventArgs
    {
        /// <summary>재인증이 필요한 이유 (UI 표시용)</summary>
        public string Reason { get; }

        /// <summary>이벤트 발생 시각</summary>
        public DateTime Timestamp { get; }

        public ReAuthEventArgs(string reason)
        {
            Reason = reason;
            Timestamp = DateTime.UtcNow;
        }
    }
}
