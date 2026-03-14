using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// SystemInfo, Application, SceneManager, Time, Screen, QualitySettings를 사용하여
    /// StateSnapshot을 수집하는 구현체.
    ///
    /// CollectAsync()는 Unity API를 사용하므로 반드시 메인 스레드에서 호출해야 합니다.
    /// ContextProviderRegistry에서 커스텀 K/V를 추가로 수집합니다.
    /// </summary>
    public class StateSnapshotCollector : IStateSnapshotCollector
    {
        private readonly ContextProviderRegistry _contextRegistry;

        /// <summary>
        /// ContextProviderRegistry를 주입하여 인스턴스를 생성합니다.
        /// </summary>
        /// <param name="contextRegistry">커스텀 컨텍스트 수집에 사용할 레지스트리</param>
        public StateSnapshotCollector(ContextProviderRegistry contextRegistry)
        {
            _contextRegistry = contextRegistry ?? throw new ArgumentNullException(nameof(contextRegistry));
        }

        /// <summary>
        /// 현재 시점의 상태를 수집하여 StateSnapshot을 반환합니다.
        /// 메인 스레드에서 Unity API 데이터를 수집한 후 Task로 래핑합니다.
        /// </summary>
        public Task<StateSnapshot> CollectAsync()
        {
            try
            {
                var snapshot = new StateSnapshot
                {
                    // ── 엔진 정보 ──────────────────────────────────────
                    engine = "Unity",
                    engine_version = Application.unityVersion,
                    app_version = Application.version,
                    build_number = GetBuildNumber(),

                    // ── 플랫폼/디바이스 정보 ───────────────────────────
                    platform = Application.platform.ToString(),
                    device = SystemInfo.deviceModel,
                    os = SystemInfo.operatingSystem,
                    cpu = SystemInfo.processorType,
                    gpu = SystemInfo.graphicsDeviceName,
                    memory_mb = SystemInfo.systemMemorySize,

                    // ── 화면 정보 ──────────────────────────────────────
                    screen_width = Screen.width,
                    screen_height = Screen.height,
                    fullscreen = Screen.fullScreen,

                    // ── 런타임 정보 ────────────────────────────────────
                    scene = GetActiveSceneName(),
                    time_since_startup = Time.realtimeSinceStartup,
                    frame_count = Time.frameCount,
                    fps = CalculateFps(),
                    quality_level = QualitySettings.GetQualityLevel(),

                    // ── 메타 정보 ──────────────────────────────────────
                    captured_at = DateTime.UtcNow.ToString("O"),
                };

                // ── 커스텀 컨텍스트 수집 ───────────────────────────────
                var customContext = _contextRegistry.CollectAll();
                snapshot.SetCustomContextDictionary(customContext);

                return Task.FromResult(snapshot);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 상태 스냅샷 수집 실패: {ex.Message}");
                // 실패 시에도 빈 스냅샷 반환 (null 방지)
                return Task.FromResult(new StateSnapshot
                {
                    engine = "Unity",
                    captured_at = DateTime.UtcNow.ToString("O"),
                });
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 헬퍼
        // ──────────────────────────────────────────────────────────────

        private static string GetActiveSceneName()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                return scene.IsValid() ? scene.name : "(unknown)";
            }
            catch
            {
                return "(unknown)";
            }
        }

        private static float CalculateFps()
        {
            float deltaTime = Time.unscaledDeltaTime;
            return deltaTime > 0f ? 1f / deltaTime : 0f;
        }

        private static string GetBuildNumber()
        {
            // Unity 빌드 번호는 플랫폼마다 다른 방식으로 접근
            // 표준 방법이 없으므로 일반적으로 빈 문자열 또는 커스텀 설정에서 주입
#if UNITY_IOS
            return UnityEngine.iOS.Device.buildVersion ?? string.Empty;
#else
            return string.Empty;
#endif
        }
    }
}
