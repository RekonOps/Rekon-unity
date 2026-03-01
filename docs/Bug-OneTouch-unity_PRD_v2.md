# Bug-OneTouch-unity PRD v2.0 — Unity 로컬-퍼스트 버그 리포팅 플러그인 (Jira Cloud 전용)

- **문서 버전**: v2.2 (크래시 자동 감지 및 복구 플로우 반영)
- **작성일**: 2026-02-27 (KST), **검수일**: 2026-02-28
- **제품 코드명**: **Bug-OneTouch-unity** (가칭)
- **대상 엔진**: **Unity (우선)**
- **연동 대상**: **Jira Cloud (단독 지원)**
- **핵심 원칙**: **로컬-퍼스트(Local-first) + 포털 없음(Portal-less) + 유저 명의 이슈 생성(User identity only)**
- **변경 이력**: v1.0 (2026-02-23) → v2.0 (2026-02-27) — Jira Cloud 단독 연동으로 통합(타 이슈 트래커 제거), 시장 분석/가격 전략/GTM/리스크 매트릭스 신규 추가

---

## 0. 핵심 요약

Unity에서 결함을 발견하면 **핫키 1번**으로

- **직전 60초 영상(로컬 저장 기본)** + **스크린샷 + 로그 + 상태 스냅샷**을 자동 수집하여 **로컬 번들**로 보관하고,
- "리포트 제출" 버튼을 누르면 **Jira Cloud에 즉시 이슈를 생성**한다.
- 웹 포털은 없으며(= 우리 서비스에 버그 데이터 저장 없음), Jira Cloud OAuth 3LO 제약 때문에 **인증/토큰 갱신만 담당하는 얇은 Auth Broker**를 Supabase로 제공한다.
- OAuth 토큰(특히 refresh token)은 **Supabase Vault에 '회사(tenant)별 레코드'**로 저장한다.

**시장 포지션**: 한국 게임 시장(세계 4위, 22.9조 원)에서 Unity+Jira 교집합 대상으로 **직접 경쟁자가 사실상 부재한 블루오션** 포지션. 5개 분석 모델 종합 사업 가능성 점수 **7.2/10**, **Conditional GO** 판단.

---

## 1. 배경과 문제 정의

### 1.1 배경

게임 개발/테스트 과정에서 버그 리포트가 늦거나 불완전해지는 가장 큰 이유는:

- 리포팅이 번거롭고(작성 시간/양식/증거 수집),
- 환경/상태/로그/증거가 부족해 **재현이 안 되거나(= 재현 불가)**,
- 담당자가 "원인 좁히기"까지 가기 위해 추가 커뮤니케이션이 반복되기 때문이다.

웹 서비스 영역에서는 Jam.dev가 "증거(영상) + 컨텍스트 자동 수집 + 링크 공유 + 이슈 트래커 주입" 패턴으로 강한 제품-시장 적합성을 보여줬다. Bug-OneTouch-unity은 이 아이디어를 **게임 엔진(특히 Unity) 개발/테스트 워크플로우**에 맞게 재해석한다.

한국 게임 개발 현장의 실태를 보면, 대부분의 중소 스튜디오는 OBS 화면 녹화 + 로그 수집 스크립트 + Jira/Redmine 수동 등록 조합으로 버그를 리포팅하고 있다. 이는 비용은 낮지만 재현 일관성, 속도, 표준화가 떨어지는 구조다. QA 성숙도는 5단계 기준 평균 2~3단계 수준으로, "재현 자동화" 도구의 문제-해결 적합성이 높은 시장이다.

### 1.2 문제 정의

- QA/개발/기획 누구나 "보고 바로 남길 수 있는" 리포팅 도구가 부족하다.
- 리포트 품질이 들쑥날쑥해 "원인 파악에 충분한 정보"가 빠지기 쉽다.
- 특히 "재현 불가"가 많아지면 리포트가 무시되거나 우선순위가 떨어진다.
- 팀이 이미 Jira를 쓰고 있어도, **"증거/컨텍스트 수집"은 여전히 사람의 노동**이다.

---

## 2. 시장 분석

> 본 섹션은 Sonnet 4.6, Haiku 4.5, Opus 4.6, Codex(codex-5.3-spark-xhigh), 웹 리서치 에이전트 5개 모델의 분석을 종합한 결과이다. 분석 기준일: 2026년 2월 27일.

### 2.1 한국 게임 시장 현황

#### 시장 규모 핵심 지표

| 지표 | 수치 | 출처 |
|------|------|------|
| 한국 게임산업 전체 매출 | 22조 9,642억 원 (2023, +3.4%) | KOCCA 게임백서 2024 |
| 세계 시장 내 순위 | 4위 | KOCCA |
| 게임 제작·배급업 사업체 수 | 약 1,287~1,334개 | 통계청 / KOCCA |
| 전체 게임산업 종사자 수 | 약 84,970명 | KOCCA 게임백서 2024 |
| 게임 제작·배급업 종사자 수 | 약 51,783명 | KOCCA 게임백서 2024 |
| 중소기업 비중 | 90% 이상 | 통계청 분석 |
| 모바일 게임 매출 비중 | 59.3% | KOCCA 게임백서 2024 |
| PC 게임 매출 비중 | 25.6% | KOCCA 게임백서 2024 |
| 콘솔 게임 매출 비중 | 4.9% | KOCCA 게임백서 2024 |

모바일 비중이 59.3%로 높아 Unity 친화적 시장 특성이 강하다. 한국은 글로벌 게임 시장에서 세계 4위라는 규모를 보유하면서도, 사업체의 90% 이상이 중소기업인 구조다. 이는 Bug-OneTouch-unity의 초기 타겟인 SMB 대상 제품-시장 적합성이 높음을 시사한다.

#### Unity 사용률 교차 검증 (추정)

한국 게임사 Unity 점유율 공식 통계는 부재하나, 5개 모델의 교차 분석을 통해 추정 범위를 산출했다:

| 분석 모델 | Unity 사용률 추정 |
|-----------|----------------|
| Sonnet 4.6 | 60~75% |
| Haiku 4.5 | 모바일 60%+, 상위 1000개 모바일 71% |
| Opus 4.6 | 55~65% |
| Codex | 45~65% (모바일 캐주얼 스튜디오 더 높음) |
| 웹 리서치 | 한국 Google Play 상위 50위 수익 게임 중 56% |

**합의 범위: 55~75%, 중간값 65%** → 약 1,287~1,334개 사업체 중 **약 838개사** 추정

#### Jira 사용률 (추정)

| 데이터 | 수치 | 출처 |
|--------|------|------|
| 글로벌 개발자 Jira 사용률 | 57.5% (1위) | Stack Overflow Developer Survey 2024 |
| 한국 SMB(10~80인) 추정 Jira 사용률 | 25~40% | Codex 분석, Jira 무료 플랜 진입장벽 반영 |
| Jira Cloud 전환 추정 사업체 | 250~400개 | Opus 4.6 / Codex 분석 |
| 크래프톤 Jira Cloud 운영 | 확인됨 | 공개 정보 |

#### KOCCA 인디게임 지원 생태계

2025년 KOCCA 인디게임 지원: 76개 프로젝트, 219억원 투자 규모. Bug-OneTouch-unity Free 티어를 활용하여 KOCCA 지원 프로젝트 대상 레퍼런스 고객을 확보하는 채널로 활용 가능하다.

### 2.2 타겟 시장 규모 (TAM / SAM / SOM)

5개 분석 모델의 TAM/SAM/SOM 추정치는 기준이 상이하여 범위가 넓다. 아래는 합의 범위 기준이다.

#### TAM / SAM / SOM 시각화

```
[글로벌 TAM]
Unity 기반 스튜디오 전 세계: 약 150만+ 개발자
게임 버그 리포팅 SaaS 시장: $50M~$200M/년 (추산)
                    ↓
[한국 SAM]
Unity + Jira Cloud 교집합 한국 사업체: 약 250~400개사
연간 잠재 수익: 약 $300K~$500K/년
(팀당 $750~$2,000/년 × 팀 규모 가정)
                    ↓
[SOM - 현실적 목표]
1년차: $10K~$100K (10~20개 레퍼런스 고객)
2년차: $50K~$150K (한국 확장 + 글로벌 초기)
3년차: $150K~$400K (Atlassian Marketplace 등재 후)
```

#### Codex 분석 기준 TAM/SAM/SOM (좌석 기반)

Codex 모델은 좌석 기반 계산으로 별도 추정치를 제시했다. 아래는 공통 계산 템플릿으로 재검증한 결과이다:

**공통 계산식**: `타겟 스튜디오 수 × 유료 좌석 수 × 월 단가 × 12개월`

| 구간 | 스튜디오 수 | 좌석 수 | 월 단가 | 연간 산출 | 결과 |
|------|-----------|---------|---------|----------|------|
| TAM 하한 | 500개사 | 8석 | 8,000원 | 500 × 8 × 8,000 × 12 | **3.84억 원** |
| TAM 상한 | 700개사 | 15석 | 12,000원 | 700 × 15 × 12,000 × 12 | **15.12억 원** |

- **TAM**: 연 **3.84억~15.12억 원** (타겟 스튜디오 500~700개 × 유료 좌석 8~15석 × 월 8,000~12,000원)
- **SAM**: TAM의 25~35% → 연 **0.96억~5.29억 원** (초기 2~3년 실질 접근 가능)
- **SOM 3년**: 40~80개사 확보 시 **연 0.74억~1.73억 원 ARR**
  - 산출: 40개사 × 8석 × 8,000원 × 12 = 0.31억, 80개사 × 15석 × 12,000원 × 12 = 1.73억 (중간값 약 1억 원)

