# Rekon BasicDemo 샘플

Rekon의 핵심 플로우 — **"게임 상태를 로그로 남기고 `Rekon.Capture()`로 리포트한다"** — 를 보여주는 예제입니다.

평소 쓰는 `Debug.Log` 가 그대로 캡처되어 웹 리포트에 담기므로, 별도의 컨텍스트 API를 배울 필요가 없습니다.

---

## 포함된 파일

| 파일 | 설명 |
|------|------|
| `Scripts/SampleBugReporter.cs` | 게임 상태를 로그로 남기고 `Rekon.Capture()` 를 트리거하는 예제 |

---

## 가져오기

1. `Window` → `Package Manager` 열기
2. 목록에서 **Rekon** 패키지 선택
3. 우측 `Samples` 탭 → **BasicDemo** 옆 `Import` 클릭
4. `Assets/Samples/Rekon/<버전>/BasicDemo/` 에 복사됩니다

---

## 사용 방법

1. 씬의 빈 게임 오브젝트에 `SampleBugReporter` 컴포넌트를 추가합니다.
2. Play Mode 진입 후, 다음 중 하나로 캡처를 트리거합니다:
   - Inspector에서 컴포넌트 우클릭 → **Report Bug Now**
   - UI 버튼 `OnClick` 또는 게임 코드에서 `ReportBug("제목")` 호출
   - (또는 `Project Settings > Rekon` 의 **내장 캡처 핫키**)
3. 웹 대시보드의 리포트에서, 컴포넌트가 남긴 `Debug.Log` 들이 **로그 패널에 그대로** 보입니다.
   team_pro 플랜이면 영상/스크린샷과 **시간 동기화**되어 표시됩니다.

> **필요 조건**: 실제 전송에는 Rekon 설정(`Project Settings > Rekon` 의 라이선스 키 등)이 있어야 합니다.
> 미설정 시에는 콘솔 로그/캡처 흐름만 동작하고 업로드는 되지 않습니다.

---

## 핵심 개념

### 1) 콘솔 로그는 자동으로 캡처됩니다

```csharp
// 평소처럼 게임 상태를 로그로 남기면 됩니다.
Debug.Log($"level={level}, score={score}, hp={hp}");
```

별도 API 없이, 이 로그들이 버그 리포트에 그대로 포함되어 웹에서 보입니다
(team_pro 리플레이에서는 영상과 시간 동기화).

### 2) 코드에서 캡처 트리거

```csharp
using RekonSdk = RekonOps.Rekon.Rekon;

// 영상/스크린샷/로그를 자동 수집해 웹 대시보드로 전송
RekonSdk.Capture("플레이어 사망");
```

게임 내 이벤트(사망, 예외, 특정 조건 등)에서 호출하면, 그 순간의 상태가 리포트로 남습니다.
사용자 입력(핫키)을 통한 수동 캡처는 Rekon 내장 핫키를 사용할 수도 있습니다.
