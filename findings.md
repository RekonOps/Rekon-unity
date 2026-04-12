# 발견사항

## 2026-04-02

- `StreamingVideoRecorder`는 `_needsVFlip = (encoder != "h264_videotoolbox")`로 방향 보정 여부를 인코더 이름에 묶고 있다.
- `FrameCapturer`의 스트리밍 경로는 `AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGBA32, ...)` 결과를 그대로 FFmpeg stdin으로 넘긴다.
- `ScreenshotCapturer`는 같은 `CaptureScreenshotIntoRenderTexture` 기반이지만 PNG 인코딩 전에 항상 CPU 행 스왑을 수행한다.
- 현재 증상은 “Mac에서는 vflip 없이 정상, Windows에서는 여러 뒤집기 시도 모두 실패”이므로, 문제 축은 FFmpeg 필터보다 입력 버퍼의 실제 메모리 방향 또는 경로 차이일 가능성이 높다.
- 실제 런타임은 `RekonBootstrap`에서 `StreamingVideoRecorder`를 생성해 `FrameCapturer.Initialize(..., streamingRecorder)`로 주입하므로, Windows 영상은 스트리밍 경로가 맞다.
- `CaptureOrchestrator`도 `_streamingRecorder.StopAndExtractAsync()`를 우선 사용하므로, 레거시 `Mp4VideoEncoder`는 현재 증상의 주 경로가 아니다.
- 코드상 행 반전 보정은 `StreamingVideoRecorder.WriterLoop()` 또는 사용자가 시도한 `FrameCapturer` 단계에만 존재한다. 즉, 캡처 API와 FFmpeg 사이의 버퍼 방향을 잘못 모델링하면 어느 보정도 일관되게 맞지 않는다.
- `FrameCapturer`는 영상만 `MaxVideoHeight = 1080` 상한을 적용해 스크린샷과 다른 크기의 `RenderTexture`를 쓸 수 있다. 스크린샷 정상 여부를 그대로 영상 경로에 대입하면 오판 가능성이 있다.
- 코드 어디에도 `SystemInfo.graphicsUVStartsAtTop`, `graphicsDeviceType` 같은 그래픽스 백엔드 기반 판단이 없다. 방향 판정은 전적으로 인코더 이름(`h264_videotoolbox` 예외)과 하드코딩 가정에 의존한다.
