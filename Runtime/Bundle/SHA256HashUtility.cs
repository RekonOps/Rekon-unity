using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 파일 및 바이트 배열의 SHA-256 해시를 계산하는 유틸리티 클래스.
    /// 모든 공개 메서드는 스레드 안전(thread-safe)합니다.
    /// </summary>
    public static class SHA256HashUtility
    {
        // 파일 읽기 버퍼 크기 (64KB)
        private const int BufferSize = 65536;

        /// <summary>
        /// 파일의 SHA-256 해시를 비동기로 계산합니다.
        /// </summary>
        /// <param name="filePath">해시를 계산할 파일 경로.</param>
        /// <returns>소문자 hex 문자열 형식의 SHA-256 해시. 파일이 없으면 빈 문자열.</returns>
        /// <exception cref="ArgumentNullException">filePath가 null인 경우.</exception>
        public static async Task<string> ComputeFileHashAsync(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath), "파일 경로가 null입니다.");

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[BugOneTouch] SHA256 계산 대상 파일을 찾을 수 없습니다: {filePath}");
                return string.Empty;
            }

            return await Task.Run(() => ComputeFileHashInternal(filePath));
        }

        /// <summary>
        /// 파일의 SHA-256 해시를 동기로 계산합니다.
        /// </summary>
        /// <param name="filePath">해시를 계산할 파일 경로.</param>
        /// <returns>소문자 hex 문자열 형식의 SHA-256 해시. 파일이 없으면 빈 문자열.</returns>
        /// <exception cref="ArgumentNullException">filePath가 null인 경우.</exception>
        public static string ComputeFileHash(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath), "파일 경로가 null입니다.");

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[BugOneTouch] SHA256 계산 대상 파일을 찾을 수 없습니다: {filePath}");
                return string.Empty;
            }

            return ComputeFileHashInternal(filePath);
        }

        /// <summary>
        /// 바이트 배열의 SHA-256 해시를 계산합니다.
        /// </summary>
        /// <param name="data">해시를 계산할 바이트 배열.</param>
        /// <returns>소문자 hex 문자열 형식의 SHA-256 해시.</returns>
        /// <exception cref="ArgumentNullException">data가 null인 경우.</exception>
        public static string ComputeHash(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "해시 계산 대상 데이터가 null입니다.");

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(data);
            return ToHexString(hashBytes);
        }

        /// <summary>
        /// 문자열의 SHA-256 해시를 계산합니다 (UTF-8 인코딩).
        /// </summary>
        /// <param name="text">해시를 계산할 문자열.</param>
        /// <returns>소문자 hex 문자열 형식의 SHA-256 해시.</returns>
        /// <exception cref="ArgumentNullException">text가 null인 경우.</exception>
        public static string ComputeStringHash(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text), "해시 계산 대상 문자열이 null입니다.");

            byte[] data = Encoding.UTF8.GetBytes(text);
            return ComputeHash(data);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 스트리밍 방식으로 파일 SHA-256 해시를 계산합니다.
        /// 대용량 파일도 메모리 부담 없이 처리할 수 있습니다.
        /// </summary>
        private static string ComputeFileHashInternal(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan);

            byte[] hashBytes = sha256.ComputeHash(stream);
            return ToHexString(hashBytes);
        }

        /// <summary>
        /// 바이트 배열을 소문자 hex 문자열로 변환합니다.
        /// </summary>
        private static string ToHexString(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