> **v2.0 정오**: 기존 v2.0 초안의 TAM/SAM/SOM 수치(38억~151억)는 계산 과정에서 10배 오류가 포함되어 있었으며, 본 버전에서 공통 계산 템플릿 기준으로 재산출하여 수정하였다.

#### 시나리오별 수익 전망

| 시나리오 | 조건 | 1년차 ARR | 2년차 ARR | 3년차 ARR |
|---------|------|----------|----------|----------|
| **보수적** | 한국 단독, 자연 성장 | $12K (약 1,700만 원) | $35K (약 5,000만 원) | $90K (약 1.3억 원) |
| **기본** | 한국 + Atlassian Marketplace | $25K (약 3,600만 원) | $80K (약 1.1억 원) | $220K (약 3.2억 원) |
| **낙관적** | 글로벌 진출 + 파트너십 | $50K (약 7,200만 원) | $180K (약 2.6억 원) | $500K+ (약 7.3억 원+) |

> 주: 한국 단독 SOM 3년 누적 기준 $100K~$400K 범위가 가장 보수적이고 합리적인 추정치. 지속 가능한 사업을 위해 글로벌 확장 설계가 필수.

### 2.3 경쟁 환경 분석

#### 직접 경쟁자: 사실상 부재 (블루오션)

5개 분석 모델 모두 **Unity 게임 개발에 특화된 로컬-퍼스트 버그 리포팅 플러그인 형태의 직접 경쟁자는 없음**을 확인했다. 이는 Bug-OneTouch-unity이 시장 선점 효과를 노릴 수 있는 블루오션 포지션임을 의미한다.

#### 간접 경쟁자 / 대안 도구 상세 분석

| 도구 | 유형 | 가격 | 한국 게임업계 적합성 | 핵심 약점 |
|------|------|------|------------------|---------|
| **Backtrace (Sauce Error Reporting)** | 크래시 분석 | 무료~유료 | 중간 (크래시 전용) | 60초 재현 영상+상태 스냅샷 원클릭 UX 부재 |
| **Sentry** | 범용 에러 모니터링 | $26/월(Team) | 낮음 (게임 비특화) | 게임 QA 현장(플레이 재현/핫키 워크플로우) 최적화 제한 |
| **Jam.dev** | 웹 버그 리포팅 | $14/user/월 | 낮음 (웹 전용) | Unity 런타임 로그/게임 상태 스냅샷 지원 없음 |
| **Unity Bug Reporter** | 기본 내장 | 무료 | 중간 (기능 제한) | 60초 영상/Jira 연동 없음 |
| **인하우스 도구** | 자체 개발 | 개발 비용 | 일부 대형사 | 개발/유지 비용 높음 (데브시스터즈 사례) |
| **OBS + 스크립트 + Jira 수동** | 수동 조합 | 무료 | 중소사 현실 | 재현 일관성/속도/표준화 부재 |

#### 핵심 경쟁 구도

**Bug-OneTouch-unity의 진짜 경쟁 상대는 "현상 유지(수작업)"다.** 대부분의 중소 개발사가 구글시트, OBS 녹화, 수동 스크린샷으로 버그를 리포팅하고 있어, 습관 변화 저항이 가장 큰 진입 장벽이다.

#### 데브시스터즈 사례의 시사점

데브시스터즈가 구글시트 → 트렐로 → Jira 순으로 도구를 업그레이드한 패턴은, 한국 중소 게임사들도 **성장 단계에 따라 도구 업그레이드 의향이 있음**을 시사한다. Bug-OneTouch-unity은 이 업그레이드 사이클의 자연스러운 다음 단계로 포지셔닝 가능하다.

### 2.4 차별화 포인트

Bug-OneTouch-unity의 핵심 차별화는 3박자 조합이다:

1. **Unity 특화**: 게임 엔진 런타임 플로우(핫키 → 게임 상태 스냅샷 → 로그)에 직접 통합
2. **로컬-퍼스트**: 영상/로그 외부 서버 경유 최소화 → 보안 민감 한국 게임사에 유리
3. **60초 영상**: 재현 불가 버그를 시각적으로 포착하여 디버깅 컨텍스트 완전 제공

Backtrace(크래시 전용), Sentry(범용 모니터링), Jam.dev(웹 QA 전용) 모두 이 3박자를 동시에 충족하지 못한다.

---

## 3. 목표 / 비목표

### 3.1 목표

1. **리포팅 시간 단축**: "발견 → 리포트 제출"까지 평균 시간을 크게 줄인다.
2. **재현 불가 감소**: 최소한 "증거(영상/스크린샷) + 환경/상태/로그"를 강제하여 재현 실패 확률을 낮춘다.
3. **누구나 리포팅 가능**: QA뿐 아니라 기획/개발도 쉽게 리포팅할 수 있게 한다.
4. **Jira Cloud에 즉시 주입**: 팀이 이미 쓰는 BTS에 바로 이슈가 생성되게 한다.
5. **보안/도입장벽 최소화**: 웹 포털을 없애고, 버그 데이터는 로컬 및 고객 BTS에만 존재하게 한다.
6. **글로벌 설계 내재화**: MVP부터 영어 기반으로 개발하여 추후 글로벌 확장 마찰을 최소화한다.

### 3.2 비목표 (MVP에서 하지 않는다)

- 웹 포털(리포트 리스트/검색/대시보드) 제공
- 타 이슈 트래커 연동 (Jira Cloud 단독 집중, 게임 업계 Jira 점유율 기반 판단)
- 중복 판정/이슈 추세 분석 (해당 영역은 Jira 기능 활용)
- Jira "봇 계정" 기반 생성 (서비스 계정/프로젝트별 봇) — MVP에서는 유저 명의(User mode)만 지원
- 크래시 수집/성능 APM 수준 (Backtrace/Sentry 대체)
- 모든 플랫폼(콘솔 포함) 완전 지원 — 플랫폼별 영상 캡처는 단계적으로 확장

---

## 4. 타겟 사용자 / 페르소나

### 4.1 1차 타겟 시장

- **한국 중소 → 중견 → 대형** 게임 개발사 (프로세스는 있으나 리포팅 생산성이 낮은 조직)
- 이미 **Jira Cloud**를 사용하고 있으며, 리포팅 품질/속도 개선 니즈가 존재
- **Unity** 엔진 기반 개발팀 (모바일 캐주얼/MMORPG/인디게임 등)

### 4.2 타겟 세그먼트별 특성

| 세그먼트 | 팀 규모 | 특성 | Bug-OneTouch-unity 적합성 |
|---------|--------|------|----------------|
| 인디/소규모 | 1~5인 | 도구 예산 극소, Unity Personal 무료 | Free 티어로 진입 |
| 중소 스튜디오 | 5~20인 | QA팀 1~3인, Jira 도입 의향 | Pro 티어 주력 타겟 |
| 중견 개발사 | 20~80인 | QA 조직 분리, 보안 민감 | Studio 티어, 웹 포털 부재 약점 보완 필요 |
| 대형 개발사 | 80인+ | 인하우스 도구 있음, SSO/SLA 요구 | Enterprise, 도입 결정 주기 김 |

### 4.3 페르소나

**페르소나 A — QA 테스터 (김지연, 중소 스튜디오 QA 2년차)**
- 재현 불가/정보 부족으로 리포트가 반려되는 경험이 많다.
- OBS 녹화 → 클립 편집 → Jira 수동 입력의 반복 작업이 피로하다.
- 핫키 한 번으로 증거가 자동 수집되길 원한다.

**페르소나 B — 클라이언트 개발자 (이민준, 중소 스튜디오 클라이언트 4년차)**
- 리포트 1건으로 원인 추정이 가능하길 원한다(로그/상태/환경).
- QA가 남긴 리포트에 로그가 없어 재현에 수 시간을 낭비한다.
- 상태 스냅샷(Scene, 프레임레이트, 메모리)이 자동 포함되길 원한다.

**페르소나 C — 기획자/PM (박서영, 중소 스튜디오 기획 3년차)**
- 리포트 작성이 번거로워 직접 등록을 미루는 경우가 많다.
- Jira를 쓰지만 버그 발견 시 Slack으로만 알리고 등록은 QA에 위임한다.
- 간단하게 캡처해서 Jira에 바로 올릴 수 있는 도구가 필요하다.

---

## 5. 핵심 가치 제안

1. **"재현 불가를 놓치지 않기"**: 직전 60초 증거 확보 + 상태 스냅샷으로 컨텍스트 보존
2. **"리포팅의 마찰 제거"**: 자동 수집 + 템플릿 + 즉시 이슈 생성
3. **"담당자가 원인을 빠르게 좁히게"**: 표준화된 환경/빌드/로그/상태 묶음 제공
4. **"데이터 주권 보장"**: 로컬-퍼스트 설계로 영상 데이터가 외부 서버 경유 최소화

---

## 6. 사용자 경험 (UX) — 핵심 플로우

### 6.1 최초 설정 (1회)

1. Unity 프로젝트에 Bug-OneTouch-unity 패키지 설치 (Unity Package Manager)
2. **(옵션) Unity 계정 기반 로그인/활성화**
3. "Connect Jira" 버튼 → 브라우저에서 Jira OAuth 동의
4. Jira Site 선택(1개) → Jira Project 선택(수동, 1개) → Issue Type 선택
5. 기본 템플릿/옵션 저장

