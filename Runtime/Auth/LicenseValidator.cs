using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 라이선스 검증 클라이언트.
    /// validate-license Edge Function을 호출하여 라이선스를 검증하고,
    /// 결과를 로컬에 캐시합니다. 네트워크 실패 시 Grace Period(72시간) 내 캐시를 사용합니다.
    /// </summary>
    public class LicenseValidator
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>라이선스 검증 성공 시 발생합니다.</summary>
        public event Action<LicenseInfo> OnLicenseValidated;

        /// <summary>라이선스가 유효하지 않을 때 발생합니다.</summary>
        public event Action<string> OnLicenseInvalid;

        // ─── 응답 모델 (JsonUtility 호환) ─────────────────────────────────────────

        /// <summary>validate-license 응답 모델</summary>
        [Serializable]
        public class ValidateResponse
        {
            public bool valid;
            public string plan;
            public string workspace_id;
            public string workspace_name;
            public string expires_at;
            public string reason;
            public string message;
            // features는 중첩 객체이므로 JsonUtility로 직접 역직렬화 불가 → 별도 파싱
        }

        /// <summary>features 중첩 객체 모델</summary>
        [Serializable]
        public class FeaturesResponse
        {
            public bool jira_submit;
            public bool video_capture;
        }

        // ─── 라이선스 정보 (캐시용) ───────────────────────────────────────────────

        /// <summary>검증된 라이선스 정보를 보관하는 클래스</summary>
        public class LicenseInfo
        {
            public bool Valid;
            public string Plan;              // "free" | "trial" | "team"
            public string WorkspaceId;
            public string WorkspaceName;
            public bool JiraSubmitEnabled;
            public bool VideoCaptureEnabled;
            public DateTime? ExpiresAt;
            public DateTime LastCheckedAt;
        }

        /// <summary>캐시 직렬화용 내부 모델</summary>
        [Serializable]
        private class CachedLicenseData
        {
            public bool valid;
            public string plan;
            public string workspace_id;
            public string workspace_name;
            public bool jira_submit;
            public bool video_capture;
            public string expires_at;
            public string last_checked_at;   // ISO 8601
        }

        // ─── 상수 ─────────────────────────────────────────────────────────────────

        /// <summary>재검증 간격 (1시간)</summary>
        private const float CheckIntervalSeconds = 3600f;

        /// <summary>네트워크 실패 시 캐시 허용 시간 (72시간)</summary>
        private const float GracePeriodHours = 72f;

        /// <summary>캐시 저장 키</summary>
        private const string CachePrefsKey = "GaoZombie.BugOneTouch.LicenseCache";

        /// <summary>HTTP 요청 최대 재시도 횟수</summary>
        private const int MaxRetryCount = 3;

        /// <summary>재시도 기본 대기 시간(초)</summary>
        private const float RetryBaseDelaySeconds = 2f;

        /// <summary>HTTP 요청 타임아웃(초)</summary>
        private const float RequestTimeoutSeconds = 30f;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly SessionTokenStore _tokenStore;
        private LicenseInfo _cachedLicense;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// LicenseValidator를 초기화합니다.
        /// </summary>
        /// <param name="supabaseUrl">Supabase 프로젝트 URL</param>
        /// <param name="supabaseAnonKey">Supabase anon key</param>
        /// <param name="tokenStore">세션 토큰 저장소 (Prefs 추상화 참조용)</param>
        public LicenseValidator(string supabaseUrl, string supabaseAnonKey, SessionTokenStore tokenStore)
        {
            if (string.IsNullOrEmpty(supabaseUrl))
                throw new ArgumentNullException(nameof(supabaseUrl), "Supabase URL이 설정되지 않았습니다.");
            if (string.IsNullOrEmpty(supabaseAnonKey))
                throw new ArgumentNullException(nameof(supabaseAnonKey), "Supabase anon key가 설정되지 않았습니다.");

            _supabaseUrl = supabaseUrl.TrimEnd('/');
            _supabaseAnonKey = supabaseAnonKey;
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));

            // 저장된 캐시 로드
            _cachedLicense = LoadCacheFromPrefs();
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 라이선스를 서버에서 검증합니다.
        /// 네트워크 실패 시 Grace Period 내 캐시를 반환합니다.
        /// </summary>
        /// <param name="licenseKey">라이선스 키 (BOT-XXXX-XXXX-XXXX-XXXX)</param>
        /// <param name="userId">사용자 UUID</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>검증된 라이선스 정보</returns>
        public async Task<LicenseInfo> ValidateAsync(string licenseKey, string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(licenseKey))
                throw new ArgumentNullException(nameof(licenseKey), "라이선스 키가 필요합니다.");
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentNullException(nameof(userId), "사용자 ID가 필요합니다.");

            var url = $"{_supabaseUrl}/functions/v1/validate-license";
            var pluginVersion = GetPluginVersion();
            var body = $"{{\"license_key\":\"{EscapeJsonString(licenseKey)}\"," +
                       $"\"user_id\":\"{EscapeJsonString(userId)}\"," +
                       $"\"plugin_version\":\"{EscapeJsonString(pluginVersion)}\"}}";

            try
            {
                var responseJson = await SendWithRetryAsync(url, body, ct);
                var licenseInfo = ParseResponse(responseJson);

                if (licenseInfo.Valid)
                {
                    // 유효한 라이선스 → 캐시 저장 및 이벤트 발생
                    _cachedLicense = licenseInfo;
                    SaveCacheToPrefs(licenseInfo);
                    Debug.Log($"[BugOneTouch] 라이선스 검증 성공: plan={licenseInfo.Plan}, " +
                              $"workspace={licenseInfo.WorkspaceName}");
                    OnLicenseValidated?.Invoke(licenseInfo);
                }
                else
                {
                    // 서버가 invalid를 반환
                    _cachedLicense = licenseInfo;
                    ClearCache();
                    Debug.LogWarning("[BugOneTouch] 라이선스 무효: " +
                                     $"reason={licenseInfo.Plan ?? "unknown"}");
                    OnLicenseInvalid?.Invoke(responseJson);
                }

                return licenseInfo;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is NetworkException || ex is AggregateException)
            {
                // 네트워크/서버 오류 → Grace Period 내 캐시 사용
                Debug.LogWarning($"[BugOneTouch] 라이선스 검증 네트워크 오류: {ex.Message}");
                return HandleNetworkFailure(ex);
            }
            catch (Exception ex)
            {
                // JSON 파싱 오류 등 비네트워크 오류 → 캐시 사용하지 않고 실패 반환
                Debug.LogError($"[BugOneTouch] 라이선스 검증 처리 오류: {ex.Message}");
                var invalidInfo = new LicenseInfo
                {
                    Valid = false,
                    LastCheckedAt = DateTime.UtcNow
                };
                OnLicenseInvalid?.Invoke($"라이선스 검증 오류: {ex.Message}");
                return invalidInfo;
            }
        }

        /// <summary>
        /// 캐시된 라이선스 정보를 반환합니다 (서버 미호출).
        /// </summary>
        public LicenseInfo GetCachedLicense()
        {
            return _cachedLicense;
        }

        /// <summary>
        /// Jira 제출 가능 여부를 확인합니다.
        /// 캐시된 라이선스가 유효하고, plan이 trial/team이며,
        /// jira_submit feature가 활성화되어 있어야 합니다.
        /// </summary>
        public bool CanSubmitToJira()
        {
            if (_cachedLicense == null || !_cachedLicense.Valid)
                return false;

            // plan 확인: trial 또는 team만 허용
            if (_cachedLicense.Plan != "trial" && _cachedLicense.Plan != "team")
                return false;

            // feature 플래그 확인
            if (!_cachedLicense.JiraSubmitEnabled)
                return false;

            // Grace Period 확인
            if (!IsWithinGracePeriod(_cachedLicense.LastCheckedAt))
                return false;

            return true;
        }

        /// <summary>
        /// 재검증이 필요한지 확인합니다 (마지막 검증 후 1시간 경과 여부).
        /// </summary>
        public bool NeedsRevalidation()
        {
            if (_cachedLicense == null)
                return true;

            var elapsed = (DateTime.UtcNow - _cachedLicense.LastCheckedAt).TotalSeconds;
            return elapsed >= CheckIntervalSeconds;
        }

        // ─── 내부 메서드: HTTP 통신 ────────────────────────────────────────────────

        /// <summary>
        /// 지수 백오프로 HTTP 요청을 재시도합니다.
        /// AuthBrokerClient.SendWithRetryAsync 패턴을 따릅니다.
        /// </summary>
        private async Task<string> SendWithRetryAsync(
            string url,
            string jsonBody,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetryCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var responseJson = await SendRequestAsync(url, jsonBody, cancellationToken);
                    return responseJson;
                }
                catch (AuthBrokerException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    // 4xx 클라이언트 에러는 재시도하지 않음 (403 invalid 포함)
                    // 403의 경우 유효하지 않은 라이선스 응답이므로 body를 반환해야 함
                    if (ex.StatusCode == 403)
                    {
                        // AuthBrokerException에서 응답 본문을 추출할 수 없으므로
                        // SendRequestAsync에서 4xx도 본문을 반환하도록 처리
                        throw;
                    }
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    if (attempt < MaxRetryCount)
                    {
                        float delay = RetryBaseDelaySeconds * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning($"[BugOneTouch] 라이선스 검증 요청 실패 " +
                                         $"(시도 {attempt}/{MaxRetryCount}), " +
                                         $"{delay:F1}초 후 재시도. 에러: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                }
            }

            throw new AggregateException(
                $"라이선스 검증 요청 최대 재시도 횟수 초과 ({MaxRetryCount}회)", lastException);
        }

        /// <summary>
        /// UnityWebRequest로 단일 HTTP POST 요청을 전송합니다.
        /// AuthBrokerClient.SendRequestAsync 패턴을 따릅니다.
        /// 4xx 응답도 body를 반환합니다 (403 invalid license 처리를 위해).
        /// </summary>
        private async Task<string> SendRequestAsync(
            string url,
            string jsonBody,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<string>();
            var syncContext = SynchronizationContext.Current;

            void RunOnMainThread(Action action)
            {
                if (syncContext != null)
                    syncContext.Post(_ => action(), null);
                else
                    action();
            }

            RunOnMainThread(async () =>
            {
                UnityWebRequest request = null;
                bool isDisposed = false;
                CancellationTokenRegistration registration = default;

                try
                {
                    // POST 요청 생성
                    var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                    var uploadHandler = new UploadHandlerRaw(bodyBytes);
                    uploadHandler.contentType = "application/json";
                    request = new UnityWebRequest(url, "POST")
                    {
                        uploadHandler = uploadHandler,
                        downloadHandler = new DownloadHandlerBuffer()
                    };

                    // 헤더 설정
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {_supabaseAnonKey}");
                    request.timeout = (int)RequestTimeoutSeconds;

                    // 취소 등록
                    registration = cancellationToken.Register(() =>
                    {
                        if (!isDisposed)
                        {
                            try { request?.Abort(); }
                            catch (Exception) { /* Abort 실패 무시 */ }
                        }
                        tcs.TrySetCanceled();
                    });

                    // 요청 전송 및 완료 대기
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled();
                            return;
                        }
                        await Task.Yield();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    // 응답 처리
                    int statusCode = (int)request.responseCode;
                    string responseText = request.downloadHandler?.text ?? "";

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        // 200 OK
                        tcs.TrySetResult(responseText);
                    }
                    else if (statusCode == 403)
                    {
                        // 403 Forbidden → 라이선스 무효 응답 (body에 reason 포함)
                        tcs.TrySetResult(responseText);
                    }
                    else if (statusCode == 0)
                    {
                        // 네트워크 오류 (재시도 가능)
                        tcs.TrySetException(new NetworkException(
                            $"네트워크 오류: {request.error ?? "알 수 없음"}"));
                    }
                    else
                    {
                        // 기타 HTTP 에러
                        tcs.TrySetException(new AuthBrokerException(
                            statusCode,
                            $"HTTP {statusCode}: {request.error ?? ""} / {responseText}"));
                    }
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    isDisposed = true;
                    registration.Dispose();
                    request?.Dispose();
                }
            });

            return await tcs.Task;
        }

        // ─── 내부 메서드: 응답 파싱 ────────────────────────────────────────────────

        /// <summary>
        /// validate-license 응답 JSON을 LicenseInfo로 변환합니다.
        /// JsonUtility의 중첩 객체 제한을 우회하기 위해 features는 별도 파싱합니다.
        /// </summary>
        private LicenseInfo ParseResponse(string json)
        {
            var response = JsonUtility.FromJson<ValidateResponse>(json);
            var info = new LicenseInfo
            {
                Valid = response.valid,
                Plan = response.plan,
                WorkspaceId = response.workspace_id,
                WorkspaceName = response.workspace_name,
                LastCheckedAt = DateTime.UtcNow
            };

            // expires_at 파싱 (ISO 8601)
            if (!string.IsNullOrEmpty(response.expires_at))
            {
                if (DateTime.TryParse(response.expires_at, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                {
                    info.ExpiresAt = expiresAt.ToUniversalTime();
                }
            }

            // features 중첩 객체 파싱 (JSON에서 직접 bool 필드 추출)
            info.JiraSubmitEnabled = ExtractBoolField(json, "jira_submit") ?? false;
            info.VideoCaptureEnabled = ExtractBoolField(json, "video_capture") ?? false;

            return info;
        }

        // ─── 내부 메서드: Grace Period 처리 ────────────────────────────────────────

        /// <summary>
        /// 네트워크 실패 시 Grace Period 내 캐시를 반환하거나, 무효 처리합니다.
        /// </summary>
        private LicenseInfo HandleNetworkFailure(Exception ex)
        {
            if (_cachedLicense != null && _cachedLicense.Valid)
            {
                if (IsWithinGracePeriod(_cachedLicense.LastCheckedAt))
                {
                    var elapsed = (DateTime.UtcNow - _cachedLicense.LastCheckedAt).TotalHours;
                    Debug.LogWarning($"[BugOneTouch] 네트워크 실패 → Grace Period 캐시 사용 " +
                                     $"(경과: {elapsed:F1}시간 / {GracePeriodHours}시간)");
                    return _cachedLicense;
                }
                else
                {
                    // Grace Period 초과 → 라이선스 무효 처리
                    Debug.LogError("[BugOneTouch] Grace Period 초과 → 라이선스 무효 처리");
                    _cachedLicense.Valid = false;
                    ClearCache();
                    OnLicenseInvalid?.Invoke("Grace Period 초과로 라이선스가 무효 처리되었습니다.");
                    return _cachedLicense;
                }
            }

            // 캐시 없음 → 무효 정보 반환
            Debug.LogError("[BugOneTouch] 라이선스 검증 실패 (캐시 없음)");
            var invalidInfo = new LicenseInfo
            {
                Valid = false,
                LastCheckedAt = DateTime.UtcNow
            };
            OnLicenseInvalid?.Invoke($"라이선스 검증 실패: {ex.Message}");
            return invalidInfo;
        }

        /// <summary>
        /// 지정된 시각이 Grace Period 내에 있는지 확인합니다.
        /// </summary>
        private static bool IsWithinGracePeriod(DateTime lastCheckedUtc)
        {
            var elapsed = (DateTime.UtcNow - lastCheckedUtc).TotalHours;
            return elapsed < GracePeriodHours;
        }

        // ─── 내부 메서드: 캐시 저장/로드 ──────────────────────────────────────────

        /// <summary>
        /// 라이선스 정보를 EditorPrefs/PlayerPrefs에 JSON으로 저장합니다.
        /// </summary>
        private void SaveCacheToPrefs(LicenseInfo info)
        {
            try
            {
                var cache = new CachedLicenseData
                {
                    valid = info.Valid,
                    plan = info.Plan ?? "",
                    workspace_id = info.WorkspaceId ?? "",
                    workspace_name = info.WorkspaceName ?? "",
                    jira_submit = info.JiraSubmitEnabled,
                    video_capture = info.VideoCaptureEnabled,
                    expires_at = info.ExpiresAt?.ToString("o") ?? "",
                    last_checked_at = info.LastCheckedAt.ToString("o")
                };

                var json = JsonUtility.ToJson(cache);
                // SessionTokenStore의 암호화를 활용하여 위변조 방지
                _tokenStore.Save(json, CachePrefsKey);
                Debug.Log("[BugOneTouch] 라이선스 캐시 저장 완료 (암호화)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 라이선스 캐시 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// EditorPrefs/PlayerPrefs에서 캐시된 라이선스 정보를 로드합니다 (암호화 복호화).
        /// </summary>
        private LicenseInfo LoadCacheFromPrefs()
        {
            try
            {
                var json = _tokenStore.Load(CachePrefsKey);
                if (string.IsNullOrEmpty(json))
                    return null;

                var cache = JsonUtility.FromJson<CachedLicenseData>(json);
                if (cache == null)
                    return null;

                var info = new LicenseInfo
                {
                    Valid = cache.valid,
                    Plan = cache.plan,
                    WorkspaceId = cache.workspace_id,
                    WorkspaceName = cache.workspace_name,
                    JiraSubmitEnabled = cache.jira_submit,
                    VideoCaptureEnabled = cache.video_capture
                };

                // expires_at 파싱
                if (!string.IsNullOrEmpty(cache.expires_at))
                {
                    if (DateTime.TryParse(cache.expires_at, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                    {
                        info.ExpiresAt = expiresAt.ToUniversalTime();
                    }
                }

                // last_checked_at 파싱
                if (!string.IsNullOrEmpty(cache.last_checked_at))
                {
                    if (DateTime.TryParse(cache.last_checked_at, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var lastChecked))
                    {
                        info.LastCheckedAt = lastChecked.ToUniversalTime();
                    }
                }

                Debug.Log("[BugOneTouch] 라이선스 캐시 로드 완료");
                return info;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 라이선스 캐시 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>캐시를 삭제합니다.</summary>
        private void ClearCache()
        {
            _tokenStore.Clear(CachePrefsKey);
        }

        // ─── 유틸리티 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// JSON 문자열에서 "features" 객체 내의 bool 타입 필드를 추출합니다.
        /// features 블록 내부에서만 검색하여 다른 필드와의 충돌을 방지합니다.
        /// </summary>
        private static bool? ExtractBoolField(string json, string fieldName)
        {
            // features 객체 범위 내에서만 검색
            int featuresIdx = json.IndexOf("\"features\"", StringComparison.Ordinal);
            string searchScope = json;
            if (featuresIdx >= 0)
            {
                int braceStart = json.IndexOf('{', featuresIdx);
                if (braceStart >= 0)
                {
                    int depth = 0;
                    int braceEnd = -1;
                    for (int i = braceStart; i < json.Length; i++)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
                    }
                    if (braceEnd > braceStart)
                        searchScope = json.Substring(braceStart, braceEnd - braceStart + 1);
                }
            }

            var key = $"\"{fieldName}\":";
            int idx = searchScope.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;

            int valueStart = idx + key.Length;
            // 공백 건너뜀
            while (valueStart < searchScope.Length && searchScope[valueStart] == ' ')
                valueStart++;

            if (valueStart >= searchScope.Length)
                return null;

            // true/false 판별
            if (searchScope.Length >= valueStart + 4 &&
                searchScope.Substring(valueStart, 4) == "true")
                return true;

            if (searchScope.Length >= valueStart + 5 &&
                searchScope.Substring(valueStart, 5) == "false")
                return false;

            return null;
        }

        /// <summary>
        /// JSON 문자열 이스케이프 처리 (주입 공격 방지).
        /// </summary>
        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>플러그인 버전을 반환합니다.</summary>
        private static string GetPluginVersion()
        {
            // package.json의 version 필드를 참조하는 것이 이상적이나,
            // 런타임에서는 하드코딩 또는 ScriptableObject에서 읽어옴
            return "1.0.0";
        }

        // ─── Prefs 추상화 (에디터/빌드 분기) ──────────────────────────────────────
        // SessionTokenStore의 패턴을 따릅니다.

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
