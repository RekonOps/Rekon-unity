using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Security.Cryptography;
#endif

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// AES-256-CBC 암호화를 사용하여 토큰/민감 문자열을 보호하는 클래스.
    ///
    /// 키 파생 방식:
    ///   - 재료: SystemInfo.deviceUniqueIdentifier + 패키지명("com.gaozombie.bugbeacon")
    ///   - 알고리즘: PBKDF2 (SHA256, 100,000회 반복)
    ///   - 출력 키 길이: 256비트 (32바이트)
    ///
    /// IV 처리:
    ///   - 매 암호화마다 랜덤 IV 생성 (16바이트)
    ///   - 암호문 앞에 IV를 prepend하여 저장
    ///   - 복호화 시 앞 16바이트를 IV로 읽음
    ///
    /// OS별 추가 보호:
    ///   - Windows: DPAPI(ProtectedData)로 파생 키를 추가 보호
    ///   - macOS/기타: AES 단독 사용 (파일 기반 저장과 조합)
    /// </summary>
    public class TokenEncryptor
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        private const int    KeySizeBits  = 256;
        private const int    KeySizeBytes = KeySizeBits / 8;   // 32
        private const int    IvSizeBytes  = 16;
        private const int    Pbkdf2Iterations = 100_000;
        private const string PackageName  = "com.gaozombie.bugbeacon";

        // ──────────────────────────────────────────────────────────────
        // 파생 키 (지연 초기화, 스레드 안전)
        // ──────────────────────────────────────────────────────────────

        private static readonly Lazy<byte[]> _derivedKey = new Lazy<byte[]>(DeriveKey);

        /// <summary>
        /// 디바이스 고유 식별자와 패키지명을 조합하여 AES 키를 파생합니다.
        /// </summary>
        private static byte[] DeriveKey()
        {
            var deviceId = SystemInfo.deviceUniqueIdentifier;
            var saltSource = $"{deviceId}:{PackageName}";
            var saltBytes  = Encoding.UTF8.GetBytes(saltSource);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password:   Encoding.UTF8.GetBytes(saltSource),
                salt:       saltBytes,
                iterations: Pbkdf2Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256
            );

            var key = pbkdf2.GetBytes(KeySizeBytes);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Windows: DPAPI로 키 자체를 추가 보호
            key = ProtectKeyWithDpapi(key);
#endif
            return key;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        /// <summary>
        /// DPAPI(Data Protection API)로 키를 암호화하여 추가 보호합니다. (Windows 전용)
        /// </summary>
        private static byte[] ProtectKeyWithDpapi(byte[] key)
        {
            try
            {
                // CurrentUser 범위로 보호: 동일 Windows 사용자만 복호화 가능
                return System.Security.Cryptography.ProtectedData.Protect(
                    userData:  key,
                    optionalEntropy: Encoding.UTF8.GetBytes(PackageName),
                    scope:     System.Security.Cryptography.DataProtectionScope.CurrentUser
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TokenEncryptor] DPAPI 보호 실패, 원본 키 사용: {ex.Message}");
                return key;
            }
        }

        /// <summary>
        /// DPAPI로 보호된 키를 복원합니다. (Windows 전용)
        /// </summary>
        private static byte[] UnprotectKeyWithDpapi(byte[] protectedKey)
        {
            try
            {
                return System.Security.Cryptography.ProtectedData.Unprotect(
                    encryptedData: protectedKey,
                    optionalEntropy: Encoding.UTF8.GetBytes(PackageName),
                    scope:     System.Security.Cryptography.DataProtectionScope.CurrentUser
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TokenEncryptor] DPAPI 복원 실패: {ex.Message}");
                return protectedKey;
            }
        }
#endif

        // ──────────────────────────────────────────────────────────────
        // 공개 암호화/복호화 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 평문 문자열을 AES-256-CBC로 암호화하여 Base64 문자열을 반환합니다.
        /// 반환값 앞 16바이트(Base64 디코딩 후)는 IV입니다.
        /// </summary>
        /// <param name="plaintext">암호화할 원본 문자열</param>
        /// <returns>IV가 prepend된 Base64 암호문. 빈 입력 시 빈 문자열 반환.</returns>
        public static string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return plaintext;

            var key = GetAesKey();

            using var aes = Aes.Create();
            aes.KeySize = KeySizeBits;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key     = key;
            aes.GenerateIV(); // 랜덤 IV 생성

            var iv           = aes.IV; // 16바이트
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            byte[] cipherBytes;
            using (var encryptor = aes.CreateEncryptor())
            {
                cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
            }

            // IV(16바이트) + 암호문을 합쳐서 Base64로 인코딩
            var combined = new byte[IvSizeBytes + cipherBytes.Length];
            Buffer.BlockCopy(iv, 0, combined, 0, IvSizeBytes);
            Buffer.BlockCopy(cipherBytes, 0, combined, IvSizeBytes, cipherBytes.Length);

            return Convert.ToBase64String(combined);
        }

        /// <summary>
        /// Encrypt()로 생성된 Base64 암호문을 복호화하여 평문을 반환합니다.
        /// </summary>
        /// <param name="ciphertext">IV가 prepend된 Base64 암호문</param>
        /// <returns>복호화된 평문. 실패 시 예외 발생.</returns>
        public static string Decrypt(string ciphertext)
        {
            if (string.IsNullOrEmpty(ciphertext))
                return ciphertext;

            byte[] combined;
            try
            {
                combined = Convert.FromBase64String(ciphertext);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("암호문이 유효한 Base64 형식이 아닙니다.", ex);
            }

            if (combined.Length < IvSizeBytes + 1)
                throw new CryptographicException("암호문 길이가 너무 짧습니다. 데이터가 손상되었을 수 있습니다.");

            var key = GetAesKey();

            // IV 추출 (앞 16바이트)
            var iv          = new byte[IvSizeBytes];
            var cipherBytes = new byte[combined.Length - IvSizeBytes];
            Buffer.BlockCopy(combined, 0, iv, 0, IvSizeBytes);
            Buffer.BlockCopy(combined, IvSizeBytes, cipherBytes, 0, cipherBytes.Length);

            using var aes = Aes.Create();
            aes.KeySize = KeySizeBits;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key     = key;
            aes.IV      = iv;

            byte[] plaintextBytes;
            try
            {
                using var decryptor = aes.CreateDecryptor();
                plaintextBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("복호화 실패: 키가 다르거나 데이터가 손상되었습니다.", ex);
            }

            return Encoding.UTF8.GetString(plaintextBytes);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// AES에 실제 사용할 키를 반환합니다. Windows에서는 DPAPI 복원 처리를 포함합니다.
        /// </summary>
        private static byte[] GetAesKey()
        {
            var key = _derivedKey.Value;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Windows: DPAPI로 보호된 키를 복원한 후 사용
            key = UnprotectKeyWithDpapi(key);
#endif
            return key;
        }
    }
}
