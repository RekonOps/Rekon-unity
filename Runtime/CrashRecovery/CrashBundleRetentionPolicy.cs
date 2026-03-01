using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 크래시 번들 보존 정책을 적용하는 클래스.
    ///
    /// 두 가지 정책을 적용합니다:
    ///   1. 최대 개수 제한 (FIFO): maxCrashBundles를 초과하면 오래된 것부터 삭제
    ///   2. 보존 기간 제한: retentionDays 이상 된 번들 자동 삭제
    ///
    /// Apply() 호출 시 두 정책을 순서대로 적용합니다.
    /// </summary>
    public class CrashBundleRetentionPolicy
    {
        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private readonly int _maxBundles;
        private readonly int _retentionDays;

        // ──────────────────────────────────────────────────────────────
        // 생성자
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// CrashBundleRetentionPolicy를 초기화합니다.
        /// </summary>
        /// <param name="maxBundles">최대 보존 번들 수 (FIFO, 기본 10)</param>
        /// <param name="retentionDays">최대 보존 기간 (일, 기본 30)</param>
        public CrashBundleRetentionPolicy(int maxBundles = 10, int retentionDays = 30)
        {
            if (maxBundles <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBundles), "최대 번들 수는 1 이상이어야 합니다.");

            if (retentionDays <= 0)
                throw new ArgumentOutOfRangeException(nameof(retentionDays), "보존 기간은 1일 이상이어야 합니다.");

            _maxBundles = maxBundles;
            _retentionDays = retentionDays;
        }

        /// <summary>
        /// BugOneTouchSettings에서 정책 설정을 읽어 초기화합니다.
        /// </summary>
        /// <param name="settings">Bug-OneTouch 설정</param>
        public CrashBundleRetentionPolicy(BugOneTouchSettings settings)
            : this(settings?.maxCrashBundles ?? 10, settings?.crashBundleRetentionDays ?? 30)
        {
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 프로퍼티
        // ──────────────────────────────────────────────────────────────

        /// <summary>최대 보존 번들 수</summary>
        public int MaxBundles => _maxBundles;

        /// <summary>최대 보존 기간 (일)</summary>
        public int RetentionDays => _retentionDays;

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 보존 정책을 적용합니다.
        ///   1. 기간 초과 번들 삭제 (retentionDays 이상 경과)
        ///   2. 최대 개수 초과 번들 삭제 (오래된 것부터 FIFO)
        /// </summary>
        /// <returns>삭제된 번들 수</returns>
        public int Apply()
        {
            int deletedCount = 0;

            try
            {
                var bundles = CrashBundleWriter.ScanAllBundles();

                if (bundles.Count == 0)
                    return 0;

                // 정책 1: 기간 초과 번들 삭제
                var expiredBundles = FindExpiredBundles(bundles);
                foreach (var manifest in expiredBundles)
                {
                    if (TryDeleteBundle(manifest.id))
                    {
                        deletedCount++;
                        bundles.Remove(manifest);
                        Debug.Log($"[BugOneTouch] 기간 초과 크래시 번들 삭제: {manifest.id} (생성: {manifest.created_at})");
                    }
                }

                // 정책 2: 최대 개수 초과 번들 삭제 (FIFO - 가장 오래된 것부터)
                while (bundles.Count > _maxBundles)
                {
                    // ScanAllBundles()는 created_at 오름차순으로 정렬되어 있으므로
                    // 첫 번째 항목이 가장 오래된 것
                    var oldest = bundles[0];
                    if (TryDeleteBundle(oldest.id))
                    {
                        deletedCount++;
                        Debug.Log($"[BugOneTouch] 최대 개수 초과 크래시 번들 삭제 (FIFO): {oldest.id}");
                    }
                    bundles.RemoveAt(0);
                }

                if (deletedCount > 0)
                    Debug.Log($"[BugOneTouch] 크래시 번들 보존 정책 적용 완료: {deletedCount}개 삭제, {bundles.Count}개 유지.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 크래시 번들 보존 정책 적용 실패: {ex.Message}");
            }

            return deletedCount;
        }

        /// <summary>
        /// 현재 크래시 번들 수를 반환합니다.
        /// </summary>
        public int GetCurrentCount()
        {
            return CrashBundleWriter.ScanAllBundles().Count;
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 보존 기간이 초과된 번들 목록을 반환합니다.
        /// created_at 파싱에 실패한 번들은 제외합니다.
        /// </summary>
        private List<CrashBundleManifest> FindExpiredBundles(List<CrashBundleManifest> bundles)
        {
            var expired = new List<CrashBundleManifest>();
            var cutoffTime = DateTime.UtcNow.AddDays(-_retentionDays);

            foreach (var manifest in bundles)
            {
                if (!TryParseCreatedAt(manifest.created_at, out DateTime createdAt))
                    continue;

                if (createdAt < cutoffTime)
                    expired.Add(manifest);
            }

            return expired;
        }

        /// <summary>
        /// created_at 문자열을 DateTime으로 파싱합니다.
        /// </summary>
        private static bool TryParseCreatedAt(string createdAt, out DateTime result)
        {
            if (string.IsNullOrEmpty(createdAt))
            {
                result = default;
                return false;
            }

            return DateTime.TryParse(createdAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out result);
        }

        /// <summary>
        /// 크래시 번들 디렉토리를 삭제합니다.
        /// </summary>
        private static bool TryDeleteBundle(string bundleId)
        {
            try
            {
                string bundleDir = Path.Combine(CrashBundleWriter.CrashBundlesDir, bundleId);

                if (!Directory.Exists(bundleDir))
                {
                    Debug.LogWarning($"[BugOneTouch] 삭제할 크래시 번들 디렉토리가 없습니다: {bundleId}");
                    return true; // 이미 없는 것은 성공으로 처리
                }

                Directory.Delete(bundleDir, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 크래시 번들 삭제 실패 ({bundleId}): {ex.Message}");
                return false;
            }
        }
    }
}
