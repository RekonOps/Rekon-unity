using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// LogSerializer JSONL 직렬화 단위 테스트.
    ///
    /// SerializeAsJsonl (internal)을 직접 호출하여 파일 IO 없이 직렬화 문자열만 검증합니다.
    /// SaveAsJsonlAsync 는 IO 경로 포함 별도 [Test] 로 동기 대기(.GetAwaiter().GetResult()) 검증합니다.
    ///
    /// Unity Test Framework는 [Test] public async Task 를 광역 인식 못하는 알려진 이슈(#164)로
    /// 모든 테스트는 동기 [Test] 로 작성합니다.
    /// </summary>
    [TestFixture]
    public class LogSerializerJsonlTests
    {
        private LogSerializer _serializerNoMask;
        private LogSerializer _serializerWithMask;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _serializerNoMask   = new LogSerializer(enableMasking: false);
            _serializerWithMask = new LogSerializer(enableMasking: true);

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "LogSerializerJsonlTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ──────────────────────────────────────────────────────────────────────
        // SerializeAsJsonl: 기본 포맷
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SerializeAsJsonl_NullEntries_ReturnsEmpty()
        {
            string result = _serializerNoMask.SerializeAsJsonl(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void SerializeAsJsonl_EmptyArray_ReturnsEmpty()
        {
            string result = _serializerNoMask.SerializeAsJsonl(Array.Empty<LogEntry>());
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void SerializeAsJsonl_SingleEntry_ValidJsonLine()
        {
            var entries = new[]
            {
                new LogEntry(425.123, LogType.Error, "충돌 감지", "at Game.Run()"),
            };

            string result = _serializerNoMask.SerializeAsJsonl(entries);
            string[] lines = result.TrimEnd('\n').Split('\n');

            Assert.AreEqual(1, lines.Length, "항목 1개 → 줄 1개여야 합니다.");

            string line = lines[0];
            StringAssert.StartsWith("{", line, "JSON 객체 시작.");
            StringAssert.EndsWith("}", line, "JSON 객체 종료.");
            StringAssert.Contains("\"t_abs\":", line);
            StringAssert.Contains("\"type\":\"Error\"", line);
            StringAssert.Contains("\"msg\":\"충돌 감지\"", line);
            StringAssert.Contains("\"stack\":\"at Game.Run()\"", line);
        }

        [Test]
        public void SerializeAsJsonl_Timestamp_RoundtripPrecision()
        {
            // double 정밀도 보존 확인 (F3 포맷이 아니라 R 포맷 사용)
            double ts = 425.123456789;
            var entries = new[] { new LogEntry(ts, LogType.Log, "정밀도", "") };

            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains(ts.ToString("R", System.Globalization.CultureInfo.InvariantCulture), result);
        }

        [Test]
        public void SerializeAsJsonl_MultipleEntries_EachOnOneLine()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log,     "메시지1", ""),
                new LogEntry(2.0, LogType.Warning, "메시지2", ""),
                new LogEntry(3.0, LogType.Error,   "메시지3", "스택"),
            };

            string result = _serializerNoMask.SerializeAsJsonl(entries);
            // 마지막 \n 제거 후 분리
            string[] lines = result.TrimEnd('\n').Split('\n');

            Assert.AreEqual(3, lines.Length, "항목 3개 → 줄 3개여야 합니다.");

            // 각 줄이 유효한 JSON 시작/종료인지 확인
            foreach (string line in lines)
            {
                Assert.IsNotEmpty(line);
                StringAssert.StartsWith("{", line);
                StringAssert.EndsWith("}", line);
            }
        }

        [Test]
        public void SerializeAsJsonl_AllLogTypes_CorrectTypeField()
        {
            LogType[] types = { LogType.Log, LogType.Warning, LogType.Error, LogType.Exception, LogType.Assert };

            foreach (var logType in types)
            {
                var entries = new[] { new LogEntry(1.0, logType, "msg", "") };
                string result = _serializerNoMask.SerializeAsJsonl(entries);
                StringAssert.Contains($"\"type\":\"{logType}\"", result,
                    $"LogType.{logType} 가 올바르게 직렬화되어야 합니다.");
            }
        }

        [Test]
        public void SerializeAsJsonl_EmptyStack_EmptyStringValue()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "메시지", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains("\"stack\":\"\"", result, "스택 없으면 빈 문자열이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // SerializeAsJsonl: JSON 이스케이프
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SerializeAsJsonl_QuoteInMessage_Escaped()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "말했다 \"안녕\"", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            // 따옴표는 \" 로 이스케이프
            StringAssert.Contains("\\\"안녕\\\"", result, "큰따옴표가 \\\" 로 이스케이프되어야 합니다.");
        }

        [Test]
        public void SerializeAsJsonl_BackslashInMessage_Escaped()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "경로: C:\\Users\\name", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            // 백슬래시는 \\ 로 이스케이프
            StringAssert.Contains("C:\\\\Users\\\\name", result, "백슬래시가 \\\\ 로 이스케이프되어야 합니다.");
        }

        [Test]
        public void SerializeAsJsonl_NewlineInStackTrace_EscapedToBackslashN()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Error, "에러", "at A()\nat B()\nat C()") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);

            // JSONL 규칙: 스택 내 개행은 \n JSON 이스케이프 (실제 개행 없어야 함)
            StringAssert.Contains("at A()\\nat B()\\nat C()", result,
                "스택의 개행이 \\n 으로 이스케이프되어야 합니다.");

            // 결과 전체에서 실제 개행이 항목당 1개(줄 구분자)만 있어야 함
            string[] lines = result.Split('\n');
            // 항목 1개 → 줄 1개 + 빈 줄(마지막 \n) = 2개
            Assert.AreEqual(2, lines.Length,
                "JSONL 줄 구분자 외 실제 개행이 없어야 합니다.");
        }

        [Test]
        public void SerializeAsJsonl_CarriageReturnInMessage_Escaped()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "라인\r끝", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains("\\r", result, "\\r 이 이스케이프되어야 합니다.");
        }

        [Test]
        public void SerializeAsJsonl_TabInMessage_Escaped()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "탭\t포함", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains("\\t", result, "\\t 이 이스케이프되어야 합니다.");
        }

        [Test]
        public void SerializeAsJsonl_ControlCharInMessage_EscapedToUnicode()
        {
            // U+0001 제어문자
            var entries = new[] { new LogEntry(1.0, LogType.Log, "제어문자", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains("\\u0001", result, "제어문자가 \\uXXXX 로 이스케이프되어야 합니다.");
        }

        [Test]
        public void SerializeAsJsonl_KoreanUnicode_NotEscaped()
        {
            // 한글(U+AC00 이상)은 이스케이프 없이 그대로 출력
            var entries = new[] { new LogEntry(1.0, LogType.Log, "한글 메시지", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains("한글 메시지", result, "한글은 이스케이프 없이 그대로 출력되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // SerializeAsJsonl: 마스킹
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SerializeAsJsonl_MaskingDisabled_RawMessage()
        {
            // 마스킹 비활성 시 raw 메시지 그대로
            var entries = new[] { new LogEntry(1.0, LogType.Log, "원본 메시지", "") };
            string result = _serializerNoMask.SerializeAsJsonl(entries);
            StringAssert.Contains("원본 메시지", result);
        }

        [Test]
        public void SerializeAsJsonl_MaskingEnabled_DoesNotThrow()
        {
            // 마스킹 활성 시 예외 없이 직렬화 완료
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "메시지", "스택트레이스"),
                new LogEntry(2.0, LogType.Error, "에러메시지", "at SomeClass()"),
            };

            string result = null;
            Assert.DoesNotThrow(() => result = _serializerWithMask.SerializeAsJsonl(entries));
            Assert.IsNotNull(result);
            Assert.IsNotEmpty(result);
        }

        [Test]
        public void SerializeAsJsonl_MaskingEnabled_OutputHasCorrectLineCount()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "A", ""),
                new LogEntry(2.0, LogType.Log, "B", ""),
            };

            string result = _serializerWithMask.SerializeAsJsonl(entries);
            string[] lines = result.TrimEnd('\n').Split('\n');
            Assert.AreEqual(2, lines.Length, "마스킹 활성 시에도 줄 수가 항목 수와 일치해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // SaveAsJsonlAsync: IO 경로
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SaveAsJsonlAsync_NullPath_Throws()
        {
            var entries = new LogEntry[0];
            Assert.Throws<ArgumentNullException>(() =>
                _serializerNoMask.SaveAsJsonlAsync(entries, null).GetAwaiter().GetResult());
        }

        [Test]
        public void SaveAsJsonlAsync_EmptyPath_Throws()
        {
            var entries = new LogEntry[0];
            Assert.Throws<ArgumentNullException>(() =>
                _serializerNoMask.SaveAsJsonlAsync(entries, string.Empty).GetAwaiter().GetResult());
        }

        [Test]
        public void SaveAsJsonlAsync_ValidEntries_CreatesJsonlFile()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "JSONL 저장 테스트", "") };
            string path = Path.Combine(_tempDir, "logs.jsonl");

            _serializerNoMask.SaveAsJsonlAsync(entries, path).GetAwaiter().GetResult();

            Assert.IsTrue(File.Exists(path), ".jsonl 파일이 생성되어야 합니다.");
        }

        [Test]
        public void SaveAsJsonlAsync_ValidEntries_ContentMatchesSerialization()
        {
            var entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "내용 검증", ""),
                new LogEntry(2.0, LogType.Error, "에러 내용", "스택"),
            };
            string path = Path.Combine(_tempDir, "logs.jsonl");

            _serializerNoMask.SaveAsJsonlAsync(entries, path).GetAwaiter().GetResult();

            string content = File.ReadAllText(path, Encoding.UTF8);
            string expected = _serializerNoMask.SerializeAsJsonl(entries);
            Assert.AreEqual(expected, content, "파일 내용이 SerializeAsJsonl 결과와 일치해야 합니다.");
        }

        [Test]
        public void SaveAsJsonlAsync_NullEntries_CreatesEmptyFile()
        {
            string path = Path.Combine(_tempDir, "empty.jsonl");
            _serializerNoMask.SaveAsJsonlAsync(null, path).GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(path), "null 배열도 빈 파일을 생성해야 합니다.");
            string content = File.ReadAllText(path);
            Assert.AreEqual(string.Empty, content);
        }

        [Test]
        public void SaveAsJsonlAsync_CreatesDirectoryIfNotExists()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "디렉토리 생성", "") };
            string nestedPath = Path.Combine(_tempDir, "nested", "deep", "logs.jsonl");

            _serializerNoMask.SaveAsJsonlAsync(entries, nestedPath).GetAwaiter().GetResult();

            Assert.IsTrue(File.Exists(nestedPath), "디렉토리 자동 생성 후 파일이 존재해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 기존 Serialize / SaveAsync 가 영향 받지 않음을 확인 (회귀 방지)
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void ExistingSerialize_StillWorks_AfterJsonlAddition()
        {
            // 기존 텍스트 직렬화 메서드 동작 보존 확인
            var entries = new[] { new LogEntry(1.0, LogType.Log, "기존 TXT", "") };
            string result = _serializerNoMask.Serialize(entries);
            StringAssert.Contains("기존 TXT", result, "기존 Serialize() 는 변경되지 않아야 합니다.");
            StringAssert.Contains("Rekon", result, "기존 헤더 포함 확인.");
        }

        [Test]
        public void ExistingSaveAsync_StillWorks_AfterJsonlAddition()
        {
            var entries = new[] { new LogEntry(1.0, LogType.Log, "TXT 저장", "") };
            string txtPath = Path.Combine(_tempDir, "existing_logs.txt");

            _serializerNoMask.SaveAsync(entries, txtPath).GetAwaiter().GetResult();

            Assert.IsTrue(File.Exists(txtPath), "기존 SaveAsync() 는 .txt 파일을 생성해야 합니다.");
            string content = File.ReadAllText(txtPath);
            StringAssert.Contains("TXT 저장", content);
        }
    }
}
