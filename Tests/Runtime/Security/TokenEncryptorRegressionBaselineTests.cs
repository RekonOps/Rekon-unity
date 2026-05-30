using System;
using System.Security.Cryptography;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// TokenEncryptor 회귀 baseline (characterization) 테스트.
    ///
    /// 목적: §7 C1 (AES-CBC → AES-GCM + per-install salt 전환) 작업 전, 현재 동작을 핀(pin).
    ///       기존 TokenEncryptorTests.cs 가 라운드트립/IV고유성/변조거부를 이미 두텁게 핀하므로
    ///       여기서는 C1 이 직접 닿는 지점만 최소 보강합니다.
    ///
    /// 주의: 아래 일부 케이스는 "이상적 동작" 이 아니라 "현재 코드 동작" 을 고정한 것입니다.
    ///        C1 전환 시 의도적으로 깨질 신호이며, 각 케이스 주석에 명시했습니다.
    /// </summary>
    [TestFixture]
    public class TokenEncryptorRegressionBaselineTests
    {
        // ──────────────────────────────────────────────────────────────
        // 라운드트립 안정성 재확인 (기존과 다른 시드 — 중복 회피)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_ThenDecrypt_VeryLongTokenWithEmoji_RoundTrips()
        {
            // 매우 긴 토큰 + 이모지(UTF-8 4바이트 문자) 포함 — 현재 UTF-8 round-trip 동작 핀.
            var plaintext = "rk_live_" + new string('Z', 4096) + "_세션토큰_🔐🚀😀_end";

            var ciphertext = TokenEncryptor.Encrypt(plaintext);
            var decrypted  = TokenEncryptor.Decrypt(ciphertext);

            Assert.AreEqual(plaintext, decrypted,
                "긴 토큰 + 이모지 포함 문자열도 왕복 암/복호화가 성공해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 위조 ciphertext 복호화 — 현재 동작(예외 throw) 핀
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Decrypt_ForgedCiphertextWithWrongKeyData_ThrowsCryptographicException()
        {
            // ⚠️ 현재 동작 핀 (이상적 동작 아님):
            //    다른 키/포맷으로 만든 위조 ciphertext 복호화 시 CryptographicException 을 throw 함.
            //    §7 C1 전환 시 graceful re-auth(예외 대신 재인증 유도) 도입 예정이므로,
            //    그 시점에 이 테스트는 "의도적으로" 깨질 신호 → graceful 처리 검증으로 교체할 것.
            //
            //    구성: IV(16) + 임의 데이터(16) = 32바이트. 길이 검증은 통과하지만
            //          PKCS7 언패딩/키 불일치로 복호화 단계에서 예외 발생.
            var forged = new byte[32];
            for (int i = 0; i < forged.Length; i++)
                forged[i] = (byte)((i * 37 + 11) & 0xFF);
            var forgedBase64 = Convert.ToBase64String(forged);

            Assert.Throws<CryptographicException>(
                () => TokenEncryptor.Decrypt(forgedBase64),
                "위조 ciphertext 복호화는 현재 CryptographicException 을 throw 해야 합니다 " +
                "(C1 전환 시 graceful 처리로 교체 예정).");
        }

        // ──────────────────────────────────────────────────────────────
        // 암호문 prefix 구조 핀 — CBC IV-prepend 포맷
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Encrypt_OutputFormat_HasIvPrependAndMinimumLength()
        {
            // ⚠️ 포맷 핀 (CBC IV-prepend):
            //    현재 Encrypt 결과 = Base64( IV(16바이트) + cipher(>=16) ).
            //    §7 C1 AES-GCM 전환 시 포맷(nonce 12 + tag 등)이 바뀌어 이 테스트가 깨질 수 있음 —
            //    포맷 변경 지점을 명시적으로 고정하는 회귀 신호.
            var plaintext  = "format-pin-token";
            var ciphertext = TokenEncryptor.Encrypt(plaintext);

            byte[] combined = Convert.FromBase64String(ciphertext);

            Assert.GreaterOrEqual(combined.Length, 32,
                "암호문은 IV(16) + 최소 1 AES 블록(16) 이상이어야 합니다.");

            // 앞 16바이트가 IV 로 prepend 되어 있다는 구조적 사실 핀:
            // 같은 평문을 두 번 암호화하면 랜덤 IV 로 인해 앞 16바이트가 달라야 함.
            var ciphertext2 = TokenEncryptor.Encrypt(plaintext);
            byte[] combined2 = Convert.FromBase64String(ciphertext2);

            bool ivDiffers = false;
            for (int i = 0; i < 16; i++)
            {
                if (combined[i] != combined2[i]) { ivDiffers = true; break; }
            }
            Assert.IsTrue(ivDiffers,
                "앞 16바이트(IV)는 매 암호화마다 달라야 합니다 (랜덤 IV prepend 포맷).");
        }

        // ──────────────────────────────────────────────────────────────
        // DeriveKey 결정성 간접 핀 — 동일 프로세스 내 round-trip 항상 성공
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DeriveKey_SameProcess_EncryptDecryptAlwaysSucceeds()
        {
            // 키 캐시(_derivedKey Lazy) 동작 핀: 동일 프로세스(=동일 install) 내에서는
            // 여러 번 Encrypt → Decrypt 가 항상 성공해야 함.
            // §7 C1 per-install salt 도입 후에도 "동일 install 내 round-trip 보장" 대비.
            for (int i = 0; i < 5; i++)
            {
                var plaintext  = $"derive-key-pin-{i}";
                var ciphertext = TokenEncryptor.Encrypt(plaintext);
                var decrypted  = TokenEncryptor.Decrypt(ciphertext);

                Assert.AreEqual(plaintext, decrypted,
                    $"동일 프로세스 내 round-trip(반복 {i})이 성공해야 합니다.");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 방법3: property/불변식 보강 — 무작위 평문 다수 라운드트립
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 무작위 평문 다수에 대해 Decrypt(Encrypt(x)) == x 라운드트립 불변식.
        ///
        /// §7 C1 AES-GCM 전환 후에도 이 테스트가 통과해야 합니다 (암호 알고리즘이 바뀌더라도
        /// 동일 프로세스 내 round-trip 보장은 유지되어야 하므로).
        /// </summary>
        [Test]
        public void Property_RandomPlaintexts_RoundTripInvariant()
        {
            // 재현 가능한 시드 — 무작위성을 보장하면서도 CI 실패 시 재현 가능
            var rng = new System.Random(seed: 42);
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+한글유니코드테스트";

            for (int trial = 0; trial < 20; trial++)
            {
                // 길이: 1~512 자 무작위
                int len = rng.Next(1, 513);
                var buf = new char[len];
                for (int j = 0; j < len; j++)
                    buf[j] = chars[rng.Next(chars.Length)];
                var plaintext = new string(buf);

                var ciphertext = TokenEncryptor.Encrypt(plaintext);
                var decrypted  = TokenEncryptor.Decrypt(ciphertext);

                Assert.AreEqual(plaintext, decrypted,
                    $"라운드트립 불변식 실패 (trial={trial}, len={len}): " +
                    $"Decrypt(Encrypt(x)) != x");
            }
        }

        /// <summary>
        /// 무작위 평문에 대해 Encrypt(x) != x — 평문이 암호문으로 변환됐는지 확인.
        ///
        /// ⚠️ 포맷 핀: 현재 암호문은 Base64(IV + cipherBytes) 이므로 평문과 다를 수밖에 없음.
        ///    §7 C1 전환 후에도 이 불변식은 유지되어야 합니다.
        /// </summary>
        [Test]
        public void Property_RandomPlaintexts_CiphertextNotEqualToPlaintext()
        {
            var rng = new System.Random(seed: 99);
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

            for (int trial = 0; trial < 20; trial++)
            {
                int len = rng.Next(4, 256);
                var buf = new char[len];
                for (int j = 0; j < len; j++)
                    buf[j] = chars[rng.Next(chars.Length)];
                var plaintext = new string(buf);

                var ciphertext = TokenEncryptor.Encrypt(plaintext);

                Assert.AreNotEqual(plaintext, ciphertext,
                    $"평문 == 암호문 불변식 위반 (trial={trial}): 암호화가 적용되지 않은 것으로 의심됩니다.");
            }
        }

        /// <summary>
        /// IV-prepend 포맷 핀: 같은 평문 두 번 암호화 시 결과(Base64) 가 달라야 함.
        ///
        /// 무작위 평문 다수에 대해 검증 — 기존 TokenEncryptorTests 의 단일 케이스를 보강.
        /// §7 C1 AES-GCM 전환 후에도 nonce/IV 고유성은 반드시 유지되어야 함.
        /// </summary>
        [Test]
        public void Property_SamePlaintext_ProducesDifferentCiphertextsEachTime()
        {
            var rng = new System.Random(seed: 7);
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

            for (int trial = 0; trial < 15; trial++)
            {
                int len = rng.Next(8, 128);
                var buf = new char[len];
                for (int j = 0; j < len; j++)
                    buf[j] = chars[rng.Next(chars.Length)];
                var plaintext = new string(buf);

                var cipher1 = TokenEncryptor.Encrypt(plaintext);
                var cipher2 = TokenEncryptor.Encrypt(plaintext);

                Assert.AreNotEqual(cipher1, cipher2,
                    $"동일 평문 두 번 암호화 결과가 같음 (trial={trial}): IV/nonce 고유성 위반.");

                // 양쪽 모두 정상 복호화 — 고유성 + 정합성 동시 보장
                Assert.AreEqual(plaintext, TokenEncryptor.Decrypt(cipher1),
                    $"cipher1 복호화 실패 (trial={trial})");
                Assert.AreEqual(plaintext, TokenEncryptor.Decrypt(cipher2),
                    $"cipher2 복호화 실패 (trial={trial})");
            }
        }
    }
}
