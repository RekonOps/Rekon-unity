# 진행 로그

## 2026-04-02

- 계획 파일 생성.
- `StreamingVideoRecorder`, `FrameCapturer`, `ScreenshotCapturer`, 관련 테스트 파일 확인 시작.
- 스트리밍 경로가 실제 영상 출력 경로임을 `RekonBootstrap`, `CaptureOrchestrator`에서 확인.
- 스크린샷과 영상이 서로 다른 방향 계약을 갖고 있음을 정리.
- Unity 공식 문서에서 `graphicsUVStartsAtTop` 존재를 확인해, 방향 차이가 인코더가 아니라 그래픽스 백엔드 축이라는 점을 뒷받침.
