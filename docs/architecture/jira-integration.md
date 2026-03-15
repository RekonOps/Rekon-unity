# Jira Cloud 연동 Unity 클라이언트 설계 문서

## 개요

BugBeacon-unity의 Jira Cloud 연동 Unity 클라이언트 설계 문서입니다.
Auth Broker (Supabase Edge Functions)와 통신하여 Jira OAuth 인증 및 이슈 제출을 처리합니다.

---

## 1. 전체 아키텍처

```
[Unity Editor / Runtime]
        │
        ├── Auth 레이어
        │     ├── AuthBrokerClient       - HTTP 통신 클라이언트
        │     ├── SessionTokenStore      - JWT 토큰 암호화 저장
        │     ├── OAuthFlowManager       - OAuth 브라우저 플로우 관리
        │     ├── TokenRefreshManager    - 자동 토큰 갱신
        │     └── ReAuthHandler         - 재인증 이벤트 처리
        │
        ├── Jira 레이어
        │     ├── JiraApiClient          - Jira REST API v3 클라이언트
        │     ├── JiraIssueCreator       - 이슈 생성
        │     ├── JiraAttachmentUploader - 첨부파일 업로드
        │     └── JiraSubmissionService  - 통합 제출 서비스
        │
        └── 인터페이스
              └── IBundleStateUpdater    - 번들 상태 갱신 (M2 구현)
```

---

## 2. Auth 레이어 설계

### 2.1 AuthBrokerClient

**역할**: Auth Broker Edge Functions와 HTTP 통신

**주요 메서드**:
- `PostConnectJiraStartAsync(tenant_id, user_id)` → `ConnectStartResponse`
- `GetConnectJiraStatusAsync(connect_id)` → `ConnectStatusResponse`
- `PostTokenJiraAsync(session_token)` → `JiraTokenResponse`

**헤더 전략**:
- 인증 필요 요청: `X-Client-Token: <JWT>` 헤더 포함
- 401 응답 수신 시: `OnUnauthorized` 이벤트 발생 → ReAuthHandler로 위임

**에러 처리**:
- 네트워크 오류: 최대 3회 재시도 (지수 백오프)
- 4xx 오류: 즉시 예외 전파
- 5xx 오류: 최대 3회 재시도

---

### 2.2 SessionTokenStore

**역할**: JWT 세션 토큰 암호화 저장/로드

**저장소**:
- 에디터 실행 중: `EditorPrefs`
- 빌드 런타임: `PlayerPrefs`

**암호화 방식**:
- 알고리즘: AES-256-CBC + HMAC-SHA256
- 키 파생: PBKDF2(머신ID + 패키지명, 100,000 반복)
- IV: 저장 시 랜덤 생성, 암호문과 함께 Base64 인코딩

**저장 포맷**:
```
Base64(IV + CipherText + HMAC)
```

**토큰 만료 확인**:
- JWT payload의 `exp` 필드를 Base64Url 디코딩 후 파싱
- 만료 5분 전부터 갱신 필요 상태로 판단

---

### 2.3 OAuthFlowManager

**역할**: OAuth 2.0 브라우저 인증 플로우 관리

**플로우 순서**:
1. `AuthBrokerClient.PostConnectJiraStartAsync()` 호출
2. `Application.OpenURL(authorize_url)` → 기본 브라우저 열기
3. `AuthBrokerClient.GetConnectJiraStatusAsync()` 폴링 (2초 간격)
4. `completed` 상태 확인 → JWT 세션 토큰 수신
5. `SessionTokenStore.Save()` → 토큰 저장

**타임아웃**: 5분 (300초)
**취소**: `CancellationToken` 지원

---

### 2.4 TokenRefreshManager

**역할**: JWT 세션 토큰 자동 갱신

**갱신 트리거**:
- 토큰 만료 5분 전 자동 갱신
- 401 응답 수신 시 즉시 갱신 시도

**재시도 전략**:
- 최대 3회 재시도
- 지수 백오프: 2초 → 4초 → 8초
- 모든 재시도 실패 시 ReAuthHandler 호출

---

### 2.5 ReAuthHandler

**역할**: 토큰 갱신 최종 실패 시 사용자 재인증 유도

**동작**:
1. `SessionTokenStore.Clear()` → 토큰 삭제
2. `OnReAuthRequired` 이벤트 발생
3. UI 레이어에서 이벤트 구독 → 재인증 다이얼로그 표시

---

## 3. Jira 레이어 설계

### 3.1 JiraApiClient

**역할**: Jira REST API v3 래퍼

**토큰 흐름**:
1. `TokenRefreshManager.GetValidAccessTokenAsync()` 호출
2. 만료된 경우 `AuthBrokerClient.PostTokenJiraAsync()` 호출
3. 획득한 `access_token`을 Bearer로 Jira API 호출

**Jira API Base URL**:
```
https://api.atlassian.com/ex/jira/{cloud_id}/rest/api/3
```

---

### 3.2 JiraIssueCreator

**역할**: Jira 이슈 생성

**ADF (Atlassian Document Format) 구조**:
```json
{
  "version": 1,
  "type": "doc",
  "content": [
    {
      "type": "paragraph",
      "content": [
        { "type": "text", "text": "버그 설명" }
      ]
    }
  ]
}
```

**필수 필드**:
- `project.key`: 프로젝트 키
- `issuetype.name`: 이슈 유형 (기본: "Bug")
- `summary`: 요약 (제목)
- `description`: ADF 형식 본문
- `labels`: AdditionalLabels(요청 시 지정)와 기본 레이블 병합하여 자동 추가
- `priority.name`: 우선순위

