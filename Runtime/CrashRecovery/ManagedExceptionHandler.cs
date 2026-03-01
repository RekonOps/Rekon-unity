using System;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// C# Managed Exception을 감지하여 크래시 번들을 생성하는 클래스.
    ///
    /// 동작:
    ///   - Application.logMessageReceived에서 LogType.Exception 수신 시 즉시 번들 생성
    ///   - 동일 세션 내 중복 방지: 마지막 번들 생성 후 30초 쿨다운
    ///   - 예외 메시지 + 스택 트레이스를 번들의 crash_info.json에 포함
    ///
    /// 주의:
    ///   - C# Managed Exception만 처리 (Native crash는 Unity Crash Reporter 영역)
    ///   - Dispose() 호출 시 구독 해제
    /// </summary>
    public class ManagedExceptionHandler : IDisposable
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>중복 번들 생성 방지 쿨다운 시간 (초)</summary>
        public const float CooldownSeconds = 30f;

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private readonly CrashBundleWriter _bundleWriter;
        private bool _disposed;

        // 마지막 번들 생성 시각 (쿨다운 관리)
        private float _lastBundleCreatedTime = float.MinValue;

        // 비동기 번들 생성 중복 방지 플래그
        private bool _isBuildingBundle;

        // ──────────────────────────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────────────────────────

        /// <summary>크래시 번들 생성 완료 시 발행되는 이벤트. (번들 매니페스트)</summary>
        public event Action<CrashBundleManifest> OnCrashBundleCreated;

        // ──────────────────────────────────────────────────────────────
        // 생성자 / 소멸자
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// ManagedExceptionHandler를 초기화하고 로그 콜백을 등록합니다.
        /// </summary>
        /// <param name="bundleWriter">크래시 번들 생성기</param>
        public ManagedExceptionHandler(CrashBundleWriter bundleWriter)
        {
            _bundleWriter = bundleWriter ?? throw new ArgumentNullException(nameof(bundleWriter));

            // Unity 로그 콜백 등록 (메인 스레드 전용)
            Application.logMessageReceived += OnLogReceived;

            Debug.Log("[BugOneTouch] ManagedExceptionHandler 초기화 완료. Exception 감지 시작.");
        }

        // ──────────────────────────────────────────────────────────────
        // IDisposable
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 로그 콜백 구독을 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Application.logMessageReceived -= OnLogReceived;
            _disposed = true;

            Debug.Log("[BugOneTouch] ManagedExceptionHandler 정리 완료.");
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 프로퍼티
        // ──────────────────────────────────────────────────────────────

        /// <summary>현재 쿨다운 중인지 여부</summary>
        public bool IsOnCooldown =>
            Time.realtimeSinceStartup - _lastBundleCreatedTime < CooldownSeconds;

        /// <summary>현재 번들 생성 중인지 여부</summary>
        public bool IsBuildingBundle => _isBuildingBundle;

        // ──────────────────────────────────────────────────────────────
        // 로그 콜백
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Unity 로그 콜백. LogType.Exception 수신 시 크래시 번들 생성을 시작합니다.
        /// 메인 스레드에서 호출됩니다 (Application.logMessageReceived는 메인 스레드 전용).
        /// </summary>
        private void OnLogReceived(string condition, string stackTrace, LogType logType)
        {
            if (_disposed)
                return;

            // Exception 타입만 처리
            if (logType != LogType.Exception)
                return;

            // 쿨다운 중이면 무시
            if (IsOnCooldown)
            {
                Debug.LogWarning($"[BugOneTouch] Exception 감지 (쿨다운 중, 무시): {condition}");
                return;
            }

            // 이미 번들 생성 중이면 무시
            if (_isBuildingBundle)
            {
                Debug.LogWarning("[BugOneTouch] Exception 감지 (번들 생성 중, 무시)");
                return;
            }

            Debug.LogWarning($"[BugOneTouch] Managed Exception 감지! 크래시 번들을 생성합니다: {condition}");

            // 예외 타입 추출 (예: "NullReferenceException: Object reference not set...")
            string exceptionType = ExtractExceptionType(condition);
            string exceptionMessage = ExtractExceptionMessage(condition);

            // 비동기 번들 생성 시작
            StartBuildBundle(exceptionType, exceptionMessage, stackTrace);
        }

        // ──────────────────────────────────────────────────────────────
        // 번들 생성
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들 생성을 비동기로 시작합니다.
        /// async void 사용: 로그 콜백에서 await를 직접 사용할 수 없으므로.
        /// </summary>
        private async void StartBuildBundle(string exceptionType, string exceptionMessage, string stackTrace)
        {
            _isBuildingBundle = true;
            _lastBundleCreatedTime = Time.realtimeSinceStartup;

            try
            {
                var manifest = await _bundleWriter.BuildAsync(
                    crashType: "managed_exception",
                    exceptionType: exceptionType,
                    exceptionMessage: exceptionMessage,
                    stackTrace: stackTrace);

                if (manifest != null)
                {
                    Debug.Log($"[BugOneTouch] 크래시 번들 생성 완료: {manifest.id}");
                    OnCrashBundleCreated?.Invoke(manifest);
                }
                else
                {
                    Debug.LogWarning("[BugOneTouch] 크래시 번들 생성 실패 (null 반환).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 크래시 번들 생성 중 예외: {ex.Message}");
            }
            finally
            {
                _isBuildingBundle = false;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Unity 예외 메시지에서 예외 클래스명을 추출합니다.
        /// 예: "NullReferenceException: Object reference..." → "NullReferenceException"
        /// </summary>
        private static string ExtractExceptionType(string condition)
        {
            if (string.IsNullOrEmpty(condition))
                return "UnknownException";

            int colonIdx = condition.IndexOf(':');
            return colonIdx > 0
                ? condition.Substring(0, colonIdx).Trim()
                : condition.Trim();
        }

        /// <summary>
        /// Unity 예외 메시지에서 예외 메시지 본문을 추출합니다.
        /// 예: "NullReferenceException: Object reference..." → "Object reference..."
        /// </summary>
        private static string ExtractExceptionMessage(string condition)
        {
            if (string.IsNullOrEmpty(condition))
                return "";

            int colonIdx = condition.IndexOf(':');
            return colonIdx > 0 && colonIdx < condition.Length - 1
                ? condition.Substring(colonIdx + 1).Trim()
                : "";
        }
    }
}
