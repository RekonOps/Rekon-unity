# AI Agent 연결 & 'Agent 연결 당 과금' 전환 전략

> Rekon에 AI Agent 봇을 연결하고, 현재 **'사용자 시트 당 과금'** 모델을 **'Agent 연결 당 과금'** 모델로
> 전환하기 위한 설계·전략 문서입니다. 본 문서는 **설계(design)** 문서이며, 이 저장소(`rekon-unity`)의 코드를
> 변경하지 않습니다. 실제 구현 작업은 저장소별 작업 항목([7장](#7-저장소별-작업-항목-work-items))으로 분해합니다.

| 항목 | 내용 |
|------|------|
| 작성 목적 | (1) AI Agent 봇 연결 방식 설계, (2) 시트→에이전트 과금 모델 전환 설계 |
| 연결 범위 | (A) 헤드리스 자동 QA 봇 + (B) 외부 AI 어시스턴트(MCP/API) **둘 다** |
| 산출물 | 전략·설계 문서 (코드 변경 없음) |
| 상태 | Draft / 검토용 |

---

## 1. 개요 & 현황 진단

### 1.1 현재 아키텍처

Rekon는 **JAM.dev(Web Proxy) 패턴**을 사용합니다. Unity 플러그인은 외부 시스템에 직접 연결하지 않고
항상 웹 백엔드를 경유합니다.

```
Unity 플러그인 (이 저장소, C# SDK)
    └─> 웹 백엔드 (Web Proxy API)          ← Rekon-backend (Supabase Edge Functions + PostgreSQL)
            └─> Cloudflare R2 (파일 저장)
            └─> Supabase DB (메타데이터)
                    └─> 웹 대시보드        ← Rekon-web (Next.js)
                            └─> Jira Cloud
```

- **이 저장소(`rekon-unity`)**: Unity 클라이언트 SDK(C#). 캡처/제출/인증 클라이언트 로직.
- **`Rekon-backend`**: 인증·라이선스·리포트·과금 서버 로직(범위 밖).
- **`Rekon-web`**: 대시보드 UI·결제 화면(범위 밖).

### 1.2 현재 인증 모델 (사람 중심)

```
디바이스 ID ──> POST auth-unity-start ──> connect_id + login_url
                                              │ (브라우저에서 사람이 로그인)
            <── GET auth-unity-status ────────┘
                  access_token + workspace_id  ──> SessionTokenStore (AES-256-CBC 암호화 저장)
```

- 모든 인증이 **브라우저 기반 OAuth 폴링**이다. → **비대화형(headless) 에이전트는 로그인할 수 없다.**
- 장수명 API 키 / 서비스 계정 개념이 **없다**. (`Runtime/Auth/SupabaseAuthClient.cs`, `Runtime/Auth/SessionTokenStore.cs`)

### 1.3 현재 과금 모델 (사용자 시트 당)

- `Runtime/Auth/LicenseValidator.cs`가 `POST /api/unity/validate-license`로 엔타이틀먼트를 받아 캐시·검증.
- 플랜: `free`(max_seats=1) / `team`(무제한) / `team_pro`(무제한 + 고급 기능).
- `LicenseInfo.MaxSeats`는 **nullable int**(`null`=무제한)로 처리됨.
- **결제 프로세서(Stripe 등)는 클라이언트에 없음.** 결제·구독은 전부 백엔드/대시보드가 관리.
- 사용량 쿼터 초과 시 `Runtime/Services/ReportSubmitService.cs`의 `UsageLimitExceededException`
  → HTTP **429** + `code: "usage_limit_exceeded"` + `upgradeUrl` 패턴으로 SDK에 전달됨.

### 1.4 핵심 공백 (Gap)

| # | 공백 | 영향 |
|---|------|------|
| G1 | 비대화형(머신) 인증 부재 | 자동 QA 봇이 SDK로 로그인 불가 |
| G2 | API 키/서비스 계정 개념 부재 | 외부 AI 어시스턴트에 발급할 자격증명 없음 |
| G3 | 과금 단위가 '사용자 시트'에 고정 | '에이전트 연결' 단위 과금 불가 |
| G4 | 결제 프로세서(Stripe) 미연동 | per-agent 구독/미터링 청구 수단 없음 |
| G5 | 활성 연결(active connection) 추적 부재 | '연결 당' 과금의 측정 기반 없음 |

---

## 2. 통합 개념: "Agent"를 1급(first-class) 엔티티로 도입

워크스페이스(`tenantId`) 안의 **비인간 주체(non-human principal)** 를 단일 개념 `Agent`로 모델링한다.
헤드리스 QA 봇과 외부 AI 어시스턴트를 **하나의 과금 단위**로 통합하기 위함이다.

```
Workspace (tenantId)
 ├─ User      (userId)        ← 사람 (기존)
 └─ Agent     (agentId)       ← 비인간 주체 (신규, 새 과금 단위)
       ├─ type: "qa_bot"        (Type A: 헤드리스 SDK 클라이언트)
       └─ type: "ai_assistant"  (Type B: 외부 AI / MCP)
```

**Agent 엔티티 (백엔드 스키마 설계안):**

| 필드 | 타입 | 설명 |
|------|------|------|
| `agent_id` | UUID | 에이전트 식별자 (과금/추적 키) |
| `workspace_id` | UUID | 소속 워크스페이스(`tenantId`) |
| `name` | text | 사람이 읽는 이름 (예: "CI QA Bot", "Claude Triage") |
| `type` | enum | `qa_bot` \| `ai_assistant` |
| `credential_hash` | text | 발급 키의 해시(평문 미저장) |
| `scopes` | text[] | 권한 범위 (예: `reports:write`, `reports:read`, `jira:push`) |
| `status` | enum | `active` \| `revoked` \| `suspended`(미납 등) |
| `created_at` / `last_seen_at` | timestamptz | 발급 시각 / 마지막 활성(heartbeat) 시각 |

> **재사용:** 기존 `BOT-XXXX-XXXX-XXXX-XXXX` 라이선스 키 포맷 컨벤션과 `userId`/`tenantId` 멀티테넌트 구조를
> 그대로 활용하여 신규 개념을 최소화한다. 키 prefix를 `AGT-`(또는 기존 `BOT-`)로 표준화한다.

---

## 3. 연결 아키텍처 — Type A: 헤드리스 자동 QA 봇 (Inbound)

### 3.1 시나리오

헤드리스 Unity / CI 파이프라인 / 디바이스 팜에서 봇이 게임을 구동하며 Rekon SDK로 버그를
**사람 개입 없이** 자동 캡처·제출한다. 사람 QA 테스터를 대체/보완하는 비대화형 SDK 클라이언트다.

### 3.2 문제

현재 인증은 브라우저 로그인을 요구(§1.2)하므로 봇이 인증할 수 없다(G1).

### 3.3 해결: 머신-투-머신(Client Credentials 유사) 인증

```
1. 대시보드(Rekon-web)에서 워크스페이스 범위 "Agent Key" 발급
       → AGT-xxxx-xxxx-xxxx-xxxx  (1회 노출, 이후 해시만 저장)

2. 봇/SDK 가 Agent Key 로 단수명 토큰 교환 (브라우저 없음)
       POST /api/unity/agent/auth
       Body:    { agent_key, device_id }
       Resp:    { access_token(JWT, 단수명), agent_id, workspace_id, expires_in }

3. 이후 리포트 제출은 기존 흐름과 동일하되 agent_id 를 태깅
       POST /api/unity/reports        (Authorization: Bearer <agent JWT>)
       → R2 업로드 → confirm

4. 연결 유지: 주기적 heartbeat 로 활성 상태 갱신
       POST /api/unity/agent/heartbeat   → last_seen_at 갱신 (과금 측정 기반)
```

### 3.4 SDK 측 향후 터치포인트 *(설계만 — 본 작업에서 미수정)*

| 변경 지점 | 내용 | 재사용 패턴 |
|-----------|------|-------------|
| `RekonSettings` | `agentKey`(또는 환경변수 `REKON_AGENT_KEY`) 필드 추가 | `Runtime/Settings/RekonSettings.cs`의 `tenantId`/`userId` |
| 신규 헤드리스 인증 클라이언트 | Agent Key→JWT 교환, 브라우저 플로우 우회 | `SupabaseAuthClient`/`SessionTokenStore` 구조 복제 |
| 리포트 태깅 | `agent_id`를 제출 페이로드/매니페스트에 포함 | `ReportSubmitService.ReportSubmitRequest`, `BundleManifest` |
| heartbeat | 기존 폴링 인프라로 주기 전송 | `PendingUploadManager`/폴링 패턴 |

> CI 친화성을 위해 Agent Key는 **환경변수 주입**을 1순위로 지원한다(시크릿을 ScriptableObject에 커밋 금지).

---

## 4. 연결 아키텍처 — Type B: 외부 AI 어시스턴트 (Outbound, MCP/API)

### 4.1 시나리오

Claude 등 외부 AI 어시스턴트가 Rekon 리포트를 **조회·분류(triage)·생성**한다.
예: "어제 들어온 크래시 리포트를 묶어서 우선순위를 매기고 Jira에 등록해줘."

### 4.2 인터페이스: Rekon MCP 서버 (+ 공개 REST API)

```
외부 AI (Claude 등)
   └─(MCP)─> Rekon MCP 서버  ──(REST)──> Rekon-backend 공개 API ──> 리포트 DB / Jira
```

**MCP 도구(설계안):**

| 도구 | 설명 | 권장 scope |
|------|------|-----------|
| `list_reports` | 워크스페이스 리포트 목록/필터 | `reports:read` |
| `get_report` | 단일 리포트 상세(메타+아티팩트 링크) | `reports:read` |
| `triage_report` | 우선순위/라벨/중복판정 갱신 | `reports:write` |
| `create_report` | 리포트 생성(외부 소스 유입) | `reports:write` |
| `push_to_jira` | Jira 이슈 등록 | `jira:push` |

### 4.3 인증 & 과금 매핑

- 연결된 AI 어시스턴트 1개 = **Agent 연결 1개**(`type: ai_assistant`).
- 인증: 워크스페이스 범위 API 키(§2) 또는 OAuth 앱. 모든 호출은 `agent_id` 스코프로 기록.
- **위치:** MCP 서버 + 공개 API는 **별도 서비스/백엔드**(이 저장소 아님) → [7장](#7-저장소별-작업-항목-work-items) 작업 항목.

---

## 5. 과금 모델 전환: 사용자 시트 당 → Agent 연결 당

핵심 질문 — **부여 / 체크 / 결제** 세 축으로 정리한다.

### 5.1 부여 (Assign / Provision)

- 백엔드에 `agent` 엔티티(§2) 도입, 대시보드에서 생성/폐기 UI로 자격증명 발급.
- 엔타이틀먼트: `max_seats` → **`max_agents`** 로 대체/병행.
  기존 `LicenseInfo.MaxSeats`의 **nullable-int 처리 패턴을 그대로 미러링**하여 `MaxAgents`(`null`=무제한)를 추가.
- `validate-license` 응답에 `max_agents`, 현재 `active_agents` 수를 포함.
- 플랜 재정의(예시):

| 플랜 | 포함 에이전트 | 추가 에이전트 단가 | 비고 |
|------|--------------|-------------------|------|
| Free | 1 | — | 평가용 |
| Team | 3 | $X / agent·월 | 사람 시트와 분리/병행 |
| Team Pro | 10 | $Y / agent·월 | 고급 기능 + 우선 한도 |

### 5.2 체크 (Check / Meter / Enforce)

- **프로비저닝 한도:** 등록(`/api/unity/agent/auth` 최초 등록) 시 `max_agents` 초과면 거부.
- **활성 추적:** heartbeat(§3.3) → **TTL 기반 활성 에이전트 집합** 유지(`last_seen_at`).
  월 피크 동시 활성 수 / 기간 내 활성 에이전트 수를 산정 → 미터링 기반.
- **SDK 전달:** `LicenseValidator`를 에이전트 엔타이틀먼트 인지하도록 확장. 한도 초과 시
  기존 **429 + `upgradeUrl`**(`UsageLimitExceededException`) 패턴을 재사용하여 일관된 UX 유지.
- **미터링 파이프라인:** 사용 이벤트(에이전트 등록/heartbeat/리포트 제출/API 호출) → 집계 테이블 → 결제 연동(§5.3).

### 5.3 결제 (Collect)

현재 결제 프로세서가 없으므로 **Stripe 신규 도입**이 전제다(G4).

| 옵션 | 방식 | 장점 | 단점 |
|------|------|------|------|
| **1. Licensed quantity** | `subscription.item.quantity` 를 에이전트 증감에 동기화 | 예측 가능, 청구 단순 | 동시성/실사용 반영 약함 |
| **2. Metered billing** | 활성 에이전트-시간/리포트량을 Stripe usage records로 보고, 기간 말 청구 | 사용량 기반, 공정 | 청구액 변동성, 구현 복잡 |
| **3. 하이브리드 (권장)** | 약정 per-agent 수량(옵션1) + 초과분 metered(옵션2) | 예측성 + 공정성 균형 | 두 경로 모두 구현 필요 |

- **Webhook 처리:** `customer.subscription.updated`(수량/플랜 변경), `invoice.paid`,
  `invoice.payment_failed`(dunning) → 미납 시 `agent.status = suspended`로 **에이전트 접근 토글**.
- **흐름 요약:**

```
대시보드에서 에이전트 추가/삭제
   └─> Stripe subscription.item.quantity 동기화 (옵션1)
사용 이벤트(heartbeat/리포트) 집계
   └─> Stripe usage records 보고 (옵션2/3)
기간 말 인보이스 생성 ── 결제 성공 → status=active
                         결제 실패 → dunning → status=suspended → agent JWT 발급 거부
```

### 5.4 '연결(connection)'의 과금 정의 — 권장

'Agent 연결 당'의 **연결**을 무엇으로 셀지 명확히 정의해야 한다.

| 정의 | 측정 | 권장도 |
|------|------|--------|
| 프로비저닝된 에이전트 수 | 등록된 `agent` 행 수 | 청구 단순, 1차 권장 |
| 동시 활성(peak concurrent) | heartbeat TTL 기준 월 피크 | 동시성 반영, 하이브리드 상한 |
| 순수 사용량(metered) | 리포트 수/에이전트-시간 | 저사용 고객 유리, 초과분에 적용 |

> **권장:** "**프로비저닝된 활성 에이전트 / 월**"을 기본 단위로, "**리포트량 초과분 metered**"를 가산하는 하이브리드.

---

## 6. 마이그레이션 전략 (시트 → 에이전트)

- **전환 방식:**
  - *듀얼-런(권장)*: 전환기에 사람 시트 과금과 에이전트 과금을 **병행**. 기존 고객 충격 최소화.
  - *전면 컷오버*: 일괄 전환 + 기존 고객 **그랜드페더링**(가격 보호) 제공.
- **기존 고객 매핑:** 초기 `max_agents` 기본값 설정, Stripe 구독 아이템 이전, 가격/혜택 커뮤니케이션 플랜.
- **SDK 하위호환:** 사람 OAuth 경로는 **그대로 유지**, 에이전트 경로는 **가산적(additive)** 으로 추가하여
  기존 사용자 영향 0.

---

## 7. 저장소별 작업 항목 (Work Items)

> 이 문서는 설계만 담는다. 실제 구현은 아래로 분해된다. **굵은 항목**이 선행 의존성(critical path)이다.

### `Rekon-backend` (Supabase) — 대부분의 핵심 작업
- [ ] **`agent` 테이블/엔티티 + scopes/status 모델**
- [ ] **`POST /api/unity/agent/auth` (Agent Key → 단수명 JWT 교환)**
- [ ] `POST /api/unity/agent/heartbeat` + 활성 추적(TTL)
- [ ] `validate-license` 응답에 `max_agents` / `active_agents` 추가
- [ ] **Stripe 연동 + webhook(`subscription.updated`/`invoice.paid`/`invoice.payment_failed`)**
- [ ] 사용 이벤트 집계 → Stripe usage records (하이브리드)
- [ ] 외부 AI용 공개 REST API + scope 기반 인가

### `Rekon-web` (Next.js) — 대시보드/결제 UI
- [ ] 에이전트 관리 UI(생성/폐기/키 1회 노출)
- [ ] 빌링 페이지(플랜·에이전트 수·인보이스·dunning 안내)
- [ ] **Rekon MCP 서버**(또는 별도 서비스)와 도구 정의(§4.2)

### `rekon-unity` (이 저장소) — **본 작업 범위 아님, 향후 티켓**
- [ ] `RekonSettings.agentKey`(+ 환경변수) 필드
- [ ] 헤드리스 인증 클라이언트(브라우저 우회) — `SupabaseAuthClient` 패턴 복제
- [ ] 리포트에 `agent_id` 태깅 — `ReportSubmitRequest`/`BundleManifest` 확장
- [ ] `LicenseValidator`/`LicenseInfo`에 `MaxAgents`(nullable) 추가 + 429/`upgradeUrl` 재사용

---

## 부록 A. 참고한 기존 코드 (재사용/연계 지점)

| 파일 | 재사용 포인트 |
|------|--------------|
| `Runtime/Auth/LicenseValidator.cs` | `LicenseInfo`, `max_seats` nullable 처리 → `max_agents` 미러링 |
| `Runtime/Auth/SupabaseAuthClient.cs` | 토큰 교환/폴링 구조 → 머신 인증 복제 |
| `Runtime/Auth/SessionTokenStore.cs` | AES-256 토큰 저장 → 에이전트 JWT 저장 |
| `Runtime/Services/ReportSubmitService.cs` | `ReportSubmitRequest`, `UsageLimitExceededException`(429+`upgradeUrl`) |
| `Runtime/Bundle/BundleManifest.cs` | `agent_id`/통합 메타 확장 지점 |
| `Runtime/Settings/RekonSettings.cs` | `tenantId`/`userId` 멀티테넌트 → 향후 `agentKey` |

## 부록 B. 미해결 결정 사항 (검토 필요)

1. **과금 단위 최종 확정**: 프로비저닝 수 vs 동시 활성 vs 하이브리드(§5.4 권장).
2. **사람 시트 vs 에이전트의 관계**: 완전 대체인지, 병행 청구인지.
3. **무료 한도**: Free 플랜에 에이전트 1개 제공 여부(평가 경험).
4. **Agent Key 수명/회전 정책**: 만료·회전·폐기 주기.
5. **MCP 서버 호스팅**: `Rekon-web` 내부 vs 독립 서비스.
