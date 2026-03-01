using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 크래시 복구를 위해 로그·상태·영상을 주기적으로 디스크에 플러시하는 MonoBehaviour.
    ///
    /// 3개의 독립 코루틴으로 동작합니다:
    ///   - 로그 플러시:  매 logFlushInterval초 (기본 5초)
    ///   - 상태 플러시:  매 stateFlushInterval초 (기본 10초)
    ///   - 영상 플러시:  매 videoFlushInterval초 (기본 30초)
    ///
    /// 플러시 디렉토리: {persistentDataPath}/BugOneTouch/crash_recovery/active/
    ///
    /// 특징:
    ///   - DontDestroyOnLoad로 씬 전환에도 지속
    ///   - Application.isPlaying 체크로 Play Mode에서만 동작
    ///   - OnDestroy에서 리소스 정리 (IDisposable 패턴)
    /// </summary>
    public class PeriodicFlushManager : MonoBehaviour, IDisposable
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>플러시 데이터 저장 디렉토리명</summary>
        public const string ActiveDirName = "active";

        /// <summary>로그 플러시 파일명</summary>
        public const string LogsFlushFileName = "logs_flush.zip";

        /// <summary>상태 플러시 파일명</summary>
        public const string StateFlushFileName = "state_flush.json";

        /// <summary>영상 플러시 디렉토리명</summary>
        public const string VideoFlushDirName = "video_flush";

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private BugOneTouchSettings _settings;
        private LogRingBuffer _logBuffer;
        private LogSerializer _logSerializer;
        private IStateSnapshotCollector _stateCollector;
        private VideoSegmentWriter _videoWriter;

        private MappedFileWriter _fileWriter;

        private bool _initialized;
        private bool _disposed;

        // 코루틴 핸들 (중복 실행 방지용)
        private Coroutine _logCoroutine;
        private Coroutine _stateCoroutine;
        private Coroutine _videoCoroutine;

        // ──────────────────────────────────────────────────────────────
        // 공개 프로퍼티
        // ──────────────────────────────────────────────────────────────

        /// <summary>플러시 데이터 저장 루트 디렉토리 경로</summary>
        public static string CrashRecoveryDir =>
            Path.Combine(Application.persistentDataPath, "BugOneTouch", "crash_recovery");

        /// <summary>active/ 디렉토리 경로 (플러시 데이터 저장 위치)</summary>
        public static string ActiveDir =>
            Path.Combine(CrashRecoveryDir, ActiveDirName);

        /// <summary>마지막 로그 플러시 시각 (UTC)</summary>
        public DateTime LastLogFlushTime { get; private set; }

        /// <summary>마지막 상태 플러시 시각 (UTC)</summary>
        public DateTime LastStateFlushTime { get; private set; }

        /// <summary>마지막 영상 플러시 시각 (UTC)</summary>
        public DateTime LastVideoFlushTime { get; private set; }

        // ──────────────────────────────────────────────────────────────
        // 초기화
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// PeriodicFlushManager를 초기화합니다.
        /// Initialize() 호출 전에는 플러시가 시작되지 않습니다.
        /// </summary>
        /// <param name="settings">Bug-OneTouch 설정</param>
        /// <param name="logBuffer">로그 링버퍼</param>
        /// <param name="logSerializer">로그 직렬화기</param>
        /// <param name="stateCollector">상태 스냅샷 수집기</param>
        /// <param name="videoWriter">영상 세그먼트 쓰기 (null 허용)</param>
        public void Initialize(
            BugOneTouchSettings settings,
            LogRingBuffer logBuffer,
            LogSerializer logSerializer,
            IStateSnapshotCollector stateCollector,
            VideoSegmentWriter videoWriter = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
            _logSerializer = logSerializer ?? throw new ArgumentNullException(nameof(logSerializer));
            _stateCollector = stateCollector ?? throw new ArgumentNullException(nameof(stateCollector));
            _videoWriter = videoWriter; // null 허용

            _fileWriter = new MappedFileWriter();

            // active/ 디렉토리 생성
            Directory.CreateDirectory(ActiveDir);

            _initialized = true;

            Debug.Log($"[BugOneTouch] PeriodicFlushManager 초기화 완료. 플러시 경로: {ActiveDir}");
        }

        // ──────────────────────────────────────────────────────────────
        // Unity 라이프사이클
        // ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!_initialized)
            {
                Debug.LogWarning("[BugOneTouch] PeriodicFlushManager: Initialize() 호출 없이 Start()가 실행됐습니다.");
                return;
            }

            StartFlushCoroutines();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        // ──────────────────────────────────────────────────────────────
        // 플러시 코루틴 시작
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 3개의 플러시 코루틴을 시작합니다.
        /// </summary>
        public void StartFlushCoroutines()
        {
            if (!_initialized)
                return;

            if (_logCoroutine == null)
                _logCoroutine = StartCoroutine(LogFlushCoroutine());

            if (_stateCoroutine == null)
                _stateCoroutine = StartCoroutine(StateFlushCoroutine());

            if (_videoCoroutine == null && _videoWriter != null)
                _videoCoroutine = StartCoroutine(VideoFlushCoroutine());
        }

        /// <summary>
        /// 모든 플러시 코루틴을 중단합니다.
        /// </summary>
        public void StopFlushCoroutines()
        {
            if (_logCoroutine != null)
            {
                StopCoroutine(_logCoroutine);
                _logCoroutine = null;
            }

            if (_stateCoroutine != null)
            {
                StopCoroutine(_stateCoroutine);
                _stateCoroutine = null;
            }

            if (_videoCoroutine != null)
            {
                StopCoroutine(_videoCoroutine);
                _videoCoroutine = null;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 코루틴 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 로그 플러시 코루틴. logFlushInterval초마다 실행됩니다.
        /// LogRingBuffer의 현재 항목을 ZIP으로 압축하여 디스크에 저장합니다.
        /// </summary>
        private IEnumerator LogFlushCoroutine()
        {
            while (!_disposed && Application.isPlaying)
            {
                float interval = _settings != null ? _settings.logFlushInterval : 5f;
                yield return new WaitForSecondsRealtime(interval);

                if (!Application.isPlaying || _disposed)
                    yield break;

                // Task를 시작하고 완료까지 대기
                Task flushTask = FlushLogsAsync();
                yield return new WaitUntil(() => flushTask.IsCompleted);
            }
        }

        /// <summary>
        /// 상태 플러시 코루틴. stateFlushInterval초마다 실행됩니다.
        /// StateSnapshotCollector로 수집한 스냅샷을 JSON으로 저장합니다.
        /// </summary>
        private IEnumerator StateFlushCoroutine()
        {
            while (!_disposed && Application.isPlaying)
            {
                float interval = _settings != null ? _settings.stateFlushInterval : 10f;
                yield return new WaitForSecondsRealtime(interval);

                if (!Application.isPlaying || _disposed)
                    yield break;

                Task flushTask = FlushStateAsync();
                yield return new WaitUntil(() => flushTask.IsCompleted);
            }
        }

        /// <summary>
        /// 영상 플러시 코루틴. videoFlushInterval초마다 실행됩니다.
        /// VideoSegmentWriter를 통해 현재 프레임 버퍼를 세그먼트로 저장합니다.
        /// </summary>
        private IEnumerator VideoFlushCoroutine()
        {
            while (!_disposed && Application.isPlaying && _videoWriter != null)
            {
                float interval = _settings != null ? _settings.videoFlushInterval : 30f;
                yield return new WaitForSecondsRealtime(interval);

                if (!Application.isPlaying || _disposed)
                    yield break;

                Task flushTask = FlushVideoAsync();
                yield return new WaitUntil(() => flushTask.IsCompleted);
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 플러시 구현 (비동기)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 LogRingBuffer의 항목을 ZIP으로 압축하여 active/logs_flush.zip에 저장합니다.
        /// </summary>
        public async Task FlushLogsAsync()
        {
            if (_disposed || _logBuffer == null || _logSerializer == null)
                return;

            try
            {
                var entries = _logBuffer.GetEntries();
                string destPath = Path.Combine(ActiveDir, LogsFlushFileName);

                await _logSerializer.SaveAsync(entries, destPath);

                LastLogFlushTime = DateTime.UtcNow;
                Debug.Log($"[BugOneTouch] 로그 플러시 완료: {entries.Length}개 항목 → {destPath}");
            }
            catch (Exception ex)
            {
                // 플러시 실패는 게임에 영향을 주지 않음
                Debug.LogWarning($"[BugOneTouch] 로그 플러시 실패 (무시): {ex.Message}");
            }
        }

        /// <summary>
        /// 현재 상태 스냅샷을 수집하여 active/state_flush.json에 저장합니다.
        /// </summary>
        public async Task FlushStateAsync()
        {
            if (_disposed || _stateCollector == null)
                return;

            try
            {
                var snapshot = await _stateCollector.CollectAsync();
                string json = JsonUtility.ToJson(snapshot, prettyPrint: false);
                string destPath = Path.Combine(ActiveDir, StateFlushFileName);

                bool ok = await _fileWriter.WriteTextAsync(destPath, json);

                if (ok)
                {
                    LastStateFlushTime = DateTime.UtcNow;
                    Debug.Log($"[BugOneTouch] 상태 플러시 완료 → {destPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 상태 플러시 실패 (무시): {ex.Message}");
            }
        }

        /// <summary>
        /// VideoSegmentWriter를 통해 현재 프레임 버퍼를 active/video_flush/에 저장합니다.
        /// </summary>
        public async Task FlushVideoAsync()
        {
            if (_disposed || _videoWriter == null)
                return;

            try
            {
                await _videoWriter.FlushSegmentAsync();

                LastVideoFlushTime = DateTime.UtcNow;
                Debug.Log("[BugOneTouch] 영상 플러시 완료");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 영상 플러시 실패 (무시): {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // IDisposable
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 모든 코루틴을 중단하고 리소스를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopFlushCoroutines();

            Debug.Log("[BugOneTouch] PeriodicFlushManager 정리 완료.");
        }

        // ──────────────────────────────────────────────────────────────
        // 정적 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// PeriodicFlushManager 인스턴스를 씬에 생성하고 반환합니다.
        /// DontDestroyOnLoad로 설정됩니다.
        /// </summary>
        public static PeriodicFlushManager CreateInstance()
        {
            var go = new GameObject("[BugOneTouch] PeriodicFlushManager");
            return go.AddComponent<PeriodicFlushManager>();
        }
    }
}
