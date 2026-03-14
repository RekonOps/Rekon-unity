# Auth Broker 설계 문서

## 개요

BugBeacon-unity 프로젝트의 Supabase 기반 OAuth 2.0 Auth Broker 설계 문서입니다.
Unity 클라이언트와 Jira(Atlassian) 간의 OAuth 3-Legged OAuth(3LO) 인증을 중개합니다.

---

## 1. OAuth 3LO 전체 시퀀스 다이어그램

```
Unity Client          Auth Broker (Edge Fn)       Jira (Atlassian)        Supabase DB
     |                       |                          |                      |
     | POST /connect-jira-start                         |                      |
     | { tenant_id, user_id } |                         |                      |
     |----------------------->|                         |                      |
     |                        | INSERT oauth_connections|                      |
     |                        | (status=pending,        |                      |
     |                        |  state=random_uuid)     |                      |
     |                        |-------------------------------------------->  |
     |                        |                         |              INSERT  |
     |                        |<--------------------------------------------|  |
     | { connect_id,          |                         |                      |
     |   authorize_url }      |                         |                      |
     |<-----------------------|                         |                      |
     |                        |                         |                      |
     | [User opens browser]   |                         |                      |
     | GET authorize_url ---->|                         |                      |
     |   (Jira consent page)  |------------------------>|                      |
     |                        |   redirect to Jira      |                      |
     |                        |                         |                      |
     |                        |   [User grants consent] |                      |
     |                        |                         |                      |
     |                        | GET /connect-jira-callback                     |
     |                        | ?code=AUTH_CODE&state=UUID                     |
     |                        |<------------------------|                      |
     |                        |                         |                      |
     |                        | SELECT oauth_connections|                      |
     |                        | WHERE state=UUID        |                      |
     |                        |-------------------------------------------->  |
     |                        |<--------------------------------------------|  |
     |                        | [state 유효성 검증]      |                      |
     |                        | [만료 체크]              |                      |
     |                        |                         |                      |
     |                        | POST /oauth/token       |                      |
     |                        | { code, redirect_uri,   |                      |
     |                        |   client_id, secret }   |                      |
     |                        |------------------------>|                      |
     |                        |<------------------------|                      |
     |                        | { access_token,         |                      |
     |                        |   refresh_token }       |                      |
     |                        |                         |                      |
     |                        | GET /oauth/token/accessible-resources           |
     |                        |------------------------>|                      |
     |                        |<------------------------|                      |
     |                        | [{ id: cloud_id, ... }] |                      |
     |                        |                         |                      |
     |                        | private.store_refresh_token()                  |
     |                        |-------------------------------------------->  |
     |                        | UPDATE oauth_connections                       |
     |                        | (status=completed,      |                      |
     |                        |  cloud_id, state=null)  |                      |
     |                        |-------------------------------------------->  |
     |                        |<--------------------------------------------|  |
     |                        |                         |                      |
     |   HTML: "연결 완료"     |                         |                      |
     |   [창 자동 닫기]         |                         |                      |
     |<-----------------------|                         |                      |
     |                        |                         |                      |
     | GET /connect-jira-status?connect_id=UUID         |                      |
     |----------------------->|                         |                      |
     |                        | SELECT oauth_connections|                      |
     |                        |-------------------------------------------->  |
     |                        |<--------------------------------------------|  |
     | { status: "completed", |                         |                      |
     |   session_token: JWT } |                         |                      |
     |<-----------------------|                         |                      |
     |                        |                         |                      |
     | [이후 API 호출]          |                         |                      |
     | POST /token-jira       |                         |                      |
     | X-Client-Token: JWT    |                         |                      |
     |----------------------->|                         |                      |
     |                        | JWT 검증                 |                      |
     |                        | private.get_refresh_token()                    |
     |                        |-------------------------------------------->  |
     |                        |<--------------------------------------------|  |
     |                        | POST /oauth/token       |                      |
     |                        | { refresh_token,        |                      |
     |                        |   grant_type }          |                      |
     |                        |------------------------>|                      |
     |                        |<------------------------|                      |
     |                        | { access_token,         |                      |
     |                        |   refresh_token(new) }  |                      |
     |                        |                         |                      |
     |                        | [rotate refresh_token]  |                      |
     |                        | private.store_refresh_token()                  |
     |                        |-------------------------------------------->  |
     | { access_token,        |                         |                      |
     |   expires_at,          |                         |                      |
     |   cloud_id }           |                         |                      |
     |<-----------------------|                         |                      |
```

---

## 2. Edge Function 엔드포인트 설계

### 2.1 POST /functions/v1/connect-jira-start

**목적**: OAuth 흐름 시작, Jira 인증 URL 반환

**요청**:
```json
{
  "tenant_id": "uuid",
  "user_id": "string (Unity external ID)"
}
```

**처리 흐름**:
1. Rate limit 체크 (IP: 30req/min, tenant: 60req/min)
2. tenant 존재 여부 확인 (없으면 자동 생성)
3. user 존재 여부 확인 (없으면 자동 생성)
4. oauth_connections 레코드 생성
   - state: 랜덤 UUID (CSRF 방지)
   - state_expires_at: 현재 + 10분
   - status: pending
