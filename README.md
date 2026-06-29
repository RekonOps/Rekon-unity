# Rekon Unity 플러그인

> Unity 게임 개발자를 위한 원클릭 버그 리포팅 플러그인 -- 플레이 모드에서 영상/스크린샷/로그를 자동 캡처하여 **웹 대시보드에 저장**하고, Jira 이슈 등록은 웹 대시보드에서 수행합니다.

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-0.5.1-brightgreen.svg)](CHANGELOG.md)

---

## 개요

- **Unity 플러그인**: 플레이 모드에서 버그를 캡처해 영상/스크린샷/로그를 웹 대시보드에 저장합니다.
- **Jira 등록**: Unity 플러그인은 Jira에 직접 연결하지 않습니다. Jira 이슈 등록은 웹 대시보드에서 수행합니다.

---

## 주요 기능

- **원클릭 인게임 버그 리포팅** -- 플레이 중 캡처 단축키(기본 `Ctrl/Cmd+Shift+B`, 변경 가능)로 버그 리포트 UI를 열고, 한 번의 클릭으로 제출
- **자동 영상 캡처** -- 링 버퍼 기반 프레임 녹화 (기본 15fps, 1280x720) + FFmpeg MP4 인코딩
- **스크린샷/로그/게임 상태 자동 수집** -- 씬 이름, 디바이스 정보, Unity 버전, 프레임레이트, 최근 로그 자동 첨부
- **[웹 저장] 원클릭 제출** -- 웹 로그인만 되어있으면 항상 사용 가능. Jira 등록은 웹 대시보드에서 수행
- **Supabase Auth 웹 로그인** -- 브라우저 기반 인증 플로우 (auth-unity-start 폴링 방식)
- **Cloudflare R2 파일 업로드** -- 영상/스크린샷/로그 파일을 R2에 업로드 (재시도 3회, 실패 시 로컬 저장)
- **오프라인 동작** -- 네트워크 오류 시 `pending/` 디렉토리에 로컬 저장 후 복구 시 자동 재시도
- **라이선스 검증** -- 웹 로그인(JWT) 기반 플랜 검증 + 오프라인 Grace Period 72시간
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
https://github.com/RekonOps/Rekon-unity.git#v0.5.1
```

4. **Add** 클릭 -- Unity가 패키지를 자동으로 다운로드합니다.

또는 `Packages/manifest.json`에 직접 추가:

```json
{
  "dependencies": {
    "dev.rekonops.rekon": "https://github.com/RekonOps/Rekon-unity.git#v0.5.1"
  }
}
```

> **최신 버전 확인**: [GitHub Releases](https://github.com/RekonOps/Rekon-unity/releases) 페이지에서 최신 태그를 확인하세요.

---

## 빠른 시작 가이드

### 1. 웹 로그인 (auth-unity-start 폴링 방식)

Unity 에디터에서 **Project Settings > Rekon** 열기 > **[웹 로그인]** 버튼 클릭

내부 동작 흐름:
1. 플러그인이 백엔드 `auth-unity-start` API에 `device_id`를 전송하여 `connect_id`와 `login_url`을 받습니다.
2. 브라우저가 자동으로 열리며 `login_url`로 Supabase Auth 로그인을 진행합니다.
3. 로그인 완료 후 플러그인이 `auth-unity-status` API를 3초 간격으로 폴링합니다.
4. 완료 감지 시 `access_token`과 `workspace_id`를 자동으로 Settings에 저장합니다.
5. Settings Window에 **"연동됨 (워크스페이스명)"** 상태가 표시됩니다.

### 2. 버그 캡처

1. **플레이 모드** 진입
2. 버그 발생 시 **캡처 단축키**(기본 `Ctrl/Cmd + Shift + B`, Settings에서 변경 가능) 누르기
3. 제목과 설명 입력
4. **[웹 저장]** 버튼 클릭하여 제출 (웹 대시보드에 저장됨)

> **Jira 이슈 등록**: Unity 플러그인에서 Jira에 직접 등록하지 않습니다. 버그 리포트가 웹 대시보드에 저장된 후, [웹 대시보드](https://rekonops.dev)의 **워크스페이스 > 이슈 상세** 페이지에서 [Jira 등록] 버튼을 클릭하여 Jira 이슈를 생성하세요.

### 3. Jira 연동 (웹 대시보드에서)

웹 대시보드의 **설정 > Jira 연동** (`/settings/jira`)에서 Jira Cloud OAuth 인증을 진행합니다.
- 연동 완료 후 이슈 상세 페이지에서 [Jira 등록] 버튼이 활성화됩니다.

---

## 설정 (RekonSettings)

`Project Settings > Rekon`에서 설정 가능한 항목:

| 설정 | 설명 | 기본값 |
|------|------|--------|
| 핫키 | 버그 리포트 UI 열기 | Ctrl/Cmd+Shift+B |
| 영상 FPS | 프레임 캡처 속도 | 15 |
| 영상 해상도 | 캡처 해상도 | 1280x720 |
| 로그 버퍼 크기 | 최근 로그 보관 개수 | 최근 N개 |
| 번들 보관 한도 | 로컬 번들 최대 개수/용량 | 자동 정리 |
| 웹 연동 상태 | 웹 로그인 연결 여부 및 워크스페이스명 표시 | 미연동 |

---

## 오프라인 동작

네트워크 오류 또는 웹 로그인이 되어있지 않은 경우:

1. 캡처된 데이터는 `Application.persistentDataPath/pending/` 디렉토리에 로컬 저장됩니다.
2. 네트워크 복구 또는 로그인 완료 시 백그라운드에서 자동으로 재시도합니다.
3. `PendingUploadManager`가 재시도를 관리하며, 최대 3회 지수 백오프 방식으로 재시도합니다.

---

## SDK 무결성 검증

모든 GitHub Release에는 SHA-256 체크섬 파일과 CycloneDX SBOM이 첨부됩니다.
다운로드한 tarball이 변조되지 않았는지 확인하는 방법:

```bash
# macOS / Linux
sha256sum -c rekon-unity-v0.5.1.tgz.sha256
# 출력: rekon-unity-v0.5.1.tgz: OK

# Windows (PowerShell)
$expected = (Get-Content "rekon-unity-v0.5.1.tgz.sha256" -Raw).Split(" ")[0].Trim()
$actual   = (Get-FileHash "rekon-unity-v0.5.1.tgz" -Algorithm SHA256).Hash.ToLower()
if ($expected -eq $actual) { "OK" } else { "FAIL" }
```

> 자세한 검증 방법 및 SBOM 확인은 [SECURITY.md](./SECURITY.md) 참조.

---

## 라이선스

이 프로젝트는 **Apache License 2.0**으로 제공됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

Copyright 2026 RekonOps
