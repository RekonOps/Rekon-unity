# Asset Store 제출 준비 가이드

Bug-OneTouch 플러그인의 Unity Asset Store 제출을 위한 가이드입니다.

---

## 제품 정보

### 제목

```
Bug-OneTouch: 원터치 버그 리포트 for Jira
```

영문 제목:
```
Bug-OneTouch: One-Touch Bug Reporter for Jira
```

### 짧은 설명 (160자 이내)

```
Play Mode에서 F12 한 번으로 스크린샷·로그·영상을 자동 수집하고 Jira 이슈를 생성합니다. 로컬 저장·자동 재시도·크래시 복구 기능 포함.
```

영문:
```
Press F12 in Play Mode to auto-capture screenshots, logs & video, then submit directly to Jira Cloud. Includes local storage, auto-retry, and crash recovery.
```

### 상세 설명

```
Bug-OneTouch는 Unity 개발팀의 버그 리포팅 워크플로우를 혁신합니다.

게임 플레이 중 버그를 발견하면, F12 한 번으로:
- 현재 화면 스크린샷 자동 캡처
- 최근 500줄 로그 자동 수집 (민감 정보 자동 마스킹)
- 게임 상태 스냅샷 (씬, FPS, 메모리 등)
- 최근 60초 영상 클립 자동 저장

Jira Cloud와 OAuth 2.0으로 안전하게 연결하고, 한 번의 클릭으로 Jira 이슈를 생성합니다.

주요 기능:
• 원터치 캡처: F12 하나로 모든 버그 데이터 수집
• Jira Cloud 통합: OAuth 2.0 PKCE 인증, 이슈/첨부파일 자동 생성
• 영상 버그 리포트: 최대 120초 영상 링 버퍼로 버그 재현 영상 첨부
• 로컬 번들 저장: 오프라인 시에도 번들 저장 후 자동 재시도
• 크래시 복구: 에디터 크래시 시 크래시 직전 데이터 자동 복구
• 보안: 이메일, IP, 토큰 자동 마스킹, AES-256 토큰 암호화
• 커스터마이징: BugOneTouchContext.Add(), IContextProvider로 게임 데이터 추가

Unity 2022.3 LTS 이상, Jira Cloud 계정 필요.
```

### 카테고리

- 기본 카테고리: **Tools > Editor Extensions**
- 보조 카테고리: **Tools > Integration**

### 키워드

```
bug report, jira, qa, testing, bug tracker, game development, debugging, screenshot, video capture, crash recovery, workflow, productivity
```

한국어 키워드:
```
버그리포트, 버그추적, 지라연동, QA, 테스팅, 게임개발, 디버깅, 스크린샷, 영상캡처, 크래시복구
```

---

## 가격 정보

| 구분 | 가격 | 비고 |
|------|------|------|
| 정가 | $49.99 USD | |
| 출시 기념 할인 | $29.99 USD | 출시 후 2주 |
| 에듀케이션 할인 | 별도 문의 | |

**라이선스 유형:** Extension Asset (프로젝트 당 라이선스)

---

## 시스템 요구사항

### 최소 요구사항

| 항목 | 요구사항 |
|------|----------|
| Unity 버전 | 2022.3.22f1 이상 |
| 렌더 파이프라인 | Built-in, URP, HDRP 모두 지원 |
| .NET 버전 | .NET Standard 2.1 |
| 플랫폼 | Windows, macOS, Linux (에디터) |
| 디스크 공간 | 최소 100MB (번들 저장 공간 별도) |

### 권장 요구사항

| 항목 | 권장사항 |
|------|----------|
| Unity 버전 | 2022.3 LTS 최신 패치 이상 |
| RAM | 8GB 이상 |
| 디스크 공간 | 5GB 이상 (기본 번들 보관 한도) |

### 외부 의존성

| 항목 | 요구사항 |
|------|----------|
| Jira Cloud | 유효한 Atlassian 계정 |
| Auth Broker | 자체 Supabase 인스턴스 또는 제공된 서버 |
| 네트워크 | Jira Cloud API 접근 가능한 인터넷 연결 |

---

## 스크린샷 목록

Asset Store 제출에 필요한 스크린샷 항목입니다. 최소 5장, 최대 10장.

### 필수 스크린샷

1. **메인 버그 리포트 폼** (1920x1080)
   - Play Mode에서 F12 눌러 열린 버그 리포트 작성 화면
   - 스크린샷 미리보기, 로그 첨부 체크박스, Jira 프로젝트 선택 UI 표시

2. **Project Settings - Bug-OneTouch 패널** (1920x1080)
   - Unity Project Settings 내 Bug-OneTouch 설정 패널
   - 핫키, 영상, 로그, Jira 연결 상태 표시

3. **Bundle Manager 창** (1920x1080)
   - 번들 목록, 상태(대기중/완료/실패), 재시도 버튼
   - 여러 번들이 나열된 상태

