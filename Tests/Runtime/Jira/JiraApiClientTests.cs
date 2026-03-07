using System;
using System.Collections.Generic;
using NUnit.Framework;
using GaoZombie.BugOneTouch;

#pragma warning disable CS0618 // Obsolete 경고 억제 (JiraAttachmentUploader 테스트 하위 호환성)

namespace GaoZombie.BugOneTouch.Tests
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

        // ─── JiraAttachmentUploader 요청 모델 테스트 ──────────────────────────────

        [Test]
        public void AttachmentItem_필드_정상_초기화()
        {
            // Act
            var item = new JiraAttachmentUploader.AttachmentItem
            {
                FileName = "screenshot.png",
                Data = new byte[1024],
                ContentType = "image/png"
            };

            // Assert
            Assert.AreEqual("screenshot.png", item.FileName);
            Assert.AreEqual(1024, item.Data.Length);
            Assert.AreEqual("image/png", item.ContentType);
        }

        [Test]
        public void AttachmentItem_기본_ContentType_octet_stream()
        {
            // Act
            var item = new JiraAttachmentUploader.AttachmentItem
            {
                FileName = "data.bin",
                Data = new byte[10]
            };

            // Assert
            Assert.AreEqual("application/octet-stream", item.ContentType,
                "기본 ContentType은 application/octet-stream이어야 합니다.");
        }

        [Test]
        public void UploadResult_초기_상태_빈_목록()
        {
            // Act
            var result = new JiraAttachmentUploader.UploadResult();

            // Assert
            Assert.AreEqual(0, result.SucceededFiles.Count);
            Assert.AreEqual(0, result.SkippedFiles.Count);
            Assert.AreEqual(0, result.FailedFiles.Count);
            Assert.IsFalse(result.IsFullySuccessful, "빈 결과는 완전 성공이 아니어야 합니다.");
            Assert.IsFalse(result.HasAnySuccess, "빈 결과는 어떤 성공도 없어야 합니다.");
        }

        [Test]
        public void UploadResult_성공_파일_추가_후_HasAnySuccess_true()
        {
            // Arrange
            var result = new JiraAttachmentUploader.UploadResult();

            // Act
            result.SucceededFiles.Add("screenshot.png");

            // Assert
            Assert.IsTrue(result.HasAnySuccess, "성공 파일이 있을 때 HasAnySuccess는 true여야 합니다.");
        }

        [Test]
        public void UploadResult_성공만_있을_때_IsFullySuccessful_true()
        {
            // Arrange
            var result = new JiraAttachmentUploader.UploadResult();

            // Act
            result.SucceededFiles.Add("screenshot.png");
            result.SucceededFiles.Add("video.mp4");

            // Assert
            Assert.IsTrue(result.IsFullySuccessful, "건너뜀/실패 없이 성공만 있을 때 IsFullySuccessful은 true여야 합니다.");
        }

        [Test]
        public void UploadResult_건너뜀_파일_있을_때_IsFullySuccessful_false()
        {
            // Arrange
            var result = new JiraAttachmentUploader.UploadResult();

            // Act
            result.SucceededFiles.Add("screenshot.png");
            result.SkippedFiles.Add("large-video.mp4");

            // Assert
            Assert.IsFalse(result.IsFullySuccessful, "건너뜀 파일이 있을 때 IsFullySuccessful은 false여야 합니다.");
        }

        [Test]
        public void UploadResult_실패_파일_있을_때_IsFullySuccessful_false()
        {
            // Arrange
            var result = new JiraAttachmentUploader.UploadResult();

            // Act
            result.SucceededFiles.Add("screenshot.png");
            result.FailedFiles.Add(("broken.txt", "업로드 실패"));

            // Assert
            Assert.IsFalse(result.IsFullySuccessful, "실패 파일이 있을 때 IsFullySuccessful은 false여야 합니다.");
        }

        // ─── JiraSubmissionService 요청 모델 테스트 ───────────────────────────────

        [Test]
        public void SubmissionRequest_기본_첨부파일_목록_비어있음()
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
            Assert.IsNotNull(request.Attachments, "Attachments는 null이 아니어야 합니다.");
            Assert.AreEqual(0, request.Attachments.Count, "기본 Attachments는 빈 목록이어야 합니다.");
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
            Assert.IsNull(result.AttachmentResult, "초기 AttachmentResult는 null이어야 합니다.");
            Assert.IsNull(result.ErrorMessage, "초기 ErrorMessage는 null이어야 합니다.");
        }

        [Test]
        public void SubmissionResult_IsPartialSuccess_이슈성공_첨부실패()
        {
            // Arrange
            var uploadResult = new JiraAttachmentUploader.UploadResult();
            uploadResult.SucceededFiles.Add("ok.png");
            uploadResult.FailedFiles.Add(("fail.log", "오류"));

            var result = new JiraSubmissionService.SubmissionResult
            {
                Success = true,
                IssueKey = "PROJ-789",
                AttachmentResult = uploadResult
            };

            // Assert
            Assert.IsTrue(result.IsPartialSuccess,
                "이슈 생성 성공 + 첨부파일 부분 실패 시 IsPartialSuccess는 true여야 합니다.");
        }

        [Test]
        public void SubmissionResult_IsPartialSuccess_전체성공_false()
        {
            // Arrange
            var uploadResult = new JiraAttachmentUploader.UploadResult();
            uploadResult.SucceededFiles.Add("ok.png");

            var result = new JiraSubmissionService.SubmissionResult
            {
                Success = true,
                IssueKey = "PROJ-789",
                AttachmentResult = uploadResult
            };

            // Assert
            Assert.IsFalse(result.IsPartialSuccess,
                "전체 성공 시 IsPartialSuccess는 false여야 합니다.");
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

        // ─── 크기 제한 상수 테스트 ────────────────────────────────────────────────

        [Test]
        public void JiraAttachmentUploader_기본_크기제한_10MB()
        {
            // Assert
            Assert.AreEqual(10 * 1024 * 1024, JiraAttachmentUploader.DefaultMaxFileSizeBytes,
                "기본 파일 크기 제한은 10MB(10 * 1024 * 1024 bytes)여야 합니다.");
        }
    }
}

#pragma warning restore CS0618
