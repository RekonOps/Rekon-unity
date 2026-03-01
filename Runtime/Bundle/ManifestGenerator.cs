using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// CaptureResult를 BundleManifest로 변환하는 생성기.
    /// SHA-256 해시 계산은 BundleWriter 단계에서 수행하므로,
    /// 이 단계에서는 아티팩트 경로와 크기만 수집합니다.
    /// </summary>
    public class ManifestGenerator
    {
        // 플러그인 버전 상수 (package.json과 동기화)
        private const string PluginVersion = "0.1.0";

        /// <summary>
        /// CaptureResult를 기반으로 BundleManifest 초안을 생성합니다.
        /// SHA-256 해시는 BundleWriter에서 파일 복사 후 채워집니다.
        /// </summary>
        /// <param name="captureResult">캡처 파이프라인 실행 결과.</param>
        /// <returns>생성된 BundleManifest. captureResult가 null이면 예외를 던집니다.</returns>
        /// <exception cref="ArgumentNullException">captureResult가 null인 경우.</exception>
        /// <exception cref="InvalidOperationException">최소 하나의 아티팩트도 없는 경우.</exception>
        public BundleManifest Generate(CaptureResult captureResult)
        {
            if (captureResult == null)
                throw new ArgumentNullException(nameof(captureResult), "캡처 결과가 null입니다.");

            if (!captureResult.IsPartialSuccess)
                throw new InvalidOperationException("최소 하나의 유효한 아티팩트가 필요합니다.");

            var artifacts = BuildArtifactList(captureResult);

            var manifest = new BundleManifest
            {
                id              = Guid.NewGuid().ToString("D"),
                created_at      = captureResult.Timestamp.ToUniversalTime().ToString("O"),
                plugin_version  = PluginVersion,
                unity_version   = Application.unityVersion,
                title           = string.Empty,
                description     = string.Empty,
                artifacts       = artifacts,
                state           = BundleState.Created,
                jira_issue_key  = null,
                registered_at   = null,
                retry_count     = 0,
            };

            manifest.RecalculateTotalSize();

            return manifest;
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// CaptureResult에서 유효한 아티팩트 목록을 생성합니다.
        /// SHA-256 해시는 빈 문자열로 초기화됩니다 (BundleWriter에서 채움).
        /// </summary>
        private static List<BundleArtifact> BuildArtifactList(CaptureResult result)
        {
            var artifacts = new List<BundleArtifact>(4);

            // 스크린샷 (PNG)
            if (!string.IsNullOrEmpty(result.ScreenshotPath) && File.Exists(result.ScreenshotPath))
            {
                artifacts.Add(new BundleArtifact
                {
                    type        = BundleArtifactType.Screenshot,
                    file_name   = Path.GetFileName(result.ScreenshotPath),
                    size_bytes  = new FileInfo(result.ScreenshotPath).Length,
                    sha256_hash = string.Empty,
                });
            }

            // 로그 (ZIP)
            if (!string.IsNullOrEmpty(result.LogsPath) && File.Exists(result.LogsPath))
            {
                artifacts.Add(new BundleArtifact
                {
                    type        = BundleArtifactType.Log,
                    file_name   = Path.GetFileName(result.LogsPath),
                    size_bytes  = new FileInfo(result.LogsPath).Length,
                    sha256_hash = string.Empty,
                });
            }

            // 상태 스냅샷 (JSON)
            if (!string.IsNullOrEmpty(result.StatePath) && File.Exists(result.StatePath))
            {
                artifacts.Add(new BundleArtifact
                {
                    type        = BundleArtifactType.State,
                    file_name   = Path.GetFileName(result.StatePath),
                    size_bytes  = new FileInfo(result.StatePath).Length,
                    sha256_hash = string.Empty,
                });
            }

            // 영상 세그먼트 (디렉토리)
            if (!string.IsNullOrEmpty(result.VideoPath) && Directory.Exists(result.VideoPath))
            {
                long dirSize = CalculateDirectorySize(result.VideoPath);
                artifacts.Add(new BundleArtifact
                {
                    type        = BundleArtifactType.Video,
                    file_name   = Path.GetFileName(result.VideoPath.TrimEnd('/', '\\')),
                    size_bytes  = dirSize,
                    sha256_hash = string.Empty, // 디렉토리는 해시 없음
                });
            }

            return artifacts;
        }

        /// <summary>
        /// 디렉토리 내 모든 파일 크기 합계를 계산합니다.
        /// </summary>
        private static long CalculateDirectorySize(string directoryPath)
        {
            long total = 0L;
            try
            {
                foreach (string filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    total += new FileInfo(filePath).Length;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 디렉토리 크기 계산 실패: {ex.Message}");
            }
            return total;
        }
    }
}
