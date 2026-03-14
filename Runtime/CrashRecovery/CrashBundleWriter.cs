using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 크래시 발생 시 active/ 디렉토리의 플러시 데이터를 수집하여
    /// crash_bundles/ 에 크래시 번들을 생성하는 클래스.
    ///
    /// 크래시 번들 구조:
    ///   {persistentDataPath}/BugBeacon/crash_bundles/{timestamp}/
    ///   ├── manifest.json       (type: "crash", data_integrity 포함)
    ///   ├── logs_flush.zip      (active/에서 복사)
    ///   ├── state_flush.json    (active/에서 복사)
    ///   ├── video_flush/        (active/에서 복사, 존재하는 경우)
    ///   └── crash_info.json     (예외 정보)
    ///
    /// data_integrity:
    ///   각 파일의 SHA256 해시와 존재 여부, 전체 검증 결과 포함
    /// </summary>
    public class CrashBundleWriter
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>크래시 번들 저장 루트 디렉토리명</summary>
        public const string CrashBundlesDirName = "crash_bundles";

        /// <summary>크래시 번들 타임스탬프 형식</summary>
        private const string TimestampFormat = "yyyyMMdd_HHmmss_fff";

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private readonly MappedFileWriter _fileWriter;

        // ──────────────────────────────────────────────────────────────
        // 내부 캐시 (지연 초기화)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// CrashBundlesDir 캐시.
        /// Application.persistentDataPath는 메인 스레드에서만 접근 가능하므로
        /// 필드 초기화자 대신 처음 접근 시점에 초기화합니다.
        /// </summary>
        private static string _crashBundlesDir;

        // ──────────────────────────────────────────────────────────────
        // 공개 프로퍼티
        // ──────────────────────────────────────────────────────────────

        /// <summary>크래시 번들 저장 루트 디렉토리 경로</summary>
        public static string CrashBundlesDir =>
            _crashBundlesDir ??= Path.Combine(Application.persistentDataPath, "BugBeacon", CrashBundlesDirName);

        // ──────────────────────────────────────────────────────────────
        // 생성자
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// CrashBundleWriter를 초기화합니다.
        /// </summary>
        public CrashBundleWriter()
        {
            _fileWriter = new MappedFileWriter();
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들을 생성합니다.
        /// active/ 디렉토리의 플러시 데이터를 새 번들 디렉토리로 복사합니다.
        /// </summary>
        /// <param name="crashType">크래시 원인 유형 (예: "managed_exception", "abnormal_exit")</param>
        /// <param name="exceptionType">예외 클래스명 (null 허용)</param>
        /// <param name="exceptionMessage">예외 메시지 (null 허용)</param>
        /// <param name="stackTrace">스택 트레이스 (null 허용)</param>
        /// <returns>생성된 크래시 번들 매니페스트. 실패 시 null.</returns>
        public async Task<CrashBundleManifest> BuildAsync(
            string crashType = "abnormal_exit",
            string exceptionType = null,
            string exceptionMessage = null,
            string stackTrace = null)
        {
            string timestamp = DateTime.UtcNow.ToString(TimestampFormat);
            string bundleDir = Path.Combine(CrashBundlesDir, timestamp);

            try
            {
                // 번들 디렉토리 생성
                Directory.CreateDirectory(bundleDir);

                string activeDir = PeriodicFlushManager.ActiveDir;

                // 플러시 데이터 복사 및 무결성 검증
                var integrity = await CopyFlushDataAsync(activeDir, bundleDir);

                // crash_info.json 생성
                await WriteCrashInfoAsync(bundleDir, crashType, exceptionType, exceptionMessage, stackTrace);

                // manifest.json 생성
                var manifest = BuildManifest(timestamp, crashType, exceptionType, exceptionMessage, stackTrace, integrity);
                await WriteManifestAsync(bundleDir, manifest);

                // active/ 디렉토리 클린업
                CleanupActiveDir(activeDir);

                Debug.Log($"[BugBeacon] 크래시 번들 생성 완료: {bundleDir} (무결성: {integrity.overall})");

                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 크래시 번들 생성 실패: {ex.Message}");

                // 실패한 번들 디렉토리 정리
                TryDeleteDirectory(bundleDir);
                return null;
            }
        }

        /// <summary>
        /// 크래시 번들 디렉토리 경로를 반환합니다.
        /// </summary>
        public static string GetBundleDir(string timestamp)
        {
            return Path.Combine(CrashBundlesDir, timestamp);
        }

        /// <summary>
        /// 모든 크래시 번들 매니페스트를 스캔하여 반환합니다.
        /// 생성 시각 오름차순(오래된 것이 앞)으로 정렬됩니다.
        /// </summary>
        public static List<CrashBundleManifest> ScanAllBundles()
        {
            var results = new List<CrashBundleManifest>();

            if (!Directory.Exists(CrashBundlesDir))
                return results;

            foreach (string dir in Directory.GetDirectories(CrashBundlesDir))
            {
                string manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    string json = File.ReadAllText(manifestPath, Encoding.UTF8);
                    var manifest = JsonUtility.FromJson<CrashBundleManifest>(json);
                    if (manifest != null && !string.IsNullOrEmpty(manifest.id))
                        results.Add(manifest);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BugBeacon] 크래시 번들 manifest 파싱 실패 ({dir}): {ex.Message}");
                }
            }

            // 생성 시각 오름차순 정렬
            results.Sort((a, b) => string.Compare(a.created_at, b.created_at, StringComparison.Ordinal));
            return results;
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현 - 플러시 데이터 복사
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// active/ 디렉토리의 플러시 데이터를 번들 디렉토리로 복사하고
        /// 각 파일의 무결성(SHA256)을 검증합니다.
        /// </summary>
        private static async Task<DataIntegrity> CopyFlushDataAsync(string activeDir, string bundleDir)
        {
            var integrity = new DataIntegrity();

            // 로그 ZIP 복사
            string logsSrc = Path.Combine(activeDir, PeriodicFlushManager.LogsFlushFileName);
            string logsDst = Path.Combine(bundleDir, PeriodicFlushManager.LogsFlushFileName);
            integrity.logs_ok = await TryCopyFileAsync(logsSrc, logsDst);
            if (integrity.logs_ok)
                integrity.logs_sha256 = await SHA256HashUtility.ComputeFileHashAsync(logsDst);

            // 상태 JSON 복사
            string stateSrc = Path.Combine(activeDir, PeriodicFlushManager.StateFlushFileName);
            string stateDst = Path.Combine(bundleDir, PeriodicFlushManager.StateFlushFileName);
            integrity.state_ok = await TryCopyFileAsync(stateSrc, stateDst);
            if (integrity.state_ok)
                integrity.state_sha256 = await SHA256HashUtility.ComputeFileHashAsync(stateDst);

            // 영상 디렉토리 복사
            string videoSrc = Path.Combine(activeDir, PeriodicFlushManager.VideoFlushDirName);
            string videoDst = Path.Combine(bundleDir, PeriodicFlushManager.VideoFlushDirName);
            integrity.video_ok = await TryCopyDirectoryAsync(videoSrc, videoDst);

            // 전체 무결성 판정
            integrity.overall = DetermineOverall(integrity);

            return integrity;
        }

        /// <summary>
        /// 파일을 복사합니다. 소스가 없으면 false 반환.
        /// </summary>
        private static async Task<bool> TryCopyFileAsync(string src, string dst)
        {
            if (!File.Exists(src))
                return false;

            try
            {
                await Task.Run(() => File.Copy(src, dst, overwrite: true));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] 파일 복사 실패 ({src} → {dst}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 디렉토리를 재귀 복사합니다. 소스가 없으면 false 반환.
        /// </summary>
        private static async Task<bool> TryCopyDirectoryAsync(string src, string dst)
        {
            if (!Directory.Exists(src))
                return false;

            try
            {
                await Task.Run(() => CopyDirectoryRecursive(src, dst));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] 디렉토리 복사 실패 ({src} → {dst}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 디렉토리를 재귀적으로 복사합니다.
        /// </summary>
        private static void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (string file in Directory.GetFiles(src))
            {
                string fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dst, fileName), overwrite: true);
            }

            foreach (string subDir in Directory.GetDirectories(src))
            {
                string subDirName = Path.GetFileName(subDir);
                CopyDirectoryRecursive(subDir, Path.Combine(dst, subDirName));
            }
        }

        /// <summary>
        /// 전체 무결성 상태를 결정합니다.
        /// PRD 스펙에 따라 "complete" / "partial" / "missing" 값을 반환합니다.
        /// </summary>
        private static string DetermineOverall(DataIntegrity integrity)
        {
            bool anyOk = integrity.logs_ok || integrity.state_ok || integrity.video_ok;
            bool allOk = integrity.logs_ok && integrity.state_ok; // 영상은 선택사항

            if (allOk)
                return "complete"; // PRD 스펙: "ok" 대신 "complete" 사용 (AC-26)
            if (anyOk)
                return "partial";
            return "missing";
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현 - 크래시 정보 파일 생성
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// crash_info.json 파일을 생성합니다.
        /// </summary>
        private async Task WriteCrashInfoAsync(
            string bundleDir,
            string crashType,
            string exceptionType,
            string exceptionMessage,
            string stackTrace)
        {
            var crashInfo = new CrashInfo
            {
                crash_type = crashType ?? "unknown",
                exception_type = exceptionType ?? "",
                exception_message = exceptionMessage ?? "",
                stack_trace = stackTrace ?? "",
                occurred_at = DateTime.UtcNow.ToString("O"),
                platform = Application.platform.ToString(),
                unity_version = Application.unityVersion,
                app_version = Application.version,
            };

            string json = JsonUtility.ToJson(crashInfo, prettyPrint: true);
            string destPath = Path.Combine(bundleDir, "crash_info.json");

            await _fileWriter.WriteTextAsync(destPath, json);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현 - 매니페스트 생성
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// CrashBundleManifest를 생성합니다.
        /// </summary>
        private static CrashBundleManifest BuildManifest(
            string timestamp,
            string crashType,
            string exceptionType,
            string exceptionMessage,
            string stackTrace,
            DataIntegrity integrity)
        {
            return new CrashBundleManifest
            {
                id = timestamp,
                type = "crash",
                created_at = DateTime.UtcNow.ToString("O"),
                plugin_version = "1.0.0",
                unity_version = Application.unityVersion,
                crash_type = crashType ?? "unknown",
                exception_type = exceptionType ?? "",
                exception_message = exceptionMessage ?? "",
                stack_trace = stackTrace ?? "",
                data_integrity = integrity,
                jira_issue_key = null,
                registered_at = null,
            };
        }

        /// <summary>
        /// manifest.json을 원자적으로 씁니다.
        /// </summary>
        private async Task WriteManifestAsync(string bundleDir, CrashBundleManifest manifest)
        {
            string json = JsonUtility.ToJson(manifest, prettyPrint: true);
            string destPath = Path.Combine(bundleDir, "manifest.json");
            await _fileWriter.WriteTextAsync(destPath, json);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현 - active/ 클린업
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들 생성 후 active/ 디렉토리를 초기화합니다.
        /// 다음 플러시 사이클을 위해 디렉토리는 유지하고 파일만 삭제합니다.
        /// </summary>
        private static void CleanupActiveDir(string activeDir)
        {
            try
            {
                if (!Directory.Exists(activeDir))
                    return;

                // 파일 삭제
                foreach (string file in Directory.GetFiles(activeDir))
                {
                    try { File.Delete(file); }
                    catch { /* 개별 파일 삭제 실패는 무시 */ }
                }

                // 서브 디렉토리 삭제
                foreach (string dir in Directory.GetDirectories(activeDir))
                {
                    TryDeleteDirectory(dir);
                }

                Debug.Log("[BugBeacon] active/ 디렉토리 클린업 완료.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] active/ 클린업 실패 (무시): {ex.Message}");
            }
        }

        /// <summary>
        /// 디렉토리를 안전하게 삭제합니다.
        /// </summary>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] 디렉토리 삭제 실패 ({path}): {ex.Message}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 데이터 모델
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 크래시 번들 데이터 무결성 정보.
    /// manifest.json의 data_integrity 필드에 포함됩니다.
    /// </summary>
    [Serializable]
    public class DataIntegrity
    {
        /// <summary>로그 파일 존재 및 무결성 여부</summary>
        public bool logs_ok;

        /// <summary>로그 파일 SHA256 해시</summary>
        public string logs_sha256;

        /// <summary>상태 파일 존재 및 무결성 여부</summary>
        public bool state_ok;

        /// <summary>상태 파일 SHA256 해시</summary>
        public string state_sha256;

        /// <summary>영상 디렉토리 존재 여부</summary>
        public bool video_ok;

        /// <summary>
        /// 전체 무결성 상태 (PRD 스펙 AC-26).
        /// "complete": 필수 데이터 모두 정상
        /// "partial": 일부만 존재
        /// "missing": 유효한 데이터 없음
        /// </summary>
        public string overall;
    }

    /// <summary>
    /// 크래시 번들 매니페스트.
    /// manifest.json으로 직렬화됩니다.
    /// </summary>
    [Serializable]
    public class CrashBundleManifest
    {
        /// <summary>번들 ID (타임스탬프 형식: yyyyMMdd_HHmmss_fff)</summary>
        public string id;

        /// <summary>번들 유형 ("crash")</summary>
        public string type;

        /// <summary>생성 시각 (ISO 8601 UTC)</summary>
        public string created_at;

        /// <summary>플러그인 버전</summary>
        public string plugin_version;

        /// <summary>Unity 버전</summary>
        public string unity_version;

        /// <summary>크래시 원인 유형 (managed_exception / abnormal_exit / unknown)</summary>
        public string crash_type;

        /// <summary>예외 클래스명 (managed_exception인 경우)</summary>
        public string exception_type;

        /// <summary>예외 메시지</summary>
        public string exception_message;

        /// <summary>스택 트레이스</summary>
        public string stack_trace;

        /// <summary>데이터 무결성 정보</summary>
        public DataIntegrity data_integrity;

        /// <summary>Jira 이슈 키 (미등록 시 null)</summary>
        public string jira_issue_key;

        /// <summary>Jira 등록 완료 시각 (미등록 시 null)</summary>
        public string registered_at;

        /// <summary>
        /// Jira 등록 완료 여부 (AC-20/24).
        /// 초기값 false, Jira 제출 성공 시 true로 갱신됩니다.
        /// </summary>
        public bool registered = false;
    }

    /// <summary>
    /// crash_info.json에 저장되는 크래시 세부 정보.
    /// </summary>
    [Serializable]
    public class CrashInfo
    {
        /// <summary>크래시 원인 유형</summary>
        public string crash_type;

        /// <summary>예외 클래스명</summary>
        public string exception_type;

        /// <summary>예외 메시지</summary>
        public string exception_message;

        /// <summary>스택 트레이스</summary>
        public string stack_trace;

        /// <summary>크래시 발생 시각 (ISO 8601 UTC)</summary>
        public string occurred_at;

        /// <summary>플랫폼</summary>
        public string platform;

        /// <summary>Unity 버전</summary>
        public string unity_version;

        /// <summary>앱 버전</summary>
        public string app_version;
    }
}
