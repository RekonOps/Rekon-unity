using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// Rekon Unity Plugin - Entry point and version information.
    /// 코드에서 버그 리포트 캡처를 수동 발동하는 public 진입점(<see cref="Capture"/>)을 제공합니다.
    /// </summary>
    public static class Rekon
    {
        // ⚠️ package.json 의 version 과 자동 동기화됩니다(Editor/RekonVersionSync.cs).
        //    직접 수정하지 마세요 — 에디터 로드 시 자동 갱신됩니다(immutable 설치면 릴리스 값 유지).
        public const string Version = "0.5.0";
        public const string DisplayName = "Rekon";

        /// <summary>
        /// 코드에서 캡처를 발동하기 위한 오케스트레이터 핸들.
        /// <see cref="RekonBootstrap"/> 가 Play Mode 초기화 시 주입하고, Domain Reload 시 리셋합니다.
        /// </summary>
        internal static ICaptureOrchestrator Orchestrator { get; set; }

        /// <summary>
        /// <see cref="Capture"/> 로 지정된 다음 리포트 제목(1회성).
        /// SilentSubmitManager.GenerateTitle 이 이 값을 우선 소비하고 즉시 비웁니다.
        /// </summary>
        internal static string PendingReportTitle { get; set; }

        /// <summary>
        /// 코드에서 버그 리포트 캡처(영상+로그+스크린샷)를 수동 발동합니다.
        /// 캡처 핫키(Ctrl/Cmd+Shift+B)와 동일한 경로이며, 캡처 완료 후 Silent Submit으로 자동 제출됩니다.
        /// </summary>
        /// <param name="title">리포트 제목(선택). 미지정 시 "[접두어] 씬이름 타임스탬프"로 자동 생성됩니다.</param>
        public static void Capture(string title = null)
        {
            if (Orchestrator == null)
            {
                Debug.LogWarning("[Rekon] Capture() 가 호출됐지만 SDK가 아직 초기화되지 않았습니다. " +
                                 "Play Mode 인지, Resources/RekonSettings.asset 이 존재하는지 확인하세요.");
                return;
            }

            if (!string.IsNullOrEmpty(title))
                PendingReportTitle = title;

            // fire-and-forget — 캡처 완료 시 OnCaptureCompleted → SilentSubmitManager가 자동 제출.
            _ = CaptureInternalAsync();
        }

        private static async Task CaptureInternalAsync()
        {
            try
            {
                await Orchestrator.StartAsync();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Rekon] Capture 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