5. Jira authorize_url 생성

**응답**:
```json
{
  "connect_id": "uuid",
  "authorize_url": "https://auth.atlassian.com/authorize?..."
}
```

**에러 케이스**:
- 400: 필수 파라미터 누락
- 429: Rate limit 초과
- 500: DB 오류

---

### 2.2 GET /functions/v1/connect-jira-callback

**목적**: Jira OAuth 콜백 처리, 토큰 교환

**요청 (Query Params)**:
```
?code=AUTH_CODE&state=CSRF_UUID
```

**처리 흐름**:
1. state 파라미터로 oauth_connections 조회
2. state_expires_at 만료 체크 (10분 TTL)
3. state 즉시 무효화 (NULL로 업데이트, 1회성)
4. Atlassian 토큰 엔드포인트로 code 교환
5. refresh_token → Vault 암호화 저장
6. accessible-resources API로 cloud_id 획득
7. oauth_connections 상태 completed로 업데이트
8. HTML 응답 반환 (창 닫기 스크립트 포함)

**응답**: HTML (200)
```html
<!DOCTYPE html>
<html>
<body>
  <p>Jira 연결이 완료되었습니다. 이 창을 닫아주세요.</p>
  <script>window.close();</script>
</body>
</html>
```

**에러 케이스**:
- 400: code 또는 state 누락
- 400: state 만료 또는 불일치
- 502: Atlassian API 오류

---

### 2.3 GET /functions/v1/connect-jira-status

**목적**: 연결 상태 폴링

**요청 (Query Params)**:
```
?connect_id=UUID
```

**처리 흐름**:
1. connect_id로 oauth_connections 조회
2. status 반환
3. completed 상태면 JWT 세션 토큰 발급

**응답**:
```json
{
  "status": "pending | completed | error",
  "session_token": "JWT (completed 상태에서만)"
}
```

**에러 케이스**:
- 400: connect_id 누락
- 404: 연결 정보 없음

---

### 2.4 POST /functions/v1/token-jira

**목적**: Jira access_token 갱신 (클라이언트용)

**요청 헤더**:
```
X-Client-Token: <JWT>
Content-Type: application/json
```

**처리 흐름**:
1. X-Client-Token JWT 추출 및 검증
2. JWT에서 tenant_id, user_id 추출
3. Rate limit 체크 (tenant 기준: 60req/min)
4. refreshing_at 락 체크 (5초 TTL, 동시 갱신 방지)
5. Vault에서 refresh_token 조회
6. Atlassian /oauth/token 호출 (grant_type: refresh_token)
7. 새 refresh_token이 있으면 Vault 원자적 갱신
8. refreshing_at 락 해제
9. access_token 반환

**응답**:
```json
{
  "access_token": "string",
  "expires_at": "ISO8601",
  "cloud_id": "string"
}
```

**에러 케이스**:
- 401: JWT 누락 또는 만료
- 404: 연결 정보 없음
- 429: Rate limit 초과
- 502: Atlassian API 오류

---

## 3. DB 스키마 ERD (텍스트)

```
private 스키마
═══════════════════════════════════════════════════════════════

┌─────────────────────────┐
│       tenants           │
├─────────────────────────┤
│ id          UUID PK     │
│ name        TEXT NN     │
│ created_at  TIMESTAMPTZ │
│ updated_at  TIMESTAMPTZ │
└──────────┬──────────────┘
           │ 1
           │
           │ N
┌──────────▼──────────────┐
│        users            │
├─────────────────────────┤
│ id          UUID PK     │
│ tenant_id   UUID FK ──┐ │
│ external_id TEXT NN   │ │  (Unity user ID)
│ display_name TEXT     │ │
│ created_at  TIMESTAMPTZ │
│ updated_at  TIMESTAMPTZ │
│                         │
│ UNIQUE(tenant_id,       │
│        external_id)     │
└──────────┬──────────────┘
           │ 1             └── tenants.id
           │
           │ N
┌──────────▼──────────────────────────────────┐
│           oauth_connections                  │
├──────────────────────────────────────────────┤
│ id               UUID PK                     │
│ user_id          UUID FK ──> users.id        │
│ tenant_id        UUID FK ──> tenants.id      │
│ provider         TEXT DEFAULT 'jira'         │
│ cloud_id         TEXT (Atlassian cloud ID)   │
│ project_key      TEXT                        │
│ scopes           TEXT[]                      │
│ refresh_secret_id UUID (vault.secrets.id)    │
│ status           TEXT ('pending'|'completed' │
│                       |'error')              │
│ state            TEXT (CSRF, 1회용)           │
│ state_expires_at TIMESTAMPTZ                 │
│ refreshing_at    TIMESTAMPTZ (동시갱신 방지)  │
│ created_at       TIMESTAMPTZ                 │
│ updated_at       TIMESTAMPTZ                 │
│                                              │
│ UNIQUE(user_id, provider)                    │
└──────────────────────────────────────────────┘

vault 스키마 (Supabase 내장)
═══════════════════════════════════════════════════════════════

┌─────────────────────────────────────────────┐
│           vault.secrets                      │
├─────────────────────────────────────────────┤
│ id         UUID PK                          │
│ name       TEXT UNIQUE                      │
│            (oauth_refresh:jira:             │
│             {tenant_id}:{user_id})          │
│ secret     TEXT (AES-256 암호화)             │
│ created_at TIMESTAMPTZ                      │
│ updated_at TIMESTAMPTZ                      │
└─────────────────────────────────────────────┘

인덱스
═══════════════════════════════════════════════════════════════
idx_users_tenant        : users(tenant_id)
idx_users_external      : users(external_id)
idx_connections_user    : oauth_connections(user_id)
idx_connections_state   : oauth_connections(state)
```

