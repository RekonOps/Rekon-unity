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

## 본 repo 핵심 키워드 (간단 인덱스)

> 정식 정의는 위 glossary 파일 참조. 본 인덱스는 빠른 navigation 용.

- **Unity 2021.3+** — 최소 지원 버전
- **UPM (Unity Package Manager)** — 패키지 배포 형식, `package.json` + Samples~
- **Namespace** — `RekonOps.Rekon`
- **Log prefix** — `[Rekon]`
- **Async** — `async/await` + `Task` (UniTask 사용 X)
- **ScriptableObject** — 설정 기반 (RekonSettings)
- **Editor/** — 에디터 전용 (Editor namespace, EditorWindow, AssetPostprocessor)
- **Runtime/** — 런타임 코드 (MonoBehaviour, ScriptableObject)
- **Performance Timeline (T1~T7)** — 성능 수집 7종 (CPU/GPU/Mem/FPS/Battery/Net/Custom)
- **AsyncGPUReadback** — 스크린샷/영상 캡처 비동기 GPU 읽기
- **FFmpeg** — 영상 인코딩 (별도 설치 가이드 v0.2.11)
- **IL2CPP** — Dev Build 빌드 호환성 보장 (Lovable 포인트)
- **Inspector Snapshot / Prefab Diff** — Editor 전용 Lovable 기능 (로드맵)
- **GitHub Actions + Release** — 패키지 배포 자동화

> Phase 2 spec 작성 완료 후 본 인덱스를 자동 갱신합니다.