> MVP 제약: 사용자 1명은 Jira 1개 프로젝트만 사용한다고 가정.

### 6.2 버그 발견 → 캡처 (핫키)

- 사용자가 게임 플레이(또는 에디터 플레이) 중 결함 발견
- 핫키(예: F12) → **즉시 스크린샷 캡처** + **리포트 오버레이 UI 표시**
  - **[MVP 제외]** 모바일 런타임 디버그 버튼은 Phase B 이후 확장 범위 (섹션 8.5.2 참조)
- 동시에 "직전 60초 영상"을 리포트 번들로 확정 저장(로컬)

### 6.3 리포트 작성 → 이슈 생성

1. 제목/설명/재현 스텝/기대·실제/심각도 입력
2. 기본 첨부: 스크린샷 + 로그 + 상태 스냅샷
3. (옵션) 영상 첨부 ON/OFF
4. "Submit to Jira" 클릭
5. 성공 시: 이슈 링크 표시 + 클립보드 복사

### 6.4 실패 시 기본 정책

- **Auth 실패 / 토큰 만료 / 연결 끊김** → "Re-authenticate" 안내 및 버튼 제공
- 나머지 오류(권한 부족, 업로드 제한 등):
  - 로컬 번들은 유지(데이터 유실 금지)
  - 사용자에게 Jira 설정 확인 안내(최소 메시지)
  - 재시도는 동일 버튼으로 가능

### 6.5 플레이 모드 / 에디터 모드 동작 구분

Bug-OneTouch-unity은 Unity의 두 가지 실행 컨텍스트에서 서로 다른 기능 집합을 활성화한다. Unity는 `#if UNITY_EDITOR` 전처리 지시자와 `Application.isEditor` 런타임 체크를 통해 에디터 전용 코드와 런타임 코드를 분리한다.

#### 플레이 모드 (Play Mode / Standalone Build) — 핵심 캡처 기능 활성

게임이 실행 중인 컨텍스트(에디터 내 Play 버튼 클릭 또는 빌드된 Standalone 실행). **버그 재현 증거 수집의 핵심 기능**이 이 모드에서 동작한다.

| 기능 | Unity API / 구현 방식 |
|------|------|
| 60초 영상 링버퍼 | `UnityEngine.Video` 또는 FFMPEG 래퍼 + `RenderTexture` 기반 프레임 버퍼 |
| 스크린샷 캡처 | `ScreenCapture.CaptureScreenshotAsTexture()` 또는 `ReadPixels()` 기반 즉시 캡처 |
| 로그 수집 | `Application.logMessageReceived` 콜백 후킹으로 실시간 로그 링버퍼 구성 |
| 상태 스냅샷 | `Time.realtimeSinceStartup`, `Application.version`, `SystemInfo`, `Scene.name`, 활성 `GameObject` 상태 직렬화 |
| 핫키 트리거 | `Input.GetKeyDown()` 또는 신규 `InputSystem.Keyboard.current[key].wasPressedThisFrame` 기반 |

> 에디터 내 Play Mode에서는 `Application.isEditor == true`이지만 게임 로직은 실제 실행된다. Standalone 빌드에서는 `Application.isEditor == false`. 두 경로 모두 동일한 캡처 코드 경로를 사용하되, 에디터 Play Mode에서는 `UnityEditor` 네임스페이스 호출이 추가로 가능하다.

#### 에디터 모드 — 설정/관리 기능만 활성

게임이 실행되지 않은 상태(Unity Editor가 열려 있지만 Play 중이 아닌 상태). **캡처 기능은 비활성화**되며, 설정 및 관리 기능만 동작한다.

| 기능 | Unity API / 구현 방식 |
|------|------|
| Jira 연결 설정 | `EditorWindow` 기반 커스텀 설정 패널, `EditorPrefs` 또는 `ScriptableObject`로 설정 저장 |
| 캡처 설정 | `ScriptableObject` 기반 설정 에셋(`BugOneTouchSettings.asset`) — 핫키, 링버퍼 크기, 해상도 설정 |
| 번들 관리 | `EditorWindow` 기반 이전 캡처 번들 목록 조회, 재전송, 삭제 |
| 플러그인 설정 | 로그 마스킹 규칙(`LogFilter ScriptableObject`), 보존 정책 설정 |

> 에디터 전용 코드는 `Assets/Editor/` 폴더 또는 Assembly Definition의 `Editor` 플랫폼 제한으로 격리된다. `#if UNITY_EDITOR` 가드를 통해 Standalone 빌드에 에디터 코드가 포함되지 않도록 보장한다.

#### 모드별 기능 활성화 요약

| 기능 | 에디터 모드 | Play Mode | Standalone |
|------|:---:|:---:|:---:|
| 60초 영상 링버퍼 | - | O | O |
| 스크린샷 캡처 | - | O | O |
| 로그 링버퍼 | - | O | O |
| 상태 스냅샷 | - | O | O |
| 핫키 트리거 | - | O | O |
| Jira 연결 설정 | O | - | - |
| 캡처/링버퍼 설정 | O | - | - |
| 번들 관리 패널 | O | - | - |
| 로그 마스킹 설정 | O | - | - |

### 6.6 크래시 자동 감지 및 복구 플로우

Bug-OneTouch-unity은 플레이 모드 중 크래시가 발생하더라도 버그 재현 데이터를 최대한 보존하고, 에디터 재실행 후 Jira에 즉시 등록할 수 있는 자동 복구 플로우를 제공한다.

#### 6.6.1 크래시 시 데이터 보존 전략 (3중 레이어)

**레이어 1 — 주기적 플러시** **(Must)**

크래시 발생 여부와 무관하게, 플레이 모드 동안 설정된 주기에 따라 링버퍼 데이터를 디스크에 저장한다.

| 대상 | 기본 플러시 주기 | 저장 방식 |
|------|:----------:|---------|
| 로그 링버퍼 | 5초 | `crash_log.txt` 덮어쓰기 |
| 상태 스냅샷 | 10초 | `state_snapshot.json` 덮어쓰기 |
| 영상 슬라이딩 윈도우 | 30초 | `replay_buffer` 세그먼트 교체 |

- 저장 경로: `Application.persistentDataPath/BugOneTouch/crash_bundles/`
- 영상은 슬라이딩 윈도우 방식으로 최대 60초 분량을 유지하며, 30초 단위로 세그먼트를 교체한다.
- 구현 시 `MemoryMappedFile`을 활용하여 OS 레벨 캐싱으로 크래시 시에도 데이터 보존율을 높인다. Unity 2022.3.22f1 미만에서는 macOS/Linux 호환성 문제로 일반 `FileStream` 폴백을 사용한다.

**레이어 2 — Managed 예외 즉시 번들 생성** **(Should)**

- `Application.logMessageReceived` 콜백에서 `LogType.Exception`을 감지하면 즉시 `CrashBundleWriter.WriteCrashBundle()`을 호출한다.
- 감지 즉시 현재 링버퍼 데이터 전체를 크래시 번들로 패키징하고 `registered: false` 플래그로 저장한다.
- NullReferenceException, IndexOutOfRangeException 등 C# Managed 예외에 대해 ~95% 보존율을 기대할 수 있다.

**레이어 3 — abnormal_exit.flag 비정상 종료 감지** **(Must)**

- 플레이 모드 진입 시(`OnEnable`): `abnormal_exit.flag` 파일을 생성한다.
- 정상 종료 시(`OnApplicationQuitting`): `abnormal_exit.flag` 파일을 삭제한다.
- 다음 에디터 시작 시 플래그 파일이 잔존하면 비정상 종료(크래시, 강제 종료, 프리즈 등)로 판단하고, `crash_bundles` 폴더를 스캔하여 미처리 번들을 복구 대상으로 마킹한다.

#### 6.6.2 크래시 유형별 보존 범위

| 크래시 유형 | 감지 방법 | 데이터 보존율 | 비고 |
|------------|----------|:----------:|------|
| Managed 예외 (NullRef 등) | logMessageReceived | ~95% | 즉시 번들 생성 가능 |
| Native 크래시 (SIGSEGV) | abnormal_exit.flag | ~70% | 마지막 플러시까지만 |
| 에디터 전체 다운 | abnormal_exit.flag | ~70% | 주기적 플러시 의존 |
| OOM / GPU Hang | abnormal_exit.flag | ~50% | 플러시 실행 불가할 수 있음 |

#### 6.6.3 에디터 재실행 후 복구 UI

에디터가 재실행되면 `[InitializeOnLoad]` 어트리뷰트를 통해 `CrashBundleScanner`가 자동 실행된다.

1. `crash_bundles` 폴더를 스캔하여 `manifest.json`의 `registered: false`인 항목을 필터링한다.
2. 미등록 크래시 번들이 존재하면 에디터 하단 상태바에 토스트 알림을 표시한다.
3. 사용자가 [확인]을 클릭하면 크래시 리포트 목록 창(`CrashRecoveryWindow`)이 열린다.

**크래시 리포트 목록 UI (EditorWindow)**:

