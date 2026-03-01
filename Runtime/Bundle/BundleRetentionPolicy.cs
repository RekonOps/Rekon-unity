using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 번들 보관 정책을 적용하는 클래스.
    ///
    /// FIFO(선입선출) 방식으로 다음 기준 중 하나라도 초과 시 오래된 번들을 삭제합니다:
    ///   - 최대 번들 수: BugOneTouchSettings.maxBundles (기본 200개)
    ///   - 최대 디스크 사용량: BugOneTouchSettings.maxDiskUsageMB (기본 5,120MB)
    /// </summary>
    public class BundleRetentionPolicy
    {
        private readonly BugOneTouchSettings _settings;
        private readonly BundleRepository _repository;

        /// <summary>
        /// BundleRetentionPolicy를 초기화합니다.
        /// </summary>
        /// <param name="settings">플러그인 설정 (maxBundles, maxDiskUsageMB 참조).</param>
        /// <param name="repository">번들 저장소.</param>
        /// <exception cref="ArgumentNullException">settings 또는 repository가 null인 경우.</exception>
        public BundleRetentionPolicy(BugOneTouchSettings settings, BundleRepository repository)
        {
            _settings   = settings   ?? throw new ArgumentNullException(nameof(settings), "BugOneTouchSettings가 null입니다.");
            _repository = repository ?? throw new ArgumentNullException(nameof(repository), "BundleRepository가 null입니다.");
        }

        /// <summary>
        /// 보관 정책을 적용합니다.
        /// 최대 번들 수 또는 최대 디스크 사용량을 초과한 경우, 오래된 번들부터 삭제합니다.
        /// </summary>
        /// <returns>삭제된 번들 수.</returns>
        public async Task<int> ApplyAsync()
        {
            int deletedCount = 0;

            // 전체 번들 목록 조회 (created_at 오름차순 = 오래된 것이 앞)
            List<BundleManifest> allBundles = await _repository.GetAllAsync();

            // 최대 번들 수 초과 처리
            deletedCount += await ApplyCountLimitAsync(allBundles);

            // 최대 디스크 사용량 초과 처리 (삭제 후 목록 갱신)
            allBundles = await _repository.GetAllAsync();
            deletedCount += await ApplyDiskLimitAsync(allBundles);

            if (deletedCount > 0)
                Debug.Log($"[BugOneTouch] 보관 정책 적용 완료: {deletedCount}개 번들 삭제됨");

            return deletedCount;
        }

        /// <summary>
        /// 현재 번들이 보관 정책 기준(개수/용량)을 초과하는지 확인합니다.
        /// </summary>
        /// <returns>초과하면 true.</returns>
        public async Task<bool> IsOverLimitAsync()
        {
            var (count, totalBytes) = await _repository.GetStorageStatsAsync();

            long maxBytes = (long)_settings.maxDiskUsageMB * 1024L * 1024L;

            return count >= _settings.maxBundles || totalBytes >= maxBytes;
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들 수 제한을 적용합니다.
        /// 최대 번들 수를 초과하면 오래된 번들부터 삭제합니다.
        /// </summary>
        private async Task<int> ApplyCountLimitAsync(List<BundleManifest> bundles)
        {
            int maxCount = _settings.maxBundles;
            int deletedCount = 0;

            while (bundles.Count - deletedCount > maxCount)
            {
                var oldest = bundles[deletedCount];

                try
                {
                    await _repository.DeleteAsync(oldest.id);
                    Debug.Log($"[BugOneTouch] 보관 정책(개수 초과) - 번들 삭제: {oldest.id} (생성: {oldest.created_at})");
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BugOneTouch] 번들 삭제 실패 ({oldest.id}): {ex.Message}");
                    break; // 삭제 실패 시 중단 (무한 루프 방지)
                }
            }

            return deletedCount;
        }

        /// <summary>
        /// 디스크 사용량 제한을 적용합니다.
        /// 최대 디스크 사용량을 초과하면 오래된 번들부터 삭제합니다.
        /// </summary>
        private async Task<int> ApplyDiskLimitAsync(List<BundleManifest> bundles)
        {
            long maxBytes = (long)_settings.maxDiskUsageMB * 1024L * 1024L;
            int deletedCount = 0;

            // 현재 총 사용량 계산
            long currentBytes = 0L;
            foreach (var bundle in bundles)
                currentBytes += bundle.total_size_bytes;

            int index = 0;
            while (currentBytes > maxBytes && index < bundles.Count)
            {
                var oldest = bundles[index];

                try
                {
                    await _repository.DeleteAsync(oldest.id);
                    currentBytes -= oldest.total_size_bytes;
                    Debug.Log($"[BugOneTouch] 보관 정책(용량 초과) - 번들 삭제: {oldest.id} " +
                              $"(크기: {oldest.total_size_bytes}B, 잔여: {currentBytes}B/{maxBytes}B)");
                    deletedCount++;
                    index++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BugOneTouch] 번들 삭제 실패 ({oldest.id}): {ex.Message}");
                    break; // 삭제 실패 시 중단
                }
            }

            return deletedCount;
        }
    }
}
