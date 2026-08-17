# Rekon — Unity 버그 캡처 SDK

[English](./README.md) | **한국어**

### 버그가 터졌을 때, 이미 녹화 중이었다.

핫키를 누르는 순간, 직전 **~60초의 영상·로그·성능**이 이미 디스크에 있다.
롤링 버퍼로 항상 돌고 있기 때문에 — 버그가 터진 *뒤에* 눌러도 늦지 않는다.

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-1.0.0-brightgreen.svg)](CHANGELOG.md)

<!-- DEMO GIF (배포 전 교체 필수): 핫키 → 직전 60초 영상 + FPS 그래프 + Console 에러가 한 시점에 정렬되는 30초 캡처 -->
<!-- GIF 준비 전까지는 아래 스펙 텍스트가 fallback 으로 남는다 -->

> 플레이 모드에서 `Ctrl/Cmd + Shift + B` → 직전 ~60초 영상 + 스크린샷 + 로그 + 게임 상태(Scene/FPS/메모리)가
> 한 번에 캡처돼 웹 대시보드에 도착한다. Jira 이슈 등록은 대시보드에서 클릭 한 번.

```
# UPM Git URL (Package Manager > Add package from git URL...)
https://github.com/RekonOps/Rekon-unity.git#v1.0.0
```

---

## 이런 적 있나

비기술자 QA가 티켓에 이렇게 쓴다 — **"캐릭터가 이상해요."**

스크린샷 한 장과 그 한 줄을 들고, 당신은 추측으로 디버깅을 시작한다.
어느 씬이었는지, 그때 FPS가 떨어졌는지, Console에 뭐가 찍혔는지 — 아무것도 없이.
재현은 안 되고, 티켓은 "재현 불가"로 닫힌다.

Rekon은 추측으로 시작하던 디버깅을, **증거로 시작하는 디버깅**으로 바꾼다.

---

## 왜 다른가

기존 도구는 "버그가 터진 시점"을 다시 찾으려 한다. Rekon은 그 순간이 **이미 잡혀 있다**는 전제에서 출발한다.

### 1. 사후에 눌러도 직전 60초가 남는다

녹화 버튼이 없다. 플레이 모드 동안 롤링(링) 버퍼가 항상 직전 구간을 돌고 있다.
버그를 보고 *나서* 핫키를 눌러도, 직전 ~60초 영상이 이미 디스크에 있다. (기본 15fps · 1280×720)

> "녹화를 깜빡했다"가 구조적으로 불가능하다.

### 2. 영상과 성능이 같은 타임라인에 있다

캡처된 영상의 한 시점에 — 그 순간의 **FPS 급락**, **메모리 스파이크**, **Console 에러 한 줄**이 함께 정렬된다.
영상에서 "여기서 끊겼다" 싶은 프레임이, 그래프에서 무슨 일이 있었는지와 곧장 맞물린다.

"1/100으로 터지는 간헐 크래시", "특정 기기에서만"의 그 순간을 — 화면과 수치로 동시에 본다.

### 3. Unity의 맥락을 통째로

씬 이름, 디바이스 정보, Unity 버전, 프레임레이트, 직전 로그 — 캡처 시점의 게임 상태가 자동으로 함께 첨부된다.
범용 SDK가 모르는, Unity 플레이 모드만의 컨텍스트다.

---

## 안 보이는 것이 기능이다

Rekon은 평소엔 존재를 잊게 설계됐다. 상시 떠 있는 오버레이도, 봐야 할 대시보드도 게임 안에 없다.
핫키로만 소환되고, 그 외엔 조용히 직전 구간만 돌린다. 개발 흐름을 끊지 않는다.

---

## 설치

Unity에서 **Window > Package Manager > +  > Add package from git URL...** 에 입력:

```
https://github.com/RekonOps/Rekon-unity.git#v1.0.0
```

또는 `Packages/manifest.json`에 직접:

```json
{
  "dependencies": {
    "dev.rekonops.rekon": "https://github.com/RekonOps/Rekon-unity.git#v1.0.0"
  }
}
```