- 시간순 정렬 (최신 먼저, 타임스탬프 내림차순)
- 각 항목 구성:
  - 헤더: `yyyy-mm-dd hh:mm:ss — 크래시 리포트`
  - 크래시 메시지 요약 (1줄)
  - 데이터 보존 상태 배지: 영상(초) / 로그(완전|부분|없음) / 상태(완전|부분|없음)
  - 썸네일: `last_screenshot.png` 프리뷰 (120x80)
  - 액션 버튼: [Jira에 등록] [로컬에서 열기] [삭제]

**[Jira에 등록] 클릭 시**:

- 기존 버그 리포트 작성 UI(`BugReportWindow`)를 재활용한다.
- 제목 자동 생성: `[Crash] {crash_type}: {crash_message 첫 50자}`
- 설명에 스택 트레이스 및 시스템 정보 자동 삽입
- 첨부파일: 크래시 번들을 ZIP으로 압축하여 단일 파일로 첨부 + 스크린샷(last_screenshot.png)은 Jira 설명에 인라인 이미지로 삽입

#### 6.6.4 크래시 번들 구조

```
crash_{yyyy-MM-dd_HH-mm-ss}.bot-unity/
├── manifest.json          # 크래시 메타데이터
├── crash_log.txt          # 스택 트레이스 + 직전 500줄 로그
├── last_screenshot.png    # 크래시 직전 마지막 프레임
├── replay_buffer.mp4      # 보존된 영상 (최대 60초)
├── state_snapshot.json    # 마지막 상태 스냅샷
└── system_info.json       # OS/GPU/Unity버전/프로젝트 정보
```

#### 6.6.5 manifest.json 크래시 전용 필드

일반 번들의 `manifest.json` 필드(섹션 8.2.2)에 더하여 크래시 번들은 아래 전용 필드를 포함한다.

| 필드명 | 타입 | 설명 |
|--------|------|------|
| `crash_type` | string | `"managed_exception"` \| `"native_crash"` \| `"out_of_memory"` \| `"gpu_hang"` \| `"unknown"` |
| `crash_message` | string | 크래시 메시지 첫 줄 |
| `stack_trace` | string | 전체 스택 트레이스 |
| `data_integrity` | object | 파일별 보존 상태: `"complete"` \| `"partial"` \| `"missing"` |
| `auto_saved` | boolean | 항상 `true` (자동 저장 번들 식별자) |
| `registered` | boolean | 기본값 `false`, Jira 등록 완료 시 `true`로 갱신 |
| `jira_issue_key` | string | Jira 등록 완료 시 기록 (예: "GAME-123"), 미등록 시 null |
| `registered_at` | string | ISO 8601 형식 Jira 등록 완료 시각, 미등록 시 null |

---

## 7. 제품 범위 (Scope)

### 7.1 Unity 플러그인 (클라이언트)

**핵심 구성**
- Capture Engine (스크린샷/영상/로그/상태)
- Local Bundle Manager (저장/보관/재시도)
- Report UI (오버레이/설정)
- Jira Cloud API Client
- Auth / Connection UI (브로커 연동 및 상태 표시)

### 7.2 Auth Broker (Supabase)

- Jira Cloud OAuth 3LO의 code→token 교환, refresh token 갱신(회전) 처리
- **버그 데이터 저장 없음** (영상/스크린샷/로그는 브로커로 업로드하지 않음)
- 토큰만 저장(보안 요구사항 준수)

---

## 8. 상세 요구사항

### 8.1 캡처 요구사항 (MUST)

#### 8.1.1 스크린샷

- 캡처 트리거 시점의 화면을 PNG/JPG로 저장
- 기본 해상도: 현재 렌더링 해상도(필요 시 다운스케일 옵션)
- 저장 위치: 로컬 번들 폴더 내 `screenshot.png`

#### 8.1.2 영상 (리플레이 버퍼 기반) — 로컬 저장 기본

- 기본값: **"캡처 시점 기준 직전 60초"**를 파일로 확정 저장
- 저장 기본, 첨부는 옵션
- 기본 품질 프리셋(예시):
  - 720p / 30fps / H.264 / 목표 비트레이트(예: 8~12 Mbps)
  - 옵션: 1080p 프리셋
- 목표: 일반적인 60초 영상이 **200MB 이하**가 되도록 비트레이트 캡 제공
- 성능 요구:
  - 게임 플레이 중 프레임 드랍 최소화(링버퍼/비동기 인코딩)
  - 캡처 시점 저장 확정은 "빠르게" 완료되어야 함(사용자 체감)

> 플랫폼 지원(영상):
> - MVP: Windows Standalone + Editor 우선 (가장 구현 난이도 낮은 경로)
> - 차후: Android/iOS는 플랫폼별 인코더/레코더 적용(Phase 확장)

#### 8.1.3 로그

- 최소 포함:
  - Unity Console 로그(최근 N줄)
  - Player.log(가능한 플랫폼에서)
- 전송 전 처리:
  - 로그 압축(zip) 옵션(기본 ON)
  - 민감정보 마스킹(간단한 정규식 기반, MVP에서는 최소 수준)
- 저장 위치: `logs/` 폴더 및 `logs.zip`

#### 8.1.4 상태 스냅샷 (State Snapshot)

MVP 기본 수집(커스터마이징 없이 자동):

- 앱/빌드 정보: 게임 버전, 빌드 번호, Git commit hash(가능 시), 브랜치(가능 시), Unity 버전
- 런타임 환경: OS/디바이스/CPU/GPU/메모리/해상도/품질 설정
- 실행 컨텍스트: 현재 Scene 이름, Time.time, 프레임레이트 샘플(간단)
- 네트워크(선택): 연결 타입/간단한 ping(가능 시, MVP는 Optional)

확장 포인트(개발자 주입):

- `BugOneTouchContext.Add(key, value)` 형태로 커스텀 K/V 추가
- `IContextProvider` 인터페이스로 프로젝트별 컨텍스트 프로바이더 등록

저장 위치: `state/state.json`

#### 8.1.5 크래시 자동 캡처 (MUST)

크래시 발생 시 버그 데이터가 자동으로 보존되도록 아래 요구사항을 준수한다.

- 주기적 플러시 간격은 설정 가능하며 기본값은 아래와 같다:
  - 로그: 5초
  - 상태 스냅샷: 10초
  - 영상 슬라이딩 윈도우: 30초
- 크래시 번들 최대 보관 수: **10개** (FIFO — 초과 시 가장 오래된 번들부터 삭제)
- 보관 기간: **30일** (기본값, 설정 가능)
- 크래시 번들은 `Application.persistentDataPath/BugOneTouch/crash_bundles/` 경로에 저장된다.
- 크래시 번들 구조 및 `manifest.json` 필드 상세는 섹션 6.6.4, 6.6.5 참조.

---

### 8.2 로컬 번들 요구사항 (MUST)

#### 8.2.1 번들 구조 (예시)

```
Bug-OneTouch-unity/
  Reports/
    2026-02-27_153012_AB12CD/
      manifest.json
      screenshot.png
      video.mp4               # optional (기본 저장)
      logs.zip
      state/
        state.json
      attachments/            # optional
```

#### 8.2.2 manifest.json (MUST)

필수 필드:
- `report_id` (UUID)
- `created_at`
- `engine` / `engine_version`
- `app_version` / `build_number`
- `platform` / `device` / `os`
- `scene`
- `title` / `description` / `repro_steps` / `expected` / `actual` / `severity`
- `artifacts`: [ {type, path, size_bytes, sha256} … ]
- `integrations`: { jira: {project_key, issue_type} }

#### 8.2.3 보관 정책 (기본값)

- 번들은 기본적으로 **삭제하지 않음** (데이터 유실 방지)
- 디스크 보호를 위한 설정 제공:
  - 최대 보관 개수(예: 200개) 또는 최대 디스크 사용량(예: 5GB)
  - 초과 시 "가장 오래된 번들부터" 정리(사용자 확인 옵션)

#### 8.2.4 재시도 큐 (Offline Queue)

- 제출 실패 시 번들은 `status=pending` 유지
- "Retry submit" 기능 제공(단일 버튼)
- 성공 시 `status=submitted`로 기록하고 생성된 이슈 URL 저장

---

### 8.3 Jira Cloud 연동 (MUST) — 유저 명의만 지원

#### 8.3.1 연결 (Connect)

- OAuth 2.0 (3LO)로 유저가 Jira Cloud에 동의
- 연결 완료 후:
  - Site(Cloud ID) 선택 (유저 1개만 가정)
  - Project 선택 (수동, 1개)
  - Issue Type 선택 (수동, 1개)

#### 8.3.2 이슈 생성 (Create Issue)

이슈 본문 템플릿(예시 섹션):
- Summary
- Repro Steps
- Expected vs Actual
- Environment / Build / State Snapshot (요약 + state.json 링크/첨부)
- Artifacts(스크린샷/로그/영상) 목록 및 크기

필드:
- labels(기본: `bug-onetouch-unity`, `unity`)
- priority/severity 매핑(간단 매핑)
- component(선택)

#### 8.3.3 첨부 (Attachments)

- 기본 첨부: screenshot.png, logs.zip, state.json(또는 state.zip)
- 옵션 첨부: video.mp4
- 업로드 전:
  - Jira 업로드 제한(meta) 확인 후 초과 시 영상 자동 제외(또는 다운스케일/재인코딩은 Phase 확장)
- 업로드 실패:
  - 번들은 로컬에 유지
  - 에러 메시지 표시(권한/제한 안내 최소)

#### 8.3.4 토큰 갱신 (Refresh)

