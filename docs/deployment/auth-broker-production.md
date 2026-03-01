# Auth Broker 프로덕션 배포 가이드

Bug-OneTouch의 Jira OAuth 2.0 인증을 처리하는 Auth Broker 서버를 프로덕션 환경에 배포하는 방법입니다.

Auth Broker는 Supabase Edge Functions으로 구현되어 있으며, Jira OAuth PKCE 플로우의 보안 중계 역할을 합니다.

---

## 아키텍처 개요

```
Unity Editor                Auth Broker (Supabase)           Jira Cloud
     │                              │                              │
     │  1. OAuth 시작 요청           │                              │
     │──────────────────────────────>│                              │
     │                              │  2. Jira OAuth URL 생성       │
     │                              │──────────────────────────────>│
     │  3. 브라우저 리다이렉트 URL    │                              │
     │<──────────────────────────────│                              │
     │                              │                              │
     │  4. 브라우저에서 Jira 로그인   │                              │
     │                              │  5. 콜백 (code 수신)          │
     │                              │<──────────────────────────────│
     │                              │  6. code → token 교환         │
     │                              │──────────────────────────────>│
     │  7. 암호화된 토큰 전달         │  8. access_token 수신         │
     │<──────────────────────────────│<──────────────────────────────│
     │                              │                              │
```

**Edge Functions 목록:**

| 함수명 | 역할 |
|--------|------|
| `connect-jira-start` | OAuth PKCE 플로우 시작, 인증 URL 반환 |
| `connect-jira-callback` | Jira OAuth 콜백 처리, code → token 교환 |
| `connect-jira-status` | 현재 연결 상태 확인 |
| `token-jira` | 저장된 토큰 갱신 (Refresh Token 사용) |

---

## 사전 준비

### 필수 도구

```bash
# Supabase CLI 설치
npm install -g supabase

# 버전 확인
supabase --version

# Deno 설치 (Edge Functions 로컬 실행용)
# macOS
brew install deno

# 버전 확인
deno --version
```

### 필수 계정

