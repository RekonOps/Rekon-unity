using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 로컬 디스크에 저장된 번들 목록을 관리하는 저장소 클래스.
    ///
    /// 역할:
    ///   - 번들 디렉토리 스캔 (manifest.json 파싱)
    ///   - 번들 목록 조회 및 상태별 필터링
    ///   - 번들 상태 변경 (manifest.json 갱신)
    ///   - 번들 삭제
    /// </summary>
    public class BundleRepository
    {
        private const string ManifestFileName = "manifest.json";

        /// <summary>
        /// BundleRepository를 초기화합니다.
        /// </summary>
        public BundleRepository() { }

        // ──────────────────────────────────────────────────────────────
        // 조회
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 디스크를 스캔하여 모든 번들 목록을 반환합니다.
        /// manifest.json이 없거나 파싱에 실패한 디렉토리는 건너뜁니다.
        /// 결과는 created_at 오름차순(가장 오래된 것이 앞)으로 정렬됩니다.
        /// </summary>
        /// <returns>BundleManifest 목록. 번들이 없으면 빈 리스트.</returns>
        public async Task<List<BundleManifest>> GetAllAsync()
        {
            return await Task.Run(() => ScanBundles(bundleState: null));
        }

        /// <summary>
        /// 특정 상태의 번들 목록을 반환합니다.
        /// 결과는 created_at 오름차순으로 정렬됩니다.
        /// </summary>
        /// <param name="state">필터할 번들 상태.</param>
        /// <returns>해당 상태의 BundleManifest 목록.</returns>
        public async Task<List<BundleManifest>> GetByStateAsync(BundleState state)
        {
            return await Task.Run(() => ScanBundles(bundleState: state));
        }

        /// <summary>
        /// 특정 ID의 번들 매니페스트를 반환합니다.
        /// </summary>
        /// <param name="bundleId">번들 고유 ID.</param>
        /// <returns>BundleManifest. 없으면 null.</returns>
        public async Task<BundleManifest> GetByIdAsync(string bundleId)
        {
            if (string.IsNullOrEmpty(bundleId))
                throw new ArgumentNullException(nameof(bundleId), "번들 ID가 null 또는 빈 문자열입니다.");

            return await Task.Run(() =>
            {
                string bundleDir    = BundleWriter.GetBundleDirectory(bundleId);
                string manifestPath = Path.Combine(bundleDir, ManifestFileName);

                if (!File.Exists(manifestPath))
                    return null;

                return TryParseManifest(manifestPath);
            });
        }

        // ──────────────────────────────────────────────────────────────
        // 상태 변경
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들 상태를 변경하고 manifest.json을 원자적으로 갱신합니다.
        /// </summary>
        /// <param name="bundleId">대상 번들 ID.</param>
        /// <param name="newState">변경할 새 상태.</param>
        /// <exception cref="ArgumentNullException">bundleId가 null인 경우.</exception>
        /// <exception cref="FileNotFoundException">번들을 찾을 수 없는 경우.</exception>
        public async Task UpdateStateAsync(string bundleId, BundleState newState)
        {
            if (string.IsNullOrEmpty(bundleId))
                throw new ArgumentNullException(nameof(bundleId), "번들 ID가 null 또는 빈 문자열입니다.");

            await Task.Run(() =>
            {
                string manifestPath = GetManifestPath(bundleId);

                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException($"번들 manifest를 찾을 수 없습니다: {bundleId}", manifestPath);

                BundleManifest manifest = TryParseManifest(manifestPath);
                if (manifest == null)
                    throw new InvalidDataException($"번들 manifest 파싱 실패: {bundleId}");

                manifest.state = newState;
                WriteManifestAtomic(manifest, manifestPath);

                Debug.Log($"[BugOneTouch] 번들 상태 변경: {bundleId} → {newState}");
            });
        }

        /// <summary>
        /// 번들의 제출 성공 정보를 기록합니다.
        /// 상태를 Submitted로 변경하고, jira_issue_key와 registered_at을 갱신합니다.
        /// </summary>
        /// <param name="bundleId">대상 번들 ID.</param>
        /// <param name="jiraIssueKey">Jira 이슈 키 (예: BUG-123).</param>
        public async Task MarkSubmittedAsync(string bundleId, string jiraIssueKey)
        {
            if (string.IsNullOrEmpty(bundleId))
                throw new ArgumentNullException(nameof(bundleId), "번들 ID가 null 또는 빈 문자열입니다.");

            await Task.Run(() =>
            {
                string manifestPath = GetManifestPath(bundleId);

                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException($"번들 manifest를 찾을 수 없습니다: {bundleId}", manifestPath);

                BundleManifest manifest = TryParseManifest(manifestPath);
                if (manifest == null)
                    throw new InvalidDataException($"번들 manifest 파싱 실패: {bundleId}");

                manifest.state          = BundleState.Submitted;
                manifest.jira_issue_key = jiraIssueKey;
                manifest.registered_at  = DateTime.UtcNow.ToString("O");

                WriteManifestAtomic(manifest, manifestPath);

                Debug.Log($"[BugOneTouch] 번들 제출 완료: {bundleId} → Jira={jiraIssueKey}");
            });
        }

        /// <summary>
        /// 번들의 재시도 횟수를 증가시킵니다.
        /// </summary>
        /// <param name="bundleId">대상 번들 ID.</param>
        /// <returns>증가 후 재시도 횟수.</returns>
        public async Task<int> IncrementRetryCountAsync(string bundleId)
        {
            if (string.IsNullOrEmpty(bundleId))
                throw new ArgumentNullException(nameof(bundleId), "번들 ID가 null 또는 빈 문자열입니다.");

            return await Task.Run(() =>
            {
                string manifestPath = GetManifestPath(bundleId);

                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException($"번들 manifest를 찾을 수 없습니다: {bundleId}", manifestPath);

                BundleManifest manifest = TryParseManifest(manifestPath);
                if (manifest == null)
                    throw new InvalidDataException($"번들 manifest 파싱 실패: {bundleId}");

                manifest.retry_count++;
                WriteManifestAtomic(manifest, manifestPath);

                Debug.Log($"[BugOneTouch] 번들 재시도 횟수 증가: {bundleId} → {manifest.retry_count}회");
                return manifest.retry_count;
            });
        }

        // ──────────────────────────────────────────────────────────────
        // 삭제
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들 디렉토리를 완전히 삭제합니다.
        /// </summary>
        /// <param name="bundleId">삭제할 번들 ID.</param>
        /// <exception cref="ArgumentNullException">bundleId가 null인 경우.</exception>
        public async Task DeleteAsync(string bundleId)
        {
            if (string.IsNullOrEmpty(bundleId))
                throw new ArgumentNullException(nameof(bundleId), "번들 ID가 null 또는 빈 문자열입니다.");

            await Task.Run(() =>
            {
                string bundleDir = BundleWriter.GetBundleDirectory(bundleId);

                if (!Directory.Exists(bundleDir))
                {
                    Debug.LogWarning($"[BugOneTouch] 삭제할 번들 디렉토리가 없습니다: {bundleId}");
                    return;
                }

                Directory.Delete(bundleDir, recursive: true);
                Debug.Log($"[BugOneTouch] 번들 삭제 완료: {bundleId}");
            });
        }

        // ──────────────────────────────────────────────────────────────
        // 통계
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 전체 번들 수와 디스크 사용량(바이트)을 반환합니다.
        /// </summary>
        /// <returns>(번들 수, 총 바이트).</returns>
        public async Task<(int count, long totalBytes)> GetStorageStatsAsync()
        {
            return await Task.Run(() =>
            {
                List<BundleManifest> all = ScanBundles(null);
                long totalBytes = 0L;
                foreach (var m in all)
                    totalBytes += m.total_size_bytes;
                return (all.Count, totalBytes);
            });
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들 루트 디렉토리를 스캔하여 manifest.json이 있는 번들 목록을 반환합니다.
        /// bundleState가 지정된 경우 해당 상태만 필터합니다.
        /// </summary>
        private static List<BundleManifest> ScanBundles(BundleState? bundleState)
        {
            string bundlesRoot = BundleWriter.GetBundlesRootDirectory();

            if (!Directory.Exists(bundlesRoot))
                return new List<BundleManifest>();

            var results = new List<BundleManifest>();

            foreach (string bundleDir in Directory.GetDirectories(bundlesRoot))
            {
                string manifestPath = Path.Combine(bundleDir, ManifestFileName);
                if (!File.Exists(manifestPath))
                    continue;

                BundleManifest manifest = TryParseManifest(manifestPath);
                if (manifest == null || !manifest.IsValid())
                    continue;

                // 상태 필터 적용
                if (bundleState.HasValue && manifest.state != bundleState.Value)
                    continue;

                results.Add(manifest);
            }

            // created_at 오름차순 정렬 (오래된 것이 앞)
            results.Sort((a, b) => string.Compare(a.created_at, b.created_at, StringComparison.Ordinal));

            return results;
        }

        /// <summary>
        /// manifest.json 파일을 파싱하여 BundleManifest를 반환합니다.
        /// 파싱 실패 시 null을 반환합니다.
        /// </summary>
        private static BundleManifest TryParseManifest(string manifestPath)
        {
            try
            {
                string json = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
                var manifest = JsonUtility.FromJson<BundleManifest>(json);
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] manifest.json 파싱 실패 ({manifestPath}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// manifest.json을 원자적으로 씁니다 (temp → rename).
        /// </summary>
        private static void WriteManifestAtomic(BundleManifest manifest, string manifestPath)
        {
            string tempPath = manifestPath + ".tmp";

            string json = JsonUtility.ToJson(manifest, prettyPrint: true);
            File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);

            if (File.Exists(manifestPath))
                File.Delete(manifestPath);
            File.Move(tempPath, manifestPath);
        }

        /// <summary>
        /// 번들 ID를 기반으로 manifest.json 경로를 반환합니다.
        /// </summary>
        private static string GetManifestPath(string bundleId)
        {
            return Path.Combine(BundleWriter.GetBundleDirectory(bundleId), ManifestFileName);
        }
    }
}
