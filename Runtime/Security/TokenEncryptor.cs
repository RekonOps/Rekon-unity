using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// AES-256-CBC 암호화를 사용하여 토큰/민감 문자열을 보호하는 클래스.
    ///
    /// 키 파생 방식:
    ///   - 재료: SystemInfo.deviceUniqueIdentifier + 패키지명("dev.rekonops.rekon")
    ///   - 알고리즘: PBKDF2 (SHA256, 100,000회 반복)
    ///   - 출력 키 길이: 256비트 (32바이트)
    ///
    /// IV 처리:
    ///   - 매 암호화마다 랜덤 IV 생성 (16바이트)
    ///   - 암호문 앞에 IV를 prepend하여 저장
    ///   - 복호화 시 앞 16바이트를 IV로 읽음
    /// </summary>
    public class TokenEncryptor
    {
        private const int    KeySizeBits  = 256;
        private const int    KeySizeBytes = KeySizeBits / 8;
        private const int    IvSizeBytes  = 16;
        private const int    Pbkdf2Iterations = 100_000;
        private const string PackageName  = "dev.rekonops.rekon";

        private static readonly Lazy<byte[]> _derivedKey = new Lazy<byte[]>(DeriveKey);

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

            return pbkdf2.GetBytes(KeySizeBytes);
        }

        public static string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return plaintext;

            var key = _derivedKey.Value;

            using var aes = Aes.Create();
            aes.KeySize = KeySizeBits;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key     = key;
            aes.GenerateIV();

            var iv             = aes.IV;
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            byte[] cipherBytes;
            using (var encryptor = aes.CreateEncryptor())
            {
                cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
            }

            var combined = new byte[IvSizeBytes + cipherBytes.Length];
            Buffer.BlockCopy(iv, 0, combined, 0, IvSizeBytes);
            Buffer.BlockCopy(cipherBytes, 0, combined, IvSizeBytes, cipherBytes.Length);

            return Convert.ToBase64String(combined);
        }

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
                throw new CryptographicException("암호문 길이가 너무 짧습니다.");

            var key = _derivedKey.Value;

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
    }
}