- refresh token은 회전(rotating)이므로, refresh 응답에서 새 refresh token이 오면 반드시 교체 저장
- 연속 refresh 경쟁 방지를 위해 "연결 단위 락" 적용(브로커에서 처리)

---

### 8.4 Auth Broker (Supabase) 요구사항 (MUST)

#### 8.4.1 목적

Jira Cloud 3LO는 클라이언트 단독 구현(특히 데스크톱/모바일)에서 안전하게 secret을 숨기기 어렵기 때문에 code→token 교환 및 refresh를 서버에서 처리한다.

**핵심 원칙**: 버그 데이터는 저장하지 않는다(토큰만 저장).

#### 8.4.2 Supabase 구성

- **Edge Functions**: OAuth 시작/콜백/토큰 발급
- **Database (private schema)**: tenant/user/connection 메타데이터
- **Vault**: refresh token 저장(회사 tenant별 레코드)

#### 8.4.3 데이터 모델 (개념)

**`private.tenants`**
- `id` (uuid)
- `name` (company name)
- `created_at`

**`private.users`**
- `id` (uuid)
- `tenant_id`
- `unity_user_id` (MVP: Unity 계정 식별자)
- `email` (옵션)
- `created_at`

**`private.oauth_connections`**
- `id` (uuid)
- `tenant_id`
- `user_id`
- `provider` (`jira`)
- `provider_account_id` (예: cloudId)
- `scopes`
- `refresh_secret_id` (uuid, vault secret reference)
- `metadata` (jsonb: project_key, issue_type 등)
- `updated_at`

**`vault.secrets`**
- 이름 규칙 예: `oauth_refresh:jira:{tenant_id}:{user_id}`

> 중요한 운영 원칙: Vault 복호화 view 접근 권한은 서비스 역할(서버)만 가진다.
> 클라이언트/일반 사용자 권한으로는 접근 불가.

#### 8.4.4 브로커 API (MVP 최소)

- `POST /connect/jira/start` → 반환: `connect_id`, `authorize_url`
- `GET /connect/jira/status?connect_id=...` → 반환: `pending | completed | error`
- `GET /connect/jira/callback` (OAuth redirect endpoint)
- `POST /token/jira` → 입력: `session_token` (헤더), `tenant_id`, `user_id` / 출력: `access_token`, `expires_at`, `cloud_id`, `project_key`, etc.

> 구현 팁: 플랫폼/디바이스 대응을 위해 "브라우저 기반 연결 + 상태 polling" 형태가 Unity Runtime에서도 안정적이다.

#### 8.4.4-1 클라이언트 → 브로커 인증 모델 (MUST)

브로커 API는 토큰 발급/갱신을 수행하므로, 클라이언트 자체의 신원을 서버 측에서 검증해야 한다. MVP에서는 다음 세션 토큰 기반 인증을 적용한다:

**인증 플로우**:

```
[Unity 플러그인]                         [Auth Broker (Supabase)]
      │                                           │
      │ 1. POST /connect/jira/start               │
      │   Headers: X-Client-Token: <session_token> │
      │──────────────────────────────────────────→ │
      │                                           │ 2. session_token 검증
      │                                           │    (Supabase Auth JWT 또는
      │                                           │     API Key + tenant_id 매칭)
      │ 3. connect_id, authorize_url              │
      │ ←────────────────────────────────────────── │
      │                                           │
      │ 4. POST /token/jira                       │
      │   Headers: X-Client-Token: <session_token> │
      │   Body: { tenant_id, user_id }            │
      │──────────────────────────────────────────→ │
      │                                           │ 5. session_token 검증
      │                                           │    + tenant_id/user_id 소유권 확인
      │                                           │    (요청자가 해당 tenant/user인지)
      │ 6. access_token, expires_at, ...          │
      │ ←────────────────────────────────────────── │
```

**세션 토큰 발급 및 관리**:
- 최초 OAuth 연결(`/connect/jira/start`) 성공 시, 브로커가 세션 토큰(JWT, 유효기간 24시간)을 발급
- 세션 토큰에는 `tenant_id`, `user_id`, `exp`(만료 시간)이 서버 서명으로 포함
- 이후 모든 브로커 API 호출 시 `X-Client-Token` 헤더로 전달
- 브로커는 매 요청마다: (1) JWT 서명 검증, (2) 만료 확인, (3) body의 tenant_id/user_id와 토큰 내 클레임 일치 확인
- 세션 토큰 만료 시: 클라이언트에 401 반환 → Unity 플러그인이 재인증 플로우 안내

**보안 요구사항**:
- 세션 토큰은 Supabase Auth 서명 키로 서명 (HS256 또는 RS256)
- tenant_id/user_id를 body로 보내도, 서버는 반드시 세션 토큰 내 클레임과 교차 검증
- Rate limiting: tenant별 분당 60회, IP별 분당 30회
- 세션 토큰은 Unity 플러그인 로컬 보안 저장소(EditorPrefs 암호화 또는 OS Keychain)에 보관

#### 8.4.5 보안 요구사항 (필수)

- 브로커는 토큰/시크릿만 처리하며, 버그 데이터는 저장하지 않는다.
- `state` 값 강제(랜덤, TTL, 1회성)로 CSRF 방지
- 토큰/시크릿/사용자 PII를 로그에 남기지 않는다(레드랙션)
- Vault 사용 및 private schema로 데이터 API 노출 최소화
- 회전 refresh token 저장 갱신을 원자적으로 처리(동시성 락)

---

### 8.5 인증/계정 — Unity 계정 기반 (MVP)

#### 8.5.1 목표

"사용자 1명 / 회사 1테넌트" 전제에서 최소한의 사용자 식별과 라이선싱을 가능하게 한다.

#### 8.5.2 MVP 정책

- Unity Editor에서 사용자는 이미 Unity 계정으로 로그인하는 경우가 많다.
- MVP에서는:
  - Unity 계정 식별자(또는 Unity 프로젝트/조직 정보)를 기반으로 내부 `user_id`를 생성/매핑
  - 회사 테넌트는 최초 활성화 시 생성(수동 입력: 회사명)
- 런타임(빌드)에서 Unity 계정이 없을 수 있으므로:
  - MVP에서는 "런타임 제출"은 추후 확장으로 두고, 우선 Editor/PC dev build 중심으로 출시 가능

> 주: Unity 계정 SSO 구현 난이도/제약이 발생할 수 있으므로, Phase 확장 시 이메일/매직링크 등의 대체 인증 옵션을 추가한다.

---

## 9. 가격 전략

### 9.1 가격 구조

5개 분석 모델의 권고를 종합한 SMB 친화형 좌석 기반(seat-based) 구독 구조:

| 티어 | 가격 | 대상 | 포함 기능 |
|------|------|------|---------|
| **Free** | 무료 | 인디/1~3인 팀, 월 50건 이하 | 월 50건 리포트, 30초 영상, 기본 Jira 연동 |
| **Pro** | $9/seat/월 (약 12,000원) | 중소 팀 (5~20인) | 무제한 리포트, 60초 영상, 전체 Jira 연동, 이메일 지원 |
| **Studio** | $15/seat/월 (약 20,000원) | 중견 개발사 (20~80인) | Pro 기능 + SSO, 감사 로그, 우선 지원, SLA |
| **Enterprise** | 협의 | 대형 개발사 (80인+) | 커스텀 배포, 온프레미스 옵션, 전담 CS, 커스텀 계약 |

**연간 결제 할인**: 월 기준 대비 20% 할인 (연간 일시 결제 시)

**가격 설정 근거**:
- Jam.dev $14/user 대비 저렴하게 진입 장벽을 낮춤
- Sentry Team $26/월보다 게임 특화 가치를 낮은 가격에 제공
- 한국 SMB 툴 비용 민감도 반영 → "좌석 기반 저가 + 빠른 ROI" 구조
- KOCCA 인디 지원 프로젝트 대상 Free 티어로 레퍼런스 확보

### 9.2 PLG (Product-Led Growth) 전환 설계

Free → Pro 전환 트리거:
- 팀 규모 증가 (3인 초과 시 Pro 필요)
- 월 리포트 한도 초과 (50건)
- 영상 길이 한도 초과 (30초 → 60초)
- Jira 고급 연동 필요 (커스텀 필드, 컴포넌트 등)
- 우선 지원 요청

Pro → Studio 전환 트리거:
- SSO 요구
- 감사 로그 요구
- SLA 요구
- 팀 단위 관리 기능 요구

### 9.3 수익화 로드맵

| 단계 | 시기 | 목표 | KPI |
|------|------|------|-----|
| **1단계: 레퍼런스 확보** | 0~6개월 | Free 티어 10~20개사 레퍼런스 | 재현 불가 버그 감소 정량 지표 확보 |
| **2단계: 유료 전환** | 6~18개월 | Pro 전환 30~50개사 | MRR $5K+ |
| **3단계: 업셀** | 18개월+ | Studio/Enterprise 계약 | ARR $100K+ |

---

## 10. Go-to-Market 전략

### 10.1 Phase 1: 시장 검증 (0~6개월) — 씨앗 심기

**목표**: 한국 인디/중소 개발사 10~20개 레퍼런스 고객 확보, 제품-시장 적합성 검증

