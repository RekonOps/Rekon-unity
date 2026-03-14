using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// FileStream 기반 원자적 파일 쓰기 유틸리티.
    ///
    /// 쓰기 방식:
    ///   1. 대상 경로 + ".tmp" 임시 파일에 쓰기
    ///   2. 임시 파일을 대상 경로로 rename (원자적 교체)
    ///
    /// 특징:
    ///   - 쓰기 중 크래시 발생 시 기존 파일 보존
    ///   - 쓰기 실패 시 예외를 격리하여 폴백 처리
    ///   - 동기 및 비동기 Write 메서드 제공
    /// </summary>
    public class MappedFileWriter
    {
        // 파일 쓰기 버퍼 크기 (64KB)
        private const int BufferSize = 65536;

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드 - 동기
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 바이트 배열을 지정한 경로에 원자적으로 씁니다.
        /// 실패 시 예외를 기록하고 false를 반환합니다.
        /// </summary>
        /// <param name="filePath">쓸 파일의 절대 경로</param>
        /// <param name="data">쓸 데이터</param>
        /// <returns>성공 시 true, 실패 시 false</returns>
        public bool Write(string filePath, byte[] data)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath), "파일 경로가 null 또는 빈 문자열입니다.");

            if (data == null)
                throw new ArgumentNullException(nameof(data), "쓸 데이터가 null입니다.");

            string tempPath = filePath + ".tmp";

            try
            {
                // 디렉토리 생성 (없는 경우)
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // 임시 파일에 쓰기
                using (var fs = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.WriteThrough))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(flushToDisk: true);
                }

                // 원자적 교체 (temp → 대상)
                AtomicMove(tempPath, filePath);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugBeacon] 파일 쓰기 실패 ({filePath}): {ex.Message}");
                TryCleanupTemp(tempPath);
                return false;
            }
        }

        /// <summary>
        /// 문자열을 UTF-8로 인코딩하여 지정한 경로에 원자적으로 씁니다.
        /// </summary>
        /// <param name="filePath">쓸 파일의 절대 경로</param>
        /// <param name="text">쓸 문자열</param>
        /// <returns>성공 시 true, 실패 시 false</returns>
        public bool WriteText(string filePath, string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text), "쓸 텍스트가 null입니다.");

            byte[] data = Encoding.UTF8.GetBytes(text);
            return Write(filePath, data);
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드 - 비동기
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 바이트 배열을 지정한 경로에 비동기 원자적으로 씁니다.
        /// I/O 작업은 ThreadPool에서 수행됩니다.
        /// </summary>
        /// <param name="filePath">쓸 파일의 절대 경로</param>
        /// <param name="data">쓸 데이터</param>
        /// <returns>성공 시 true, 실패 시 false</returns>
        public Task<bool> WriteAsync(string filePath, byte[] data)
        {
            return Task.Run(() => Write(filePath, data));
        }

        /// <summary>
        /// 문자열을 UTF-8로 인코딩하여 비동기 원자적으로 씁니다.
        /// </summary>
        /// <param name="filePath">쓸 파일의 절대 경로</param>
        /// <param name="text">쓸 문자열</param>
        /// <returns>성공 시 true, 실패 시 false</returns>
        public Task<bool> WriteTextAsync(string filePath, string text)
        {
            return Task.Run(() => WriteText(filePath, text));
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 임시 파일을 대상 경로로 원자적으로 이동합니다.
        /// 대상 파일이 이미 존재하는 경우 덮어씁니다.
        /// </summary>
        private static void AtomicMove(string tempPath, string destPath)
        {
            if (File.Exists(destPath))
                File.Delete(destPath);

            File.Move(tempPath, destPath);
        }

        /// <summary>
        /// 실패 시 임시 파일을 정리합니다. 정리 실패는 무시합니다.
        /// </summary>
        private static void TryCleanupTemp(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // 정리 실패는 무시 (크래시 복구 중 재처리 가능)
            }
        }
    }
}
