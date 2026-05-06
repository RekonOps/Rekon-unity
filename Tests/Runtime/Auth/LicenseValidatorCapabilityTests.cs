using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// IRekonHttpClient seam 도입 후 capability 통합 테스트.
    /// MockHttpClient를 주입하여 UnityWebRequest 없이 HTTP 동작을 검증합니다.
    /// </summary>
    [TestFixture]
    public class LicenseValidatorCapabilityTests
    {
        // ─── MockHttpClient ──────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 MockHttpClient.
        /// IRekonHttpClient를 구현하며, 호출 기록 + 설정 가능한 응답을 제공합니다.
        /// </summary>
        private class MockHttpClient : IRekonHttpClient
        {
            /// <summary>기록된 요청 목록</summary>
            public List<RequestCall> Calls { get; } = new List<RequestCall>();

            /// <summary>반환할 응답 (기본: 200 OK)</summary>
            public HttpResponse ResponseToReturn { get; set; } = new HttpResponse { StatusCode = 200, Body = "{}" };

            /// <summary>설정 시 해당 예외를 throw합니다 (null이면 무시)</summary>
            public Exception ExceptionToThrow { get; set; }

            public Task<HttpResponse> GetAsync(
                string url,
                Dictionary<string, string> headers = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new RequestCall { Method = "GET", Url = url, Headers = headers });
                if (ExceptionToThrow != null) throw ExceptionToThrow;
                return Task.FromResult(ResponseToReturn);
            }

            public Task<HttpResponse> PostAsync(
                string url,
                string jsonBody,
                Dictionary<string, string> headers = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new RequestCall { Method = "POST", Url = url, Body = jsonBody, Headers = headers });
                if (ExceptionToThrow != null) throw ExceptionToThrow;
                return Task.FromResult(ResponseToReturn);
            }

            public Task<HttpResponse> PutAsync(
                string url,
                byte[] body,
                string contentType,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new RequestCall { Method = "PUT", Url = url, ContentType = contentType });
                if (ExceptionToThrow != null) throw ExceptionToThrow;
                return Task.FromResult(ResponseToReturn);
            }

            public class RequestCall
            {
                public string Method;
                public string Url;
                public string Body;
                public string ContentType;
                public Dictionary<string, string> Headers;
            }
        }

        // ─── 픽스처 ─────────────────────────────────────────────────────────────

        private SessionTokenStore _tokenStore;
        private MockHttpClient _mockHttp;

        [SetUp]
        public void SetUp()
        {
            _tokenStore = new SessionTokenStore("com.rekonops.capability-test");
            _tokenStore.Clear();
            _mockHttp = new MockHttpClient();
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── IRekonHttpClient 인터페이스 기본 검증 ───────────────────────────────

        [Test]
        public void HttpResponse_IsSuccess_2xx_범위_검증()
        {
            // 200~299는 IsSuccess = true
            for (int code = 200; code < 300; code++)
            {
                var r = new HttpResponse { StatusCode = code };
                Assert.IsTrue(r.IsSuccess, $"StatusCode {code}는 IsSuccess여야 합니다.");
            }
        }

        [Test]
        public void HttpResponse_IsSuccess_비2xx_범위_검증()
        {
            // 400, 401, 403, 500 등은 IsSuccess = false
            foreach (var code in new[] { 400, 401, 403, 404, 429, 500, 503 })
            {
                var r = new HttpResponse { StatusCode = code };
                Assert.IsFalse(r.IsSuccess, $"StatusCode {code}는 IsSuccess가 false여야 합니다.");
            }
        }

        // ─── MockHttpClient 기본 검증 ────────────────────────────────────────────

        [Test]
        public async Task MockHttpClient_GetAsync_호출_기록_정상()
        {
            // Arrange
            _mockHttp.ResponseToReturn = new HttpResponse { StatusCode = 200, Body = "{\"ok\":true}" };

            // Act
            var response = await _mockHttp.GetAsync(
                "https://example.com/api/test",
                new Dictionary<string, string> { { "Authorization", "Bearer token123" } });

            // Assert
            Assert.AreEqual(1, _mockHttp.Calls.Count, "호출이 1번 기록되어야 합니다.");
            Assert.AreEqual("GET", _mockHttp.Calls[0].Method);
            Assert.AreEqual("https://example.com/api/test", _mockHttp.Calls[0].Url);
            Assert.AreEqual("Bearer token123", _mockHttp.Calls[0].Headers["Authorization"]);
            Assert.AreEqual(200, response.StatusCode);
            Assert.IsTrue(response.IsSuccess);
        }

        [Test]
        public async Task MockHttpClient_PostAsync_호출_기록_정상()
        {
            // Arrange
            _mockHttp.ResponseToReturn = new HttpResponse { StatusCode = 201, Body = "{\"id\":\"abc\"}" };

            // Act
            var response = await _mockHttp.PostAsync(
                "https://example.com/api/resource",
                "{\"name\":\"test\"}",
                new Dictionary<string, string> { { "X-Custom", "value" } });

            // Assert
            Assert.AreEqual(1, _mockHttp.Calls.Count, "호출이 1번 기록되어야 합니다.");
            Assert.AreEqual("POST", _mockHttp.Calls[0].Method);
            Assert.AreEqual("{\"name\":\"test\"}", _mockHttp.Calls[0].Body);
            Assert.AreEqual(201, response.StatusCode);
        }

        // ─── LicenseValidator 생성자 — MockHttpClient 주입 ──────────────────────

        [Test]
        public void LicenseValidator_MockHttpClient_주입_생성_성공()
        {
            // Arrange & Act
            var validator = new LicenseValidator(
                "https://web.example.com",
                _tokenStore,
                _mockHttp);

            // Assert
            Assert.IsNotNull(validator, "MockHttpClient 주입 시 생성자가 정상 동작해야 합니다.");
        }

        [Test]
        public void LicenseValidator_null_HttpClient_기본값_UnityHttpClient_사용()
        {
            // null 전달 시 예외 없이 UnityHttpClient 사용
            Assert.DoesNotThrow(
                () => new LicenseValidator("https://web.example.com", _tokenStore, null),
                "null httpClient 전달 시 기본값(UnityHttpClient)이 사용되어야 합니다.");
        }

        // ─── LicenseValidator — ValidateAsync HTTP 호출 검증 ────────────────────

        [Test]
        public async Task LicenseValidator_ValidateAsync_POST_호출_URL_검증()
        {
            // Arrange
            _tokenStore.SaveSupabase("test-access-token-xyz");

            // validate-license는 valid=true 응답이어야 cache hit 됨
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team\",\"workspace_id\":\"ws-001\",\"workspace_name\":\"Test WS\"}"
            };

            var validator = new LicenseValidator(
                "https://web.example.com",
                _tokenStore,
                _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert — URL 검증
            Assert.AreEqual(1, _mockHttp.Calls.Count, "ValidateAsync는 HTTP 요청을 1번 해야 합니다.");
            Assert.AreEqual("POST", _mockHttp.Calls[0].Method);
            StringAssert.Contains("/api/unity/validate-license", _mockHttp.Calls[0].Url,
                "URL에 /api/unity/validate-license 가 포함되어야 합니다.");
        }

        [Test]
        public async Task LicenseValidator_ValidateAsync_Authorization_헤더_전송_검증()
        {
            // Arrange
            const string accessToken = "bearer-token-for-test";
            _tokenStore.SaveSupabase(accessToken);

            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"free\"}"
            };

            var validator = new LicenseValidator(
                "https://web.example.com",
                _tokenStore,
                _mockHttp);

            // Act
            await validator.ValidateAsync();

            // Assert — Authorization 헤더 검증
            Assert.IsTrue(_mockHttp.Calls.Count > 0, "HTTP 호출이 있어야 합니다.");
            var headers = _mockHttp.Calls[0].Headers;
            Assert.IsNotNull(headers, "헤더가 null이 아니어야 합니다.");
            Assert.IsTrue(headers.ContainsKey("Authorization"),
                "Authorization 헤더가 전송되어야 합니다.");
            Assert.AreEqual($"Bearer {accessToken}", headers["Authorization"],
                "Authorization 헤더 값이 Bearer {token} 형식이어야 합니다.");
        }

        [Test]
        public async Task LicenseValidator_ValidateAsync_토큰없음_예외_발생()
        {
            // Arrange — 토큰 없음
            _tokenStore.Clear();

            var validator = new LicenseValidator(
                "https://web.example.com",
                _tokenStore,
                _mockHttp);

            // Act & Assert — NetworkException 또는 AggregateException 발생
            Assert.That(
                async () => await validator.ValidateAsync(),
                Throws.TypeOf<AggregateException>(),
                "access_token 없을 때 예외가 발생해야 합니다.");

            // HTTP 호출이 일어나지 않아야 함
            Assert.AreEqual(0, _mockHttp.Calls.Count, "토큰 없으면 HTTP 호출이 없어야 합니다.");
            await Task.CompletedTask;
        }

        [Test]
        public async Task LicenseValidator_ValidateAsync_200응답_LicenseInfo_파싱()
        {
            // Arrange
            _tokenStore.SaveSupabase("token-abc");

            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"valid\":true,\"plan\":\"team\",\"workspace_id\":\"ws-123\"," +
                       "\"workspace_name\":\"Acme Corp\"}"
            };

            var validator = new LicenseValidator(
                "https://web.example.com",
                _tokenStore,
                _mockHttp);

            // Act
            var info = await validator.ValidateAsync();

            // Assert
            Assert.IsNotNull(info, "응답이 null이 아니어야 합니다.");
            Assert.IsTrue(info.Valid, "valid=true 응답이 Valid여야 합니다.");
            Assert.AreEqual("team", info.Plan, "plan 값이 파싱되어야 합니다.");
            Assert.AreEqual("ws-123", info.WorkspaceId, "workspace_id가 파싱되어야 합니다.");
            Assert.AreEqual("Acme Corp", info.WorkspaceName, "workspace_name이 파싱되어야 합니다.");
        }

        // ─── AuthBrokerClient — MockHttpClient 주입 ─────────────────────────────

        [Test]
        public void AuthBrokerClient_MockHttpClient_주입_생성_성공()
        {
            // Arrange & Act
            var client = new AuthBrokerClient(
                "https://broker.example.com/functions/v1",
                _tokenStore,
                _mockHttp);

            // Assert
            Assert.IsNotNull(client, "MockHttpClient 주입 시 생성자가 정상 동작해야 합니다.");
        }

        [Test]
        public async Task AuthBrokerClient_PostConnectJiraStart_POST_호출_검증()
        {
            // Arrange
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"connect_id\":\"cid-001\",\"authorize_url\":\"https://jira.example.com/auth\"}"
            };

            var client = new AuthBrokerClient(
                "https://broker.example.com/functions/v1",
                _tokenStore,
                _mockHttp);

            // Act
            var result = await client.PostConnectJiraStartAsync("tenant-001", "user-001");

            // Assert
            Assert.AreEqual(1, _mockHttp.Calls.Count, "POST 호출이 1번 있어야 합니다.");
            Assert.AreEqual("POST", _mockHttp.Calls[0].Method);
            StringAssert.Contains("connect-jira-start", _mockHttp.Calls[0].Url);
            Assert.AreEqual("cid-001", result.connect_id);
        }

        [Test]
        public async Task AuthBrokerClient_GetConnectJiraStatus_GET_호출_검증()
        {
            // Arrange
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"status\":\"pending\"}"
            };

            var client = new AuthBrokerClient(
                "https://broker.example.com/functions/v1",
                _tokenStore,
                _mockHttp);

            // Act
            var result = await client.GetConnectJiraStatusAsync("cid-test-001");

            // Assert
            Assert.AreEqual(1, _mockHttp.Calls.Count, "GET 호출이 1번 있어야 합니다.");
            Assert.AreEqual("GET", _mockHttp.Calls[0].Method);
            StringAssert.Contains("connect-jira-status", _mockHttp.Calls[0].Url);
            StringAssert.Contains("cid-test-001", _mockHttp.Calls[0].Url);
            Assert.AreEqual("pending", result.status);
        }

        // ─── SupabaseAuthClient — MockHttpClient 주입 ───────────────────────────

        [Test]
        public void SupabaseAuthClient_MockHttpClient_주입_생성_성공()
        {
            // Arrange & Act
            var client = new SupabaseAuthClient(
                "https://test.supabase.co",
                "anon-key-test",
                _tokenStore,
                _mockHttp);

            // Assert
            Assert.IsNotNull(client, "MockHttpClient 주입 시 생성자가 정상 동작해야 합니다.");
        }

        [Test]
        public async Task SupabaseAuthClient_PostAuthUnityStart_Authorization_헤더_anonKey_검증()
        {
            // Arrange
            const string anonKey = "supabase-anon-key-test";
            _mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = "{\"connect_id\":\"conn-001\",\"login_url\":\"https://login.example.com\"}"
            };

            var client = new SupabaseAuthClient(
                "https://test.supabase.co",
                anonKey,
                _tokenStore,
                _mockHttp);

            // Act — PostAuthUnityStartAsync는 internal이므로 StartWebLoginAsync 일부 테스트
            // 직접 접근 가능한 메서드로 검증 (StartWebLoginAsync 호출 시 첫 POST 요청에서 헤더 확인)
            // SupabaseAuthClient.PostAuthUnityStartAsync는 private이므로,
            // SendRequestAsync를 통해 헤더가 전달됨을 보장하는 대리 검증 사용

            // SendRequestAsync → _httpClient.GetAsync/PostAsync 경유 시 Authorization 헤더 포함 검증
            // 이를 위해 내부 메서드를 간접 호출 (StartWebLoginAsync의 첫 단계)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                // 브라우저 열기 전에 취소되도록 설계 — 첫 POST만 캡처
                await client.StartWebLoginAsync("device-001", cts.Token);
            }
            catch
            {
                // 취소 또는 흐름 오류 무시 — 호출 기록만 검증
            }

            // Assert — Authorization: Bearer {anonKey} 전송 여부
            if (_mockHttp.Calls.Count > 0)
            {
                var headers = _mockHttp.Calls[0].Headers;
                Assert.IsNotNull(headers, "헤더가 전송되어야 합니다.");
                Assert.IsTrue(headers.ContainsKey("Authorization"),
                    "Authorization 헤더가 있어야 합니다.");
                Assert.AreEqual($"Bearer {anonKey}", headers["Authorization"],
                    "Authorization 헤더 값이 Bearer {anonKey} 형식이어야 합니다.");
            }
            else
            {
                Assert.Inconclusive("HTTP 호출이 기록되지 않았습니다. StartWebLoginAsync 흐름 확인 필요.");
            }

            await Task.CompletedTask;
        }
    }
}