**채널 전략**:
- **Unity Asset Store 무료 등록**: Discovery 채널 확보, 글로벌 접점 기반 마련
- **KOCCA 인디게임 지원 프로그램 연계**: 76개 지원 프로젝트 대상 무료 Beta 프로그램 운영
- **한국 게임 개발자 커뮤니티**: NDC(넥슨 개발자 컨퍼런스) 발표, 인디 게임쇼 참가
- **직접 영업**: Unity 사용 확인된 중소 스튜디오 20개사 타겟 콜드 아웃리치

**성공 기준**:
- Free 티어 활성 팀 10개사 이상
- 재현 불가 버그 감소율 정량 지표 1건 이상 확보
- NPS 40+

**주요 작업**:
- Unity Package Manager 패키지 배포 설정
- 영어 기반 UI 개발 (한국어 로컬라이즈 별도 레이어)
- Atlassian Marketplace 등재 요건 사전 검토 및 개발 규격 반영

### 10.2 Phase 2: 한국 SMB 집중 (6~18개월) — 수확

**목표**: 한국 중소 스튜디오 30~50개사 유료 전환, MRR $5K+ 달성

**채널 전략**:
- **케이스 스터디 발행**: Phase 1 레퍼런스 고객의 "재현 불가 버그 X% 감소" 정량 성과 콘텐츠화
- **G-Star 참가**: 한국 최대 게임 전시회 부스 운영 또는 스피킹
- **Atlassian Marketplace 등재**: Jira 생태계 내 글로벌 접점 확보 (파트너 프로그램 신청)
- **QA 아웃소싱사 채널 제휴**: 한국 QA 외주 업체와 번들 제안

**성공 기준**:
- MRR $5,000 이상
- Pro 이상 유료 팀 30개사 이상
- Atlassian Marketplace 등재 완료

### 10.2-1 리소스 계획

#### Phase별 최소 팀 구성

| Phase | 역할 | FTE | 비고 |
|-------|------|-----|------|
| **Phase 1 (0~6개월)** | Unity 클라이언트 개발자 | 1.0 | 캡처 엔진 + UI + Jira 연동 |
| | 백엔드/인프라 (Supabase) | 0.5 | Auth Broker + 배포 |
| | PM / 기획 | 0.5 | 제품 관리 + 고객 인터뷰 |
| | **소계** | **2.0 FTE** | |
| **Phase 2 (6~18개월)** | Unity 클라이언트 개발자 | 1.5 | 기능 고도화 + 플랫폼 확장 |
| | 백엔드/인프라 | 1.0 | 멀티테넌시 + 결제 연동 |
| | PM / 기획 | 0.5 | GTM + Marketplace 등재 |
| | 마케팅 / BD | 0.5 | 커뮤니티 + 파트너십 |
| | **소계** | **3.5 FTE** | |
| **Phase 3 (18개월+)** | 개발팀 | 3.0 | 클라이언트 2 + 백엔드 1 |
| | PM | 1.0 | 글로벌 제품 관리 |
| | 마케팅 / BD | 1.0 | 글로벌 마케팅 |
| | 고객 지원 | 0.5 | |
| | **소계** | **5.5 FTE** | |

#### 분기별 주요 산출물

| 분기 | 산출물 |
|------|--------|
| Q1 (0~3개월) | MVP 프로토타입, Alpha 릴리즈, Unity Package 배포 파이프라인 |
| Q2 (3~6개월) | Beta 릴리즈, 10개사 레퍼런스 고객, Unity Asset Store 등재 |
| Q3 (6~9개월) | GA 릴리즈, Pro 티어 런칭, 유료 전환 시작 |
| Q4 (9~12개월) | Atlassian Marketplace 등재, 케이스 스터디 발행, MRR $2K+ |
| Q5~Q6 (12~18개월) | Studio 티어 런칭, 다중 사용자 지원, MRR $5K+ |
| Q7+ (18개월+) | 글로벌 마케팅, Unreal Engine 검토, ARR $100K+ 목표 |

### 10.3 Phase 3: 글로벌 확장 (18개월+) — 스케일

**목표**: 영어권 시장 진입, ARR $100K+ 달성

**조건**: MRR $5K+ 달성 시 글로벌(영어권) 마케팅 투자 확대. 미달 시 가격·기능·타겟 조정 후 피벗 검토.

**채널 전략**:
- **Atlassian Marketplace**: 글로벌 Jira 사용자 57.5% 접점 활용
- **Product Hunt 런칭**: 글로벌 개발자 커뮤니티 초기 접점
- **Unity Forum / Reddit (r/gamedev, r/Unity3D)**: 커뮤니티 마케팅
- **GDC (Game Developers Conference)**: 글로벌 게임 개발사 대상 영업

**글로벌 확장 로드맵 (Korea-First, Global-Scale)**:

| 지역 | 시기 | 전략 |
|------|------|------|
| 한국 | 0~18개월 | 검증 거점, 레퍼런스 확보 |
| 일본 | 18~30개월 | Unity 기반 모바일 강국, 한국 사례 레퍼런스 활용 |
| 북미/유럽 | 24개월+ | Atlassian Marketplace, GDC, 영어 컨텐츠 |
| 동남아 | 30개월+ | Unity 모바일 성장 시장 |

---

## 11. 성공 지표

### 11.1 제품 지표

- 캡처 실행 대비 번들 생성 성공률(%)
- "Submit" 대비 이슈 생성 성공률(%)
- 이슈 생성 평균 소요시간(버튼 클릭→완료)
- 영상 첨부 사용률(옵션 ON 비율)
- "재현 불가" 라벨 비율 변화(고객사 협조 시)
- 월 활성 팀(MAT, Monthly Active Teams)
- 월 활성 리포트(MAR, Monthly Active Reports)

### 11.2 품질 지표

- 번들 손상률(파일 누락/깨짐)
- 로그/상태 스냅샷 필드 누락률
- 프레임 드랍/성능 영향(기본 시나리오에서 허용 범위)

### 11.3 운영 SLO / 지원 정책

| SLO 항목 | 목표 | 측정 방식 |
|---------|------|----------|
| Auth Broker 가용성 | 99.5% (월간) | Supabase 상태 모니터링 |
| 토큰 발급 응답시간 (p95) | < 2초 | Edge Function 로그 |
| OAuth 연결 성공률 | > 95% | connect/callback 성공/실패 비율 |

| 지원 정책 | Free | Pro | Studio | Enterprise |
|----------|------|-----|--------|-----------|
| 지원 채널 | GitHub Issues | 이메일 | 이메일 + 우선 큐 | 전담 CS |
| 응답 SLA | Best effort | 48시간 | 24시간 | 4시간 |
| 장애 공지 | 상태 페이지 | 상태 페이지 + 이메일 | 상태 페이지 + 이메일 | 전담 알림 |

### 11.4 사업 지표

- MRR (Monthly Recurring Revenue)
- ARR (Annual Recurring Revenue)
- Free → Pro 전환율
- NPS (Net Promoter Score) — 목표: 40+
- 유료 팀 수 (Customer Count)
- 평균 팀 규모(좌석 수)

---

## 12. MVP 범위 정의 (Must / Should / Could)

### Must (MVP 필수)

- Unity 캡처 핫키 + 오버레이 UI
- 스크린샷 + 로그 + 상태 스냅샷 자동 수집
- 직전 60초 영상 **로컬 저장**
- 로컬 번들 관리(저장/재시도/제출 상태)
- Jira Cloud 연결(OAuth) + 이슈 생성 + 첨부(스크린샷/로그/상태) + 영상 옵션 첨부
- Supabase Auth Broker + Vault 저장(tenant별)
- 영어 기반 UI (글로벌 설계 내재화)
- **크래시 복구**: 주기적 플러시 + `abnormal_exit.flag` 비정상 종료 감지 + 크래시 번들 목록 UI + Jira 등록 (섹션 6.6 참조)

### Should (MVP+)

- 영상 품질 프리셋(720p/1080p) 및 파일 크기 타겟
- 로그 마스킹 규칙 편집 UI
- 제출 실패 시 더 친절한 원인 분류(권한/용량/레이트리밋)
- Unity Asset Store 등재
- Atlassian Marketplace 등재 준비
- **크래시 복구**: Managed 예외(`logMessageReceived`) 감지 즉시 번들 생성 (레이어 2, 섹션 6.6.1 참조)

### Could (후속)

- 런타임(모바일) 제출 UX 개선(디바이스 페어링)
- Jira 필드 매핑(커스텀 필드, 컴포넌트, 에픽 등)
- Self-hosted 브로커(대형사/보안 요구 대응)
- 팀 단위 대시보드/정책 관리 (중견사 확장 시 웹 포털 부재 약점 보완)
- 포털/구독형 SaaS(아카이브/검색/요약/협업)
- Unreal Engine 지원 (멀티엔진 로드맵)
- **크래시 복구**: 크래시 번들 일괄 Jira 등록, 크래시 패턴 분석 (동일 스택 트레이스 클러스터링)

---

## 13. 리스크 매트릭스

### 13.1 전체 리스크 매트릭스

