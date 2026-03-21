# Rekon 사용자 가이드

> Unity 개발자를 위한 원터치 버그 리포트 플러그인

---

## 목차

1. [빠른 시작 (5분 안에 시작하기)](#빠른-시작)
2. [설치 방법](#설치-방법)
3. [초기 설정](#초기-설정)
4. [Jira Cloud 연결](#jira-cloud-연결)
5. [Play Mode에서 버그 리포트 작성하기](#play-mode에서-버그-리포트-작성하기)
6. [번들 관리](#번들-관리)
7. [크래시 복구](#크래시-복구)
8. [커스터마이징](#커스터마이징)
9. [트러블슈팅](#트러블슈팅)

---

## 빠른 시작

5분 안에 첫 버그 리포트를 제출하는 최단 경로입니다.

### 1단계: 설치

Unity Package Manager에서 Git URL로 설치합니다.

```
https://github.com/RekonOps/Rekon-unity.git
```

### 2단계: Settings 생성

`Assets` 메뉴 → `Create` → `Rekon` → `Settings`를 선택하여 `RekonSettings` 에셋을 생성합니다.

생성된 에셋을 Project Settings의 Rekon 항목에 할당합니다.

### 3단계: Play Mode에서 테스트

Play Mode 진입 후 `F12` 키를 누릅니다. 버그 리포트 UI가 열리면 설치가 완료된 것입니다.

---

## 설치 방법

### UPM Git URL (권장)

1. Unity 에디터 메뉴 → `Window` → `Package Manager` 열기
2. 좌측 상단 `+` 버튼 → `Add package from git URL...` 선택
3. 아래 URL 입력 후 `Add` 클릭:

```
https://github.com/RekonOps/Rekon-unity.git
```

특정 버전을 사용하려면 URL 끝에 `#버전태그`를 붙입니다:

```
https://github.com/RekonOps/Rekon-unity.git#v0.1.0
```

### .unitypackage로 설치

1. [GitHub Releases](https://github.com/RekonOps/Rekon-unity/releases) 페이지에서 최신 `.unitypackage` 파일 다운로드
2. Unity 에디터로 해당 파일을 드래그 앤 드롭하거나, `Assets` 메뉴 → `Import Package` → `Custom Package...` 선택
3. 모든 파일이 체크된 상태로 `Import` 클릭

### 수동 설치

1. 저장소를 클론하거나 ZIP으로 다운로드:

```
git clone https://github.com/RekonOps/Rekon-unity.git
```

2. 클론한 폴더를 Unity 프로젝트의 `Packages` 디렉토리 안으로 복사합니다:

```
YourUnityProject/
  Packages/
    dev.rekonops.rekon/   ← 여기에 복사
```

3. Unity 에디터를 재시작하면 자동으로 인식됩니다.

---

## 초기 설정

### Settings 에셋 생성

1. Project 창에서 설정을 저장할 폴더로 이동 (예: `Assets/Settings`)
2. 우클릭 → `Create` → `Rekon` → `Settings` 선택
3. 파일명은 `RekonSettings`로 유지하거나 원하는 이름으로 변경

### Project Settings에 등록

1. `Edit` 메뉴 → `Project Settings` → `Rekon` 항목 선택
2. `Settings Asset` 필드에 생성한 에셋을 드래그하여 할당
3. 설정 패널에서 각 항목을 조정

### 핫키 설정

| 항목 | 설명 | 기본값 |
|------|------|--------|
| Capture Hotkey | 버그 캡처를 시작하는 키 | `F12` |

핫키는 Play Mode에서만 작동합니다. 에디터 모드에서는 Rekon 에디터 윈도우를 통해 수동으로 캡처할 수 있습니다.

### 영상 녹화 설정

| 항목 | 설명 | 기본값 |
|------|------|--------|
| Video Enabled | 영상 링 버퍼 활성화 | 켜짐 |
| Video Width | 녹화 해상도 가로 | 1280 |
| Video Height | 녹화 해상도 세로 | 720 |
| Video FPS | 초당 프레임 수 | 30 |
| Video Buffer Seconds | 버퍼 유지 시간(초) | 60 |
| Video Bitrate Mbps | 목표 비트레이트 | 10 Mbps |

영상 버퍼는 항상 최근 N초를 메모리에 유지합니다. 버그 리포트 시 해당 영상이 자동으로 첨부됩니다.

### 로그 수집 설정

| 항목 | 설명 | 기본값 |
|------|------|--------|
| Log Buffer Size | 링 버퍼에 유지할 최대 로그 줄 수 | 500 |
| Masking Rules Path | 커스텀 마스킹 규칙 JSON 파일 경로 | (없음) |

로그는 자동으로 이메일, IP 주소, 토큰/시크릿 등의 민감 정보가 마스킹됩니다. 추가 마스킹 규칙이 필요하면 `Masking Rules Path`에 JSON 파일 경로를 지정합니다.

---

## Jira Cloud 연결

### 사전 준비

- Jira Cloud 계정 (atlassian.net)
- 이슈를 생성할 Jira 프로젝트의 프로젝트 키 (예: `GAME`, `BUG`)
- Auth Broker 서버 배포 (자체 Supabase 인스턴스 또는 제공된 서버 사용)

### OAuth 연결 절차

**1단계: Auth Broker URL 설정**

`RekonSettings` 에셋의 `Auth Broker URL` 필드에 Auth Broker 서버 주소를 입력합니다:

```
https://your-project.supabase.co/functions/v1
```

**2단계: OAuth 인증 시작**

`Project Settings` → `Rekon` → `Jira 연결` 버튼을 클릭합니다.

**3단계: 브라우저 인증**

자동으로 브라우저가 열리고 Jira OAuth 인증 페이지로 이동합니다. Atlassian 계정으로 로그인하고 Rekon 앱의 권한 요청을 승인합니다.

**4단계: 연결 확인**

브라우저 인증이 완료되면 Unity 에디터 Settings 패널에 "연결됨" 상태와 연결된 Atlassian 계정 이메일이 표시됩니다.

**5단계: Jira 프로젝트 설정**

- `Default Project Key`: 이슈를 생성할 기본 Jira 프로젝트 키 입력 (예: `GAME`)
- `Default Labels`: 자동으로 추가할 레이블 설정 (기본값: `rekon-unity`, `unity`)

### 토큰 갱신

액세스 토큰은 만료 전 자동으로 갱신됩니다. 토큰 갱신에 실패하면 Settings 패널에 경고가 표시되며, `재연결` 버튼으로 OAuth 플로우를 다시 시작할 수 있습니다.

---

## Play Mode에서 버그 리포트 작성하기

### 기본 워크플로우

**1단계: 버그 트리거**

Play Mode 실행 중 버그가 발생하면 설정된 핫키(`F12`)를 누릅니다. 화면 우측 하단에 캡처 진행 오버레이가 표시됩니다.

**2단계: 자동 캡처**

Rekon가 자동으로 다음 항목을 수집합니다:

- 현재 화면 스크린샷 (PNG)
- 최근 로그 (링 버퍼에 저장된 내용)
- 게임 상태 스냅샷 (씬 이름, 프레임 수, 메모리 사용량 등)
- 영상 클립 (활성화된 경우, 버그 직전 N초)

캡처는 최대 5초 이내에 완료됩니다. 타임아웃 시 수집된 항목만 포함하여 진행됩니다.

**3단계: 리포트 작성**

캡처 완료 후 버그 리포트 폼이 열립니다:

- `제목`: 버그를 한 줄로 설명합니다 (필수)
- `설명`: 버그 재현 방법, 예상 동작, 실제 동작을 작성합니다
- `심각도`: Critical / High / Medium / Low 중 선택
- `프로젝트`: Jira 프로젝트 키 (설정에서 변경 가능)
- `첨부 파일`: 자동 수집된 스크린샷, 로그, 영상 파일 목록이 표시됩니다

**4단계: 제출**

`제출` 버튼을 클릭하면 번들이 생성되어 로컬에 저장되고, Jira가 연결된 경우 즉시 업로드됩니다. 네트워크 오류 시 번들은 재시도 큐에 추가됩니다.

### 제출 취소

폼에서 `취소` 버튼을 클릭하면 수집된 데이터가 임시 폴더에서 삭제됩니다.

---

## 번들 관리

번들은 캡처된 버그 리포트 데이터가 로컬에 저장된 단위입니다.

### 번들 상태

| 상태 | 설명 |
|------|------|
| 대기중 | 로컬에 저장됨, 제출 예정 |
| 업로드중 | 현재 Jira에 전송 중 |
| 완료 | Jira 이슈 생성 성공 |
| 실패 | 전송 실패, 재시도 예정 |

### 번들 관리 창

`Window` 메뉴 → `Rekon` → `Bundle Manager`를 선택하여 번들 관리 창을 엽니다.

- 번들 목록과 상태를 확인할 수 있습니다
- 특정 번들을 수동으로 재시도할 수 있습니다
- 불필요한 번들을 선택하여 삭제할 수 있습니다

### 자동 재시도

전송 실패한 번들은 백그라운드에서 자동으로 재시도됩니다. 재시도는 지수 백오프(Exponential Backoff) 방식으로 간격이 늘어납니다.

### 보관 정책

| 항목 | 기본값 |
|------|--------|
| 최대 번들 수 | 200개 |
| 최대 디스크 사용량 | 5,120 MB |

보관 한도를 초과하면 가장 오래된 완료된 번들부터 자동 삭제됩니다.

---

## 크래시 복구

### 자동 크래시 감지

Rekon는 Play Mode 진입 시 자동으로 이전 세션의 비정상 종료를 감지합니다. 에디터 크래시, 강제 종료, 또는 비정상 종료가 감지되면 세션 시작 시 크래시 복구 윈도우가 표시됩니다.

### 크래시 데이터 수집

크래시가 발생하기 전까지의 데이터가 자동으로 저장됩니다:

- 크래시 직전 로그 (설정한 주기마다 플러시)
- 게임 상태 스냅샷 (설정한 주기마다 플러시)
- 영상 클립 (설정한 주기마다 플러시)

### 크래시 복구 플로우

**1단계: 복구 윈도우 표시**

다음 Play Mode 진입 시 "크래시 감지됨" 다이얼로그가 표시됩니다. 감지된 크래시 발생 시각과 수집된 데이터 항목이 나열됩니다.

**2단계: 리포트 작성**

`버그 리포트 작성` 버튼을 클릭하면 크래시 데이터가 포함된 버그 리포트 폼이 열립니다. 크래시 스택 트레이스가 있으면 설명란에 자동으로 포함됩니다.

**3단계: Jira 등록**

일반 버그 리포트와 동일하게 `제출` 버튼으로 Jira에 이슈를 생성합니다. 크래시 번들임을 나타내는 `crash-recovery` 레이블이 자동으로 추가됩니다.

**4단계: 무시**

크래시 리포트가 필요 없다면 `무시` 버튼을 클릭합니다. 해당 크래시 데이터는 보관 정책에 따라 자동 삭제됩니다.

### 크래시 복구 설정

| 항목 | 설명 | 기본값 |
|------|------|--------|
| Log Flush Interval | 로그 플러시 주기(초) | 5초 |
| State Flush Interval | 상태 플러시 주기(초) | 10초 |
| Video Flush Interval | 영상 플러시 주기(초) | 30초 |
| Max Crash Bundles | 보관할 최대 크래시 번들 수 | 10개 |
| Crash Bundle Retention Days | 크래시 번들 보관 기간(일) | 30일 |

---

## 커스터마이징

### 커스텀 컨텍스트 데이터 추가

게임 코드에서 버그 리포트에 포함될 커스텀 데이터를 동적으로 추가할 수 있습니다.

```csharp
using RekonOps.Rekon;

// 게임 데이터를 컨텍스트에 추가
RekonContext.Add("current_level", "5");
RekonContext.Add("player_hp", playerHp.ToString());
RekonContext.Add("scene_name", SceneManager.GetActiveScene().name);

// 특정 키 제거
RekonContext.Remove("current_level");

// 모든 컨텍스트 초기화 (씬 전환 시 등)
RekonContext.Clear();
```

추가된 데이터는 버그 리포트의 상태 스냅샷에 포함되어 Jira 이슈 설명란에 첨부됩니다.

### IContextProvider 구현

씬 전환이나 게임 상태 변화에 따라 자동으로 업데이트되는 컨텍스트 제공자를 구현할 수 있습니다.

```csharp
using System.Collections.Generic;
using RekonOps.Rekon;

// IContextProvider 구현
public class GameStateContextProvider : IContextProvider
{
    public Dictionary<string, string> GetContext()
    {
        return new Dictionary<string, string>
        {
            { "level",          GameManager.CurrentLevel.ToString() },
            { "score",          GameManager.Score.ToString() },
            { "player_position", PlayerController.Position.ToString() },
            { "enemy_count",    EnemyManager.ActiveCount.ToString() },
        };
    }
}
```

구현한 프로바이더를 `ContextProviderRegistry`에 등록합니다:

```csharp
using RekonOps.Rekon;

// 게임 시작 시 등록
var provider = new GameStateContextProvider();
Rekon.Instance.ContextRegistry.Register(provider);

// 게임 종료 시 해제
Rekon.Instance.ContextRegistry.Unregister(provider);
```

버그 캡처 시 `GetContext()`가 자동으로 호출되어 최신 상태가 수집됩니다.

### 커스텀 마스킹 규칙

기본 마스킹 규칙(이메일, IP, 토큰) 외에 프로젝트에 맞는 추가 규칙을 JSON 파일로 정의할 수 있습니다.

`masking-rules.json` 파일 작성 예시:

```json
{
  "rules": [
    {
      "name": "플레이어 ID",
      "pattern": "player_id:\\s*\\d+",
      "replacement": "player_id: [MASKED]",
      "enabled": true
    },
    {
      "name": "인앱 결제 영수증",
      "pattern": "receipt=[A-Za-z0-9+/=]+",
      "replacement": "receipt=[MASKED:RECEIPT]",
      "enabled": true
    }
  ]
}
```

작성한 파일 경로를 `RekonSettings`의 `Masking Rules Path`에 입력합니다.

---

## 트러블슈팅

### Q1. F12를 눌러도 버그 리포트 UI가 열리지 않습니다.

**확인 사항:**

1. Play Mode인지 확인합니다. Rekon는 에디터 모드에서 핫키가 작동하지 않습니다.
2. `RekonSettings` 에셋이 Project Settings에 올바르게 할당되어 있는지 확인합니다.
3. `Capture Hotkey` 설정이 `F12`로 되어 있는지 확인합니다.
4. 게임 코드에서 `Input.GetKeyDown(KeyCode.F12)`를 별도로 처리하고 있다면 이벤트가 소비될 수 있습니다. Input System이 Input Manager(Legacy)로 설정되어 있는지 확인합니다.

### Q2. Jira 연결 후 이슈가 생성되지 않습니다.

**확인 사항:**

1. `Auth Broker URL`이 올바르게 설정되어 있는지 확인합니다.
2. Settings 패널에서 Jira 연결 상태가 "연결됨"인지 확인합니다.
3. `Default Project Key`가 존재하는 Jira 프로젝트 키인지 확인합니다 (대소문자 구분).
4. 연결된 Atlassian 계정이 해당 프로젝트에 이슈 생성 권한이 있는지 확인합니다.
5. Bundle Manager에서 번들 상태를 확인합니다. "실패" 상태라면 Console 창에서 오류 메시지를 확인합니다.

### Q3. 영상이 버그 리포트에 포함되지 않습니다.

**확인 사항:**

1. `RekonSettings`에서 `Video Enabled`가 체크되어 있는지 확인합니다.
2. Play Mode 진입 후 충분한 시간(최소 몇 초)이 지난 후 캡처했는지 확인합니다. 링 버퍼가 채워지기 전에 캡처하면 영상이 없을 수 있습니다.
3. Unity 에디터 Console 창에서 `[Rekon]` 관련 오류 메시지를 확인합니다.
4. 메모리 제한으로 인해 영상 버퍼가 비활성화되지 않았는지 확인합니다.

### Q4. 크래시 복구 윈도우가 표시되지 않습니다.

**확인 사항:**

1. 이전 Play Mode 세션이 실제로 비정상 종료(크래시)되었는지 확인합니다. 정상 종료된 경우에는 표시되지 않습니다.
2. `RekonSettings`의 Crash Recovery 설정이 활성화되어 있는지 확인합니다.
3. `Application.persistentDataPath` 경로에 크래시 복구 데이터가 저장되었는지 확인합니다.

### Q5. 로그에 민감 정보가 포함되어 있습니다.

**해결 방법:**

기본 마스킹 규칙은 이메일, IPv4 주소, 토큰/시크릿 키-값 쌍을 자동으로 처리합니다. 추가로 마스킹이 필요한 패턴이 있다면 [커스텀 마스킹 규칙](#커스텀-마스킹-규칙) 섹션을 참고하여 JSON 규칙 파일을 작성합니다.

---

## 추가 도움말

- [API 레퍼런스](api-reference.md) - 공개 API 전체 목록
- [GitHub Issues](https://github.com/RekonOps/Rekon-unity/issues) - 버그 리포트 및 기능 요청
- [CONTRIBUTING.md](../CONTRIBUTING.md) - 기여 방법