---

### 3.3 JiraAttachmentUploader

**역할**: 이슈에 첨부파일 업로드

**필수 헤더**:
- `X-Atlassian-Token: no-check` (XSRF 비활성화)
- `Content-Type: multipart/form-data`

**크기 제한**:
- 기본 제한: 파일당 10MB
- 초과 시: 경고 로그 출력 후 건너뜀

**멀티파트 구성**:
```
POST /rest/api/3/issue/{issueKey}/attachments
Content-Type: multipart/form-data; boundary=...
X-Atlassian-Token: no-check

--boundary
Content-Disposition: form-data; name="file"; filename="screenshot.png"
Content-Type: image/png

[바이너리 데이터]
--boundary--
```

---

### 3.4 JiraSubmissionService

**역할**: 이슈 생성 + 첨부파일 업로드 + 번들 상태 갱신 통합

**실행 순서**:
1. 이슈 생성 (`JiraIssueCreator`)
2. 첨부파일 업로드 (`JiraAttachmentUploader`)
3. 번들 상태 갱신 (`IBundleStateUpdater`)

**부분 실패 처리**:
- 이슈 생성 성공 + 첨부 실패 → 이슈 키 포함하여 부분 성공 반환
- 이슈 생성 실패 → 전체 실패 반환
- 진행 상태 이벤트: `OnProgressChanged(progress, message)`

---

## 4. 인터페이스 설계

### 4.1 IBundleStateUpdater

```csharp
// M2에서 실제 구현
public interface IBundleStateUpdater
{
    Task UpdateSubmittedAsync(string bundleId, string jiraIssueKey, string jiraIssueUrl);
    Task UpdateFailedAsync(string bundleId, string errorMessage);
}
```

---

## 5. 데이터 모델

### 5.1 ConnectStartResponse

```csharp
public class ConnectStartResponse
{
    public string ConnectId { get; set; }    // UUID
    public string AuthorizeUrl { get; set; } // Jira OAuth URL
}
```

### 5.2 ConnectStatusResponse

```csharp
public class ConnectStatusResponse
{
    public string Status { get; set; }        // "pending" | "completed" | "error"
    public string SessionToken { get; set; }  // JWT (completed 시에만)
}
```

### 5.3 JiraTokenResponse

```csharp
public class JiraTokenResponse
{
    public string AccessToken { get; set; }  // Jira access token
    public string ExpiresAt { get; set; }    // ISO8601
    public string CloudId { get; set; }      // Atlassian cloud ID
}
```

### 5.4 CreateIssueRequest

```csharp
public class CreateIssueRequest
{
    public string ProjectKey { get; set; }
    public string IssueType { get; set; }    // 기본: "Bug"
    public string Summary { get; set; }
    public string Description { get; set; }  // 마크다운 텍스트 (내부에서 ADF 변환)
    public string[] Labels { get; set; }
    public string Priority { get; set; }     // "Highest" | "High" | "Medium" | "Low"
}
```

### 5.5 CreateIssueResult

```csharp
public class CreateIssueResult
{
    public string IssueKey { get; set; }   // 예: "PROJ-123"
    public string IssueUrl { get; set; }   // self URL
}
```

---

## 6. 에러 처리 전략

| 에러 유형 | 처리 방법 |
|-----------|-----------|
| 네트워크 단절 | 최대 3회 재시도 (지수 백오프) |
| 401 Unauthorized | 토큰 갱신 시도 → 실패 시 재인증 요청 |
| 403 Forbidden | 즉시 실패 (권한 없음 메시지) |
| 404 Not Found | 즉시 실패 |
| 429 Rate Limited | Retry-After 헤더 준수 후 재시도 |
| 5xx Server Error | 최대 3회 재시도 |
| 타임아웃 (5분) | OAuthFlowManager 취소 + 사용자 알림 |

---

## 7. UnityWebRequest 스레드 안전성

Unity의 `UnityWebRequest`는 메인 스레드에서만 실행 가능합니다.

**해결 방법**:
- `async/await` 패턴 사용
- `SynchronizationContext` 캡처 후 콜백에서 `Post()` 사용
- `UnityWebRequestAsyncOperation`을 `Task`로 래핑

```csharp
// 패턴 예시
var context = SynchronizationContext.Current;
// ... 비동기 작업 ...
context.Post(_ => {
    // 메인 스레드 코드
}, null);
```

---

## 8. 보안 고려사항

- JWT 토큰은 AES-256 암호화 후 EditorPrefs/PlayerPrefs 저장
- 암호화 키는 SystemInfo.deviceUniqueIdentifier + 패키지명으로 파생 (PBKDF2)
- 액세스 토큰은 메모리에서만 처리 (저장 금지)
- 로그에 토큰 값 노출 금지
- HTTPS 전용 통신

---

## 9. 파일 구조

```
Runtime/
├── Auth/
│   ├── AuthBrokerClient.cs
│   ├── SessionTokenStore.cs
│   ├── OAuthFlowManager.cs
│   ├── TokenRefreshManager.cs
│   └── ReAuthHandler.cs
├── Jira/
│   ├── JiraApiClient.cs
│   ├── JiraIssueCreator.cs
│   ├── JiraAttachmentUploader.cs
│   └── JiraSubmissionService.cs
└── Core/
    └── IBundleStateUpdater.cs

Tests/
└── Runtime/
    ├── Auth/
    │   ├── AuthBrokerClientTests.cs
    │   └── TokenRefreshTests.cs
    └── Jira/
        └── JiraApiClientTests.cs
```