| 리스크 | 발생 가능성 | 영향도 | 심각도 | 대응 방안 |
|--------|-----------|--------|--------|---------|
| **한국 단독 시장 규모 한계** | 확정적 | 높음 | 치명적 | 글로벌 설계를 MVP부터 반영, Atlassian Marketplace 등재 |
| **SaaS 구독 저항 (한국)** | 높음 | 중간 | 높음 | Free 티어 제공, PLG 전략, 연간 결제 할인, 로컬 결제 지원 |
| **인지도 부재 / 마케팅 비용** | 높음 | 중간 | 높음 | KOCCA 지원 연계, NDC/G-Star, 커뮤니티 마케팅 |
| **Backtrace/Sentry 기능 확장** | 중간 | 중간 | 중간 | Unity 특화 + 60초 영상 + 로컬-퍼스트 3박자 차별화 유지 |
| **개인정보/보안 우려 (영상 데이터)** | 중간 | 중간 | 중간 | 로컬-퍼스트 아키텍처 명확히 커뮤니케이션, 보안 감사 문서화 |
| **초기 설치/설정 마찰** | 중간 | 중간 | 중간 | UPM 원클릭 설치, 5분 온보딩 가이드 제공 |
| **고객 이탈 (Free → 유료 전환 실패)** | 중간 | 중간 | 중간 | PLG 전략, 전환 트리거 명확화, 사용량 기반 넛지 |
| **Unity 플랫폼 정책 변경** | 낮음~중간 | 높음 | 중간 | Unreal Engine 지원 로드맵 준비, 플랫폼 의존 분산 |
| **웹 포털 부재 (중견사 확장 약점)** | 낮음~중간 | 중간 | 중간 | Studio 티어 이상 최소 팀 대시보드 로드맵 명시 |
| **OAuth 토큰 회전/만료 연결 끊김** | 낮음~중간 | 중간 | 낮음~중간 | Re-auth UX 필수, 브로커 락 처리 |
| **refresh token 유출** | 낮음 | 높음 | 중간 | Vault/접근통제/로그 레드랙션/키 회전 정책 |
| **멀티테넌시 데이터 혼선** | 낮음 | 높음 | 중간 | tenant_id 기반 분리, 서비스 역할만 시크릿 접근 |
| **Jira 첨부 업로드 제한** | 낮음~중간 | 낮음 | 낮음 | 업로드 제한 체크 후 안전한 실패 처리, 영상 자동 제외 |
| **Native 크래시 시 데이터 손실** | 높음 | 중간 | 중간 | 주기적 플러시 간격 단축(기본 5~10초) + 영상 세그먼트 5초 단위 교체 옵션 제공 |
| **MemoryMappedFile Unity 버전 호환성** | 낮음 | 높음 | 중간 | Unity 2022.3.22f1+ 최소 요구사항 명시; 미지원 버전은 일반 `FileStream` 폴백 자동 적용 |

### 13.2 규제/법적 준수 고려사항

| 항목 | 설명 | MVP 대응 |
|------|------|---------|
| **개인정보 보호 (PIPA)** | 영상/스크린샷에 개인정보가 포함될 수 있음 | 로컬-퍼스트 아키텍처로 외부 전송 최소화. Jira 첨부 시 사용자 동의 기반. 로그 마스킹 기본 적용 |
| **영상 수집 고지** | QA 외 플레이어가 사용할 경우 영상 녹화에 대한 고지 필요 | MVP는 내부 개발/QA 도구로 한정. 외부 배포 시 녹화 고지 UI 추가 (Phase B) |
| **Jira OAuth 데이터 처리** | Atlassian 3rd party app 정책 준수 | OAuth scope 최소 권한 원칙. 토큰만 서버 저장, 사용자 데이터 미저장 |
| **GDPR (글로벌 확장 시)** | EU 사용자 대상 시 데이터 처리 동의 필요 | Phase C(글로벌 확장) 시 DPA 문서 및 데이터 처리 동의 UI 추가 |

### 13.3 리스크 우선순위 요약

```
[즉시 대응 필요 — RED]
1. 한국 단독 TAM 한계 → 글로벌 설계 내재화 (MVP 착수 전)
2. SaaS 구독 저항 → Free 티어 + PLG 전략 (개발 착수 전 비즈니스 모델 확정)

[중기 모니터링 — YELLOW]
3. 인지도 부재 → KOCCA/NDC/G-Star 커뮤니티 마케팅 (0~6개월)
4. 경쟁사 기능 확장 → 차별화 포인트 강화 (지속)
5. 웹 포털 부재 → Studio 티어 이상 최소 관리 기능 로드맵 수립

[장기 관리 — GREEN]
6. Unity 플랫폼 리스크 → 멀티엔진 로드맵 (18개월+)
7. 보안 우려 → 문서화 및 감사 인증 (지속)
```

---

## 14. 출시 이후 확장 로드맵

### Phase A — 엔진용 도구 (현재 PRD, 0~6개월)

- Portal-less, local-first, Jira Cloud 즉시 주입
- 최소 브로커(토큰만 저장)
- 영어 기반 UI, Unity Asset Store 등재
- 한국 인디/중소 레퍼런스 10~20개사 확보

### Phase B — 수익 안정화 및 글로벌 진입 (6~18개월)

- 팀 단위 템플릿/필드 매핑 고도화
- 다중 사용자(회사 테넌트 내) 지원
- 런타임(모바일) 제출 고도화
- Atlassian Marketplace 등재 (글로벌 Jira 사용자 접점)
- 최소 팀 대시보드 (중견사 확장 시 웹 포털 부재 약점 보완)
- 일본 시장 진출 검토

### Phase C — SaaS 플랫폼화 (18개월+)

- 리포트 링크 공유/아카이브/검색
- AI 요약/자동 필드 채움
- Unreal Engine 지원 (멀티엔진 로드맵)
- Self-hosted 브로커 (대형사/보안 요구 대응)
- 포털/구독형 SaaS (선택적)
- 북미/유럽/동남아 글로벌 확장

---

## 15. Acceptance Criteria

### 15.1 핵심 기능 (Core)

1. 사용자가 Unity Editor/PC dev build에서 핫키를 누르면 **5초 이내**(체감 기준) 오버레이가 뜨고, 번들이 생성된다.
2. 번들에는 최소 `manifest.json + screenshot + logs + state.json`이 포함된다.
3. 영상은 기본으로 로컬에 저장되고, 제출 시 옵션으로 첨부 가능하다.
4. Jira Cloud 연결을 완료하면 "Submit to Jira" 한 번으로 이슈가 생성되고, 링크가 반환된다.
5. Jira 토큰이 만료/무효가 되면 "Re-auth" 플로우로 복구 가능하다.
6. 모든 실패 케이스에서 번들은 로컬에 남아 데이터 유실이 없다.
7. Supabase Vault에는 refresh token이 tenant/user 기준으로 저장되며, 일반 클라이언트 권한으로 평문 접근이 불가능하다.
8. UI는 영어로 작성되며, 한국어 로컬라이즈를 별도 레이어로 지원한다.

### 15.2 오프라인/재시도 (Offline Queue — 섹션 8.2.4)

9. 제출 실패 시 번들은 `status=pending` 상태로 유지되며, "Retry submit" 버튼으로 재제출이 가능하다.
10. 재제출 성공 시 `status=submitted`로 기록되고, 생성된 Jira 이슈 URL이 번들 메타데이터에 저장된다.

### 15.3 업로드 제한 대응 (섹션 8.3.3)

11. 제출 전 Jira 첨부 업로드 제한(meta API)을 확인하고, 제한 초과 시 영상 첨부를 자동 제외하며 사용자에게 알린다.
12. 업로드 실패 시 에러 유형(권한 부족/용량 제한/네트워크)을 구분하여 사용자에게 안내 메시지를 표시한다.

### 15.4 로그 마스킹 (섹션 8.1.3)

13. 로그 전송 전 정규식 기반 민감정보 마스킹(이메일, IP, 토큰 패턴)이 기본 적용된다.
14. 마스킹 규칙은 설정 파일(JSON)로 커스터마이징 가능하며, 기본 규칙은 최소 3개 이상 제공된다.

### 15.5 토큰 보안 (섹션 8.4.5)

15. refresh token 회전(rotating) 시 새 refresh token 저장과 이전 token 폐기가 원자적(atomic)으로 처리되며, 동시 refresh 요청은 연결 단위 락으로 직렬화된다.
16. Auth Broker의 모든 API 호출은 세션 토큰(JWT) 기반 클라이언트 인증을 거치며, tenant_id/user_id 소유권이 서버 측에서 검증된다.

### 15.6 번들 무결성 (섹션 8.2.2)

17. `manifest.json`은 생성 시 필수 필드(`report_id`, `created_at`, `engine`, `engine_version`, `app_version`, `build_number`, `platform`, `device`, `os`, `scene`, `title`, `description`, `severity`, `artifacts`, `integrations`)가 모두 존재하며, 필수 필드 누락 시 번들 생성을 실패 처리하고 사용자에게 알린다.
18. `artifacts` 배열의 각 항목은 `sha256` 해시를 포함하며, 제출 시 파일 무결성 검증에 사용된다.

### 15.7 크래시 자동 감지 및 복구 (섹션 6.6, 8.1.5)

