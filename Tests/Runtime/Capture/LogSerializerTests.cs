using System.Collections;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GaoZombie.BugBeacon;

namespace GaoZombie.BugBeacon.Tests
{
    /// <summary>
    /// LogSerializer 단위 테스트.
    /// </summary>
    [TestFixture]
    public class LogSerializerTests
    {
        private LogSerializer _serializer;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _serializer = new LogSerializer();
            _tempDir = Path.Combine(Path.GetTempPath(), "LogSerializerTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ──────────────────────────────────────────────────────────────
        // Serialize 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Serialize_NullEntries_ReturnsEmptyString()
        {
            string result = _serializer.Serialize(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void Serialize_EmptyArray_ReturnsEmptyString()
        {
            string result = _serializer.Serialize(System.Array.Empty<LogEntry>());
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void Serialize_SingleEntry_ContainsMessage()
        {
            var entries = new[]
            {
                new LogEntry(1.234, LogType.Log, "안녕하세요 테스트", ""),
            };

            string result = _serializer.Serialize(entries);

            Assert.IsNotEmpty(result);
            StringAssert.Contains("안녕하세요 테스트", result);
            StringAssert.Contains("Log", result);
        }

        [Test]
        public void Serialize_ErrorEntry_ContainsStackTrace()
        {
            var entries = new[]
            {
                new LogEntry(2.0, LogType.Error, "에러 발생", "at SomeClass.Method()"),
            };

            string result = _serializer.Serialize(entries);

            StringAssert.Contains("에러 발생", result);
            StringAssert.Contains("at SomeClass.Method()", result);
        }

        [Test]
        public void Serialize_MultipleEntries_ContainsAllMessages()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log,     "메시지 1", ""),
                new LogEntry(2.0, LogType.Warning, "메시지 2", ""),
                new LogEntry(3.0, LogType.Error,   "메시지 3", "스택"),
            };

            string result = _serializer.Serialize(entries);

            StringAssert.Contains("메시지 1", result);
            StringAssert.Contains("메시지 2", result);
            StringAssert.Contains("메시지 3", result);
        }

        [Test]
        public void Serialize_ContainsHeader()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "헤더 테스트", ""),
            };

            string result = _serializer.Serialize(entries);

            StringAssert.Contains("BugBeacon", result);
            StringAssert.Contains("항목 수: 1", result);
        }

        // ──────────────────────────────────────────────────────────────
        // SaveAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SaveAsync_ValidEntries_CreatesZipFile()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "ZIP 테스트 메시지", ""),
            };
            string zipPath = Path.Combine(_tempDir, "logs.zip");

            var task = _serializer.SaveAsync(entries, zipPath);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted, $"저장 실패: {task.Exception?.Message}");
            Assert.IsTrue(File.Exists(zipPath), "ZIP 파일이 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_ValidEntries_ZipContainsLogsTxt()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "ZIP 내부 확인 테스트", ""),
            };
            string zipPath = Path.Combine(_tempDir, "logs.zip");

            var task = _serializer.SaveAsync(entries, zipPath);
            yield return new WaitUntil(() => task.IsCompleted);

            // ZIP 파일 내부에 logs.txt가 있는지 확인
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

            Assert.AreEqual(1, archive.Entries.Count, "ZIP에 항목이 1개여야 합니다.");
            Assert.AreEqual("logs.txt", archive.Entries[0].FullName, "파일명이 logs.txt여야 합니다.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_ValidEntries_ZipContentContainsMessage()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Error, "검색할 메시지 ABC123", "스택트레이스 내용"),
            };
            string zipPath = Path.Combine(_tempDir, "logs.zip");

            var saveTask = _serializer.SaveAsync(entries, zipPath);
            yield return new WaitUntil(() => saveTask.IsCompleted);

            // ZIP 내용 읽기
            var loadTask = _serializer.LoadAsync(zipPath);
            yield return new WaitUntil(() => loadTask.IsCompleted);

            string content = loadTask.Result;
            StringAssert.Contains("검색할 메시지 ABC123", content);
            StringAssert.Contains("스택트레이스 내용", content);
        }

        [UnityTest]
        public IEnumerator SaveAsync_NullEntries_CreatesZipWithEmptyContent()
        {
            string zipPath = Path.Combine(_tempDir, "empty_logs.zip");

            var task = _serializer.SaveAsync(null, zipPath);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.IsTrue(File.Exists(zipPath));
        }

        [Test]
        public void SaveAsync_NullPath_ThrowsArgumentNullException()
        {
            var entries = new LogEntry[0];
            Assert.ThrowsAsync<System.ArgumentNullException>(
                async () => await _serializer.SaveAsync(entries, null));
        }

        [UnityTest]
        public IEnumerator SaveAsync_CreatesDirectoryIfNotExists()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "디렉토리 생성 테스트", "") };
            string nestedPath = Path.Combine(_tempDir, "nested", "deep", "logs.zip");

            var task = _serializer.SaveAsync(entries, nestedPath);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.IsTrue(File.Exists(nestedPath));
        }
    }
}
