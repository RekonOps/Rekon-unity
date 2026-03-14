using System;
using System.Collections.Generic;
using NUnit.Framework;
using GaoZombie.BugBeacon;

namespace GaoZombie.BugBeacon.Tests
{
    /// <summary>
    /// Jira API 클라이언트 단위 테스트.
    /// UnityWebRequest 의존성으로 인해 실제 HTTP 호출 대신
    /// 모델 유효성, ADF 빌더, 이슈 요청 검증 로직을 테스트합니다.
    /// </summary>
    [TestFixture]
    public class JiraApiClientTests
    {
        // ─── JiraApiException 테스트 ──────────────────────────────────────────────

        [Test]
        public void JiraApiException_StatusCode_정상_저장()
        {
            // Act
            var ex = new JiraApiException(403, "Forbidden");

            // Assert
            Assert.AreEqual(403, ex.StatusCode);
            Assert.AreEqual("Forbidden", ex.Message);
        }

        [Test]
        public void JiraApiException_다양한_상태코드()
        {
            // Act & Assert
            Assert.AreEqual(400, new JiraApiException(400, "Bad Request").StatusCode);
            Assert.AreEqual(401, new JiraApiException(401, "Unauthorized").StatusCode);
            Assert.AreEqual(404, new JiraApiException(404, "Not Found").StatusCode);
            Assert.AreEqual(429, new JiraApiException(429, "Rate Limited").StatusCode);
            Assert.AreEqual(500, new JiraApiException(500, "Server Error").StatusCode);
        }

        // ─── AdfBuilder 테스트 ────────────────────────────────────────────────────

        [Test]
        public void AdfBuilder_CreateFromText_정상_JSON_생성()
        {
            // Arrange
            const string text = "버그가 발생했습니다.";

            // Act
            var adf = AdfBuilder.CreateFromText(text);

            // Assert
            Assert.IsNotNull(adf, "ADF JSON이 생성되어야 합니다.");
            Assert.IsTrue(adf.Contains("\"version\":1"), "version 필드가 포함되어야 합니다.");
            Assert.IsTrue(adf.Contains("\"type\":\"doc\""), "doc 타입이 포함되어야 합니다.");
            Assert.IsTrue(adf.Contains("\"type\":\"paragraph\""), "paragraph가 포함되어야 합니다.");
            Assert.IsTrue(adf.Contains("버그가 발생했습니다."), "본문 텍스트가 포함되어야 합니다.");
        }

        [Test]
        public void AdfBuilder_CreateFromText_빈_문자열_빈_단락_생성()
        {
            // Act
            var adf = AdfBuilder.CreateFromText("");

            // Assert
            Assert.IsNotNull(adf);
            Assert.IsTrue(adf.Contains("\"version\":1"), "version 필드가 포함되어야 합니다.");
            Assert.IsTrue(adf.Contains("\"type\":\"doc\""), "doc 타입이 포함되어야 합니다.");
        }

        [Test]
        public void AdfBuilder_CreateFromText_null_빈_단락_생성()
        {
            // Act
            var adf = AdfBuilder.CreateFromText(null);

            // Assert
            Assert.IsNotNull(adf);
            Assert.IsTrue(adf.Contains("\"type\":\"doc\""), "doc 타입이 포함되어야 합니다.");
        }

        [Test]
        public void AdfBuilder_CreateFromText_줄바꿈_특수문자_이스케이프()
        {
            // Arrange
            const string text = "줄1\n줄2\t탭";

            // Act
            var adf = AdfBuilder.CreateFromText(text);

            // Assert
            Assert.IsNotNull(adf);
            // 줄바꿈이 \n으로 이스케이프되어야 함
            Assert.IsTrue(adf.Contains("\\n"), "줄바꿈이 이스케이프되어야 합니다.");
        }

