using System;
using System.IO;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 비정상 종료(크래시)를 감지하는 클래스.
    ///
    /// 동작 원리:
    ///   - Play 시작 시 abnormal_exit.flag 파일 생성
    ///   - 정상 종료 시 flag 파일 삭제
    ///   - 크래시 후 다음 시작 시 flag 파일이 남아 있으면 비정상 종료로 간주
    ///
    /// 플래그 파일 경로:
    ///   {persistentDataPath}/BugBeacon/crash_recovery/abnormal_exit.flag
    ///
    /// 주의:
    ///   - RuntimeInitializeOnLoadMethod로 자동 초기화 (씬 로드 전)
    ///   - Editor 측 플래그 확인은 CrashBundleScanner (Editor 전용)에서 처리
    ///   - Native crash는 감지 불가 (Unity Crash Reporter 영역)
    /// </summary>
    public class AbnormalExitDetector
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>플래그 파일명</summary>
        public const string FlagFileName = "abnormal_exit.flag";

        // ──────────────────────────────────────────────────────────────
        // 내부 캐시 (지연 초기화)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// FlagFilePath 캐시.
        /// Application.persistentDataPath는 메인 스레드에서만 접근 가능하므로
        /// 필드 초기화자 대신 처음 접근 시점에 초기화합니다.
        /// </summary>
        private static string _flagFilePath;

        // ──────────────────────────────────────────────────────────────
        // 공개 프로퍼티
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 플래그 파일 절대 경로.
        /// {persistentDataPath}/BugBeacon/crash_recovery/abnormal_exit.flag
        /// </summary>
        public static string FlagFilePath =>
            _flagFilePath ??= Path.Combine(
                Application.persistentDataPath,
                "BugBeacon",
                "crash_recovery",
                FlagFileName);

        /// <summary>
        /// 이전 세션이 비정상 종료되었는지 여부.
        /// 플래그 파일이 존재하면 true.
        /// </summary>
        public static bool WasPreviousSessionAbnormal =>
            File.Exists(FlagFilePath);

        // ──────────────────────────────────────────────────────────────
        // 자동 초기화 (RuntimeInitializeOnLoadMethod)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬 로드 전에 자동으로 호출됩니다.
        /// 이전 세션의 비정상 종료 여부를 확인하고 flag 파일을 생성합니다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnRuntimeInitialize()
        {
            try
            {
                bool previousCrash = WasPreviousSessionAbnormal;

                if (previousCrash)
                {
                    Debug.LogWarning("[BugBeacon] 이전 세션 비정상 종료 감지! 크래시 번들을 생성합니다.");
                    // 크래시 번들 생성은 CrashBundleWriter에서 별도 처리
                    // 여기서는 감지 이벤트만 발행
                    OnAbnormalExitDetected?.Invoke();
                }
                else
                {
                    Debug.Log("[BugBeacon] 이전 세션 정상 종료 확인.");
                }

                // 현재 세션의 flag 파일 생성 (Play 시작)
                CreateFlagFile();

                // 정상 종료 시 flag 삭제 등록
                Application.quitting += OnApplicationQuitting;

                Debug.Log($"[BugBeacon] AbnormalExitDetector 초기화 완료. 플래그: {FlagFilePath}");
            }
            catch (Exception ex)
            {
                // 초기화 실패가 게임 시작을 방해하면 안 됨
                Debug.LogError($"[BugBeacon] AbnormalExitDetector 초기화 실패: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 이전 세션 비정상 종료 감지 시 발행되는 이벤트.
        /// CrashBundleWriter나 다른 복구 시스템에서 구독할 수 있습니다.
        /// </summary>
        public static event Action OnAbnormalExitDetected;

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드 (수동 제어용)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// flag 파일을 수동으로 생성합니다.
        /// Play Mode 시작 시 자동 호출되므로 보통 직접 호출할 필요 없습니다.
        /// </summary>
        public static void CreateFlagFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(FlagFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 플래그 파일에 세션 시작 시각 기록
                string content = DateTime.UtcNow.ToString("O");
                File.WriteAllText(FlagFilePath, content, System.Text.Encoding.UTF8);

                Debug.Log($"[BugBeacon] 비정상 종료 플래그 생성: {FlagFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] 비정상 종료 플래그 생성 실패 (무시): {ex.Message}");
            }
        }

        /// <summary>
        /// flag 파일을 수동으로 삭제합니다.
        /// 정상 종료 시 자동 호출됩니다.
        /// </summary>
        public static void DeleteFlagFile()
        {
            try
            {
                if (File.Exists(FlagFilePath))
                {
                    File.Delete(FlagFilePath);
                    Debug.Log("[BugBeacon] 비정상 종료 플래그 삭제 완료 (정상 종료).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] 비정상 종료 플래그 삭제 실패 (무시): {ex.Message}");
            }
        }

        /// <summary>
        /// 플래그 파일에 기록된 세션 시작 시각을 반환합니다.
        /// 플래그 파일이 없거나 파싱 실패 시 null 반환.
        /// </summary>
        public static DateTime? GetFlagTimestamp()
        {
            try
            {
                if (!File.Exists(FlagFilePath))
                    return null;

                string content = File.ReadAllText(FlagFilePath, System.Text.Encoding.UTF8).Trim();
                if (DateTime.TryParse(content, out DateTime dt))
                    return dt;

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 이벤트 핸들러
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Application.quitting 이벤트 핸들러.
        /// 정상 종료 시 flag 파일을 삭제합니다.
        /// </summary>
        private static void OnApplicationQuitting()
        {
            Application.quitting -= OnApplicationQuitting;
            DeleteFlagFile();
        }
    }
}