- [Supabase](https://supabase.com) 계정 및 프로젝트
- [Atlassian Developer](https://developer.atlassian.com) 계정 (OAuth App 등록용)

---

## Jira OAuth App 등록

### 1. Atlassian Developer Console 접속

1. [https://developer.atlassian.com/console/myapps/](https://developer.atlassian.com/console/myapps/) 접속
2. `Create` → `OAuth 2.0 integration` 선택
3. 앱 이름 입력: `Bug-OneTouch`

### 2. 권한 설정

`Permissions` 탭에서 다음 권한을 추가합니다:

| API | 권한 |
|-----|------|
| Jira API | `read:jira-work` |
| Jira API | `write:jira-work` |
| Jira API | `read:jira-user` |

> Jira 이슈 생성 및 첨부파일 업로드에는 `write:jira-work` 권한이 필요합니다.

### 3. 콜백 URL 설정

`Authorization` 탭 → `Add callback URL`에 다음 URL을 추가합니다:

```
https://<your-project-ref>.supabase.co/functions/v1/connect-jira-callback
```

로컬 개발용 URL도 추가합니다:

```
http://localhost:54321/functions/v1/connect-jira-callback
```

### 4. 클라이언트 ID 및 시크릿 발급

`Settings` 탭에서 다음 값을 기록합니다:

- `Client ID` (OAuth 앱 클라이언트 ID)
- `Secret` (OAuth 앱 클라이언트 시크릿)

> 시크릿은 한 번만 표시됩니다. 반드시 안전한 곳에 저장하세요.

---

## Supabase 프로젝트 설정

### 1. 프로젝트 생성

1. [https://supabase.com/dashboard](https://supabase.com/dashboard) 접속
2. `New Project` 클릭
3. 프로젝트 정보 입력:
   - **Name**: `bug-onetouch-auth`
   - **Database Password**: 강력한 비밀번호 설정 (기록 필수)
   - **Region**: 팀이 위치한 지역 근처 선택 (예: Northeast Asia)
4. `Create new project` 클릭
5. 프로젝트 생성 완료 후 Project Settings → API에서 다음 값을 기록합니다:
   - `Project URL` (예: `https://abcdefghijkl.supabase.co`)
   - `anon key` (공개 키)
   - `service_role key` (관리자 키, 절대 클라이언트에 노출 금지)

### 2. Supabase CLI로 프로젝트 연결

```bash
# 저장소 루트에서 실행
cd /path/to/Bug-OneTouch-unity

# Supabase 로그인
supabase login

# 원격 프로젝트 연결
supabase link --project-ref <your-project-ref>
```

---

## 환경 변수 설정

### 필수 환경 변수

Supabase Dashboard → Settings → Edge Functions → Secrets에서 다음 환경 변수를 설정합니다:

| 변수명 | 값 | 설명 |
|--------|-----|------|
| `JIRA_CLIENT_ID` | Atlassian OAuth App Client ID | Jira OAuth 앱 클라이언트 ID |
| `JIRA_CLIENT_SECRET` | Atlassian OAuth App Client Secret | Jira OAuth 앱 시크릿 (절대 공개 금지) |
| `JIRA_CALLBACK_URL` | `https://<ref>.supabase.co/functions/v1/connect-jira-callback` | OAuth 콜백 URL |
| `TOKEN_ENCRYPTION_KEY` | 32바이트 랜덤 Base64 문자열 | 토큰 암호화 키 (AES-256) |
| `SUPABASE_URL` | `https://<ref>.supabase.co` | Supabase 프로젝트 URL |
| `SUPABASE_SERVICE_ROLE_KEY` | Supabase service_role key | 관리자 권한 키 |

### 환경 변수 설정 방법 (CLI)

```bash
# Supabase CLI로 시크릿 설정
supabase secrets set JIRA_CLIENT_ID=your_client_id
supabase secrets set JIRA_CLIENT_SECRET=your_client_secret
supabase secrets set JIRA_CALLBACK_URL=https://your-ref.supabase.co/functions/v1/connect-jira-callback
supabase secrets set TOKEN_ENCRYPTION_KEY=$(openssl rand -base64 32)
supabase secrets set SUPABASE_SERVICE_ROLE_KEY=your_service_role_key

# 설정된 시크릿 목록 확인 (값은 표시되지 않음)
supabase secrets list
```

### TOKEN_ENCRYPTION_KEY 생성

```bash
# 32바이트(256비트) 랜덤 키 생성
openssl rand -base64 32
```

> 이 키를 잃어버리면 저장된 모든 토큰을 복호화할 수 없습니다. 반드시 안전한 곳에 백업하세요.

### 로컬 개발용 .env 파일

`.env.local` 파일을 생성하고 Git에 커밋하지 않도록 `.gitignore`에 추가합니다:

```bash
# supabase/.env.local (절대 Git에 커밋하지 말 것)
JIRA_CLIENT_ID=your_development_client_id
JIRA_CLIENT_SECRET=your_development_client_secret
JIRA_CALLBACK_URL=http://localhost:54321/functions/v1/connect-jira-callback
TOKEN_ENCRYPTION_KEY=your_local_encryption_key_base64_32bytes
SUPABASE_URL=http://localhost:54321
SUPABASE_SERVICE_ROLE_KEY=your_local_service_role_key
```

---

## Edge Functions 배포

### 1. 로컬 테스트

```bash
# Supabase 로컬 환경 시작
supabase start

# Edge Functions 로컬 실행 (별도 터미널)
supabase functions serve --env-file ./supabase/.env.local

# 테스트 요청 (curl)
curl -X POST http://localhost:54321/functions/v1/connect-jira-start \
  -H "Content-Type: application/json" \
  -d '{"callback_port": 12345}'
```

### 2. 프로덕션 배포

```bash
# 모든 Edge Functions 한 번에 배포
supabase functions deploy

# 특정 함수만 배포
supabase functions deploy connect-jira-start
supabase functions deploy connect-jira-callback
supabase functions deploy connect-jira-status
supabase functions deploy token-jira

# 배포 상태 확인
supabase functions list
```

### 3. 배포 확인

```bash
# 배포된 함수 헬스 체크
curl https://<your-project-ref>.supabase.co/functions/v1/connect-jira-status \
  -H "Authorization: Bearer <anon-key>"
```

---

## DNS 설정

### 커스텀 도메인 설정 (선택 사항)

기본 Supabase URL(`*.supabase.co`) 대신 커스텀 도메인을 사용하려면:

1. Supabase Dashboard → Settings → Custom Domains
2. `Add Custom Domain` 클릭
3. 도메인 입력 (예: `auth.yourgame.com`)
4. DNS 설정에 CNAME 레코드 추가:

```
TYPE  NAME              VALUE
CNAME auth.yourgame.com  <your-project-ref>.supabase.co
```

5. TLS 인증서 자동 발급 완료 후 커스텀 URL 사용 가능

### Unity 플러그인 설정 업데이트

커스텀 도메인 또는 Supabase URL을 `BugOneTouchSettings`의 `Auth Broker URL`에 입력합니다:

```
https://auth.yourgame.com/functions/v1
```

또는 기본 Supabase URL:

```
https://<your-project-ref>.supabase.co/functions/v1
```

---

## 모니터링 및 로깅

### Supabase Dashboard 모니터링

1. **Edge Functions 로그**: Dashboard → Edge Functions → 각 함수 선택 → Logs
   - 실시간 로그 확인
   - 오류 메시지 필터링 가능

2. **API 사용량**: Dashboard → Reports → API
   - 요청 수, 응답 시간, 오류율 확인

3. **데이터베이스 모니터링**: Dashboard → Reports → Database
   - 연결 수, 쿼리 성능 확인

### 알림 설정

Supabase Dashboard → Settings → Alerts에서 다음 항목에 대한 알림을 설정합니다:

| 알림 항목 | 임계값 | 알림 채널 |
|-----------|--------|-----------|
| Edge Function 오류율 | 5% 이상 | 이메일, Slack |
| 데이터베이스 연결 수 | 80% 이상 | 이메일 |
| API 응답 시간 | 2초 이상 | Slack |

### 외부 모니터링 (권장)

Uptime Robot 또는 Better Uptime을 사용하여 주요 엔드포인트의 가용성을 모니터링합니다:

```
# 모니터링할 URL 목록
https://<your-project-ref>.supabase.co/functions/v1/connect-jira-status
```

---

## 백업 전략

### 데이터베이스 백업

Supabase는 프로 플랜 이상에서 자동 일별 백업을 제공합니다.

**수동 백업 (pg_dump 방식):**

```bash
# Supabase DB 연결 문자열 확인
supabase db remote --project-ref <your-project-ref>

# pg_dump로 전체 백업
pg_dump -h db.<your-project-ref>.supabase.co \
        -U postgres \
        -d postgres \
        --no-password \
        -f backup_$(date +%Y%m%d_%H%M%S).sql
```

**백업 주기 권장 사항:**

| 환경 | 백업 주기 | 보관 기간 |
|------|-----------|-----------|
| 프로덕션 | 매일 자동 | 30일 |
| 스테이징 | 주 1회 | 7일 |

### 환경 변수 백업

프로덕션 환경의 시크릿 값은 별도의 안전한 저장소(예: 1Password, AWS Secrets Manager)에 백업합니다.

---

## 보안 체크리스트

배포 전 반드시 확인해야 할 보안 항목입니다.

### 환경 변수 보안

- [ ] `JIRA_CLIENT_SECRET` Git에 커밋되지 않음 확인
- [ ] `TOKEN_ENCRYPTION_KEY` Git에 커밋되지 않음 확인
- [ ] `SUPABASE_SERVICE_ROLE_KEY` 클라이언트 코드에 포함되지 않음 확인
- [ ] `.env.local` 파일 `.gitignore`에 등록 확인

### Supabase 보안 설정

- [ ] RLS(Row Level Security) 활성화 확인
- [ ] anon key 권한 최소화 (읽기 전용으로 제한)
- [ ] 불필요한 테이블/컬럼 외부 접근 차단

### Jira OAuth App 보안

- [ ] 콜백 URL에 로컬호스트 URL 외 불필요한 URL 없음 확인
- [ ] 최소 필요 권한만 부여 확인
- [ ] 사용하지 않는 OAuth App 비활성화

### 네트워크 보안

- [ ] HTTPS 통신만 허용 (HTTP → HTTPS 리다이렉트)
- [ ] CORS 설정 올바름 확인
- [ ] Rate Limiting 설정 확인

---

## 트러블슈팅

### 문제: OAuth 콜백 후 "invalid_grant" 오류

**원인:** PKCE code_verifier와 code_challenge 불일치, 또는 authorization code 만료

**해결:**
1. Unity 에디터에서 Jira 연결을 재시도합니다
2. 브라우저 캐시를 지우고 다시 시도합니다
3. `connect-jira-start` 함수 로그에서 PKCE 관련 오류 메시지를 확인합니다

### 문제: Edge Function 응답 없음 (504 Timeout)

**원인:** Jira API 응답 지연 또는 Edge Function cold start

**해결:**
1. Supabase Dashboard에서 해당 함수의 실행 로그를 확인합니다
2. Jira Cloud 상태 페이지([https://jira-software.status.atlassian.com](https://jira-software.status.atlassian.com))에서 장애 여부를 확인합니다
3. Edge Function의 타임아웃 설정을 늘립니다 (최대 10초)

### 문제: "Token decryption failed" 오류

**원인:** `TOKEN_ENCRYPTION_KEY`가 변경되거나 손상됨

**해결:**
1. Supabase Secrets에서 `TOKEN_ENCRYPTION_KEY` 값이 올바른지 확인합니다
2. 키가 변경된 경우, 저장된 토큰이 모두 무효화됩니다. 사용자들이 Jira를 재연결해야 합니다
3. Unity 에디터에서 Jira 연결 해제 후 재연결을 안내합니다

### 문제: Supabase Functions 배포 실패

**원인:** Deno 버전 불일치, import 오류, 타입 오류

**해결:**
```bash
# 로컬에서 타입 체크
supabase functions serve --inspect-mode brk

# 배포 로그 상세 출력
supabase functions deploy connect-jira-start --debug
```