        [Test]
        public void AdfBuilder_CreateFromText_큰따옴표_이스케이프()
        {
            // Arrange
            const string text = "오류 메시지: \"null pointer exception\"";

            // Act
            var adf = AdfBuilder.CreateFromText(text);

            // Assert
            Assert.IsNotNull(adf);
            Assert.IsTrue(adf.Contains("\\\""), "큰따옴표가 이스케이프되어야 합니다.");
        }

        [Test]
        public void AdfBuilder_CreateEmpty_정상_JSON_생성()
        {
            // Act
            var adf = AdfBuilder.CreateEmpty();

            // Assert
            Assert.IsNotNull(adf);
            Assert.IsTrue(adf.Contains("\"version\":1"));
            Assert.IsTrue(adf.Contains("\"type\":\"doc\""));
            Assert.IsTrue(adf.Contains("\"content\":[]"), "빈 content가 포함되어야 합니다.");
        }

        [Test]
        public void AdfBuilder_CreateWithSections_제목_섹션_정상_생성()
        {
            // Arrange
            var sections = new Dictionary<string, string>
            {
                { "재현 단계", "1단계: 앱 실행\n2단계: 버그 트리거" },
                { "예상 동작", "정상 동작해야 합니다." },
                { "실제 동작", "크래시가 발생했습니다." }
            };

            // Act
            var adf = AdfBuilder.CreateWithSections("버그 리포트", sections);

            // Assert
            Assert.IsNotNull(adf);
            Assert.IsTrue(adf.Contains("버그 리포트"), "제목이 포함되어야 합니다.");
            Assert.IsTrue(adf.Contains("재현 단계"), "섹션 제목이 포함되어야 합니다.");
            Assert.IsTrue(adf.Contains("\"type\":\"heading\""), "heading 타입이 포함되어야 합니다.");
        }

        // ─── MultipartFormData 테스트 ─────────────────────────────────────────────

        [Test]
        public void MultipartFormData_필드_추가_정상()
        {
            // Arrange
            var formData = new MultipartFormData();

            // Act
            formData.AddField("key1", "value1");
            formData.AddField("key2", "value2");

            // Assert
            Assert.AreEqual("value1", formData.Fields["key1"]);
            Assert.AreEqual("value2", formData.Fields["key2"]);
        }

        [Test]
        public void MultipartFormData_파일_추가_정상()
        {
            // Arrange
            var formData = new MultipartFormData();
            var fileData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG 헤더

            // Act
            formData.AddFile("screenshot.png", fileData, "image/png");

            // Assert
            Assert.AreEqual(1, formData.Files.Count);
            Assert.AreEqual("screenshot.png", formData.Files[0].FileName);
            Assert.AreEqual("image/png", formData.Files[0].ContentType);
            Assert.AreEqual(fileData.Length, formData.Files[0].Data.Length);
        }

        [Test]
        public void MultipartFile_필드_정상_저장()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3 };

            // Act
            var file = new MultipartFile("test.txt", data, "text/plain");