> 최신 버전은 [GitHub Releases](https://github.com/RekonOps/Rekon-unity/releases)에서 확인.

---

## 사용법

영상 캡처는 **FFmpeg**로 인코딩한다. 패키지에 번들되지 않으니, 영상 캡처를 쓰려면 OS에 맞게 한 번만 설치한다 (PC/Mac, 모바일 미지원):

| OS | 설치 |
|----|------|
| **macOS** | `brew install ffmpeg` |
| **Windows** | `choco install ffmpeg` 또는 `winget install ffmpeg` |
| **Linux** | `sudo apt install ffmpeg` (배포판에 맞게) |

> Rekon은 PATH와 brew/choco/winget 기본 설치 경로를 자동으로 찾는다 (Unity 에디터가 셸 PATH를 상속하지 않아도 동작). FFmpeg가 없으면 영상 없이 스크린샷·로그만 캡처된다.

설치했다면, 그 다음은 핫키 하나다.

1. **플레이 모드**에서 버그 발생
2. **`Ctrl/Cmd + Shift + B`** — 직전 ~60초가 캡처된다 (Settings에서 변경 가능)
3. 제목·설명 입력 → **[웹 저장]** → 웹 대시보드에 도착

대시보드에서 리포트를 열고 **[Jira 등록]** 버튼으로 이슈를 만든다.

> **경계는 정직하게**: Unity 플러그인은 *캡처와 저장*만 한다. Jira 연결은 웹 대시보드에서 한 번 인증해두면 된다 (`/settings/jira`). Unity가 Jira에 직접 붙지 않는다.

<details>
<summary>웹 로그인 / Jira 연동 내부 흐름 (펼치기)</summary>

**웹 로그인** — `Project Settings > Rekon > [웹 로그인]`
1. 플러그인이 `device_id`를 백엔드에 보내 일회용 로그인 URL을 받는다.
2. 브라우저가 자동으로 열리고 로그인을 진행한다.
3. 완료를 감지하면 토큰과 워크스페이스가 Settings에 자동 저장된다.
4. Settings에 **"연동됨 (워크스페이스명)"** 표시.

**Jira** — 웹 대시보드 `설정 > Jira 연동`에서 Jira Cloud OAuth 인증.
연동 후 리포트 상세 페이지의 [Jira 등록] 버튼이 활성화된다.

</details>

---

## 설정 (`Project Settings > Rekon`)

| 설정 | 설명 | 기본값 |
|------|------|--------|
| 핫키 | 캡처 단축키 | `Ctrl/Cmd+Shift+B` |
| 영상 FPS | 프레임 캡처 속도 | 15 |
| 영상 해상도 | 캡처 해상도 | 1280×720 |
| 로그 버퍼 | 최근 로그 보관 개수 | 최근 N개 |
| 번들 보관 한도 | 로컬 번들 최대 개수/용량 | 자동 정리 |
| 웹 연동 상태 | 로그인 여부 및 워크스페이스명 | 미연동 |

---

## 왜 만들었나

재현이 안 돼서 티켓을 못 닫아본 적이 있다.
QA가 "캐릭터가 이상해요"라고 쓴 그 장면을, 끝내 한 번도 못 보고 닫은 적이 있다.

그 순간의 진실은 60초면 증발한다. 그래서 그 증거가 죽지 않게 만들었다.

---

## 잃지 않는다

- **오프라인 자동 재시도** — 네트워크가 끊기면 캡처는 로컬(`pending/`)에 저장되고, 복구되면 백그라운드에서 자동 재시도한다 (최대 3회, 지수 백오프). 데이터를 흘리지 않는다.
- **민감 정보 마스킹** — 로그 내 이메일·IP·토큰 등은 자동으로 마스킹된다.
- **무결성 검증** — 모든 Release에 SHA-256 체크섬과 CycloneDX SBOM이 첨부된다. 받은 tarball이 변조되지 않았는지 직접 확인할 수 있다 → [SECURITY.md](./SECURITY.md).
- **MIT OSS** — 코드는 열려 있다. 당신의 증거를 누군가의 벽 안에 가두지 않는다.

---

## 요구사항

| 항목 | 조건 |
|------|------|
| Unity | 2022.3 LTS 이상 |
| .NET | Standard 2.1 |
| FFmpeg | PC/Mac 영상 캡처 시 필수 — **모바일 미지원** |

> 모바일 빌드에서는 영상 캡처가 동작하지 않는다. 숨기지 않고 적어둔다.

---

## 기여에 대하여

Rekon 은 투명성과 배포를 위해 소스를 공개하고 있습니다. **현재 외부 Pull Request 는 받지 않습니다** — 대신 버그 리포트와 기능 제안은 [Issues](https://github.com/RekonOps/Rekon-unity/issues) 로 언제든 환영합니다.

보안 취약점은 공개 이슈 대신 [SECURITY.md](SECURITY.md) 의 비공개 채널로 알려주세요.

## 라이선스

**MIT License** — [LICENSE](LICENSE) 참조.

Copyright 2026 RekonOps
