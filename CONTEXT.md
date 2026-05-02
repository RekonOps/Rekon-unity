# Context — Rekon-unity

> 도메인 글로서리와 ADR 은 Rekon-Context repo 에 단일 출처로 관리합니다.
> 본 repo 의 도메인 정보는 다음 위치를 우선 참조하세요.

## 도메인 글로서리

- 공통: `~/IdeaProjects/Rekon-Context/spec/shared/glossary-2026-05-02-ver.md`
- 본 repo 전용: `~/IdeaProjects/Rekon-Context/spec/unity/glossary-2026-05-02-ver.md`

## 활성 스펙

- `~/IdeaProjects/Rekon-Context/spec/unity/*.md`

## ADR

- `~/IdeaProjects/Rekon-Context/decisions.md` (ADR-### 형식)

## 본 repo spec 파일 (2026-05-02 ver, 7개)

| Spec | 파일 경로 |
|------|----------|
| Glossary | `~/IdeaProjects/Rekon-Context/spec/unity/glossary-2026-05-02-ver.md` |
| Architecture (88파일/33,090 LOC) | `~/IdeaProjects/Rekon-Context/spec/unity/architecture-2026-05-02-ver.md` |
| Capture Tech | `~/IdeaProjects/Rekon-Context/spec/unity/capture-tech-2026-05-02-ver.md` |
| Data Collection Strategy | `~/IdeaProjects/Rekon-Context/spec/unity/data-collection-strategy-2026-05-02-ver.md` |
| Video Pipeline | `~/IdeaProjects/Rekon-Context/spec/unity/video-pipeline-2026-05-02-ver.md` |
| Performance Timeline (11 필드) | `~/IdeaProjects/Rekon-Context/spec/unity/performance-timeline-2026-05-02-ver.md` |
| Test Strategy (Editor/Integration/Runtime) | `~/IdeaProjects/Rekon-Context/spec/unity/test-strategy-2026-05-02-ver.md` |

## 빠른 키워드 인덱스 (간단)

- **Unity 2022.3+** — 최소 지원 버전 (current 0.2.14)
- **UPM (Unity Package Manager)** — 패키지 배포 형식, `package.json` + Samples~
- **Namespace** — `RekonOps.Rekon`
- **Log prefix** — `[Rekon]`
- **Async** — `async/await` + `Task` (UniTask 사용 X)
- **ScriptableObject** — 설정 기반 (RekonSettings)
- **Editor/** — Editor 전용 (CrashRecovery, UI, RekonSettingsWindow)
- **Runtime/** — 런타임 (Auth/Bundle/Capture/Core/Performance/Video 등 14개 폴더)
- **Performance Timeline** — Runtime/Performance/ (5 파일), 11 필드 + 플랜별 분기 (free 3개 / team 이상 10개)
- **AsyncGPUReadback** — 스크린샷/영상 캡처 비동기 GPU 읽기
- **FFmpeg** — 영상 인코딩 (`Mp4VideoEncoder.cs`, h264_nvenc/videotoolbox/libx264 폴백)
- **IL2CPP** — Dev Build 빌드 호환성 보장 (Lovable 포인트)
- **GitHub Actions + Release** — 패키지 배포 자동화
