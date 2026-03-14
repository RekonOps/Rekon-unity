using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 영상 인코더 MVP 구현.
    ///
    /// MVP 전략: 프레임 시퀀스를 개별 PNG 파일로 저장합니다.
    ///   outputPath/
    ///     frame_000000.png
    ///     frame_000001.png
    ///     ...
    ///     metadata.json  (타임스탬프 정보)
    ///
    /// 향후 교체: IVideoEncoder 인터페이스를 통해 FFmpeg/MediaFoundation 구현으로 교체 가능합니다.
    ///
    /// 스레드: Task.Run을 사용하여 파일 I/O를 백그라운드 스레드에서 처리합니다.
    /// </summary>
    public class VideoEncoder : IVideoEncoder
    {
        /// <summary>출력 파일 확장자. 디렉토리 출력이므로 빈 문자열.</summary>
        public string OutputExtension => "";

        /// <summary>인코딩 시 권장 타임아웃 (초)</summary>
        public float RecommendedTimeoutSeconds => 5f;

        /// <summary>
        /// 프레임 배열을 PNG 시퀀스로 비동기 저장합니다.
        /// </summary>
        /// <param name="frames">인코딩할 프레임 배열 (시간순)</param>
        /// <param name="outputPath">출력 디렉토리 경로</param>
        /// <param name="config">인코딩 설정</param>
        /// <param name="cancellationToken">취소 토큰 (현재 사용되지 않음)</param>
        public async Task EncodeAsync(FrameData[] frames, string outputPath, VideoEncoderConfig config, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (frames == null || frames.Length == 0)
            {
                Debug.LogWarning("[BugBeacon] VideoEncoder: 인코딩할 프레임이 없습니다.");
                return;
            }

            try
            {
                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                // 메타데이터 및 프레임 저장을 백그라운드에서 수행
                await Task.Run(() => SaveFrameSequence(frames, outputPath, config));

                Debug.Log($"[BugBeacon] 영상 인코딩 완료: {outputPath} ({frames.Length}프레임)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 영상 인코딩 실패: {ex.Message}");
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        private void SaveFrameSequence(FrameData[] frames, string outputPath, VideoEncoderConfig config)
        {
            // PNG 인코딩: RGBA32 원시 데이터를 Texture2D로 변환 후 PNG 저장은
            // 메인 스레드에서만 가능하므로, 여기서는 원시 바이트를 .raw 파일로 저장합니다.
            // 향후 FFmpeg 사용 시 이 .raw 파일을 입력으로 사용할 수 있습니다.
            //
            // 주의: Unity Texture2D API는 메인 스레드 전용이므로
            //       백그라운드 스레드에서는 원시 바이트를 직접 저장합니다.

            // 메타데이터 생성
            var metadataBuilder = new System.Text.StringBuilder();
            metadataBuilder.AppendLine("{");
            metadataBuilder.AppendLine($"  \"frame_count\": {frames.Length},");
            metadataBuilder.AppendLine($"  \"width\": {config.Width},");
            metadataBuilder.AppendLine($"  \"height\": {config.Height},");
            metadataBuilder.AppendLine($"  \"fps\": {config.Fps},");
            metadataBuilder.AppendLine($"  \"format\": \"RGBA32\",");
            metadataBuilder.AppendLine($"  \"encoded_at\": \"{DateTime.UtcNow:O}\",");
            metadataBuilder.AppendLine("  \"frames\": [");

            for (int i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                string fileName = $"frame_{i:D6}.raw";
                string filePath = Path.Combine(outputPath, fileName);

                // 유효한 프레임만 저장
                if (frame.IsValid)
                {
                    File.WriteAllBytes(filePath, frame.Data);
                }

                string comma = (i < frames.Length - 1) ? "," : "";
                metadataBuilder.AppendLine($"    {{\"file\": \"{fileName}\", \"timestamp\": {frame.Timestamp:F6}}}{comma}");
            }

            metadataBuilder.AppendLine("  ]");
            metadataBuilder.AppendLine("}");

            string metadataPath = Path.Combine(outputPath, "metadata.json");
            File.WriteAllText(metadataPath, metadataBuilder.ToString(), System.Text.Encoding.UTF8);
        }
    }
}
