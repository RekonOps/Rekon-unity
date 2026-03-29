using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ManifestGenerator 단위 테스트.
    /// BuildArtifactList(private static) 동작을 Generate() 경유 및 리플렉션으로 검증합니다.
    /// </summary>
    [TestFixture]
    public class ManifestGeneratorTests
    {
        private ManifestGenerator _generator;
        private string _tempDir;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _generator = new ManifestGenerator();
            _tempDir = Path.Combine(Path.GetTempPath(), "ManifestGeneratorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ──────────────────────────────────────────────────────────────
        // BuildArtifactList — ScreenshotEntries 관련 (리플렉션)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void BuildArtifactList_ScreenshotEntries_3개_시_아티팩트_3개_추가()
        {
            var method = GetBuildArtifactListMethod();
            Assert.IsNotNull(method, "BuildArtifactList 메서드를 찾을 수 없습니다.");

            var captureResult = CreateResultWithScreenshotEntries(3);

            var artifacts = (System.Collections.Generic.List<BundleArtifact>)method.Invoke(null, new object[] { captureResult });

            var screenshotArtifacts = artifacts.Where(a => a.type == BundleArtifactType.Screenshot).ToList();
            Assert.AreEqual(3, screenshotArtifacts.Count,
                "ScreenshotEntries 3개일 때 Screenshot 아티팩트가 3개여야 합니다.");
        }

        [Test]
        public void BuildArtifactList_ScreenshotEntries_파일명_순서대로_screenshot_N_png()
        {
            var method = GetBuildArtifactListMethod();
            Assert.IsNotNull(method, "BuildArtifactList 메서드를 찾을 수 없습니다.");

            var captureResult = CreateResultWithScreenshotEntries(3);

            var artifacts = (System.Collections.Generic.List<BundleArtifact>)method.Invoke(null, new object[] { captureResult });

            var screenshotArtifacts = artifacts
                .Where(a => a.type == BundleArtifactType.Screenshot)
                .OrderBy(a => a.file_name)
                .ToList();

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual($"screenshot_{i}.png", screenshotArtifacts[i].file_name,
                    $"인덱스 {i} 아티팩트 파일명이 screenshot_{i}.png여야 합니다.");
            }
        }

        [Test]
        public void BuildArtifactList_ScreenshotEntries_null이면_추가_없음()
        {
            var method = GetBuildArtifactListMethod();
            Assert.IsNotNull(method, "BuildArtifactList 메서드를 찾을 수 없습니다.");

            // LogsPath만 있는 CaptureResult (ScreenshotEntries = null)
            string logsPath = CreateTempFile("logs.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            string statePath = CreateTempFile("state.json", System.Text.Encoding.UTF8.GetBytes("{}"));

            var captureResult = new CaptureResult
            {
                ScreenshotEntries = null,
                LogsPath          = logsPath,
                StatePath         = statePath,
                Timestamp         = DateTime.UtcNow,
            };

            var artifacts = (System.Collections.Generic.List<BundleArtifact>)method.Invoke(null, new object[] { captureResult });

            var screenshotArtifacts = artifacts.Where(a => a.type == BundleArtifactType.Screenshot).ToList();
            Assert.AreEqual(0, screenshotArtifacts.Count,
                "ScreenshotEntries가 null이면 Screenshot 아티팩트가 없어야 합니다.");
        }

        [Test]
        public void BuildArtifactList_빈_PngBytes_항목_스킵()
        {
            var method = GetBuildArtifactListMethod();
            Assert.IsNotNull(method, "BuildArtifactList 메서드를 찾을 수 없습니다.");

            string logsPath = CreateTempFile("logs.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            string statePath = CreateTempFile("state.json", System.Text.Encoding.UTF8.GetBytes("{}"));

            // 3개 항목 중 첫 번째만 빈 바이트
            var entries = new ScreenshotEntry[]
            {
                new ScreenshotEntry(new byte[0], DateTime.UtcNow),        // 빈 항목 → 스킵
                new ScreenshotEntry(new byte[] { 0x89, 0x50 }, DateTime.UtcNow), // 유효
                new ScreenshotEntry(new byte[] { 0x89, 0x50 }, DateTime.UtcNow), // 유효
            };

            var captureResult = new CaptureResult
            {
                ScreenshotEntries = entries,
                LogsPath          = logsPath,
                StatePath         = statePath,
                Timestamp         = DateTime.UtcNow,
            };

            var artifacts = (System.Collections.Generic.List<BundleArtifact>)method.Invoke(null, new object[] { captureResult });

            var screenshotArtifacts = artifacts.Where(a => a.type == BundleArtifactType.Screenshot).ToList();
            Assert.AreEqual(2, screenshotArtifacts.Count,
                "PngBytes가 빈 항목은 스킵되어 유효한 2개만 아티팩트로 등록되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // Generate() 경유 통합 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Generate_복수스크린샷_3개_아티팩트_3개_포함()
        {
            var captureResult = CreateResultWithScreenshotEntries(3);
            var manifest = _generator.Generate(captureResult);

            var screenshotArtifacts = manifest.artifacts
                .Where(a => a.type == BundleArtifactType.Screenshot)
                .ToList();

            Assert.AreEqual(3, screenshotArtifacts.Count,
                "Generate() 결과 매니페스트에 Screenshot 아티팩트 3개가 포함되어야 합니다.");
        }

        [Test]
        public void Generate_복수스크린샷_SHA256_초기값_빈문자열()
        {
            var captureResult = CreateResultWithScreenshotEntries(2);
            var manifest = _generator.Generate(captureResult);

            foreach (var artifact in manifest.artifacts.Where(a => a.type == BundleArtifactType.Screenshot))
            {
                Assert.AreEqual(string.Empty, artifact.sha256_hash,
                    "Generate() 단계에서 SHA-256 해시는 빈 문자열이어야 합니다 (BundleWriter에서 채움).");
            }
        }

        [Test]
        public void Generate_복수스크린샷_SizeBytes_올바르게_설정()
        {
            string logsPath = CreateTempFile("logs.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            string statePath = CreateTempFile("state.json", System.Text.Encoding.UTF8.GetBytes("{}"));

            byte[] png1 = new byte[100];
            byte[] png2 = new byte[200];

            var entries = new ScreenshotEntry[]
            {
                new ScreenshotEntry(png1, DateTime.UtcNow),
                new ScreenshotEntry(png2, DateTime.UtcNow),
            };

            var captureResult = new CaptureResult
            {
                ScreenshotEntries = entries,
                LogsPath          = logsPath,
                StatePath         = statePath,
                Timestamp         = DateTime.UtcNow,
            };

            var manifest = _generator.Generate(captureResult);

            var screenshotArtifacts = manifest.artifacts
                .Where(a => a.type == BundleArtifactType.Screenshot)
                .OrderBy(a => a.file_name)
                .ToList();

            Assert.AreEqual(2, screenshotArtifacts.Count);
            Assert.AreEqual(100L, screenshotArtifacts[0].size_bytes,
                "첫 번째 스크린샷 크기가 100이어야 합니다.");
            Assert.AreEqual(200L, screenshotArtifacts[1].size_bytes,
                "두 번째 스크린샷 크기가 200이어야 합니다.");
        }

        [Test]
        public void Generate_NullCaptureResult_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _generator.Generate(null));
        }

        [Test]
        public void Generate_아티팩트없는_CaptureResult_ThrowsInvalidOperationException()
        {
            var captureResult = new CaptureResult
            {
                ScreenshotEntries = null,
                LogsPath          = null,
                StatePath         = null,
                Timestamp         = DateTime.UtcNow,
            };

            Assert.Throws<InvalidOperationException>(() => _generator.Generate(captureResult));
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 리플렉션으로 ManifestGenerator.BuildArtifactList 메서드를 가져옵니다.
        /// </summary>
        private static MethodInfo GetBuildArtifactListMethod()
        {
            return typeof(ManifestGenerator).GetMethod(
                "BuildArtifactList",
                BindingFlags.NonPublic | BindingFlags.Static);
        }

        /// <summary>
        /// 지정한 개수의 유효한 ScreenshotEntries를 가진 CaptureResult를 생성합니다.
        /// LogsPath, StatePath도 더미 파일로 채워집니다.
        /// </summary>
        private CaptureResult CreateResultWithScreenshotEntries(int count)
        {
            string logsPath = CreateTempFile("logs.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            string statePath = CreateTempFile("state.json", System.Text.Encoding.UTF8.GetBytes("{}"));

            var entries = new ScreenshotEntry[count];
            for (int i = 0; i < count; i++)
            {
                byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, (byte)i, 0x00 };
                entries[i] = new ScreenshotEntry(png, DateTime.UtcNow);
            }

            return new CaptureResult
            {
                ScreenshotEntries = entries,
                LogsPath          = logsPath,
                StatePath         = statePath,
                Timestamp         = DateTime.UtcNow,
            };
        }

        /// <summary>
        /// 임시 파일을 생성하고 경로를 반환합니다.
        /// </summary>
        private string CreateTempFile(string suffix, byte[] content)
        {
            string path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}_{suffix}");
            File.WriteAllBytes(path, content);
            return path;
        }
    }
}
