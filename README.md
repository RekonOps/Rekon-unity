# Bug-OneTouch Unity 플러그인

> Unity 게임 개발자를 위한 원클릭 버그 리포팅 플러그인 -- 플레이 모드에서 영상/스크린샷/로그를 자동 캡처하여 웹 대시보드 저장 또는 Jira 이슈로 즉시 등록합니다.

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## 주요 기능

- **원클릭 인게임 버그 리포팅** -- 플레이 중 F12 핫키(변경 가능)로 버그 리포트 UI를 열고, 한 번의 클릭으로 제출
- **자동 영상 캡처** -- 링 버퍼 기반 프레임 녹화 (기본 15fps, 1280x720) + FFmpeg MP4 인코딩
- **스크린샷/로그/게임 상태 자동 수집** -- 씬 이름, 디바이스 정보, Unity 버전, 프레임레이트, 최근 로그 자동 첨부
- **[웹 저장] / [Jira 등록] 이중 경로** -- 웹 대시보드에 저장하거나 Jira Cloud에 직접 이슈 등록
- **Supabase Auth 웹 로그인** -- 브라우저 기반 인증으로 별도 계정 설정 불필요
- **Cloudflare R2 파일 업로드** -- 영상/스크린샷/로그 파일을 R2에 업로드 (재시도 3회, 실패 시 로컬 저장)
- **라이선스 검증** -- 서버 기반 라이선스 키 검증 + 오프라인 Grace Period 72시간
- **AES-256-CBC 세션 암호화** -- OAuth 토큰 및 세션 데이터 로컬 암호화 저장
- **크래시 복구** -- Memory-Mapped File 기반 비정상 종료 감지 및 데이터 복구
- **민감 정보 자동 마스킹** -- 로그 내 이메일, IP, 토큰 등 자동 마스킹

---

## 요구사항

| 항목 | 최소 버전 |
|------|----------|
| Unity | 2022.3 LTS 이상 |
| .NET | Standard 2.1 |
| FFmpeg | PC/Mac 영상 캡처 시 필수 (모바일 미지원) |

---

## 설치 방법

### UPM (Unity Package Manager) - Git URL

1. Unity에서 **Window > Package Manager** 열기
2. 좌측 상단 **+** 버튼 클릭 > **Add package from git URL...** 선택
3. 아래 URL 입력:

```
https://github.com/Project-Bug-OneTouch/Bug-OneTouch-unity.git#v1.0.0
```

4. **Add** 클릭 -- Unity가 패키지를 자동으로 다운로드합니다.

또는 `Packages/manifest.json`에 직접 추가:

```json
{
  "com.gaozombie.bug-onetouch": "https://github.com/Project-Bug-OneTouch/Bug-OneTouch-unity.git#v1.0.0"
}
```

---

## 빠른 시작 가이드

### 1. 웹 로그인

Unity 에디터에서 **Project Settings > Bug OneTouch** 열기 > **[웹 로그인]** 버튼 클릭
- 브라우저가 열리며 Supabase Auth 로그인 진행
- 로그인 완료 시 Unity에 자동 연결

### 2. 라이선스 키 입력 (팀 사용 시)

관리자가 [웹 대시보드](https://bug-onetouch.com)에서 발급한 라이선스 키를 Settings에 입력:
- **Project Settings > Bug OneTouch > License Key** 에 `BOT-XXXX-XXXX-XXXX-XXXX` 입력

### 3. 버그 캡처

1. **플레이 모드** 진입
2. 버그 발생 시 **F12** (기본 핫키) 누르기
3. 제목과 설명 입력
4. **[웹 저장]** 또는 **[Jira 등록]** 선택하여 제출

### 4. Jira 연동 (선택)

**Project Settings > Bug OneTouch > Jira 연동** 에서 OAuth 인증 진행
- Jira Cloud 계정 연결 후 [Jira 등록] 버튼이 활성화됩니다.

---

## 설정 (BugOneTouchSettings)

`Project Settings > Bug OneTouch`에서 설정 가능한 항목:

| 설정 | 설명 | 기본값 |
|------|------|--------|
| 핫키 | 버그 리포트 UI 열기 | F12 |
| 영상 FPS | 프레임 캡처 속도 | 15 |
| 영상 해상도 | 캡처 해상도 | 1280x720 |
| 로그 버퍼 크기 | 최근 로그 보관 개수 | 최근 N개 |
| 번들 보관 한도 | 로컬 번들 최대 개수/용량 | 자동 정리 |

---

## 프로젝트 구조

```
Runtime/           # 핵심 런타임 모듈
  Auth/            # 웹 로그인, 라이선스 검증, OAuth, 세션 저장
  Bundle/          # 번들 패키징, 로컬 저장소, 제출 대기열
  Capture/         # 스크린샷, 로그 수집, 게임 상태 스냅샷
  CrashRecovery/   # 크래시 감지 및 복구
  Input/           # 핫키 관리
  Jira/            # Jira REST API 클라이언트, 이슈 생성
  Network/         # R2 업로드 클라이언트
  Security/        # 토큰 암호화, 로그 마스킹, 레이트 리미터
  Settings/        # ScriptableObject 기반 설정
  UI/              # 인게임 오버레이 UI
  Video/           # 프레임 캡처, 링 버퍼, MP4 인코딩
Editor/            # 에디터 UI (Settings 패널, Bundle Manager)
Tests/             # EditMode / PlayMode 테스트
Samples~/          # 사용 예제 (커스텀 컨텍스트)
docs/              # 사용자 가이드, API 레퍼런스
```

---

## 관련 저장소

| 저장소 | 설명 |
|--------|------|
| [Bug-OneTouch-backend](https://github.com/Project-Bug-OneTouch/Bug-OneTouch-backend) | Supabase 백엔드 (Edge Functions, DB) |
| [Bug-OneTouch-web](https://github.com/Project-Bug-OneTouch/Bug-OneTouch-web) | 웹 대시보드 (Next.js) |

---

## 라이선스

이 프로젝트는 **MIT 라이선스**로 제공됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

Copyright 2026 GaoZombie