            // Assert
            Assert.AreEqual("test.txt", file.FileName);
            Assert.AreSame(data, file.Data);
            Assert.AreEqual("text/plain", file.ContentType);
        }

        // ─── JiraIssueCreator 요청 모델 테스트 ───────────────────────────────────

        [Test]
        public void CreateIssueRequest_기본값_확인()
        {
            // Act
            var request = new JiraIssueCreator.CreateIssueRequest
            {
                ProjectKey = "PROJ",
                Summary = "테스트 버그"
            };

            // Assert
            Assert.AreEqual("Bug", request.IssueType, "기본 이슈 유형은 'Bug'여야 합니다.");
            Assert.AreEqual("Medium", request.Priority, "기본 우선순위는 'Medium'이어야 합니다.");
            Assert.IsNotNull(request.AdditionalLabels, "AdditionalLabels는 null이 아니어야 합니다.");
            Assert.AreEqual(0, request.AdditionalLabels.Length, "기본 AdditionalLabels는 빈 배열이어야 합니다.");
        }

        [Test]
        public void CreateIssueResult_필드_정상_저장()
        {
            // Act
            var result = new JiraIssueCreator.CreateIssueResult
            {
                IssueKey = "PROJ-456",
                IssueUrl = "https://api.atlassian.com/ex/jira/cloud-id/rest/api/3/issue/PROJ-456"
            };

            // Assert
            Assert.AreEqual("PROJ-456", result.IssueKey);
            Assert.IsTrue(result.IssueUrl.Contains("PROJ-456"), "이슈 URL에 이슈 키가 포함되어야 합니다.");
        }

        // ─── JiraSubmissionService 요청 모델 테스트 ───────────────────────────────

        [Test]
        public void SubmissionRequest_필수_필드_설정()
        {
            // Act
            var request = new JiraSubmissionService.SubmissionRequest
            {
                BundleId = "bundle-123",
                IssueRequest = new JiraIssueCreator.CreateIssueRequest
                {
                    ProjectKey = "PROJ",
                    Summary = "테스트"
                }
            };

            // Assert
            Assert.AreEqual("bundle-123", request.BundleId, "BundleId가 올바르게 설정되어야 합니다.");
            Assert.IsNotNull(request.IssueRequest, "IssueRequest는 null이 아니어야 합니다.");
            Assert.AreEqual("PROJ", request.IssueRequest.ProjectKey, "ProjectKey가 올바르게 설정되어야 합니다.");
        }

        [Test]
        public void SubmissionResult_초기_상태_확인()
        {
            // Act
            var result = new JiraSubmissionService.SubmissionResult();

            // Assert
            Assert.IsFalse(result.Success, "초기 Success는 false여야 합니다.");
            Assert.IsNull(result.IssueKey, "초기 IssueKey는 null이어야 합니다.");
            Assert.IsNull(result.IssueUrl, "초기 IssueUrl은 null이어야 합니다.");
            Assert.IsNull(result.ErrorMessage, "초기 ErrorMessage는 null이어야 합니다.");
        }

        [Test]
        public void SubmissionResult_성공_상태_확인()
        {
            // Arrange & Act
            var result = new JiraSubmissionService.SubmissionResult
            {
                Success = true,
                IssueKey = "PROJ-789",
                IssueUrl = "https://example.atlassian.net/browse/PROJ-789"
            };

            // Assert
            Assert.IsTrue(result.Success, "Success가 true여야 합니다.");
            Assert.AreEqual("PROJ-789", result.IssueKey, "IssueKey가 올바르게 설정되어야 합니다.");
            Assert.IsNotNull(result.IssueUrl, "IssueUrl은 null이 아니어야 합니다.");
        }

        // ─── IBundleStateUpdater 인터페이스 존재 확인 ────────────────────────────

        [Test]
        public void IBundleStateUpdater_인터페이스_존재_확인()
        {
            // Assert - 인터페이스가 존재하는지 타입 확인
            var interfaceType = typeof(IBundleStateUpdater);
            Assert.IsNotNull(interfaceType, "IBundleStateUpdater 인터페이스가 존재해야 합니다.");
            Assert.IsTrue(interfaceType.IsInterface, "IBundleStateUpdater는 인터페이스여야 합니다.");
        }

        [Test]
        public void IBundleStateUpdater_메서드_시그니처_확인()
        {
            // Arrange
            var interfaceType = typeof(IBundleStateUpdater);

            // Act & Assert
            var updateSubmitted = interfaceType.GetMethod("UpdateSubmittedAsync");
            var updateFailed = interfaceType.GetMethod("UpdateFailedAsync");
            var updateSubmitting = interfaceType.GetMethod("UpdateSubmittingAsync");

            Assert.IsNotNull(updateSubmitted, "UpdateSubmittedAsync 메서드가 존재해야 합니다.");
            Assert.IsNotNull(updateFailed, "UpdateFailedAsync 메서드가 존재해야 합니다.");
            Assert.IsNotNull(updateSubmitting, "UpdateSubmittingAsync 메서드가 존재해야 합니다.");
        }

    }
}
