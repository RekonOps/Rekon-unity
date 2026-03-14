# BugBeacon Unity 캡처 엔진 아키텍처 설계

## 1. 개요

캡처 엔진은 개발자가 핫키를 누르는 순간 스크린샷, 로그, 상태 정보, 영상(링버퍼)을 동시에 수집하여
하나의 번들로 묶어 반환하는 파이프라인입니다.

---

## 2. 캡처 파이프라인 클래스 다이어그램

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         RekonOps.BugBeacon                           │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                     Input Layer                                  │   │
│  │                                                                  │   │
│  │  <<interface>>          <<interface>>                            │   │
│  │  IHotkeyProvider        (감지 전략)                               │   │
│  │  + IsTriggered(): bool  ┌────────────────────────┐              │   │
│  │         △               │ LegacyInputProvider     │              │   │
│  │         │               │ + IsTriggered(): bool   │              │   │
│  │         │               │ (Input.GetKeyDown)      │              │   │
│  │         │               └────────────────────────┘              │   │
│  │         │               ┌────────────────────────┐              │   │
│  │         └───────────────│ NewInputSystemProvider  │              │   │
│  │                         │ + IsTriggered(): bool   │              │   │
│  │                         │ (#if ENABLE_INPUT_SYSTEM)│              │   │
│  │                         └────────────────────────┘              │   │
│  │                                                                  │   │
│  │  ┌──────────────────────────────────────────────────────────┐   │   │
│  │  │ HotkeyManager : MonoBehaviour                             │   │   │
│  │  │ - _provider: IHotkeyProvider                              │   │   │
│  │  │ - _settings: BugBeaconSettings                          │   │   │
│  │  │ + event OnCaptureTrigger: Action                          │   │   │
│  │  │ + Update(): void   (Play Mode 한정)                        │   │   │
│  │  └──────────────────────────────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    Capture Layer                                  │   │
│  │                                                                  │   │
│  │  <<interface>>                  <<interface>>                    │   │
│  │  IScreenshotCapturer            ILogCollector                    │   │
│  │  + CaptureAsync(): Task<byte[]> + GetEntries(): LogEntry[]       │   │
│  │         △                              △                         │   │
│  │         │                              │                         │   │
│  │  ScreenshotCapturer             LogRingBuffer                    │   │
│  │  + CaptureAsync(): Task<byte[]> + Add(LogEntry): void            │   │
│  │  (CaptureScreenshotAsTexture)   + GetEntries(): LogEntry[]       │   │
│  │  (EncodeToPNG)                  (순환 배열, head/tail)            │   │
│  │  (ThreadPool 비동기 저장)         (Application.logMessageReceived)│   │
│  │                                                                  │   │
│  │  <<struct>>                                                      │   │
│  │  LogEntry                                                        │   │
│  │  + timestamp: double                                             │   │
│  │  + logType: LogType                                              │   │
│  │  + message: string                                               │   │
│  │  + stackTrace: string                                            │   │
│  │                                                                  │   │
│  │  LogSerializer                                                   │   │
│  │  + Serialize(LogEntry[]): string                                 │   │
│  │  + SaveAsync(entries, path): Task                                │   │
│  │  (System.IO.Compression.ZipArchive)                              │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    State Layer                                    │   │
│  │                                                                  │   │
│  │  <<interface>>                                                   │   │
│  │  IStateSnapshotCollector                                         │   │
│  │  + CollectAsync(): Task<StateSnapshot>                           │   │
│  │         △                                                        │   │
│  │         │                                                        │   │
│  │  StateSnapshotCollector                                          │   │
│  │  + CollectAsync(): Task<StateSnapshot>                           │   │
│  │  (SystemInfo, Application, SceneManager, Time, Screen)           │   │
│  │  (ContextProviderRegistry 참조)                                  │   │
│  │                                                                  │   │
│  │  StateSnapshot                      BugBeaconContext           │   │
│  │  + engine: string                   + Add(key, value): void      │   │
│  │  + engine_version: string           + Remove(key): void          │   │
│  │  + app_version: string              + Clear(): void              │   │
│  │  + platform: string                 - _context: Dictionary<>     │   │
│  │  + device: string                                                │   │
│  │  + scene: string                    <<interface>>                │   │
│  │  + fps: float                       IContextProvider             │   │
│  │  + custom_context: Dictionary<>     + GetContext(): Dictionary<> │   │
│  │  (JsonUtility 직렬화 가능)                                        │   │
│  │                                     ContextProviderRegistry      │   │
│  │                                     + Register(IContextProvider) │   │
│  │                                     + Unregister(IContextProvider│   │
│  │                                     + CollectAll(): Dictionary<> │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                     Video Layer                                   │   │
│  │                                                                  │   │
│  │  <<interface>>                  <<interface>>                    │   │
│  │  IFrameCapturer                 IVideoEncoder                    │   │
│  │  + StartCapturing(): void       + EncodeAsync(frames, path):     │   │
│  │  + StopCapturing(): void            Task                         │   │
│  │         △                              △                         │   │
│  │         │                              │                         │   │
│  │  FrameCapturer                  VideoEncoder                     │   │
│  │  + StartCapturing(): void       + EncodeAsync(): Task            │   │
│  │  + StopCapturing(): void        (PNG 시퀀스 저장 MVP)            │   │
│  │  (RenderTexture, Camera.main)   (향후 FFmpeg 교체 가능)           │   │
│  │  (AsyncGPUReadback.Request)                                      │   │
│  │  (Time.unscaledTime 스로틀링)                                     │   │
│  │                                                                  │   │
│  │  FrameData                      FrameRingBuffer : IDisposable    │   │
│  │  + data: byte[]                 + Add(FrameData): void           │   │
│  │  + width: int                   + GetFrames(): FrameData[]       │   │
│  │  + height: int                  (NativeArray<byte> 풀링)         │   │
│  │  + timestamp: double            (capacity = fps * bufferSeconds)  │   │
│  │                                                                  │   │
│  │  FramePool                      VideoSegmentWriter               │   │
│  │  + Rent(size): NativeArray<byte>+ 30초 단위 슬라이딩 윈도우        │   │
│  │  + Return(array): void          + 최대 2세그먼트 유지 (총 60초)    │   │
│  │                                 + FlushSegment(): void           │   │
│  │                                                                  │   │
│  │  VideoEncoderConfig                                              │   │
│  │  + width, height, fps, bitrate                                   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    Core / Orchestration Layer                     │   │
│  │                                                                  │   │
│  │  <<interface>>                                                   │   │
│  │  ICaptureOrchestrator                                            │   │
│  │  + StartAsync(): Task<CaptureResult>                             │   │
│  │         △                                                        │   │
│  │         │                                                        │   │
│  │  CaptureOrchestrator                                             │   │
│  │  - _screenshot: IScreenshotCapturer                              │   │
│  │  - _log: ILogCollector                                           │   │
│  │  - _state: IStateSnapshotCollector                               │   │
│  │  - _video: IFrameCapturer                                        │   │
│  │  - _encoder: IVideoEncoder                                       │   │
│  │  + StartAsync(): Task<CaptureResult>                             │   │
│  │  (Task.WhenAll 병렬 수집)                                         │   │
│  │  (5초 타임아웃)                                                   │   │
│  │  (CaptureProgressEvent 발행)                                      │   │
│  │                                                                  │   │
│  │  CaptureResult                  CaptureProgressEvent             │   │
│  │  + screenshotPath: string       + stage: string                  │   │
│  │  + logsPath: string             + progress: float (0~1)          │   │
│  │  + statePath: string                                             │   │
│  │  + videoPath: string                                             │   │
│  │  + timestamp: DateTime                                           │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                   Settings Layer                                  │   │
│  │                                                                  │   │
│  │  BugBeaconSettings : ScriptableObject                          │   │
│  │  + captureHotkey: KeyCode                                        │   │
│  │  + screenshotDownscale: int                                      │   │
│  │  + videoEnabled: bool                                            │   │
│  │  + videoWidth/Height/Fps: int                                    │   │
│  │  + videoBufferSeconds: int                                       │   │
│  │  + logBufferSize: int                                            │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. 핫키 → 캡처 → 번들 생성 시퀀스 다이어그램

```
사용자          HotkeyManager      CaptureOrchestrator    병렬 서브시스템
  │                  │                      │
  │ [F12 누름]        │                      │
  │──────────────────►                      │
  │                  │ OnCaptureTrigger()   │
  │                  │─────────────────────►│
  │                  │                      │ (타임아웃: 5초)
  │                  │                      │
  │                  │                      │◄───────────────────────┐
  │                  │                      │  Task.WhenAll() 시작   │
  │                  │                      │                        │
  │                  │                      ├──[A]───────────────────►  ScreenshotCapturer
  │                  │                      │  CaptureAsync()           CaptureScreenshotAsTexture()
  │                  │                      │                           EncodeToPNG()
  │                  │                      │                           File.WriteAllBytesAsync()
  │                  │                      │
  │                  │                      ├──[B]───────────────────►  LogRingBuffer
  │                  │                      │  GetEntries()             GetEntries() → LogEntry[]
  │                  │                      │  LogSerializer.SaveAsync  ZipArchive 압축
  │                  │                      │
  │                  │                      ├──[C]───────────────────►  StateSnapshotCollector
  │                  │                      │  CollectAsync()           SystemInfo 수집
  │                  │                      │                           ContextProviderRegistry.CollectAll()
  │                  │                      │                           JsonUtility.ToJson()
  │                  │                      │
  │                  │                      ├──[D]───────────────────►  FrameRingBuffer + VideoEncoder
  │                  │                      │  GetFrames()              GetFrames() → FrameData[]
  │                  │                      │  EncodeAsync()            PNG 시퀀스 저장 (Task.Run)
  │                  │                      │
  │                  │            ProgressEvent(stage="screenshot", 0.25)
  │                  │            ProgressEvent(stage="logs", 0.50)
  │                  │            ProgressEvent(stage="state", 0.75)
  │                  │            ProgressEvent(stage="video", 1.0)
  │                  │                      │
  │                  │                      │ [A][B][C][D] 완료
  │                  │                      │◄───────────────────────┘
  │                  │                      │
  │                  │         CaptureResult│
  │◄─────────────────────────────────────── │
  │   {screenshotPath, logsPath,            │
  │    statePath, videoPath, timestamp}     │
  │                  │                      │
```

---

## 4. 인터페이스 정의

### 4.1 ICaptureProvider (= IScreenshotCapturer)

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 스크린샷 캡처 전략 인터페이스.
    /// 플랫폼별 구현체로 교체 가능.
    /// </summary>
    public interface IScreenshotCapturer
    {
        /// <summary>
        /// 현재 프레임을 PNG 바이트로 캡처하여 반환합니다.
        /// </summary>
        System.Threading.Tasks.Task<byte[]> CaptureAsync();
    }
}
```

### 4.2 IContextProvider

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 커스텀 K/V 컨텍스트 데이터를 제공하는 인터페이스.
    /// 게임 코드에서 등록하여 상태 스냅샷에 포함시킵니다.
    /// </summary>
    public interface IContextProvider
    {
        /// <summary>
        /// 현재 컨텍스트 데이터를 Dictionary 형태로 반환합니다.
        /// Key 충돌 시 나중에 등록된 프로바이더가 우선합니다.
        /// </summary>
        System.Collections.Generic.Dictionary<string, string> GetContext();
    }
}
```

### 4.3 IHotkeyProvider

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 핫키 입력 감지 전략 인터페이스.
    /// Legacy Input / New Input System 중 하나를 주입합니다.
    /// </summary>
    public interface IHotkeyProvider
    {
        /// <summary>
        /// 이번 프레임에 캡처 핫키가 눌렸는지 반환합니다.
        /// </summary>
        bool IsTriggered(UnityEngine.KeyCode key);
    }
}
```

### 4.4 ILogCollector

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 로그 수집기 인터페이스.
    /// </summary>
    public interface ILogCollector
    {
        /// <summary>
        /// 현재 버퍼에 저장된 로그를 시간순으로 반환합니다.
        /// </summary>
        LogEntry[] GetEntries();
    }
}
```