19. 플레이 모드가 실행 중인 동안 설정된 주기(로그 5초, 상태 10초, 영상 30초 기본값)에 따라 링버퍼가 디스크에 플러시되며, 각 플러시 간격은 `BugOneTouchSettings`에서 변경 가능하다.
20. Unity 플레이 모드 중 Managed 예외(`LogType.Exception`)가 발생하면 `crash_{timestamp}.bot-unity` 번들이 자동 생성되고, `manifest.json`의 `crash_type`이 `"managed_exception"`, `registered`가 `false`로 설정된다.
21. 플레이 모드 중 에디터가 비정상 종료(Native 크래시, 강제 종료 등)된 후 에디터를 재시작하면, `abnormal_exit.flag` 파일의 잔존 여부로 비정상 종료를 감지하고 `crash_bundles` 폴더의 미처리 번들을 복구 대상으로 마킹한다.
22. `registered: false`인 크래시 번들이 1개 이상 존재하는 상태로 에디터를 시작하면, 에디터 시작 후 2초 이내에 크래시 번들 목록이 시간순(최신 먼저)으로 표시된다.
23. [Jira에 등록] 버튼 클릭 시, 제목(`[Crash] {crash_type}: {crash_message 첫 50자}`), 설명(스택 트레이스 + 시스템 정보 포함), 첨부파일(번들 ZIP + 스크린샷), 우선순위(Critical), 레이블(`auto-crash-report`, `bug-onetouch-unity`)이 자동으로 채워진 Jira 등록 폼이 표시된다.
24. Jira 등록 완료 후, 해당 번들의 `manifest.json`에서 `registered` 필드가 `true`로 갱신되고 `jira_issue_key` 및 `registered_at` 필드가 기록된다.
25. 크래시 번들 보관 수가 최대값(기본 10개)을 초과하면 FIFO 정책에 따라 가장 오래된 번들부터 자동 삭제된다. `registered: false` 상태인 번들은 삭제 전 사용자에게 경고를 표시한다.
26. 크래시 번들의 `data_integrity` 필드는 번들 생성 시점에 각 파일(`crash_log`, `last_screenshot`, `replay_buffer`, `state_snapshot`, `system_info`)의 실제 존재 여부와 완전성을 반영하여 `"complete"` / `"partial"` / `"missing"` 중 하나로 설정되며, 이 값은 복구 UI의 보존 상태 배지에 정확히 표시된다.

---

## 16. 시장 분석 출처

### 한국 게임 시장 통계

1. **KOCCA (한국콘텐츠진흥원)** — 2024 대한민국 게임백서: 한국 게임산업 매출 22조 9,642억 원 (2023), 세계 4위
   - URL: https://www.kocca.kr/kocca/bbs/view/B0000146/2008086.do
   - 발행일: 2024년
2. **통계청** — 게임 제작/배급업 사업체 수: 약 1,287~1,334개
   - URL: https://kosis.kr (경제총조사 > 산업별 사업체수)
   - 참조 연도: 2022~2023년
3. **KOCCA** — 2025년 인디게임 지원: 76개 프로젝트, 219억원 투자
   - URL: https://www.kocca.kr/kocca/bbs/list/B0000147.do (콘텐츠진흥원 공고 게시판)
   - 참조 연도: 2025년

### Unity / Jira 사용률 데이터

4. **Unity Technologies** — 공식 발표: 상위 1000개 모바일 게임 71% Unity 사용
   - URL: https://investors.unity.com/news/news-details/2025/Unity-Launches-Native-Cross-Platform-Commerce-Management-for-Game-Developers-Worldwide/default.aspx
   - 발행일: 2025년
5. **Stack Overflow Developer Survey 2024** — Jira: 57.5%로 1위 프로젝트 관리 도구
   - URL: https://survey.stackoverflow.co/2024/technology#most-popular-technologies-tools-tech
   - 발행일: 2024년 6월
6. **웹 리서치 (Google Play 분석)** — 한국 Google Play 상위 50위 수익 게임 중 Unity 56%
   - 방법론: 2026년 2월 기준 Google Play 한국 매출 상위 50위 게임의 APK 분석 (libunity, libil2cpp 존재 여부)
   - 신뢰 수준: 추정치 (공식 통계 아님)
7. **크래프톤 (공개 정보)** — Jira Cloud 운영 확인
   - 근거: 크래프톤 채용 공고 및 기술 블로그에서 Jira Cloud 사용 확인
   - URL: https://careers.krafton.com (채용 페이지 JD 참조)

### 경쟁사 가격 및 시장 데이터

8. **Jam.dev** — 공식 사이트: $14/사용자/월, 2023년 $9M Series A 투자
   - URL: https://jam.dev/pricing
   - 확인일: 2026년 2월
9. **Sentry** — 공식 사이트: Team 플랜 $26/월
   - URL: https://sentry.io/pricing/
   - 확인일: 2026년 2월
10. **Backtrace (Sauce Error Reporting)** — 공식 사이트: 무료~유료 크래시 분석 도구
    - URL: https://saucelabs.com/products/error-reporting
    - 확인일: 2026년 2월
11. **Atlassian Jira Pricing** — 공식 사이트
    - URL: https://www.atlassian.com/software/jira/pricing
    - 확인일: 2026년 2월

### 한국 SaaS 시장

12. **시장 조사 리포트** — 한국 SaaS 시장: 2024년 31.4억 달러, CAGR 9.4%
    - 근거: Statista / Mordor Intelligence 한국 SaaS 시장 보고서 (2024년 발행)
    - URL: https://www.mordorintelligence.com/industry-reports/south-korea-saas-market
    - 참고: 유료 보고서이며, 공개 요약본 기준

### 사례 연구

13. **데브시스터즈 (공개 인터뷰/발표)** — 구글시트 → 트렐로 → Jira 도구 업그레이드 패턴
    - 근거: NDC 발표 자료 및 데브시스터즈 기술 블로그
    - URL: https://tech.devsisters.com/ (기술 블로그 아카이브)
14. **크래프톤 (공개 정보)** — Jira Cloud 도입 사례
    - 근거: 채용 공고 JD 및 기술 발표 자료

### 기술 참고

15. **Atlassian** — Marketplace 파트너 프로그램 공식 문서
    - URL: https://www.atlassian.com/licensing/cloud
    - 확인일: 2026년 2월
16. **Supabase** — Auth / Vault / Edge Functions 기술 문서
    - URL: https://supabase.com/docs
    - 확인일: 2026년 2월
17. **Unity** — Package Manager 공식 문서, Windows Standalone 빌드 지원 명세
    - URL: https://docs.unity3d.com/Manual/Packages.html
    - 확인일: 2026년 2월

---

## Appendix — 참고 구현 메모 및 분석 메타데이터

### A1. 구현 참고 메모 (비요구사항)

- Jira Cloud 3LO는 code grant 기반이며 refresh token은 회전(rotating) 방식임.
- Supabase Vault는 암호화 저장을 제공하나, 복호화 view 접근 권한을 엄격히 제한해야 함.
- 정확한 엔드포인트/스코프/제한은 구현 시점에 공식 문서를 기준으로 확정.
- Unity Package Manager 배포 시 영어 기반 패키지 메타데이터 필수 (글로벌 배포 요건).

### A2. 시장 분석 메타데이터

| 항목 | 내용 |
|------|------|
| 분석 모델 수 | 5개 (Sonnet 4.6, Haiku 4.5, Opus 4.6, Codex, 웹 리서치 에이전트) |
| 가중 평균 사업 가능성 점수 | 7.2/10 (5개 모델 종합) |
| Codex 단독 평가 | 7.8/10 |
| 최종 Go/No-Go 판단 | Conditional GO |
| 분석 기준일 | 2026년 2월 27일 |
| 총괄 | Opus 4.6 시장 분석 총괄 |

### A3. v1.0 → v2.0 주요 변경사항 요약

| 변경 유형 | 내용 |
|---------|------|
| **제거** | 타 이슈 트래커 연동 완전 삭제 — Jira Cloud 단독으로 통합 (관련 요구사항, API, 데이터 모델 제거) |
| **신규** | 섹션 2: 시장 분석 (TAM/SAM/SOM, 경쟁 환경, 차별화) |
| **신규** | 섹션 9: 가격 전략 (Free/Pro/Studio/Enterprise, PLG 전략, 수익화 로드맵) |
| **신규** | 섹션 10: GTM 전략 (3단계 Phase, Korea-First Global-Scale) |
| **신규** | 섹션 13: 리스크 매트릭스 (발생확률/영향도/대응방안) |
| **신규** | 섹션 16: 시장 분석 출처 |
| **강화** | 섹션 14: 출시 이후 확장 로드맵 (글로벌 확장 로드맵 추가) |
| **강화** | 섹션 3: 비목표에 타 이슈 트래커 제외 명시 (Jira Cloud 단독) |
| **강화** | 섹션 12: MVP 범위에 글로벌 설계 내재화(영어 UI) 추가 |
| **유지** | 섹션 8: 기술 요구사항 전체 (캡처/번들/Jira/Supabase/인증) |
| **유지** | 섹션 11: 성공 지표 (사업 지표 추가) |
| **유지** | 섹션 15: Acceptance Criteria (영어 UI 조건 추가) |

### A4. v2.1 → v2.2 주요 변경사항 요약

| 변경 유형 | 내용 |
|---------|------|
| **신규** | 섹션 6.6: 크래시 자동 감지 및 복구 플로우 (6.6.1~6.6.5) — 3중 레이어 보존 전략, 크래시 유형별 보존율, 번들 구조, manifest 크래시 전용 필드 |
| **신규** | 섹션 8.1.5: 크래시 자동 캡처 요구사항 (플러시 주기, FIFO 보관 정책, 보관 기간) |
| **신규** | 섹션 15.7: 크래시 자동 감지 및 복구 Acceptance Criteria 8개 (AC 19~26) |
| **강화** | 섹션 12: Must/Should/Could 각각에 크래시 복구 분류 명시 |
| **강화** | 섹션 13.1: 리스크 매트릭스에 Native 크래시 데이터 손실, MemoryMappedFile 버전 호환성 리스크 2건 추가 |
