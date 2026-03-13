using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 오프라인/미로그인 시 전송 실패한 번들을 pending 큐에 등록하는 매니저.
    ///
    /// pending 큐 관리:
    ///   - {persistentDataPath}/BugOneTouch/pending/ 폴더 사용
    ///   - 각 항목은 {bundleId}.pending.json 파일
    ///   - 번들 자체는 BundleWriter가 이미 로컬에 저장
    ///
    /// 자동 재시도 없음 — 향후 미전송 리포트 UI에서 수동 재전송을 지원합니다.
    /// </summary>
    public class PendingUploadManager
    {
        // ─── 상수 ──────────────────────────────────────────────────────────

        private const string PendingFolderName = "pending";
        private const string PendingFileSuffix = ".pending.json";

        // ─── pending 큐 항목 모델 ──────────────────────────────────────────

        /// <summary>
        /// pending 큐 파일에 직렬화되는 데이터 모델.
        /// </summary>
        [Serializable]
        public class PendingEntry
        {
            public string bundleId;
            public string title;
            public string createdAt;
        }

        // ─── 생성자 ──────────────────────────────────────────────────────────

        /// <summary>
        /// PendingUploadManager를 초기화합니다.
        /// pending 디렉토리가 없으면 자동 생성합니다.
        /// </summary>
        public PendingUploadManager()
        {
            EnsurePendingDirectory();
            Debug.Log("[BugOneTouch] PendingUploadManager 초기화 완료");
        }

        // ─── 공개 메서드 ──────────────────────────────────────────────────────

        /// <summary>
        /// 번들을 pending 큐에 등록합니다.
        /// pending JSON 파일을 atomic하게 생성합니다. (temp → Move 패턴)
        /// </summary>
        /// <param name="manifest">등록할 번들 매니페스트</param>
        public async Task EnqueueAsync(BundleManifest manifest)
        {
            if (manifest == null)
            {
                Debug.LogWarning("[BugOneTouch] PendingUpload: null 매니페스트를 enqueue 시도");
                return;
            }

            try
            {
                var entry = new PendingEntry
                {
                    bundleId = manifest.id,
                    title = manifest.title ?? "",
                    createdAt = DateTime.UtcNow.ToString("O")
                };

                string pendingPath = GetPendingFilePath(manifest.id);
                string json = JsonUtility.ToJson(entry, prettyPrint: true);
                await Task.Run(() =>
                {
                    string tempPath = pendingPath + ".tmp";
                    File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                    if (File.Exists(pendingPath))
                        File.Delete(pendingPath);
                    File.Move(tempPath, pendingPath);
                });

                Debug.Log($"[BugOneTouch] PendingUpload: 큐에 등록 완료 (bundleId={manifest.id})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] PendingUpload: 큐 등록 실패 ({manifest.id}): {ex.Message}");
            }
        }

        /// <summary>
        /// pending 폴더를 스캔하여 모든 pending 항목 목록을 반환합니다.
        /// 생성 시각 오름차순(오래된 것 먼저)으로 정렬됩니다.
        /// 향후 미전송 리포트 UI에서 사용됩니다.
        /// </summary>
        /// <returns>PendingEntry 목록. 비어있으면 빈 리스트.</returns>
        public List<PendingEntry> GetPendingEntries()
        {
            var entries = new List<PendingEntry>();
            string pendingDir = GetPendingDirectory();

            if (!Directory.Exists(pendingDir))
                return entries;

            foreach (string filePath in Directory.GetFiles(pendingDir, $"*{PendingFileSuffix}"))
            {
                try
                {
                    string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                    var entry = JsonUtility.FromJson<PendingEntry>(json);
                    if (entry != null && !string.IsNullOrEmpty(entry.bundleId))
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BugOneTouch] PendingUpload: pending 파일 파싱 실패 ({filePath}): {ex.Message}");
                }
            }

            // 생성 시각 오름차순 정렬 (오래된 것 먼저)
            entries.Sort((a, b) => string.Compare(a.createdAt, b.createdAt, StringComparison.Ordinal));

            return entries;
        }

        /// <summary>
        /// pending 항목 수를 반환합니다.
        /// </summary>
        public int GetPendingCount()
        {
            string pendingDir = GetPendingDirectory();
            if (!Directory.Exists(pendingDir))
                return 0;

            return Directory.GetFiles(pendingDir, $"*{PendingFileSuffix}").Length;
        }

        /// <summary>
        /// 특정 bundleId의 pending 파일을 삭제합니다.
        /// </summary>
        public void RemovePendingFile(string bundleId)
        {
            try
            {
                string pendingPath = GetPendingFilePath(bundleId);
                if (File.Exists(pendingPath))
                {
                    File.Delete(pendingPath);
                    Debug.Log($"[BugOneTouch] PendingUpload: pending 파일 제거 (bundleId={bundleId})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] PendingUpload: pending 파일 삭제 실패 ({bundleId}): {ex.Message}");
            }
        }

        // ─── 경로 유틸리티 ──────────────────────────────────────────────────

        /// <summary>
        /// pending 디렉토리 경로를 반환합니다.
        /// </summary>
        public static string GetPendingDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "BugOneTouch", PendingFolderName);
        }

        /// <summary>
        /// 특정 bundleId의 pending 파일 경로를 반환합니다.
        /// </summary>
        private static string GetPendingFilePath(string bundleId)
        {
            return Path.Combine(GetPendingDirectory(), $"{bundleId}{PendingFileSuffix}");
        }

        /// <summary>
        /// pending 디렉토리가 존재하지 않으면 생성합니다.
        /// </summary>
        private static void EnsurePendingDirectory()
        {
            string dir = GetPendingDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[BugOneTouch] PendingUpload: pending 디렉토리 생성: {dir}");
            }
        }
    }
}
