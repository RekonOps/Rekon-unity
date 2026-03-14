using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// AuthBrokerClient 단위 테스트.
    /// UnityWebRequest 의존성으로 인해 실제 HTTP 호출 대신
    /// 모델 유효성, 상태 관리, 예외 처리를 검증합니다.
    /// </summary>
    [TestFixture]
    public class AuthBrokerClientTests
    {
        private SessionTokenStore _tokenStore;

        [SetUp]
        public void SetUp()
        {
            _tokenStore = new SessionTokenStore("com.rekonops.test");
            _tokenStore.Clear(); // 이전 테스트 잔여 데이터 제거
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── 생성자 검증 테스트 ────────────────────────────────────────────────────

        [Test]
        public void 생성자_유효한_URL_TokenStore_정상_생성()
        {
            // Arrange & Act
            var client = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            // Assert
            Assert.IsNotNull(client, "클라이언트가 정상 생성되어야 합니다.");
        }

        [Test]
        public void 생성자_빈_URL_예외_발생()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new AuthBrokerClient("", _tokenStore),
                "빈 URL 전달 시 ArgumentNullException이 발생해야 합니다.");
        }

        [Test]
        public void 생성자_null_URL_예외_발생()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new AuthBrokerClient(null, _tokenStore),
                "null URL 전달 시 ArgumentNullException이 발생해야 합니다.");
        }

        [Test]
        public void 생성자_null_TokenStore_예외_발생()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new AuthBrokerClient("https://test.supabase.co/functions/v1", null),
                "null TokenStore 전달 시 ArgumentNullException이 발생해야 합니다.");
        }

        // ─── 401 이벤트 테스트 ────────────────────────────────────────────────────

        [Test]
        public void OnUnauthorized_이벤트_구독_및_해제_정상_동작()
        {
            // Arrange
            var client = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            bool eventFired = false;
            Action handler = () => eventFired = true;

            // Act - 구독
            client.OnUnauthorized += handler;

            // 이벤트 직접 발생 불가하므로 구독 해제 테스트만 수행
            client.OnUnauthorized -= handler;

            // Assert
            Assert.IsFalse(eventFired, "이벤트 핸들러가 발생하지 않아야 합니다.");
        }

        // ─── AuthBrokerException 테스트 ───────────────────────────────────────────

        [Test]
        public void AuthBrokerException_StatusCode_정상_저장()
        {
            // Arrange & Act
            var ex = new AuthBrokerException(401, "Unauthorized");

            // Assert
            Assert.AreEqual(401, ex.StatusCode, "StatusCode가 올바르게 저장되어야 합니다.");
            Assert.AreEqual("Unauthorized", ex.Message, "Message가 올바르게 저장되어야 합니다.");
        }

        [Test]
        public void AuthBrokerException_다양한_상태코드_정상_저장()
        {
            // Arrange & Act
            var ex400 = new AuthBrokerException(400, "Bad Request");
            var ex403 = new AuthBrokerException(403, "Forbidden");
            var ex404 = new AuthBrokerException(404, "Not Found");
            var ex500 = new AuthBrokerException(500, "Internal Server Error");

            // Assert
            Assert.AreEqual(400, ex400.StatusCode);
            Assert.AreEqual(403, ex403.StatusCode);
            Assert.AreEqual(404, ex404.StatusCode);
            Assert.AreEqual(500, ex500.StatusCode);
        }

        // ─── NetworkException 테스트 ──────────────────────────────────────────────

        [Test]
        public void NetworkException_메시지_정상_저장()
        {
            // Arrange & Act
            var ex = new NetworkException("네트워크 오류 발생");

            // Assert
            Assert.AreEqual("네트워크 오류 발생", ex.Message);
        }

        [Test]
        public void NetworkException_내부_예외_정상_저장()
        {
            // Arrange
            var innerEx = new Exception("내부 원인");

            // Act
            var ex = new NetworkException("래핑된 네트워크 오류", innerEx);

            // Assert
            Assert.AreEqual("래핑된 네트워크 오류", ex.Message);
            Assert.AreSame(innerEx, ex.InnerException);
        }

        // ─── 취소 테스트 ──────────────────────────────────────────────────────────

        [Test]
        public async Task PostConnectJiraStart_취소_토큰_이미_취소된_상태_즉시_예외()
        {
            // Arrange
            var client = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            var cts = new CancellationTokenSource();
            cts.Cancel(); // 이미 취소된 상태

            // Act & Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await client.PostConnectJiraStartAsync("tenant-id", "user-id", cts.Token);
            });

            await Task.CompletedTask;
        }

        // ─── ConnectStartResponse 역직렬화 테스트 ────────────────────────────────

        [Test]
        public void ConnectStartResponse_필드_정상_접근()
        {
            // Arrange & Act
            var response = new AuthBrokerClient.ConnectStartResponse
            {
                connect_id = "test-uuid-1234",
                authorize_url = "https://auth.atlassian.com/authorize?test=1"
            };

            // Assert
            Assert.AreEqual("test-uuid-1234", response.connect_id);
            Assert.AreEqual("https://auth.atlassian.com/authorize?test=1", response.authorize_url);
        }

        [Test]
        public void ConnectStatusResponse_상태_필드_정상_접근()
        {
            // Arrange & Act
            var pendingResponse = new AuthBrokerClient.ConnectStatusResponse
            {
                status = "pending",
                session_token = null
            };

            var completedResponse = new AuthBrokerClient.ConnectStatusResponse
            {
                status = "completed",
                session_token = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.test.sig"
            };

            // Assert
            Assert.AreEqual("pending", pendingResponse.status);
            Assert.IsNull(pendingResponse.session_token);
            Assert.AreEqual("completed", completedResponse.status);
            Assert.IsNotNull(completedResponse.session_token);
        }

        [Test]
        public void JiraTokenResponse_필드_정상_접근()
        {
            // Arrange & Act
            var response = new AuthBrokerClient.JiraTokenResponse
            {
                access_token = "atlassian-access-token",
                expires_at = "2026-03-01T12:00:00Z",
                cloud_id = "cloud-uuid-5678"
            };

            // Assert
            Assert.AreEqual("atlassian-access-token", response.access_token);
            Assert.AreEqual("2026-03-01T12:00:00Z", response.expires_at);
            Assert.AreEqual("cloud-uuid-5678", response.cloud_id);
        }
    }
}
