using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// PeriodicFlushManager 및 MappedFileWriter 단위 테스트.
    /// 주기적 플러시 동작과 원자적 파일 쓰기를 검증합니다.
    /// </summary>
    [TestFixture]
    public class PeriodicFlushTests
    {
        private string _tempDir;
        private MappedFileWriter _fileWriter;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PeriodicFlushTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _fileWriter = new MappedFileWriter();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // MappedFileWriter - 동기 Write 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Write_ValidData_CreatesFile()
        {
            string filePath = Path.Combine(_tempDir, "test.bin");
            byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            bool result = _fileWriter.Write(filePath, data);

            Assert.IsTrue(result, "Write()는 성공 시 true를 반환해야 합니다.");
            Assert.IsTrue(File.Exists(filePath), "파일이 생성되어야 합니다.");
        }

        [Test]
        public void Write_ValidData_FileContentsMatch()
        {
            string filePath = Path.Combine(_tempDir, "test.bin");
            byte[] data = new byte[] { 0xAA, 0xBB, 0xCC };

            _fileWriter.Write(filePath, data);

            byte[] read = File.ReadAllBytes(filePath);
            Assert.AreEqual(data, read, "파일 내용이 쓴 데이터와 일치해야 합니다.");
        }

        [Test]
        public void Write_OverwritesExistingFile()
        {
            string filePath = Path.Combine(_tempDir, "overwrite.bin");
            byte[] original = new byte[] { 0x01, 0x02 };
            byte[] updated = new byte[] { 0x03, 0x04, 0x05 };

            _fileWriter.Write(filePath, original);
            _fileWriter.Write(filePath, updated);

            byte[] read = File.ReadAllBytes(filePath);
            Assert.AreEqual(updated, read, "기존 파일을 덮어써야 합니다.");
        }

        [Test]
        public void Write_CreatesParentDirectories()
        {
            string nestedPath = Path.Combine(_tempDir, "nested", "deep", "test.bin");

            bool result = _fileWriter.Write(nestedPath, new byte[] { 0x01 });

            Assert.IsTrue(result, "중간 디렉토리를 생성하고 쓰기에 성공해야 합니다.");
            Assert.IsTrue(File.Exists(nestedPath), "중첩된 경로에 파일이 생성되어야 합니다.");
        }

        [Test]
        public void Write_NoTempFileRemains()
        {
            string filePath = Path.Combine(_tempDir, "atomic.bin");
            string tempPath = filePath + ".tmp";

            _fileWriter.Write(filePath, new byte[] { 0x01 });

            Assert.IsFalse(File.Exists(tempPath), "쓰기 완료 후 임시 파일이 남아 있으면 안 됩니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // MappedFileWriter - WriteText 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void WriteText_ValidString_CreatesFile()
        {
            string filePath = Path.Combine(_tempDir, "test.json");
            string content = "{\"key\":\"value\"}";

            bool result = _fileWriter.WriteText(filePath, content);

            Assert.IsTrue(result, "WriteText()는 성공 시 true를 반환해야 합니다.");
            Assert.IsTrue(File.Exists(filePath), "파일이 생성되어야 합니다.");
        }

        [Test]
        public void WriteText_ContentIsUTF8Encoded()
        {
            string filePath = Path.Combine(_tempDir, "utf8.txt");
            string content = "한글 테스트: Hello World!";

            _fileWriter.WriteText(filePath, content);

            string read = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            Assert.AreEqual(content, read, "UTF-8로 정확하게 저장되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // MappedFileWriter - 비동기 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public async Task WriteAsync_ValidData_CreatesFile()
        {
            string filePath = Path.Combine(_tempDir, "async.bin");
            byte[] data = System.Text.Encoding.UTF8.GetBytes("비동기 쓰기 테스트");

            bool result = await _fileWriter.WriteAsync(filePath, data);

            Assert.IsTrue(result, "WriteAsync()는 성공 시 true를 반환해야 합니다.");
            Assert.IsTrue(File.Exists(filePath), "파일이 생성되어야 합니다.");
        }

        [Test]
        public async Task WriteTextAsync_ValidString_ContentMatches()
        {
            string filePath = Path.Combine(_tempDir, "async_text.json");
            string content = "{\"engine\":\"Unity\",\"version\":\"2022.3.22f1\"}";

            await _fileWriter.WriteTextAsync(filePath, content);

            string read = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            Assert.AreEqual(content, read, "비동기 쓰기 내용이 일치해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // MappedFileWriter - 입력 검증
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Write_NullFilePath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _fileWriter.Write(null, new byte[] { 0x01 }));
        }

        [Test]
        public void Write_NullData_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _fileWriter.Write(Path.Combine(_tempDir, "test.bin"), null));
        }

        [Test]
        public void WriteText_NullText_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _fileWriter.WriteText(Path.Combine(_tempDir, "test.txt"), null));
        }

        // ──────────────────────────────────────────────────────────────
        // LogSerializer - 로그 직렬화/저장 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public async Task LogSerializer_SaveAsync_CreatesTxtFile()
        {
            var serializer = new LogSerializer();
            var entries = new LogEntry[]
            {
                new LogEntry(1.0, LogType.Log, "테스트 로그 1", ""),
                new LogEntry(2.0, LogType.Warning, "테스트 경고", ""),
                new LogEntry(3.0, LogType.Error, "테스트 오류", "at Test.Method()"),
            };

            string txtPath = Path.Combine(_tempDir, "logs_flush.txt");
            await serializer.SaveAsync(entries, txtPath);

            Assert.IsTrue(File.Exists(txtPath), "TXT 파일이 생성되어야 합니다.");
            Assert.Greater(new FileInfo(txtPath).Length, 0, "TXT 파일이 비어 있으면 안 됩니다.");
        }

        [Test]
        public async Task LogSerializer_SaveAsync_EmptyEntries_CreatesTxt()
        {
            var serializer = new LogSerializer();
            string txtPath = Path.Combine(_tempDir, "empty_logs.txt");

            // 빈 배열도 정상 처리되어야 함
            await serializer.SaveAsync(Array.Empty<LogEntry>(), txtPath);

            Assert.IsTrue(File.Exists(txtPath), "빈 로그도 TXT 파일을 생성해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // PeriodicFlushManager - 경로 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void ActiveDir_IsUnderCrashRecoveryDir()
        {
            Assert.IsTrue(
                PeriodicFlushManager.ActiveDir.StartsWith(PeriodicFlushManager.CrashRecoveryDir),
                "ActiveDir는 CrashRecoveryDir의 하위 경로여야 합니다.");
        }

        [Test]
        public void ActiveDir_ContainsActiveDirName()
        {
            Assert.IsTrue(
                PeriodicFlushManager.ActiveDir.EndsWith(PeriodicFlushManager.ActiveDirName),
                $"ActiveDir는 '{PeriodicFlushManager.ActiveDirName}'으로 끝나야 합니다.");
        }

        [Test]
        public void CrashRecoveryDir_IsPersistentDataPath()
        {
            Assert.IsTrue(
                PeriodicFlushManager.CrashRecoveryDir.StartsWith(Application.persistentDataPath),
                "CrashRecoveryDir는 persistentDataPath 하위에 있어야 합니다.");
        }
    }
}