---

## 4. Vault 사용 패턴

### 4.1 비밀 이름 규칙

```
oauth_refresh:jira:{tenant_id}:{user_id}
```

예시:
```
oauth_refresh:jira:550e8400-e29b-41d4-a716-446655440000:6ba7b810-9dad-11d1-80b4-00c04fd430c8
```

### 4.2 Vault CRUD 패턴

**저장 (원자적 upsert)**:
```sql
-- private.store_refresh_token(tenant_id, user_id, token)
-- 1. 기존 시크릿 삭제 (name으로)
-- 2. 새 시크릿 삽입
-- 3. secret_id 반환
```

**조회**:
```sql
-- private.get_refresh_token(tenant_id, user_id)
-- vault.decrypted_secrets 뷰 사용 (자동 복호화)
```

**삭제**:
```sql
-- private.delete_refresh_token(tenant_id, user_id)
-- 연결 해제 시 사용
```

### 4.3 보안 고려사항

- `SECURITY DEFINER` 함수로만 접근 (직접 vault 접근 차단)
- AES-256-GCM 암호화 (Supabase Vault 기본)
- 서비스 역할 키로만 호출 가능
- refresh_token 로테이션: 새 토큰 수신 즉시 원자적 교체

---

## 5. 보안 체크리스트

### 5.1 인증 및 인가

- [x] CSRF 방지: state 파라미터 (UUID v4, 10분 TTL)
- [x] state 1회성: 사용 즉시 NULL로 무효화
- [x] JWT 서명 검증: HS256, 24시간 만료
- [x] 서비스 역할 키: Edge Function 내에서만 사용
- [x] RLS 활성화: private 스키마 전 테이블

### 5.2 토큰 보안

- [x] refresh_token: Vault AES-256 암호화 저장
- [x] access_token: 메모리에서만 처리, 로그 미기록
- [x] 로그 리댁션: 민감 정보 마스킹 (log-redactor)
- [x] 토큰 로테이션: refresh_token 사용 시 원자적 교체

### 5.3 Rate Limiting

- [x] IP 기반: 30 req/min
- [x] 테넌트 기반: 60 req/min
- [x] 동시 refresh 방지: refreshing_at 락 (5초 TTL)

### 5.4 입력 검증

- [x] tenant_id: UUID 형식 검증
- [x] user_id: 빈 문자열 체크
- [x] code: 길이 및 형식 기본 검증
- [x] state: UUID 형식 검증

### 5.5 에러 처리

- [x] 스택 트레이스 클라이언트 노출 금지
- [x] 일반화된 에러 메시지 사용
- [x] 내부 에러는 서버 로그에만 기록

### 5.6 전송 보안

- [x] HTTPS only (Supabase Edge 기본)
- [x] CORS: 필요한 오리진만 허용
- [x] Content-Type 검증

### 5.7 운영 보안

- [x] 환경 변수로 시크릿 관리 (.env.example 제공)
- [x] 프로덕션 키 코드 미포함
- [x] Vault secret 이름 예측 불가 구조
- [x] 감사 로그: 주요 이벤트 (연결 시작/완료/해제) 기록

---

## 6. 배포 아키텍처

```
[Unity Client]
     │
     │ HTTPS
     ▼
[Supabase Edge Functions]
  ├── connect-jira-start     (POST)
  ├── connect-jira-callback  (GET)
  ├── connect-jira-status    (GET)
  └── token-jira             (POST)
     │
     │ 내부 통신
     ▼
[Supabase DB - PostgreSQL]
  ├── private.tenants
  ├── private.users
  ├── private.oauth_connections
  └── vault.secrets (암호화)
     │
     │ OAuth 2.0
     ▼
[Atlassian Auth Server]
  └── auth.atlassian.com
```

---

## 7. 환경 변수 목록

| 변수명 | 설명 | 필수 |
|--------|------|------|
| `SUPABASE_URL` | Supabase 프로젝트 URL | O |
| `SUPABASE_ANON_KEY` | Supabase 익명 키 | O |
| `SUPABASE_SERVICE_ROLE_KEY` | Supabase 서비스 역할 키 | O |
| `JIRA_CLIENT_ID` | Atlassian OAuth App Client ID | O |
| `JIRA_CLIENT_SECRET` | Atlassian OAuth App Client Secret | O |
| `JIRA_REDIRECT_URI` | OAuth 콜백 URI | O |
| `JWT_SECRET` | JWT 서명 시크릿 (32자 이상) | O |
