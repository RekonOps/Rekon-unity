#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// FFmpeg 설치 상태를 감지하는 유틸리티 클래스.
    /// 앱 실행 중 최초 1회만 실제 프로세스를 실행하여 결과를 캐싱합니다.
    /// </summary>
    public static class FfmpegHelper
    {
        // 캐시된 결과 (앱 실행 중 1회만 체크)
        private static bool? _isInstalled;
        private static string _ffmpegPath;
        private static string _versionInfo;

        // GPU 인코더 캐시
        private static string _detectedGpuEncoder;
        private static bool _gpuEncoderChecked;

        // GPU 인코더 Fallback 체인 후보 (우선순위 순)
        private static readonly string[] s_GpuEncoderCandidates =
        {
            "h264_nvenc",       // NVIDIA
            "h264_amf",         // AMD
            "h264_videotoolbox",// macOS (Apple Silicon / Intel)
            "h264_qsv",         // Intel Quick Sync
        };

        // ffmpeg -encoders 출력에서 H.264 하드웨어 인코더 행 추출용 정규식
        // 예: " V..... h264_nvenc           NVIDIA NVENC H.264 encoder"
        private static readonly Regex s_EncoderLineRegex = new Regex(
            @"^\s*V[\.\w]{5}\s+(h264_\w+)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // 멀티스레드 환경에서의 동시 접근 방지용 락 객체
        private static readonly object _lock = new object();

        /// <summary>
        /// FFmpeg가 시스템 PATH에 설치되어 있는지 확인합니다.
        /// 최초 호출 시 실제로 프로세스를 실행하며, 이후 호출은 캐시된 결과를 반환합니다.
        /// </summary>
        /// <returns>FFmpeg가 설치되어 있으면 true, 그렇지 않으면 false</returns>
        public static bool IsInstalled()
        {
            lock (_lock)
            {
                if (_isInstalled.HasValue) return _isInstalled.Value;

                // 플랫폼별 실행 경로 후보를 순서대로 시도
                string[] candidates = GetCandidatePaths();

                foreach (string candidate in candidates)
                {
                    if (TryDetectFfmpeg(candidate))
                        return _isInstalled.Value;
                }

                _isInstalled = false;
                UnityEngine.Debug.LogWarning("[BugBeacon] FFmpeg가 설치되어 있지 않습니다.");
                return _isInstalled.Value;
            }
        }

        private static bool TryDetectFfmpeg(string executablePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    // stdout을 먼저 읽어야 버퍼 데드락 방지
                    // (ffmpeg -version 출력이 길어 4KB 버퍼가 차면 WaitForExit 데드락 발생)
                    string firstLine = process.StandardOutput.ReadLine() ?? "";
                    process.StandardOutput.ReadToEnd(); // 나머지 출력 소진
                    process.StandardError.ReadToEnd();

                    bool exited = process.WaitForExit(5000);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { /* 무시 */ }
                        return false;
                    }

                    if (process.ExitCode == 0)
                    {
                        _versionInfo = firstLine.Trim();
                        _ffmpegPath = executablePath;
                        _isInstalled = true;
                        UnityEngine.Debug.Log($"[BugBeacon] FFmpeg 감지됨: {_versionInfo} (경로: {executablePath})");
                        return true;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception) { /* 파일 없음 */ }
            catch (System.IO.FileNotFoundException) { /* 파일 없음 */ }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[BugBeacon] FFmpeg 감지 중 오류 ({executablePath}): {ex.GetType().Name}");
            }
            return false;
        }

        /// <summary>
        /// FFmpeg 버전 정보를 반환합니다. 설치되지 않은 경우 빈 문자열을 반환합니다.
        /// </summary>
        public static string GetVersionInfo() => _versionInfo ?? "";

        /// <summary>
        /// 시스템에서 사용 가능한 GPU H.264 인코더를 감지합니다.
        /// 최초 호출 시 ffmpeg -encoders를 실행하여 결과를 캐싱합니다.
        /// GPU 인코더가 없으면 null을 반환합니다.
        /// </summary>
        public static string GetGpuEncoder()
        {
            lock (_lock)
            {
                if (_gpuEncoderChecked) return _detectedGpuEncoder;

                _gpuEncoderChecked = true;
                _detectedGpuEncoder = null;

                string encoderList = GetAvailableEncoders();
                if (string.IsNullOrEmpty(encoderList))
                {
                    UnityEngine.Debug.Log("[BugBeacon] GPU 인코더 감지 실패: ffmpeg -encoders 출력 없음");
                    return null;
                }

                // 정규식으로 사용 가능한 H.264 인코더 목록 추출
                var matches = s_EncoderLineRegex.Matches(encoderList);
                var availableEncoders = new System.Collections.Generic.HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in matches)
                    availableEncoders.Add(m.Groups[1].Value);

                // Fallback 체인 우선순위대로 첫 번째 매칭 인코더 선택
                foreach (string candidate in s_GpuEncoderCandidates)
                {
                    if (availableEncoders.Contains(candidate))
                    {
                        _detectedGpuEncoder = candidate;
                        UnityEngine.Debug.Log($"[BugBeacon] GPU 인코더 감지됨: {candidate}");
                        return _detectedGpuEncoder;
                    }
                }

                UnityEngine.Debug.Log("[BugBeacon] 사용 가능한 GPU 인코더 없음. libx264 CPU 인코더를 사용합니다.");
                return null;
            }
        }

        /// <summary>
        /// ffmpeg -encoders 명령어를 실행하여 사용 가능한 인코더 목록을 반환합니다.
        /// 5초 타임아웃 적용. 실패 시 빈 문자열 반환.
        /// </summary>
        private static string GetAvailableEncoders()
        {
            // ffmpeg 경로가 아직 감지되지 않은 경우 기본값 사용
            string ffmpegPath = _ffmpegPath ?? "ffmpeg";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-encoders",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                var outputBuilder = new StringBuilder();

                using (var process = new Process())
                {
                    process.StartInfo = startInfo;

                    // 비동기 stdout 읽기 (버퍼 포화로 인한 데드락 방지)
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            outputBuilder.AppendLine(e.Data);
                    };

                    // 비동기 stderr 읽기 (내용은 버리되, 버퍼 소진으로 데드락 방지)
                    process.ErrorDataReceived += (sender, e) => { /* 버퍼 소진용, 내용 무시 */ };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool exited = process.WaitForExit(5000);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { /* 무시 */ }
                        UnityEngine.Debug.LogWarning("[BugBeacon] GetAvailableEncoders: ffmpeg 타임아웃 (5초 초과)");
                        return string.Empty;
                    }

                    // WaitForExit(timeout) 후 비동기 OutputDataReceived 이벤트가 아직 처리 중일 수 있음
                    // 인수 없는 WaitForExit() 호출로 남은 비동기 출력을 모두 플러시
                    process.WaitForExit();

                    return outputBuilder.ToString();
                }
            }
            catch (System.ComponentModel.Win32Exception) { /* 파일 없음 */ }
            catch (System.IO.FileNotFoundException) { /* 파일 없음 */ }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[BugBeacon] GetAvailableEncoders 오류: {ex.GetType().Name}: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// 캐시를 초기화합니다. 설정 UI에서 "다시 확인" 버튼 등에서 사용합니다.
        /// 다음 IsInstalled() 호출 시 다시 프로세스를 실행하여 감지합니다.
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _isInstalled = null;
                _ffmpegPath = null;
                _versionInfo = null;
                // GPU 인코더 캐시도 함께 초기화
                _detectedGpuEncoder = null;
                _gpuEncoderChecked = false;
            }
        }

        /// <summary>
        /// FFmpeg 실행 경로를 반환합니다. ("ffmpeg" 또는 "ffmpeg.exe")
        /// IsInstalled()를 먼저 호출하지 않은 경우 기본값을 반환합니다.
        /// </summary>
        public static string GetPath() => _ffmpegPath ?? "ffmpeg";

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 플랫폼별 FFmpeg 실행 경로 후보 목록을 반환합니다.
        /// Unity 에디터는 셸 PATH를 상속하지 않을 수 있으므로,
        /// 일반적인 설치 경로들을 직접 시도합니다.
        /// </summary>
        private static string[] GetCandidatePaths()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new[]
            {
                "ffmpeg.exe",
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Packages", "ffmpeg.exe"),
            };
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return new[]
            {
                "ffmpeg",
                "/opt/homebrew/bin/ffmpeg",      // Apple Silicon (brew)
                "/usr/local/bin/ffmpeg",         // Intel Mac (brew)
                "/usr/bin/ffmpeg",               // 시스템 설치
            };
#else
            return new[]
            {
                "ffmpeg",
                "/usr/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
            };
#endif
        }
    }
}
#else
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 모바일 등 미지원 플랫폼에서의 FFmpeg 스텁 구현.
    /// 항상 미설치 상태를 반환합니다.
    /// </summary>
    public static class FfmpegHelper
    {
        public static bool IsInstalled() => false;
        public static string GetVersionInfo() => "";
        public static void ClearCache() { }
        public static string GetPath() => "";
        public static string GetGpuEncoder() => null;
    }
}
#endif
