using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// LogEntry 배열을 텍스트로 직렬화하고 .txt 파일로 저장하는 클래스.
    ///
    /// 출력 형식 (logs.txt):
    ///   [2024-01-01T12:00:00.000] [Error] 메시지 내용
    ///   StackTrace: ...
    ///   ---
    /// </summary>
    public class LogSerializer
    {
        private const string Separator = "---";
        private const string StackTracePrefix = "StackTrace: ";

        private readonly bool _enableMasking;
        private readonly LogMasker _masker;

        /// <summary>마스킹 비활성화 기본 생성자 (하위 호환성 유지)</summary>
        public LogSerializer() : this(false) { }

        /// <summary>
        /// 마스킹 활성화 여부를 지정하는 생성자.
        /// </summary>
        /// <param name="enableMasking">true이면 직렬화 시 민감 정보를 마스킹합니다.</param>
        public LogSerializer(bool enableMasking)
        {
            _enableMasking = enableMasking;
            if (_enableMasking)
                _masker = new LogMasker();
        }

        /// <summary>
        /// LogEntry 배열을 사람이 읽기 좋은 텍스트 형식으로 직렬화합니다.
        /// 타임스탬프는 UTC 기준 ISO 8601 형식으로 출력됩니다.
        /// </summary>
        /// <param name="entries">직렬화할 로그 항목 배열</param>
        /// <returns>직렬화된 텍스트 문자열</returns>
        public string Serialize(LogEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(entries.Length * 200);

            // 헤더 정보
            sb.AppendLine($"# BugBeacon 로그 덤프");
            sb.AppendLine($"# 생성 시각: {DateTime.UtcNow:O}");
            sb.AppendLine($"# 항목 수: {entries.Length}");
            sb.AppendLine($"# 플랫폼: {Application.platform}");
            sb.AppendLine($"# 앱 버전: {Application.version}");
            sb.AppendLine(Separator);
            sb.AppendLine();

            foreach (var entry in entries)
            {
                // 마스킹 활성화 시 민감 정보 마스킹
                string message    = _enableMasking ? _masker.MaskAll(entry.Message)    : entry.Message;
                string stackTrace = _enableMasking ? _masker.MaskAll(entry.StackTrace) : entry.StackTrace;

                // 타임스탬프를 게임 시작 기준 상대 시간으로 출력
                sb.AppendLine($"[+{entry.Timestamp:F3}s] [{entry.LogType}] {message}");

                if (!string.IsNullOrEmpty(stackTrace))
                {
                    sb.AppendLine(StackTracePrefix);
                    sb.AppendLine(stackTrace);
                }

                sb.AppendLine(Separator);
            }

            return sb.ToString();
        }

        /// <summary>
        /// LogEntry 배열을 .txt 파일로 비동기 저장합니다.
        /// </summary>
        /// <param name="entries">저장할 로그 항목 배열</param>
        /// <param name="txtPath">저장할 텍스트 파일 경로 (절대 경로)</param>
        public async Task SaveAsync(LogEntry[] entries, string txtPath)
        {
            if (string.IsNullOrEmpty(txtPath))
                throw new ArgumentNullException(nameof(txtPath));

            if (entries == null)
                entries = Array.Empty<LogEntry>();

            string serialized = Serialize(entries);

            try
            {
                string directory = Path.GetDirectoryName(txtPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                await File.WriteAllTextAsync(txtPath, serialized, Encoding.UTF8);

                Debug.Log($"[BugBeacon] 로그 저장 완료: {txtPath} ({entries.Length}개 항목)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 로그 저장 실패 (경로: {txtPath}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// .txt 파일에서 로그 텍스트를 읽어옵니다 (디버그용).
        /// </summary>
        /// <param name="txtPath">읽을 텍스트 파일 경로</param>
        /// <returns>파일 내용</returns>
        public async Task<string> LoadAsync(string txtPath)
        {
            if (!File.Exists(txtPath))
                throw new FileNotFoundException("로그 파일을 찾을 수 없습니다.", txtPath);

            return await File.ReadAllTextAsync(txtPath, Encoding.UTF8);
        }
    }
}
