using System.Security.Cryptography;
using NUnit.Framework;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// TokenEncryptor 단위 테스트.
    /// </summary>
    [TestFixture]
    public class TokenEncryptorTests
    {
        // ──────────────────────────────────────────────────────────────
        // 기본 암호화/복호화 왕복 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
        {
            var plaintext = "my-secret-token-12345";

            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var decrypted  = TokenEncryptor.Decrypt(ciphertext);

            Assert.AreEqual(plaintext, decrypted, "복호화된 텍스트가 원본과 일치해야 합니다.");
        }

        [Test]
        public void Encrypt_ThenDecrypt_UnicodeText_ReturnsOriginal()
        {
            var plaintext = "한글 토큰: 비밀번호123!@#";

            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var decrypted  = TokenEncryptor.Decrypt(ciphertext);

            Assert.AreEqual(plaintext, decrypted, "유니코드 문자열이 올바르게 복호화되어야 합니다.");
        }

        [Test]
        public void Encrypt_LongText_RoundTripSucceeds()
        {
            var plaintext = new string('A', 10000); // 10KB 문자열

            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var decrypted  = TokenEncryptor.Decrypt(ciphertext);

            Assert.AreEqual(plaintext, decrypted, "긴 문자열도 왕복 암/복호화가 성공해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // null / 빈 문자열 처리 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_NullInput_ReturnsNull()
        {
            var result = TokenEncryptor.Encrypt(null);
            Assert.IsNull(result);
        }

        [Test]
        public void Encrypt_EmptyString_ReturnsEmpty()
        {
            var result = TokenEncryptor.Encrypt(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void Decrypt_NullInput_ReturnsNull()
        {
            var result = TokenEncryptor.Decrypt(null);
            Assert.IsNull(result);
        }

        [Test]
        public void Decrypt_EmptyString_ReturnsEmpty()
        {
            var result = TokenEncryptor.Decrypt(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        // ──────────────────────────────────────────────────────────────
        // IV 고유성 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_SamePlaintext_ProducesDifferentCiphertexts()
        {
            var plaintext = "동일한 평문";

            var cipher1 = TokenEncryptor.Encrypt(plaintext);
            var cipher2 = TokenEncryptor.Encrypt(plaintext);

            Assert.AreNotEqual(cipher1, cipher2,
                "같은 평문을 두 번 암호화하면 랜덤 IV로 인해 암호문이 달라야 합니다.");
        }

        [Test]
        public void Encrypt_SamePlaintext_BothDecryptCorrectly()
        {
            var plaintext = "IV 고유성 테스트 문자열";

            var cipher1 = TokenEncryptor.Encrypt(plaintext);
            var cipher2 = TokenEncryptor.Encrypt(plaintext);

            // 다른 암호문이지만 둘 다 올바르게 복호화됨
            Assert.AreEqual(plaintext, TokenEncryptor.Decrypt(cipher1));
            Assert.AreEqual(plaintext, TokenEncryptor.Decrypt(cipher2));
        }

        // ──────────────────────────────────────────────────────────────
        // 잘못된 암호문 복호화 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Decrypt_InvalidBase64_ThrowsCryptographicException()
        {
            Assert.Throws<CryptographicException>(() =>
                TokenEncryptor.Decrypt("이것은 유효하지 않은 Base64 !!!")
            );
        }

        [Test]
        public void Decrypt_TooShortCiphertext_ThrowsCryptographicException()
        {
            // 16바이트 미만: IV도 포함할 수 없는 길이
            var tooShort = System.Convert.ToBase64String(new byte[] { 0x01, 0x02, 0x03 });
            Assert.Throws<CryptographicException>(() => TokenEncryptor.Decrypt(tooShort));
        }

        [Test]
        public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
        {
            var plaintext  = "변조 테스트";
            var ciphertext = TokenEncryptor.Encrypt(plaintext);

            // Base64 디코딩 후 임의 바이트 변조
            var bytes    = System.Convert.FromBase64String(ciphertext);
            bytes[20]   ^= 0xFF; // 데이터 부분 변조
            var tampered = System.Convert.ToBase64String(bytes);

            Assert.Throws<CryptographicException>(() => TokenEncryptor.Decrypt(tampered),
                "변조된 암호문 복호화는 예외를 발생시켜야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 암호문 형식 검증
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_Result_IsValidBase64()
        {
            var plaintext  = "Base64 검증 테스트";
            var ciphertext = TokenEncryptor.Encrypt(plaintext);

            // Base64 디코딩이 성공해야 함 (예외 없음)
            Assert.DoesNotThrow(() => System.Convert.FromBase64String(ciphertext),
                "암호문은 유효한 Base64 형식이어야 합니다.");
        }

        [Test]
        public void Encrypt_Result_HasMinimumLength()
        {
            var plaintext  = "A"; // 최소 평문
            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var bytes      = System.Convert.FromBase64String(ciphertext);

            // IV(16) + AES 블록(최소 16) = 최소 32바이트
            Assert.GreaterOrEqual(bytes.Length, 32,
                "암호문은 IV(16바이트) + 최소 1 AES 블록(16바이트) 이상이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 특수문자 및 엣지 케이스 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_SpecialCharacters_RoundTripSucceeds()
        {
            var plaintext = "!@#$%^&*()_+-=[]{}|;':\",./<>?\\`~";

            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var decrypted  = TokenEncryptor.Decrypt(ciphertext);

            Assert.AreEqual(plaintext, decrypted, "특수문자를 포함한 문자열도 왕복 암/복호화가 성공해야 합니다.");
        }

        [Test]
        public void Encrypt_WhitespaceOnly_RoundTripSucceeds()
        {
            var plaintext = "   \t\n\r  ";

            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var decrypted  = TokenEncryptor.Decrypt(ciphertext);

            Assert.AreEqual(plaintext, decrypted, "공백 문자열도 왕복 암/복호화가 성공해야 합니다.");
        }
    }
}
