using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 캡처 아티팩트를 번들 디렉토리에 복사하고 manifest.json을 생성하는 클래스.
    ///
    /// 번들 디렉토리 구조:
    ///   {persistentDataPath}/BugOneTouch/bundles/{id}/
    ///   ├── manifest.json
    ///   ├── screenshot.png
    ///   ├── logs.zip
    ///   ├── state.json
    ///   └── video/ (옵션)
    ///
    /// manifest.json은 원자적 쓰기(temp → rename)로 손상을 방지합니다.
    /// </summary>
    public class BundleWriter
    {
        private readonly ManifestGenerator _manifestGenerator;

        // ──────────────────────────────────────────────────────────────
        // 내부 캐시 (지연 초기화)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들 루트 디렉토리 캐시.
        /// Application.persistentDataPath는 메인 스레드에서만 접근 가능하므로
        /// 필드 초기화자 대신 처음 접근 시점에 초기화합니다.
        /// </summary>
        private static string _bundlesRootDirectory;

        /// <summary>
        /// BundleWriter를 초기화합니다.
        /// </summary>
        /// <param name="manifestGenerator">BundleManifest 생성기.</param>
        /// <exception cref="ArgumentNullException">manifestGenerator가 null인 경우.</exception>
        public BundleWriter(ManifestGenerator manifestGenerator)
        {
            _manifestGenerator = manifestGenerator
                ?? throw new ArgumentNullException(nameof(manifestGenerator), "ManifestGenerator가 null입니다.");
        }

        /// <summary>
        /// CaptureResult를 기반으로 번들 디렉토리를 생성하고 모든 아티팩트를 복사합니다.
        /// </summary>
        /// <param name="captureResult">캡처 파이프라인 결과.</param>
        /// <returns>작성된 BundleManifest (id, 경로 등 포함).</returns>
        /// <exception cref="ArgumentNullException">captureResult가 null인 경우.</exception>
        public async Task<BundleManifest> WriteAsync(CaptureResult captureResult)
        {
            if (captureResult == null)
                throw new ArgumentNullException(nameof(captureResult), "캡처 결과가 null입니다.");

            // 1단계: manifest 초안 생성 (해시 없음)
            BundleManifest manifest = _manifestGenerator.Generate(captureResult);

            // 2단계: 번들 디렉토리 생성
            string bundleDir = GetBundleDirectory(manifest.id);
            Directory.CreateDirectory(bundleDir);

            Debug.Log($"[BugOneTouch] 번들 디렉토리 생성: {bundleDir}");

            // 3단계: 아티팩트 복사 및 SHA-256 해시 계산
            await CopyArtifactsAsync(captureResult, bundleDir, manifest);

            // 4단계: 총 크기 재계산
            manifest.RecalculateTotalSize();

            // 5단계: manifest.json 원자적 쓰기
            await WriteManifestAtomicAsync(manifest, bundleDir);

            Debug.Log($"[BugOneTouch] 번들 생성 완료: {manifest}");

            return manifest;
        }

        /// <summary>
        /// 번들 디렉토리 경로를 반환합니다.
        /// </summary>
        public static string GetBundleDirectory(string bundleId)
        {
            return Path.Combine(GetBundlesRootDirectory(), bundleId);
        }

        /// <summary>
        /// 번들 루트 디렉토리를 반환합니다.
        /// </summary>
        public static string GetBundlesRootDirectory()
        {
            return _bundlesRootDirectory ??= Path.Combine(Application.persistentDataPath, "BugOneTouch", "bundles");
        }

        // ──────────────────────────────────────────────────────────────
        // 아티팩트 복사
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 모든 아티팩트를 번들 디렉토리로 복사하고 SHA-256 해시를 아티팩트에 기록합니다.
        /// </summary>
        private async Task CopyArtifactsAsync(CaptureResult source, string bundleDir, BundleManifest manifest)
        {
            foreach (var artifact in manifest.artifacts)
            {
                switch (artifact.type)
                {
                    case BundleArtifactType.Screenshot:
                        await CopyFileArtifactAsync(source.ScreenshotPath, bundleDir, artifact);
                        break;

                    case BundleArtifactType.Log:
                        await CopyFileArtifactAsync(source.LogsPath, bundleDir, artifact);
                        break;

                    case BundleArtifactType.State:
                        await CopyFileArtifactAsync(source.StatePath, bundleDir, artifact);
                        break;

                    case BundleArtifactType.Video:
                        await CopyDirectoryArtifactAsync(source.VideoPath, bundleDir, artifact);
                        break;

                    default:
                        Debug.LogWarning($"[BugOneTouch] 알 수 없는 아티팩트 타입: {artifact.type}");
                        break;
                }
            }
        }

        /// <summary>
        /// 단일 파일 아티팩트를 번들 디렉토리로 복사합니다.
        /// 복사 후 SHA-256 해시를 계산하여 아티팩트에 기록합니다.
        /// </summary>
        private static async Task CopyFileArtifactAsync(string sourcePath, string bundleDir, BundleArtifact artifact)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Debug.LogWarning($"[BugOneTouch] 아티팩트 원본 파일 없음: {sourcePath}");
                return;
            }

            string destPath = Path.Combine(bundleDir, artifact.file_name);

            try
            {
                await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: true));

                // 복사된 파일의 SHA-256 해시 계산
                artifact.sha256_hash = await SHA256HashUtility.ComputeFileHashAsync(destPath);
                // 파일 크기 갱신 (원본과 동일하지만 명시적으로 재계산)
                artifact.size_bytes = new FileInfo(destPath).Length;

                Debug.Log($"[BugOneTouch] 아티팩트 복사 완료: {artifact.file_name} ({artifact.size_bytes}B, sha256={artifact.sha256_hash[..8]}...)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 아티팩트 복사 실패 ({artifact.file_name}): {ex.Message}");
            }
        }

        /// <summary>
        /// 영상 디렉토리를 번들 디렉토리로 복사합니다.
        /// 디렉토리는 SHA-256 해시를 계산하지 않습니다.
        /// </summary>
        private static async Task CopyDirectoryArtifactAsync(string sourcePath, string bundleDir, BundleArtifact artifact)
        {
            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
            {
                Debug.LogWarning($"[BugOneTouch] 영상 디렉토리 없음: {sourcePath}");
                return;
            }

            string destDir = Path.Combine(bundleDir, artifact.file_name);

            try
            {
                await Task.Run(() => CopyDirectory(sourcePath, destDir));

                // 복사된 디렉토리 크기 계산
                artifact.size_bytes = await Task.Run(() => CalculateDirectorySize(destDir));
                artifact.sha256_hash = string.Empty; // 디렉토리는 해시 없음

                Debug.Log($"[BugOneTouch] 영상 디렉토리 복사 완료: {artifact.file_name} ({artifact.size_bytes}B)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 영상 디렉토리 복사 실패 ({artifact.file_name}): {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // manifest.json 원자적 쓰기
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// manifest.json을 원자적으로 씁니다.
        /// 1. 임시 파일(.tmp)에 쓰기
        /// 2. 임시 파일을 manifest.json으로 교체 (overwrite)
        /// </summary>
        private static async Task WriteManifestAtomicAsync(BundleManifest manifest, string bundleDir)
        {
            string manifestPath = Path.Combine(bundleDir, "manifest.json");
            string tempPath     = manifestPath + ".tmp";

            try
            {
                string json = JsonUtility.ToJson(manifest, prettyPrint: true);

                // 임시 파일에 쓰기
                await Task.Run(() => File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8));

                // 원자적 교체
                await Task.Run(() =>
                {
                    if (File.Exists(manifestPath))
                        File.Delete(manifestPath);
                    File.Move(tempPath, manifestPath);
                });

                Debug.Log($"[BugOneTouch] manifest.json 저장 완료: {manifestPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] manifest.json 저장 실패: {ex.Message}");

                // 임시 파일 정리
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* 정리 실패는 무시 */ }
                }

                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 파일 시스템 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 디렉토리를 재귀적으로 복사합니다.
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                File.Copy(filePath, Path.Combine(destDir, fileName), overwrite: true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string subDirName = Path.GetFileName(subDir);
                CopyDirectory(subDir, Path.Combine(destDir, subDirName));
            }
        }

        /// <summary>
        /// 디렉토리 내 모든 파일의 크기 합계를 계산합니다.
        /// </summary>
        private static long CalculateDirectorySize(string directoryPath)
        {
            long total = 0L;
            foreach (string filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                total += new FileInfo(filePath).Length;
            return total;
        }
    }
}
