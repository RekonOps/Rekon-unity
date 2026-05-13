using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// max_seats nullable 처리 회귀 테스트 (#145 옵션 A).
    /// Backend가 max_seats: null을 반환(team/team_pro 무제한)할 때
    /// LicenseValidator가 올바르게 파싱하는지 검증합니다.
    /// </summary>
    [TestFixture]
    public class LicenseMaxSeatsNullableTests
    {
        // ─── MockHttpClient ──────────────────────────────────────────────────────

        private class MockHttpClient : IRekonHttpClient
        {
            public HttpResponse ResponseToReturn { get; set; } =
                new HttpResponse { StatusCode = 200, Body = "{}" };

            public Task<HttpResponse> GetAsync(
                string url,
                Dictionary<string, string> headers = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseToReturn);

            public Task<HttpResponse> PostAsync(
                string url,
                string jsonBody,
                Dictionary<string, string> headers = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseToReturn);

            public Task<HttpResponse> PutAsync(
                string url,
                byte[] body,
                string contentType,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseToReturn);
        }

        // ─── 픽스처 ─────────────────────────────────────────────────────────────

        private SessionTokenStore _tokenStore;
        private MockHttpClient _mockHttp;

        [SetUp]
        public void SetUp()
        {
            _tokenStore = new SessionTokenStore("com.rekonops.maxseats-nullable-test");
            _tokenStore.Clear();
            _mockHttp = new MockHttpClient();
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── max_seats null 역직렬화 검증 ────────────────────────────────────────

        /// <summary>
        /// 핵심 회귀 케이스: features.max_seats = null → MaxSeats = null (무제한).
        /// team_pro 플랜의 무제한 시트 정책 반영.
        /// </summary>
        [Test]
        public async Task ValidateAsync_MaxSeatsNull_TeamPro_반환시_MaxSeatsIsNull()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team_pro\",\"workspace_id\":\"ws-001\"," +
                       "\"workspace_name\":\"Acme\",\"features\":{\"jira_submit\":true," +
                       "\"video_capture\":true,\"max_buffer_seconds\":300," +
                       "\"max_screenshot_count\":10,\"max_seats\":null}}"
            };

            var validator = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert
            Assert.IsNotNull(info, "응답이 null이 아니어야 합니다.");
            Assert.IsTrue(info.Valid, "valid=true 응답이 Valid여야 합니다.");
            Assert.AreEqual("team_pro", info.Plan, "플랜이 team_pro여야 합니다.");
            Assert.IsNull(info.MaxSeats, "max_seats: null → MaxSeats가 null이어야 합니다.");
        }

        /// <summary>
        /// team 플랜 + max_seats null → MaxSeats = null (무제한).
        /// </summary>
        [Test]
        public async Task ValidateAsync_MaxSeatsNull_Team_반환시_MaxSeatsIsNull()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team\",\"workspace_id\":\"ws-002\"," +
                       "\"workspace_name\":\"Beta\",\"features\":{\"jira_submit\":true," +
                       "\"video_capture\":true,\"max_buffer_seconds\":180," +
                       "\"max_screenshot_count\":5,\"max_seats\":null}}"
            };

            var validator = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert
            Assert.IsNull(info.MaxSeats, "team 플랜 max_seats: null → MaxSeats가 null이어야 합니다.");
        }

        /// <summary>
        /// free 플랜: max_seats = 1 → MaxSeats = 1 (유한값 정상 파싱).
        /// </summary>
        [Test]
        public async Task ValidateAsync_MaxSeats1_Free_반환시_MaxSeatsIs1()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"free\",\"workspace_id\":\"ws-003\"," +
                       "\"workspace_name\":\"Gamma\",\"features\":{\"jira_submit\":false," +
                       "\"video_capture\":false,\"max_buffer_seconds\":60," +
                       "\"max_screenshot_count\":3,\"max_seats\":1}}"
            };

            var validator = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert
            Assert.IsNotNull(info.MaxSeats, "max_seats: 1 → MaxSeats가 null이 아니어야 합니다.");
            Assert.AreEqual(1, info.MaxSeats.Value, "MaxSeats가 1이어야 합니다.");
        }

        /// <summary>
        /// 특정 시트 수(5) 반환 시 정상 파싱.
        /// </summary>
        [Test]
        public async Task ValidateAsync_MaxSeats5_반환시_MaxSeatsIs5()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team\",\"workspace_id\":\"ws-004\"," +
                       "\"workspace_name\":\"Delta\",\"features\":{\"jira_submit\":true," +
                       "\"video_capture\":true,\"max_buffer_seconds\":180," +
                       "\"max_screenshot_count\":5,\"max_seats\":5}}"
            };

            var validator = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert
            Assert.IsNotNull(info.MaxSeats, "max_seats: 5 → MaxSeats가 null이 아니어야 합니다.");
            Assert.AreEqual(5, info.MaxSeats.Value, "MaxSeats가 5여야 합니다.");
        }

        /// <summary>
        /// features 블록 없는 응답 → MaxSeats = null (필드 부재 → 무제한으로 처리).
        /// </summary>
        [Test]
        public async Task ValidateAsync_FeaturesBl록없음_MaxSeatsIsNull()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team\",\"workspace_id\":\"ws-005\"}"
            };

            var validator = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert — features 없으면 max_seats 필드도 없으므로 null 반환
            Assert.IsNull(info.MaxSeats,
                "features 블록 없을 때 MaxSeats가 null이어야 합니다 (필드 부재 → 무제한 처리).");
        }

        // ─── IsSeatUnlimited 헬퍼 검증 ──────────────────────────────────────────

        /// <summary>
        /// MaxSeats == null이면 IsSeatUnlimited() = true.
        /// </summary>
        [Test]
        public void IsSeatUnlimited_MaxSeatsNull_True()
        {
            var info = new LicenseValidator.LicenseInfo
            {
                Plan = "team_pro",
                MaxSeats = null
            };
            Assert.IsTrue(info.IsSeatUnlimited(),
                "MaxSeats == null이면 IsSeatUnlimited() = true여야 합니다.");
        }

        /// <summary>
        /// Plan = "team" + MaxSeats = 5이어도 IsSeatUnlimited() = true (plan 기준 우선).
        /// </summary>
        [Test]
        public void IsSeatUnlimited_PlanTeam_MaxSeats5_True()
        {
            var info = new LicenseValidator.LicenseInfo
            {
                Plan = "team",
                MaxSeats = 5
            };
            Assert.IsTrue(info.IsSeatUnlimited(),
                "team 플랜이면 MaxSeats 값과 무관하게 IsSeatUnlimited() = true여야 합니다.");
        }

        /// <summary>
        /// Plan = "free" + MaxSeats = 1이면 IsSeatUnlimited() = false.
        /// </summary>
        [Test]
        public void IsSeatUnlimited_PlanFree_MaxSeats1_False()
        {
            var info = new LicenseValidator.LicenseInfo
            {
                Plan = "free",
                MaxSeats = 1
            };
            Assert.IsFalse(info.IsSeatUnlimited(),
                "free 플랜 MaxSeats=1이면 IsSeatUnlimited() = false여야 합니다.");
        }

        // ─── MaxSeatsDisplay 헬퍼 검증 ──────────────────────────────────────────

        /// <summary>
        /// 무제한일 때 MaxSeatsDisplay() = "무제한".
        /// </summary>
        [Test]
        public void MaxSeatsDisplay_무제한_문자열_반환()
        {
            var info = new LicenseValidator.LicenseInfo
            {
                Plan = "team_pro",
                MaxSeats = null
            };
            Assert.AreEqual("무제한", info.MaxSeatsDisplay(),
                "MaxSeats null + team_pro 플랜이면 \"무제한\" 문자열이어야 합니다.");
        }

        /// <summary>
        /// 유한 시트일 때 MaxSeatsDisplay() = "{n}명" 형식.
        /// </summary>
        [Test]
        public void MaxSeatsDisplay_유한시트_명형식_반환()
        {
            var info = new LicenseValidator.LicenseInfo
            {
                Plan = "free",
                MaxSeats = 1
            };
            Assert.AreEqual("1명", info.MaxSeatsDisplay(),
                "free 플랜 MaxSeats=1이면 \"1명\" 문자열이어야 합니다.");
        }

        // ─── 캐시 직렬화/역직렬화 왕복 검증 ────────────────────────────────────

        /// <summary>
        /// max_seats null → 캐시 저장(-1) → 재로드 시 MaxSeats == null 복원.
        /// (캐시 왕복: ValidateAsync → SaveCacheToPrefs → 신규 validator LoadCacheFromPrefs)
        /// </summary>
        [Test]
        public async Task Cache_RoundTrip_MaxSeatsNull_복원()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team_pro\",\"workspace_id\":\"ws-rt\"," +
                       "\"workspace_name\":\"RoundTrip\",\"features\":{\"jira_submit\":true," +
                       "\"video_capture\":true,\"max_buffer_seconds\":300," +
                       "\"max_screenshot_count\":10,\"max_seats\":null}}"
            };

            // 첫 validator: 서버 응답 수신 + 캐시 저장
            var validator1 = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);
            var info1 = await validator1.ValidateAsync();
            Assert.IsNull(info1.MaxSeats, "1차 응답 MaxSeats가 null이어야 합니다.");

            // 두 번째 validator: 동일 tokenStore → 캐시 로드
            // HTTP 응답 없이 캐시에서 MaxSeats == null 복원 확인
            var noHttpMock = new MockHttpClient
            {
                ResponseToReturn = new HttpResponse { StatusCode = 500, Body = "서버오류" }
            };
            var validator2 = new LicenseValidator("https://web.example.com", _tokenStore, noHttpMock);
            var cachedInfo = validator2.GetCachedLicense();

            // Assert
            Assert.IsNotNull(cachedInfo, "캐시에서 로드된 LicenseInfo가 null이 아니어야 합니다.");
            Assert.IsNull(cachedInfo.MaxSeats, "캐시 왕복 후 MaxSeats가 null로 복원되어야 합니다.");
        }

        /// <summary>
        /// max_seats = 5 → 캐시 저장(5) → 재로드 시 MaxSeats == 5 복원.
        /// </summary>
        [Test]
        public async Task Cache_RoundTrip_MaxSeats5_복원()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-token");
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team\",\"workspace_id\":\"ws-rt2\"," +
                       "\"workspace_name\":\"RoundTrip2\",\"features\":{\"jira_submit\":true," +
                       "\"video_capture\":true,\"max_buffer_seconds\":180," +
                       "\"max_screenshot_count\":5,\"max_seats\":5}}"
            };

            var validator1 = new LicenseValidator("https://web.example.com", _tokenStore, _mockHttp);
            var info1 = await validator1.ValidateAsync();
            Assert.AreEqual(5, info1.MaxSeats.Value, "1차 응답 MaxSeats가 5이어야 합니다.");

            var noHttpMock = new MockHttpClient
            {
                ResponseToReturn = new HttpResponse { StatusCode = 500, Body = "서버오류" }
            };
            var validator2 = new LicenseValidator("https://web.example.com", _tokenStore, noHttpMock);
            var cachedInfo = validator2.GetCachedLicense();

            Assert.IsNotNull(cachedInfo, "캐시에서 로드된 LicenseInfo가 null이 아니어야 합니다.");
            Assert.IsNotNull(cachedInfo.MaxSeats, "캐시 왕복 후 MaxSeats가 null이 아니어야 합니다.");
            Assert.AreEqual(5, cachedInfo.MaxSeats.Value, "캐시 왕복 후 MaxSeats가 5로 복원되어야 합니다.");
        }
    }
}
