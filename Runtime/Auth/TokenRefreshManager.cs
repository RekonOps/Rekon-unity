using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// JWT 세션 토큰 및 Jira access_token의 자동 갱신을 관리합니다.
    /// 토큰 만료 5분 전 자동 갱신, 지수 백오프 재시도, 최종 실패 시 ReAuthHandler 호출.
    /// </summary>
    public class TokenRefreshManager : IDisposable
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const int RefreshBeforeExpirySeconds = 300; // 만료 5분 전
        private const int MaxRetryCount = 3;
        private const float RetryBaseDelaySeconds = 2f;
        private const float AccessTokenCacheMarginSeconds = 60f; // 1분 여유

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly AuthBrokerClient _brokerClient;
        private readonly SessionTokenStore _tokenStore;
        private readonly ReAuthHandler _reAuthHandler;

        // Jira access_token 인메모리 캐시
        private string _cachedAccessToken;
        private DateTime _accessTokenExpiresAt;
        private string _cachedCloudId;

        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// TokenRefreshManager를 초기화합니다.
        /// </summary>
        /// <param name="brokerClient">Auth Broker 클라이언트</param>
        /// <param name="tokenStore">세션 토큰 저장소</param>
        /// <param name="reAuthHandler">재인증 핸들러</param>
        public TokenRefreshManager(
            AuthBrokerClient brokerClient,
            SessionTokenStore tokenStore,
            ReAuthHandler reAuthHandler)
        {
            _brokerClient = brokerClient ?? throw new ArgumentNullException(nameof(brokerClient));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _reAuthHandler = reAuthHandler ?? throw new ArgumentNullException(nameof(reAuthHandler));

            // Auth Broker 401 이벤트 구독
            _brokerClient.OnUnauthorized += HandleUnauthorized;
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 유효한 Jira access_token을 반환합니다.
        /// 캐시된 토큰이 만료 예정이거나 없으면 자동 갱신합니다.
        /// </summary>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>(accessToken, cloudId) 튜플</returns>
        public async Task<(string AccessToken, string CloudId)> GetValidAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            // 캐시 유효성 확인 (1분 여유를 두고 갱신)
            if (!string.IsNullOrEmpty(_cachedAccessToken) &&
                DateTime.UtcNow.AddSeconds(AccessTokenCacheMarginSeconds) < _accessTokenExpiresAt)
            {
                return (_cachedAccessToken, _cachedCloudId);
            }

            // 갱신 필요 → 동시 갱신 방지 락 획득
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                // 락 획득 후 다시 확인 (Double-check)
                if (!string.IsNullOrEmpty(_cachedAccessToken) &&
                    DateTime.UtcNow.AddSeconds(AccessTokenCacheMarginSeconds) < _accessTokenExpiresAt)
                {
                    return (_cachedAccessToken, _cachedCloudId);
                }

                await RefreshAccessTokenAsync(cancellationToken);
                return (_cachedAccessToken, _cachedCloudId);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>
        /// 세션 토큰이 유효한지 확인하고, 만료 임박 시 갱신을 시도합니다.
        /// (현재 구현에서는 세션 토큰은 Auth Broker에 의해 관리되므로
        ///  /token-jira 호출로 검증합니다.)
        /// </summary>
        public bool HasValidSession()
        {
            return _tokenStore.HasValidToken();
        }

        /// <summary>
        /// access_token 캐시를 강제로 무효화합니다.
        /// 다음 GetValidAccessTokenAsync 호출 시 즉시 갱신됩니다.
        /// </summary>
        public void InvalidateAccessTokenCache()
        {
            _cachedAccessToken = null;
            _accessTokenExpiresAt = DateTime.MinValue;
            Debug.Log("[TokenRefreshManager] access_token 캐시 무효화");
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 지수 백오프로 Jira access_token을 갱신합니다.
        /// 3회 실패 시 ReAuthHandler를 호출합니다.
        /// </summary>
        private async Task RefreshAccessTokenAsync(CancellationToken cancellationToken)
        {
            Debug.Log("[TokenRefreshManager] Jira access_token 갱신 시작");

            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetryCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 세션 토큰 로드
                    var sessionToken = _tokenStore.Load();
                    if (string.IsNullOrEmpty(sessionToken))
                    {
                        Debug.LogWarning("[TokenRefreshManager] 세션 토큰 없음 → 재인증 필요");
                        await _reAuthHandler.TriggerReAuthAsync("세션 토큰이 만료되었습니다.");
                        throw new UnauthorizedAccessException("세션 토큰 없음. 재인증 필요.");
                    }

                    // Auth Broker에서 새 access_token 획득
                    var tokenResponse = await _brokerClient.PostTokenJiraAsync(
                        sessionToken,
                        cancellationToken);

                    if (string.IsNullOrEmpty(tokenResponse?.access_token))
                        throw new InvalidOperationException("빈 access_token 응답");

                    // 캐시 갱신
                    _cachedAccessToken = tokenResponse.access_token;
                    _cachedCloudId = tokenResponse.cloud_id;

                    // expires_at 파싱
                    if (DateTime.TryParse(tokenResponse.expires_at, null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var expiresAt))
                    {
                        _accessTokenExpiresAt = expiresAt.ToUniversalTime();
                    }
                    else
                    {
                        // 파싱 실패 시 1시간으로 기본값 설정
                        _accessTokenExpiresAt = DateTime.UtcNow.AddHours(1);
                        Debug.LogWarning("[TokenRefreshManager] expires_at 파싱 실패, 1시간으로 기본 설정");
                    }

                    Debug.Log($"[TokenRefreshManager] access_token 갱신 성공. " +
                              $"만료: {_accessTokenExpiresAt:O}, cloud_id: {_cachedCloudId}");
                    return;
                }
                catch (AuthBrokerException ex) when (ex.StatusCode == 401)
                {
                    // 401: 세션 토큰 만료 → 재인증 트리거
                    Debug.LogWarning("[TokenRefreshManager] 세션 토큰 만료 (401) → 재인증 요청");
                    _tokenStore.Clear();
                    await _reAuthHandler.TriggerReAuthAsync("Jira 연결이 만료되었습니다. 다시 연결해주세요.");
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
                        Debug.LogWarning($"[TokenRefreshManager] 갱신 실패 (시도 {attempt}/{MaxRetryCount}), " +
                                         $"{delay:F1}초 후 재시도. 에러: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                    else
                    {
                        // 최대 재시도 초과 → 재인증 요청
                        Debug.LogError($"[TokenRefreshManager] 최대 재시도 횟수 초과 → 재인증 필요. 마지막 오류: {ex.Message}");
                        await _reAuthHandler.TriggerReAuthAsync(
                            $"Jira 토큰 갱신에 실패했습니다. 다시 연결해주세요.\n오류: {ex.Message}");
                        throw new AggregateException(
                            $"토큰 갱신 최대 재시도 횟수 초과 ({MaxRetryCount}회)", lastException);
                    }
                }
            }
        }

        /// <summary>
        /// Auth Broker 401 이벤트 핸들러
        /// </summary>
        private void HandleUnauthorized()
        {
            Debug.LogWarning("[TokenRefreshManager] 401 Unauthorized 감지 → 캐시 무효화");
            InvalidateAccessTokenCache();
        }

        // ─── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_brokerClient != null)
                _brokerClient.OnUnauthorized -= HandleUnauthorized;

            _refreshLock?.Dispose();
        }
    }
}