### 4.5 IStateSnapshotCollector

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 시스템/애플리케이션 상태 스냅샷 수집 인터페이스.
    /// </summary>
    public interface IStateSnapshotCollector
    {
        /// <summary>
        /// 현재 시점의 상태를 비동기로 수집하여 반환합니다.
        /// </summary>
        System.Threading.Tasks.Task<StateSnapshot> CollectAsync();
    }
}
```

### 4.6 IFrameCapturer

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 프레임 캡처 인터페이스.
    /// GPU 읽기 전략(AsyncGPUReadback / ReadPixels)을 추상화합니다.
    /// </summary>
    public interface IFrameCapturer
    {
        void StartCapturing();
        void StopCapturing();
    }
}
```

### 4.7 IVideoEncoder

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 영상 인코더 인터페이스.
    /// MVP는 PNG 시퀀스, 향후 FFmpeg/MediaFoundation으로 교체 가능.
    /// </summary>
    public interface IVideoEncoder
    {
        System.Threading.Tasks.Task EncodeAsync(
            FrameData[] frames,
            string outputPath,
            VideoEncoderConfig config);
    }
}
```

### 4.8 ICaptureOrchestrator

```csharp
namespace RekonOps.BugBeacon
{
    /// <summary>
    /// 캡처 전체 파이프라인 오케스트레이터 인터페이스.
    /// </summary>
    public interface ICaptureOrchestrator
    {
        /// <summary>
        /// 모든 서브시스템에서 병렬로 데이터를 수집하고 결과를 반환합니다.
        /// </summary>
        System.Threading.Tasks.Task<CaptureResult> StartAsync();
    }
}
```

---

## 5. 데이터 흐름 요약

| 단계 | 컴포넌트 | 입력 | 출력 |
|------|---------|------|------|
| 1. 트리거 | HotkeyManager | 키 입력 | OnCaptureTrigger 이벤트 |
| 2. 스크린샷 | ScreenshotCapturer | - | PNG 파일 (byte[]) |
| 3. 로그 수집 | LogRingBuffer → LogSerializer | Application.log | logs.zip |
| 4. 상태 수집 | StateSnapshotCollector | SystemInfo/Time/Scene | state.json |
| 5. 영상 인코딩 | FrameRingBuffer → VideoEncoder | RenderTexture 스트림 | frames/ 디렉토리 |
| 6. 결과 조합 | CaptureOrchestrator | 위 결과들 | CaptureResult |

---

## 6. 디렉토리 구조

```
Runtime/
  Input/
    IHotkeyProvider.cs
    HotkeyManager.cs
    LegacyInputProvider.cs
    NewInputSystemProvider.cs
  Capture/
    IScreenshotCapturer.cs
    ScreenshotCapturer.cs
    LogEntry.cs
    ILogCollector.cs
    LogRingBuffer.cs
    LogSerializer.cs
    StateSnapshot.cs
    IStateSnapshotCollector.cs
    StateSnapshotCollector.cs
    BugBeaconContext.cs
    IContextProvider.cs
    ContextProviderRegistry.cs
  Video/
    FrameData.cs
    IFrameCapturer.cs
    FrameCapturer.cs
    FrameRingBuffer.cs
    FramePool.cs
    IVideoEncoder.cs
    VideoEncoderConfig.cs
    VideoEncoder.cs
    VideoSegmentWriter.cs
  Core/
    CaptureResult.cs
    CaptureProgressEvent.cs
    ICaptureOrchestrator.cs
    CaptureOrchestrator.cs
Tests/
  Runtime/
    Input/
      HotkeyManagerTests.cs
    Capture/
      ScreenshotCapturerTests.cs
      LogRingBufferTests.cs
      LogSerializerTests.cs
      StateSnapshotCollectorTests.cs
      ContextProviderTests.cs
    Video/
      FrameCapturerTests.cs
      FrameRingBufferTests.cs
      VideoEncoderTests.cs
    Core/
      CaptureOrchestratorTests.cs
```