4. **Jira 이슈 생성 결과** (1920x1080)
   - Bug-OneTouch로 생성된 Jira 이슈 화면
   - 스크린샷, 로그, 상태 정보가 첨부된 이슈 설명 표시

5. **크래시 복구 다이얼로그** (1920x1080)
   - 크래시 감지 후 복구 옵션을 제시하는 Unity 에디터 다이얼로그
   - 크래시 시각, 수집된 데이터 항목 표시

### 추가 스크린샷 (권장)

6. **캡처 진행 오버레이** (1920x1080)
   - F12 누른 후 화면 우측 하단에 표시되는 캡처 진행 오버레이
   - 단계별 진행 바(스크린샷, 로그, 상태, 영상)

7. **영상 첨부 버그 리포트** (1920x1080)
   - 영상 클립이 포함된 버그 리포트 폼
   - 영상 미리보기 썸네일과 재생 시간 표시

8. **코드 예제 - 커스텀 컨텍스트** (1920x1080)
   - VS Code 또는 Rider에서 BugOneTouchContext.Add() 코드 예제
   - 버그 리포트에 포함된 커스텀 데이터 시각화

---

## 패키지 콘텐츠 목록

Asset Store 제출 시 포함할 파일 목록입니다.

```
com.gaozombie.bug-onetouch/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE
├── Runtime/
│   ├── BugOneTouch.Runtime.asmdef
│   ├── BugOneTouch.cs
│   ├── Auth/           (OAuth 인증 브로커 클라이언트)
│   ├── Bundle/         (로컬 번들 저장소)
│   ├── Capture/        (캡처 파이프라인)
│   ├── Core/           (오케스트레이터)
│   ├── CrashRecovery/  (크래시 복구)
│   ├── Input/          (핫키 처리)
│   ├── Jira/           (Jira API 클라이언트)
│   ├── Network/        (HTTP 클라이언트)
│   ├── Security/       (마스킹, 암호화)
│   ├── Settings/       (설정 ScriptableObject)
│   ├── UI/             (버그 리포트 폼, 오버레이)
│   └── Video/          (영상 링 버퍼, 인코더)
├── Editor/
│   ├── BugOneTouch.Editor.asmdef
│   ├── BugOneTouchEditor.cs
│   ├── CrashRecovery/  (크래시 복구 에디터 UI)
│   └── UI/             (Bundle Manager, Settings Provider)
├── Tests/
│   ├── Runtime/        (런타임 단위 테스트)
│   └── Editor/         (에디터 단위 테스트)
├── Settings/
│   └── masking-rules.json  (기본 마스킹 규칙)
├── Samples~/
│   └── BasicDemo/      (기본 데모 샘플)
└── docs/
    ├── user-guide.md
    └── api-reference.md
```

---

## 릴리즈 노트

### v0.1.0 (2026-03-01) - 첫 릴리즈

이 릴리즈는 Bug-OneTouch의 첫 번째 공개 버전입니다.

**핵심 기능:**
- F12 원터치 버그 캡처 (스크린샷, 로그, 게임 상태, 영상)
- Jira Cloud OAuth 2.0 PKCE 통합
- 로컬 번들 저장 및 자동 재시도
- 에디터 크래시 복구
- 로그 민감 정보 자동 마스킹
- BugOneTouchContext / IContextProvider 커스터마이징 API
- Project Settings 통합 UI
- Bundle Manager 에디터 창

**지원 Unity 버전:** 2022.3.22f1 이상

**알려진 제한사항:**
- Jira Server/Data Center는 현재 지원하지 않습니다 (Jira Cloud 전용)
- 영상 캡처는 에디터 Play Mode에서만 지원됩니다 (런타임 빌드 미지원)
- Auth Broker 서버 자체 배포가 필요합니다 (호스팅 서비스 준비 중)

---

## 제출 체크리스트

Asset Store 제출 전 최종 확인 항목입니다.

### 패키지 검증

- [ ] `scripts/validate-package.sh` 실행 후 오류 0개 확인
- [ ] Unity 2022.3.22f1에서 패키지 임포트 테스트
- [ ] Play Mode에서 F12 동작 확인
- [ ] Jira OAuth 연결 및 이슈 생성 테스트
- [ ] 크래시 복구 동작 확인

### 문서 확인

- [ ] README.md 내용 최신 상태 확인
- [ ] CHANGELOG.md 버전 정보 업데이트
- [ ] docs/user-guide.md 내용 검토
- [ ] docs/api-reference.md 내용 검토

### 스크린샷

- [ ] 필수 스크린샷 5장 이상 준비
- [ ] 해상도 1920x1080 확인
- [ ] 개인 정보(이메일, 프로젝트 키 등) 마스킹 확인

### Asset Store 포털

- [ ] Publisher 계정 준비
- [ ] 제품 정보 입력 (제목, 설명, 키워드)
- [ ] 카테고리 선택
- [ ] 가격 설정
- [ ] 스크린샷 업로드
- [ ] 패키지 파일 업로드 (.unitypackage)
- [ ] 제출 및 심사 요청
