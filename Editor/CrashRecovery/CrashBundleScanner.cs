using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RekonOps.BugBeacon.Editor
{
    /// <summary>
    /// Unity Editor 시작 시 자동으로 크래시 번들을 스캔하는 클래스.
    ///
    /// 동작:
    ///   - [InitializeOnLoad]로 에디터 시작 시 자동 실행
    ///   - crash_bundles/ 디렉토리 스캔
    ///   - abnormal_exit.flag 파일 확인
    ///   - 미등록 크래시 번들 발견 시 CrashRecoveryWindow 자동 오픈
    ///   - 첫 스캔 후 2초 이내에 알림 표시
    /// </summary>
    [InitializeOnLoad]
    public static class CrashBundleScanner
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>에디터 시작 후 스캔 지연 시간 (초)</summary>
        private const double ScanDelaySeconds = 2.0;

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private static double _scanScheduledTime;
        private static bool _scanScheduled;

        // ──────────────────────────────────────────────────────────────
        // 초기화
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 에디터 시작 시 자동 실행되는 정적 생성자.
        /// EditorApplication.update에 스캔 타이머를 등록합니다.
        /// </summary>
        static CrashBundleScanner()
        {
            // 에디터 시작 직후 즉시 실행하면 에디터가 안정되지 않을 수 있으므로
            // ScanDelaySeconds 후에 스캔합니다.
            _scanScheduledTime = EditorApplication.timeSinceStartup + ScanDelaySeconds;
            _scanScheduled = true;

            EditorApplication.update += OnEditorUpdate;
        }

        // ──────────────────────────────────────────────────────────────
        // 에디터 업데이트
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// EditorApplication.update 콜백. 지연 시간 후 스캔을 실행합니다.
        /// </summary>
        private static void OnEditorUpdate()
        {
            if (!_scanScheduled)
                return;

            if (EditorApplication.timeSinceStartup < _scanScheduledTime)
                return;

            // 단 한 번만 실행
            _scanScheduled = false;
            EditorApplication.update -= OnEditorUpdate;

            // 스캔 실행
            PerformScan();
        }

        // ──────────────────────────────────────────────────────────────
        // 스캔 실행
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들 스캔을 수행합니다.
        /// 미등록 번들 발견 시 CrashRecoveryWindow를 자동으로 엽니다.
        /// </summary>
        public static void PerformScan()
        {
            try
            {
                bool flagExists = CheckAbnormalExitFlag();
                var unregisteredBundles = FindUnregisteredBundles();

                if (unregisteredBundles.Count == 0 && !flagExists)
                {
                    // 크래시 없음 - 정상 시작
                    return;
                }

                Debug.LogWarning(
                    $"[BugBeacon] 크래시 복구 스캔 결과: " +
                    $"미등록 번들 {unregisteredBundles.Count}개" +
                    $"{(flagExists ? ", 비정상 종료 플래그 감지" : "")}");

                if (unregisteredBundles.Count > 0)
                {
                    // CrashRecoveryWindow 자동 오픈
                    CrashRecoveryWindow.OpenWindow();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 크래시 번들 스캔 실패: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 미등록(jira_issue_key가 null인) 크래시 번들 목록을 반환합니다.
        /// </summary>
        public static List<CrashBundleManifest> FindUnregisteredBundles()
        {
            var all = CrashBundleWriter.ScanAllBundles();
            var unregistered = new List<CrashBundleManifest>();

            foreach (var manifest in all)
            {
                if (string.IsNullOrEmpty(manifest.jira_issue_key))
                    unregistered.Add(manifest);
            }

            return unregistered;
        }

        /// <summary>
        /// 모든 크래시 번들 목록을 반환합니다.
        /// </summary>
        public static List<CrashBundleManifest> FindAllBundles()
        {
            return CrashBundleWriter.ScanAllBundles();
        }

        /// <summary>
        /// abnormal_exit.flag 파일이 존재하는지 확인합니다.
        /// </summary>
        public static bool CheckAbnormalExitFlag()
        {
            try
            {
                return File.Exists(AbnormalExitDetector.FlagFilePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 스캔을 수동으로 재실행합니다.
        /// 메뉴 항목이나 버튼에서 호출할 수 있습니다.
        /// </summary>
        [MenuItem(BugBeaconEditorInfo.MenuRoot + "/크래시 복구 스캔 실행")]
        public static void ManualScan()
        {
            Debug.Log("[BugBeacon] 수동 크래시 복구 스캔 시작...");
            PerformScan();
        }
    }
}
