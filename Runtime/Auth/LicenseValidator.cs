using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RekonOps.Rekon
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
            public int max_buffer_seconds;
            public int max_screenshot_count;
            // max_seats: JsonUtility는 nullable 미지원 → int 유지 (null = -1 매핑)
            public int max_seats;
        }

        // ─── 라이선스 정보 (캐시용) ───────────────────────────────────────────────

        /// <summary>검증된 라이선스 정보를 보관하는 클래스</summary>
        public class LicenseInfo
        {
            public bool Valid;
            public string Plan;              // "free" | "team" | "team_pro"
            public string WorkspaceId;
            public string WorkspaceName;
            public bool JiraSubmitEnabled;
            public bool VideoCaptureEnabled;
            public DateTime? ExpiresAt;
            public DateTime LastCheckedAt;

            /// <summary>플랜별 최대 버퍼 시간(초). 기본값: 60 (free)</summary>
            public int MaxBufferSeconds { get; set; } = 60;

            /// <summary>플랜별 최대 스크린샷 개수. 기본값: 3 (free)</summary>
            public int MaxScreenshotCount { get; set; } = 3;

            /// <summary>
            /// 플랜별 최대 시트(멤버) 수. null = 무제한 (team/team_pro), 1 = free 기본값.
            /// #145 옵션 A: backend max_seats NULL = 무제한 정책 반영.
            /// </summary>
            public int? MaxSeats { get; set; } = 1;

            /// <summary>
            /// 시트 수가 무제한인지 여부를 반환합니다.
            /// team/team_pro 플랜 또는 MaxSeats == null 인 경우 무제한입니다.
            /// </summary>
            public bool IsSeatUnlimited()
            {
                // MaxSeats가 null이거나 plan이 team/team_pro이면 무제한
                if (!MaxSeats.HasValue) return true;
                return Plan == "team" || Plan == "team_pro";
            }

            /// <summary>
            /// 시트 한도 표시용 문자열을 반환합니다.
            /// 무제한이면 "무제한", 유한이면 "{n}명" 형식.
            /// </summary>
            public string MaxSeatsDisplay()
            {
                return IsSeatUnlimited() ? "무제한" : $"{MaxSeats?.ToString()}명";
            }

            /// <summary>연동된 외부 제공자 목록 (예: ["jira"])</summary>
            public string[] ConnectedProviders;

            /// <summary>지정된 제공자가 연동되어 있는지 확인합니다.</summary>
            public bool IsProviderConnected(string provider)
            {
                if (ConnectedProviders == null || ConnectedProviders.Length == 0)
                    return false;
                for (int i = 0; i < ConnectedProviders.Length; i++)
                {
                    if (string.Equals(ConnectedProviders[i], provider, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
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
            public string connected_providers_csv; // 쉼표 구분 문자열
            public int max_buffer_seconds;
            public int max_screenshot_count;
            // max_seats: -1 = 무제한(null), 0 이하 무효, 양수 = 한도
            // JsonUtility nullable 미지원 → -1 매직 값으로 null 표현 (#145 옵션 A)
            public int max_seats;
        }

        // ─── 상수 ─────────────────────────────────────────────────────────────────

        /// <summary>재검증 간격 (1시간)</summary>
        private const float CheckIntervalSeconds = 3600f;

        /// <summary>네트워크 실패 시 캐시 허용 시간 (72시간)</summary>
        private const float GracePeriodHours = 72f;

        /// <summary>캐시 저장 키</summary>
        private const string CachePrefsKey = "RekonOps.Rekon.LicenseCache";

        /// <summary>HTTP 요청 최대 재시도 횟수</summary>
        private const int MaxRetryCount = 3;

        /// <summary>재시도 기본 대기 시간(초)</summary>
        private const float RetryBaseDelaySeconds = 2f;

        /// <summary>HTTP 요청 타임아웃(초)</summary>
        private const float RequestTimeoutSeconds = 30f;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly string _baseUrl;
        private readonly SessionTokenStore _tokenStore;
        private readonly IRekonHttpClient _httpClient;
        private LicenseInfo _cachedLicense;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// LicenseValidator를 초기화합니다.
        /// </summary>
        /// <param name="baseUrl">웹 대시보드 기본 URL (WEB_DASHBOARD_URL 기반)</param>
        /// <param name="tokenStore">세션 토큰 저장소 (Prefs 추상화 참조용)</param>
        /// <param name="httpClient">HTTP 클라이언트 (null이면 UnityHttpClient 사용). 테스트에서 MockHttpClient 주입 가능.</param>
        public LicenseValidator(string baseUrl, SessionTokenStore tokenStore, IRekonHttpClient httpClient = null)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl), "웹 대시보드 URL이 설정되지 않았습니다.");

            _baseUrl = baseUrl.TrimEnd('/');
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _httpClient = httpClient ?? new UnityHttpClient();

            // 저장된 캐시 로드
            _cachedLicense = LoadCacheFromPrefs();
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 라이선스를 서버에서 검증합니다.
        /// JWT(access_token) 기반으로 서버에서 자동 조회합니다.
        /// 네트워크 실패 시 Grace Period 내 캐시를 반환합니다.
        /// </summary>
        /// <param name="ct">취소 토큰</param>
        /// <returns>검증된 라이선스 정보</returns>
        public async Task<LicenseInfo> ValidateAsync(CancellationToken ct = default)
        {
            var url = $"{_baseUrl}/api/unity/validate-license";
            var pluginVersion = GetPluginVersion();

            // JWT 자동 조회 path 만 사용 — licenseKey/userId 직접 전달 제거 (#169)
            string body = $"{{\"plugin_version\":\"{EscapeJsonString(pluginVersion)}\"}}";

            try
            {
                var responseJson = await SendWithRetryAsync(url, body, ct);
                var licenseInfo = ParseResponse(responseJson);

                if (licenseInfo.Valid)
                {
                    // 유효한 라이선스 → 캐시 저장 및 이벤트 발생
                    _cachedLicense = licenseInfo;
                    SaveCacheToPrefs(licenseInfo);
                    Debug.Log($"[Rekon] 라이선스 검증 성공: plan={licenseInfo.Plan}, " +
                              $"workspace={licenseInfo.WorkspaceName}");
                    OnLicenseValidated?.Invoke(licenseInfo);
                }
                else
                {
                    // 서버가 invalid를 반환
                    _cachedLicense = licenseInfo;
                    ClearCache();
                    Debug.LogWarning("[Rekon] 라이선스 무효: " +
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
                Debug.LogWarning($"[Rekon] 라이선스 검증 네트워크 오류: {ex.Message}");
                return HandleNetworkFailure(ex);
            }
            catch (Exception ex)
            {
                // JSON 파싱 오류 등 비네트워크 오류 → 캐시 사용하지 않고 실패 반환
                Debug.LogError($"[Rekon] 라이선스 검증 처리 오류: {ex.Message}");
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
        /// 캐시된 라이선스가 유효하고, plan이 free가 아니며,
        /// jira_submit feature가 활성화되어 있어야 합니다.
        /// </summary>
        public bool CanSubmitToJira()
        {
            if (_cachedLicense == null || !_cachedLicense.Valid)
                return false;

            // plan 확인: free는 허용하지 않음
            if (_cachedLicense.Plan == "free")
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
                        Debug.LogWarning($"[Rekon] 라이선스 검증 요청 실패 " +
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
        /// IRekonHttpClient를 통해 단일 HTTP POST 요청을 전송합니다.
        /// 4xx 응답도 body를 반환합니다 (403 invalid license 처리를 위해).
        /// </summary>
        private async Task<string> SendRequestAsync(
            string url,
            string jsonBody,
            CancellationToken cancellationToken)
        {
            // validate-license는 사용자 JWT(access_token)로 인증합니다.
            // apikey는 웹 프록시 서버 사이드에서 처리되므로 전송하지 않습니다.
            string accessToken = _tokenStore.LoadSupabase();
            if (string.IsNullOrEmpty(accessToken))
                throw new NetworkException("access_token이 없습니다. 웹 연동을 먼저 진행하세요.");

            var headers = new System.Collections.Generic.Dictionary<string, string>
            {
                { "Authorization", $"Bearer {accessToken}" },
                { "Accept", "application/json" }
            };

            var response = await _httpClient.PostAsync(url, jsonBody, headers, cancellationToken);

            if (response.IsSuccess)
            {
                // 200 OK
                return response.Body;
            }
            else if (response.StatusCode == 403)
            {
                // 403 Forbidden → 라이선스 무효 응답 (body에 reason 포함)
                return response.Body;
            }
            else
            {
                // 기타 HTTP 에러
                throw new AuthBrokerException(
                    response.StatusCode,
                    $"HTTP {response.StatusCode}: {response.Body}");
            }
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

            // 플랜별 제한값 파싱 (features 블록 내 int 필드)
            info.MaxBufferSeconds   = ExtractIntFieldInFeatures(json, "max_buffer_seconds",   60);
            info.MaxScreenshotCount = ExtractIntFieldInFeatures(json, "max_screenshot_count", 3);
            // max_seats: null = 무제한 (#145 옵션 A). 서버가 null 반환 시 null로 보존.
            info.MaxSeats = ExtractNullableIntFieldInFeatures(json, "max_seats");

            // connected_providers 배열 파싱 (예: ["jira"])
            info.ConnectedProviders = ExtractStringArray(json, "connected_providers");

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
                    Debug.LogWarning($"[Rekon] 네트워크 실패 → Grace Period 캐시 사용 " +
                                     $"(경과: {elapsed:F1}시간 / {GracePeriodHours}시간)");
                    return _cachedLicense;
                }
                else
                {
                    // Grace Period 초과 → 라이선스 무효 처리
                    Debug.LogError("[Rekon] Grace Period 초과 → 라이선스 무효 처리");
                    _cachedLicense.Valid = false;
                    ClearCache();
                    OnLicenseInvalid?.Invoke("Grace Period 초과로 라이선스가 무효 처리되었습니다.");
                    return _cachedLicense;
                }
            }

            // 캐시 없음 → 무효 정보 반환
            Debug.LogError("[Rekon] 라이선스 검증 실패 (캐시 없음)");
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
                    last_checked_at = info.LastCheckedAt.ToString("o"),
                    connected_providers_csv = info.ConnectedProviders != null
                        ? string.Join(",", info.ConnectedProviders)
                        : "",
                    max_buffer_seconds   = info.MaxBufferSeconds,
                    max_screenshot_count = info.MaxScreenshotCount,
                    // MaxSeats가 null(무제한)이면 -1로 직렬화 (#145 옵션 A)
                    max_seats = info.MaxSeats.HasValue ? info.MaxSeats.Value : -1
                };

                var json = JsonUtility.ToJson(cache);
                // SessionTokenStore의 암호화를 활용하여 위변조 방지
                _tokenStore.Save(json, CachePrefsKey);
                Debug.Log("[Rekon] 라이선스 캐시 저장 완료 (암호화)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 라이선스 캐시 저장 실패: {ex.Message}");
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
                    VideoCaptureEnabled = cache.video_capture,
                    ConnectedProviders = !string.IsNullOrEmpty(cache.connected_providers_csv)
                        ? cache.connected_providers_csv.Split(',')
                        : new string[0],
                    MaxBufferSeconds   = cache.max_buffer_seconds   > 0 ? cache.max_buffer_seconds   : 60,
                    MaxScreenshotCount = cache.max_screenshot_count > 0 ? cache.max_screenshot_count : 3,
                    // -1은 무제한(null)으로 복원, 그 외 양수는 그대로, 0 이하 무효는 1 (#145 옵션 A)
                    MaxSeats = cache.max_seats == -1 ? (int?)null
                             : cache.max_seats  > 0 ? cache.max_seats
                             : 1
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

                Debug.Log("[Rekon] 라이선스 캐시 로드 완료");
                return info;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] 라이선스 캐시 로드 실패: {ex.Message}");
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
        /// JSON 문자열의 "features" 객체 내에서 int 타입 필드를 추출합니다.
        /// 필드가 없거나 파싱에 실패하면 defaultValue를 반환합니다.
        /// </summary>
        private static int ExtractIntFieldInFeatures(string json, string fieldName, int defaultValue)
        {
            var result = ExtractNullableIntFieldInFeatures(json, fieldName);
            return result.HasValue ? result.Value : defaultValue;
        }

        /// <summary>
        /// JSON 문자열의 "features" 객체 내에서 nullable int 타입 필드를 추출합니다.
        /// 필드 값이 null 이면 null 반환, 숫자이면 해당 값 반환, 필드 부재 시 null 반환.
        /// #145 옵션 A: max_seats null = 무제한 처리에 사용.
        /// </summary>
        private static int? ExtractNullableIntFieldInFeatures(string json, string fieldName)
        {
            // features 객체 범위 추출
            string searchScope = json;
            int featuresIdx = json.IndexOf("\"features\"", StringComparison.Ordinal);
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

            // "fieldName": null 패턴 → null 반환
            var nullPattern = $"\"{fieldName}\"\\s*:\\s*null";
            if (System.Text.RegularExpressions.Regex.IsMatch(searchScope, nullPattern))
                return null;

            // "fieldName": 123 패턴 → 숫자 반환
            var numPattern = $"\"{fieldName}\"\\s*:\\s*(\\d+)";
            var match = System.Text.RegularExpressions.Regex.Match(searchScope, numPattern);
            if (match.Success)
                return int.Parse(match.Groups[1].Value);

            // 필드 없음 → null 반환
            return null;
        }

        /// <summary>
        /// JSON 문자열에서 문자열 배열 필드를 추출합니다.
        /// 예: "connected_providers": ["jira", "slack"] → {"jira", "slack"}
        /// </summary>
        private static string[] ExtractStringArray(string json, string fieldName)
        {
            var key = $"\"{fieldName}\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return new string[0];

            // '[' 찾기
            int bracketStart = json.IndexOf('[', idx + key.Length);
            if (bracketStart < 0) return new string[0];

            int bracketEnd = json.IndexOf(']', bracketStart);
            if (bracketEnd < 0) return new string[0];

            string content = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
            if (string.IsNullOrEmpty(content)) return new string[0];

            // 각 항목 파싱 ("jira", "slack" 등)
            var result = new System.Collections.Generic.List<string>();
            int pos = 0;
            while (pos < content.Length)
            {
                int quoteStart = content.IndexOf('"', pos);
                if (quoteStart < 0) break;
                int quoteEnd = content.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) break;
                result.Add(content.Substring(quoteStart + 1, quoteEnd - quoteStart - 1));
                pos = quoteEnd + 1;
            }

            return result.ToArray();
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
