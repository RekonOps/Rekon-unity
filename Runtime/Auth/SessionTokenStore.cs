using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// JWT 세션 토큰을 AES-256 암호화하여 EditorPrefs(에디터) 또는 PlayerPrefs(빌드)에 저장합니다.
    /// 암호화 키는 머신ID + 패키지명을 기반으로 PBKDF2로 파생됩니다.
    /// </summary>
    public class SessionTokenStore
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const string JiraPrefsKey = "RekonOps.BugOneTouch.SessionToken";
        private const string SupabasePrefsKey = "RekonOps.BugOneTouch.SupabaseToken";
        private const int Pbkdf2Iterations = 100_000;
        private const int AesKeySize = 32;       // 256 비트
        private const int AesIvSize = 16;        // 128 비트
        private const int HmacSize = 32;         // SHA-256 = 32 바이트
        private const int SaltSize = 16;         // PBKDF2 소금 크기

        // ─── 이벤트 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Supabase 토큰이 저장되었을 때 발행되는 이벤트.
        /// 로그인 감지용으로 구독합니다.
        /// </summary>
        public event Action OnTokenChanged;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly string _packageName;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// SessionTokenStore를 초기화합니다.
        /// </summary>
        /// <param name="packageName">패키지명 (키 파생에 사용됨)</param>
        public SessionTokenStore(string packageName = "com.rekonops.bug-onetouch")
        {
            _packageName = packageName;
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// JWT 토큰을 암호화하여 저장합니다 (Jira 세션용).
        /// </summary>
        /// <param name="token">저장할 JWT 토큰</param>
        public void Save(string token) => Save(token, JiraPrefsKey);

        /// <summary>
        /// JWT 토큰을 암호화하여 지정된 키에 저장합니다.
        /// </summary>
        /// <param name="token">저장할 JWT 토큰</param>
        /// <param name="prefsKey">저장 키</param>
        public void Save(string token, string prefsKey)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[SessionTokenStore] 빈 토큰 저장 시도 무시");
                return;
            }

            try
            {
                var encrypted = Encrypt(token);
                SetPrefs(prefsKey, encrypted);
                SavePrefs();
                Debug.Log($"[SessionTokenStore] 토큰 암호화 저장 완료 (키: {prefsKey})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionTokenStore] 토큰 저장 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 저장된 JWT 토큰을 복호화하여 반환합니다 (Jira 세션용).
        /// 토큰이 없거나 복호화 실패 시 null을 반환합니다.
        /// </summary>
        public string Load() => Load(JiraPrefsKey);

        /// <summary>
        /// 지정된 키에서 JWT 토큰을 복호화하여 반환합니다.
        /// </summary>
        /// <param name="prefsKey">저장 키</param>
        public string Load(string prefsKey)
        {
            try
            {
                var encrypted = GetPrefs(prefsKey);
                if (string.IsNullOrEmpty(encrypted))
                    return null;

                return Decrypt(encrypted);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionTokenStore] 토큰 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 저장된 토큰을 삭제합니다 (Jira 세션용).
        /// </summary>
        public void Clear() => Clear(JiraPrefsKey);

        /// <summary>
        /// 지정된 키의 토큰을 삭제합니다.
        /// </summary>
        /// <param name="prefsKey">저장 키</param>
        public void Clear(string prefsKey)
        {
            DeletePrefs(prefsKey);
            SavePrefs();
            Debug.Log($"[SessionTokenStore] 토큰 삭제 완료 (키: {prefsKey})");
        }

        /// <summary>
        /// 저장된 토큰이 만료되었는지 확인합니다 (Jira 세션용).
        /// JWT payload의 exp 필드를 파싱합니다.
        /// </summary>
        /// <param name="marginSeconds">만료 여유 시간 (기본 300초 = 5분)</param>
        /// <returns>만료되었으면 true</returns>
        public bool IsExpired(int marginSeconds = 300) => IsExpired(JiraPrefsKey, marginSeconds);

        /// <summary>
        /// 지정된 키의 토큰이 만료되었는지 확인합니다.
        /// </summary>
        /// <param name="prefsKey">저장 키</param>
        /// <param name="marginSeconds">만료 여유 시간 (기본 300초 = 5분)</param>
        /// <returns>만료되었으면 true</returns>
        public bool IsExpired(string prefsKey, int marginSeconds = 300)
        {
            var token = Load(prefsKey);
            if (string.IsNullOrEmpty(token))
                return true;

            try
            {
                var exp = ExtractJwtExpiry(token);
                if (exp == null)
                    return true;

                var expiryTime = DateTimeOffset.FromUnixTimeSeconds(exp.Value);
                var now = DateTimeOffset.UtcNow;
                return now.AddSeconds(marginSeconds) >= expiryTime;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionTokenStore] 토큰 만료 확인 실패: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// 저장된 토큰이 존재하고 유효한지 확인합니다 (Jira 세션용).
        /// </summary>
        public bool HasValidToken() => HasValidToken(JiraPrefsKey);

        /// <summary>
        /// 지정된 키의 토큰이 존재하고 유효한지 확인합니다.
        /// </summary>
        /// <param name="prefsKey">저장 키</param>
        public bool HasValidToken(string prefsKey)
        {
            var token = Load(prefsKey);
            return !string.IsNullOrEmpty(token) && !IsExpired(prefsKey, 0);
        }

        // ─── Supabase 편의 메서드 ────────────────────────────────────────────────────

        /// <summary>Supabase 액세스 토큰을 암호화하여 저장합니다.</summary>
        public void SaveSupabase(string token)
        {
            Save(token, SupabasePrefsKey);

            // 토큰 변경 이벤트 발행 (로그인 감지용)
            if (!string.IsNullOrEmpty(token))
            {
                try { OnTokenChanged?.Invoke(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SessionTokenStore] OnTokenChanged 핸들러 오류: {ex.Message}");
                }
            }
        }

        /// <summary>저장된 Supabase 액세스 토큰을 복호화하여 반환합니다.</summary>
        public string LoadSupabase() => Load(SupabasePrefsKey);

        /// <summary>저장된 Supabase 토큰을 삭제합니다.</summary>
        public void ClearSupabase() => Clear(SupabasePrefsKey);

        /// <summary>Supabase 토큰이 존재하고 유효한지 확인합니다.</summary>
        public bool HasValidSupabaseToken() => HasValidToken(SupabasePrefsKey);

        /// <summary>Supabase 토큰이 만료되었는지 확인합니다.</summary>
        public bool IsSupabaseExpired(int marginSeconds = 300) => IsExpired(SupabasePrefsKey, marginSeconds);

        // ─── JWT 유틸리티 ──────────────────────────────────────────────────────────

        /// <summary>
        /// JWT payload에서 exp(만료 시각) Unix 타임스탬프를 추출합니다.
        /// 외부 라이브러리 없이 Base64Url 직접 디코딩합니다.
        /// </summary>
        /// <param name="jwt">JWT 문자열</param>
        /// <returns>Unix 타임스탬프, 실패 시 null</returns>
        public static long? ExtractJwtExpiry(string jwt)
        {
            if (string.IsNullOrEmpty(jwt))
                return null;

            var parts = jwt.Split('.');
            if (parts.Length != 3)
                return null;

            try
            {
                // Base64Url → Base64 변환
                var payloadBase64 = Base64UrlToBase64(parts[1]);
                var payloadBytes = Convert.FromBase64String(payloadBase64);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);

                // exp 필드 추출 (간단한 JSON 파싱)
                return ExtractLongField(payloadJson, "exp");
            }
            catch
            {
                return null;
            }
        }

        // ─── 암호화 메서드 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 텍스트를 AES-256-CBC로 암호화합니다.
        /// 포맷: Base64(Salt + IV + CipherText + HMAC)
        /// </summary>
        private string Encrypt(string plainText)
        {
            var salt = GenerateRandomBytes(SaltSize);
            var key = DeriveKey(salt);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.GenerateIV();

            var iv = aes.IV;
            byte[] cipherText;

            using (var encryptor = aes.CreateEncryptor())
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            // HMAC-SHA256으로 무결성 검증 데이터 생성
            var dataToSign = CombineBytes(salt, iv, cipherText);
            var hmac = ComputeHmac(key, dataToSign);

            // 최종 패키지: Salt + IV + CipherText + HMAC
            var package = CombineBytes(salt, iv, cipherText, hmac);
            return Convert.ToBase64String(package);
        }

        /// <summary>
        /// AES-256-CBC로 암호화된 텍스트를 복호화합니다.
        /// HMAC 무결성 검증 포함.
        /// </summary>
        private string Decrypt(string encryptedBase64)
        {
            var package = Convert.FromBase64String(encryptedBase64);

            // 최소 크기 검증: Salt + IV + 최소 1블록(16) + HMAC
            int minSize = SaltSize + AesIvSize + 16 + HmacSize;
            if (package.Length < minSize)
                throw new CryptographicException("암호화된 데이터가 너무 짧습니다.");

            // 각 부분 추출
            var salt = new byte[SaltSize];
            var iv = new byte[AesIvSize];
            var hmac = new byte[HmacSize];
            int cipherLen = package.Length - SaltSize - AesIvSize - HmacSize;
            var cipherText = new byte[cipherLen];

            Buffer.BlockCopy(package, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(package, SaltSize, iv, 0, AesIvSize);
            Buffer.BlockCopy(package, SaltSize + AesIvSize, cipherText, 0, cipherLen);
            Buffer.BlockCopy(package, SaltSize + AesIvSize + cipherLen, hmac, 0, HmacSize);

            // 키 파생 및 HMAC 검증
            var key = DeriveKey(salt);
            var dataToVerify = CombineBytes(salt, iv, cipherText);
            var expectedHmac = ComputeHmac(key, dataToVerify);

            if (!ConstantTimeEquals(hmac, expectedHmac))
                throw new CryptographicException("HMAC 검증 실패: 데이터가 변조되었습니다.");

            // 복호화
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        // ─── 키 파생 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 머신 ID + 패키지명을 기반으로 PBKDF2로 AES 키를 파생합니다.
        /// </summary>
        private byte[] DeriveKey(byte[] salt)
        {
            // salt가 매번 다르므로 캐시를 사용하지 않고 항상 새로 파생
            var machineId = SystemInfo.deviceUniqueIdentifier;
            var keyMaterial = $"{machineId}:{_packageName}";
            var keyMaterialBytes = Encoding.UTF8.GetBytes(keyMaterial);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password: keyMaterialBytes,
                salt: salt,
                iterations: Pbkdf2Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256);

            return pbkdf2.GetBytes(AesKeySize);
        }

        // ─── 유틸리티 ─────────────────────────────────────────────────────────────

        private static byte[] GenerateRandomBytes(int size)
        {
            var bytes = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        private static byte[] ComputeHmac(byte[] key, byte[] data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        private static byte[] CombineBytes(params byte[][] arrays)
        {
            int totalLength = 0;
            foreach (var arr in arrays) totalLength += arr.Length;

            var result = new byte[totalLength];
            int offset = 0;
            foreach (var arr in arrays)
            {
                Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
                offset += arr.Length;
            }
            return result;
        }

        /// <summary>타이밍 공격 방지를 위한 상수 시간 바이트 배열 비교</summary>
        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Base64Url 인코딩을 표준 Base64로 변환합니다.</summary>
        private static string Base64UrlToBase64(string base64Url)
        {
            var base64 = base64Url.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return base64;
        }

        /// <summary>JSON 문자열에서 long 타입 필드를 추출합니다 (간단한 파서).</summary>
        private static long? ExtractLongField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\":";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;

            int valueStart = idx + key.Length;
            // 공백 건너뜀
            while (valueStart < json.Length && json[valueStart] == ' ')
                valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-'))
                valueEnd++;

            var valueStr = json.Substring(valueStart, valueEnd - valueStart);
            if (long.TryParse(valueStr, out long result))
                return result;

            return null;
        }

        // ─── Prefs 추상화 (에디터/빌드 분기) ──────────────────────────────────────

        private static void SetPrefs(string key, string value)
        {
#if UNITY_EDITOR
            EditorPrefs.SetString(key, value);
#else
            PlayerPrefs.SetString(key, value);
#endif
        }

        private static string GetPrefs(string key)
        {
#if UNITY_EDITOR
            return EditorPrefs.GetString(key, null);
#else
            return PlayerPrefs.GetString(key, null);
#endif
        }

        private static void DeletePrefs(string key)
        {
#if UNITY_EDITOR
            EditorPrefs.DeleteKey(key);
#else
            PlayerPrefs.DeleteKey(key);
#endif
        }

        private static void SavePrefs()
        {
#if !UNITY_EDITOR
            PlayerPrefs.Save();
#endif
        }
    }
}
