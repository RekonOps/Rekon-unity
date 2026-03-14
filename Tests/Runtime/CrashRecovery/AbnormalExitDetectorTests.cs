using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// AbnormalExitDetector 단위 테스트.
    /// 비정상 종료 감지 플래그 파일 생성/삭제/조회를 검증합니다.
    /// </summary>
    [TestFixture]
    public class AbnormalExitDetectorTests
    {
        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [TearDown]
        public void TearDown()
        {
            // 테스트 후 플래그 파일 정리
            AbnormalExitDetector.DeleteFlagFile();
        }

        // ──────────────────────────────────────────────────────────────
        // 플래그 파일 경로 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void FlagFilePath_IsUnderPersistentDataPath()
        {
            Assert.IsTrue(
                AbnormalExitDetector.FlagFilePath.StartsWith(Application.persistentDataPath),
                "플래그 파일 경로는 persistentDataPath 하위여야 합니다.");
        }

        [Test]
        public void FlagFilePath_ContainsFlagFileName()
        {
            Assert.IsTrue(
                AbnormalExitDetector.FlagFilePath.EndsWith(AbnormalExitDetector.FlagFileName),
                $"플래그 파일 경로는 '{AbnormalExitDetector.FlagFileName}'으로 끝나야 합니다.");
        }

        [Test]
        public void FlagFilePath_ContainsCrashRecoveryDir()
        {
            Assert.IsTrue(
                AbnormalExitDetector.FlagFilePath.Contains("crash_recovery"),
                "플래그 파일 경로는 'crash_recovery' 디렉토리를 포함해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // CreateFlagFile 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CreateFlagFile_CreatesFile()
        {
            // 사전 정리
            AbnormalExitDetector.DeleteFlagFile();

            AbnormalExitDetector.CreateFlagFile();

            Assert.IsTrue(
                File.Exists(AbnormalExitDetector.FlagFilePath),
                "CreateFlagFile() 호출 후 플래그 파일이 생성되어야 합니다.");
        }

        [Test]
        public void CreateFlagFile_FileContainsTimestamp()
        {
            AbnormalExitDetector.CreateFlagFile();

            string content = File.ReadAllText(AbnormalExitDetector.FlagFilePath);
            Assert.IsFalse(string.IsNullOrEmpty(content), "플래그 파일에 타임스탬프가 기록되어야 합니다.");
        }

        [Test]
        public void CreateFlagFile_CalledTwice_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                AbnormalExitDetector.CreateFlagFile();
                AbnormalExitDetector.CreateFlagFile();
            }, "CreateFlagFile()을 두 번 호출해도 예외가 없어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // DeleteFlagFile 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DeleteFlagFile_RemovesExistingFlag()
        {
            AbnormalExitDetector.CreateFlagFile();
            Assert.IsTrue(File.Exists(AbnormalExitDetector.FlagFilePath), "사전 조건: 플래그가 존재해야 합니다.");

            AbnormalExitDetector.DeleteFlagFile();

            Assert.IsFalse(
                File.Exists(AbnormalExitDetector.FlagFilePath),
                "DeleteFlagFile() 호출 후 플래그 파일이 삭제되어야 합니다.");
        }

        [Test]
        public void DeleteFlagFile_WhenNoFlag_DoesNotThrow()
        {
            // 플래그가 없는 상태에서 삭제 호출
            AbnormalExitDetector.DeleteFlagFile();

            Assert.DoesNotThrow(() => AbnormalExitDetector.DeleteFlagFile(),
                "플래그가 없을 때 DeleteFlagFile()을 호출해도 예외가 없어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // WasPreviousSessionAbnormal 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void WasPreviousSessionAbnormal_WhenFlagExists_ReturnsTrue()
        {
            AbnormalExitDetector.CreateFlagFile();

            Assert.IsTrue(
                AbnormalExitDetector.WasPreviousSessionAbnormal,
                "플래그 파일이 존재할 때 WasPreviousSessionAbnormal은 true여야 합니다.");
        }

        [Test]
        public void WasPreviousSessionAbnormal_WhenNoFlag_ReturnsFalse()
        {
            AbnormalExitDetector.DeleteFlagFile();

            Assert.IsFalse(
                AbnormalExitDetector.WasPreviousSessionAbnormal,
                "플래그 파일이 없을 때 WasPreviousSessionAbnormal은 false여야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // GetFlagTimestamp 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void GetFlagTimestamp_WhenFlagExists_ReturnsValidDateTime()
        {
            var beforeCreate = DateTime.UtcNow.AddSeconds(-1);
            AbnormalExitDetector.CreateFlagFile();
            var afterCreate = DateTime.UtcNow.AddSeconds(1);

            var timestamp = AbnormalExitDetector.GetFlagTimestamp();

            Assert.IsNotNull(timestamp, "플래그 파일이 있을 때 타임스탬프가 반환되어야 합니다.");
            Assert.IsTrue(
                timestamp.Value >= beforeCreate && timestamp.Value <= afterCreate,
                $"타임스탬프({timestamp.Value})가 생성 시간 범위 내여야 합니다.");
        }

        [Test]
        public void GetFlagTimestamp_WhenNoFlag_ReturnsNull()
        {
            AbnormalExitDetector.DeleteFlagFile();

            var timestamp = AbnormalExitDetector.GetFlagTimestamp();

            Assert.IsNull(timestamp, "플래그 파일이 없을 때 null이 반환되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 크래시 시뮬레이션 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CrashSimulation_FlagSurvivesWithoutDelete()
        {
            // 크래시 시뮬레이션: 플래그 생성 후 DeleteFlagFile() 미호출
            AbnormalExitDetector.CreateFlagFile();

            // 다음 세션에서 WasPreviousSessionAbnormal로 감지
            Assert.IsTrue(
                AbnormalExitDetector.WasPreviousSessionAbnormal,
                "크래시(플래그 미삭제) 후 다음 시작 시 비정상 종료로 감지되어야 합니다.");
        }

        [Test]
        public void NormalExit_FlagDeletedBeforeCheck()
        {
            // 정상 종료 시뮬레이션: 플래그 생성 후 삭제
            AbnormalExitDetector.CreateFlagFile();
            AbnormalExitDetector.DeleteFlagFile();

            // 다음 세션에서는 정상 종료로 감지
            Assert.IsFalse(
                AbnormalExitDetector.WasPreviousSessionAbnormal,
                "정상 종료(플래그 삭제) 후 다음 시작 시 정상 종료로 감지되어야 합니다.");
        }
    }
}
