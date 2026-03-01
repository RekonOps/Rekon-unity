# Bug-OneTouch-unreal PRD v1.0 — Unreal Engine 로컬-퍼스트 버그 리포팅 플러그인 (Jira Cloud 전용)

- **문서 버전**: v1.0
- **작성일**: 2026-02-27 (KST)
- **제품 코드명**: **Bug-OneTouch-unreal** (가칭)
- **대상 엔진**: **Unreal Engine 5 (UE5 우선)**
- **연동 대상**: **Jira Cloud (단독 지원)**
- **핵심 원칙**: **로컬-퍼스트(Local-first) + 포털 없음(Portal-less) + 유저 명의 이슈 생성(User identity only)**
- **관련 문서**: Bug-OneTouch-unreal PRD v2.1 (Unity) — 본 문서는 Unreal 확장 편이다

---

## 0. 핵심 요약

Unreal Engine 프로젝트에서 결함을 발견하면 **핫키 1번**으로

- **직전 60초 영상(로컬 저장 기본)** + **스크린샷 + UE_LOG + 상태 스냅샷**을 자동 수집하여 **로컬 번들**로 보관하고,
- "리포트 제출" 버튼을 누르면 **Jira Cloud에 즉시 이슈를 생성**한다.
- 웹 포털은 없으며(= 우리 서비스에 버그 데이터 저장 없음), Jira Cloud OAuth 3LO 제약 때문에 **토큰 교환만 중계하는 얇은 Auth Broker**를 Supabase로 제공한다. Auth Broker는 토큰 교환만 처리하며, 사용자 데이터를 보관하지 않는다.
- Jira 연결은 **사용자(개인) 단위 1:1** — 프로젝트가 아닌 개인이 OAuth 3LO 인증한다. OAuth 토큰(특히 refresh token)은 **로컬 머신의 `Saved/Bug-OneTouch-unreal/auth.enc`에 AES-256 암호화** 저장한다. 팀 내 여러 사용자는 각자 자신의 Jira 계정으로 인증하며, 이슈는 해당 사용자 명의로 생성된다.

**시장 포지션**: 한국 게임 시장(세계 4위, 22.9조 원)에서 Unreal+Jira 교집합 대상으로 **BetaHub 등 경쟁 도구가 클라우드 우선인 틈새에 로컬-퍼스트 포지션**을 선점. 5개 분석 모델 종합 사업 가능성 점수 **7.6/10**, **Conditional GO** 판단. Unreal 단독 성공률 20~30%, Unity+Unreal 통합 성공률 40~45% — **Unity 출시 이후 Unreal 확장이 기본 시나리오**이며, Unreal 독립 MVP 시나리오는 부록 A에 별도 기술한다.

---

## 1. 배경과 문제 정의

### 1.1 배경

게임 개발/테스트 과정에서 버그 리포트가 늦거나 불완전해지는 가장 큰 이유는:

- 리포팅이 번거롭고(작성 시간/양식/증거 수집),
- 환경/상태/로그/증거가 부족해 **재현이 안 되거나(= 재현 불가)**,
- 담당자가 "원인 좁히기"까지 가기 위해 추가 커뮤니케이션이 반복되기 때문이다.

Bug-OneTouch-unreal Unity PRD v2.1에서 확인된 "핫키 1번 → 증거 자동 수집 → Jira 즉시 주입" 패턴의 유효성을, **Unreal Engine 기반 게임 개발팀**에도 동일하게 적용한다.

한국 Unreal 시장은 2024년부터 UE5 전환이 가속화되고 있다. 넥슨의 *The First Descendant*, 크래프톤의 *inZOI*, 카카오게임즈 *Chrono Odyssey*, 넷마블 *The Seven Deadly Sins: Origin*이 모두 UE5 기반으로 출시되거나 출시 예정이며, 한국 대형사들이 UE5를 전략 엔진으로 채택하는 흐름이 뚜렷하다. 이 흐름은 UE 기반 중소 스튜디오의 저변 확대로 이어진다.

그러나 현실적으로 한국 중소 Unreal 스튜디오는 버그 리포팅에 있어 다음 문제를 그대로 안고 있다:

- Unreal Insights / Visual Logger 같은 내장 도구는 강력하지만 "QA가 원클릭으로 Jira 티켓 생성"까지는 기본 제공이 없다.
- Crash Reporter는 크래시 이후 상황만 캡처하며, 크래시 전 60초의 재현 영상을 보존하지 못한다.
- BetaHub / Oplix / FiexIt 등 경쟁 도구는 클라우드 업로드를 기본으로 설계되어, 보안 민감 스튜디오(사전 공개 빌드, IP 보호)의 도입 장벽이 있다.

### 1.2 문제 정의

- QA/개발/기획 누구나 "보고 바로 남길 수 있는" 리포팅 도구가 부족하다.
- Unreal 특화 컨텍스트(UE_LOG 카테고리, Blueprint 상태, World/Level 이름, 씬 액터 상태)가 버그 리포트에 자동 포함되지 않는다.
- "재현 불가" 버그가 많아지면 리포트가 무시되거나 우선순위가 밀린다.
- C++/Blueprint 혼합 팀에서 로그 확인만 해도 수 십 분이 소요되는 현실적 병목이 존재한다.
- 팀이 이미 Jira를 쓰고 있어도, **"증거/컨텍스트 수집"은 여전히 사람의 노동**이다.

---

## 2. 시장 분석

> 본 섹션은 Sonnet 4.6, Haiku 4.5, Opus 4.6, Codex(codex-5.3-spark-xhigh), Codex(spark) 5개 모델의 Unreal 특화 분석을 종합한 결과이다. 분석 기준일: 2026년 2월 27일.

### 2.1 한국 Unreal Engine 시장 현황

#### 시장 규모 핵심 지표

| 지표 | 수치 | 출처 |
|------|------|------|
| 한국 게임산업 전체 매출 | 22조 9,642억 원 (2023, +3.4%) | KOCCA 게임백서 2024 |
| 세계 시장 내 순위 | 4위 | KOCCA |
| 게임 제작·배급업 사업체 수 | 약 1,287~1,334개 | 통계청 / KOCCA |
| 게임 제작·배급업 종사자 수 | 약 51,783명 | KOCCA 게임백서 2024 |
| 중소기업 비중 | 90% 이상 | 통계청 분석 |
| 모바일 게임 매출 비중 | 59.3% | KOCCA 게임백서 2024 |
| PC 게임 매출 비중 | 25.6% | KOCCA 게임백서 2024 |
| 콘솔 게임 매출 비중 | 4.9% | KOCCA 게임백서 2024 |

PC+콘솔 합계 30.5% 구간이 Unreal 친화 세그먼트다. 넥슨의 장기 UE 파트너십(2025), 크래프톤 inZOI(UE5), 카카오게임즈 Chrono Odyssey(UE5) 등 대형사의 UE5 전환이 이어지고 있다.

#### Unreal Engine 사용률 교차 검증 (추정)

한국 게임사 Unreal 점유율 공식 통계는 부재하나, 5개 모델의 교차 분석을 통해 추정 범위를 산출했다:

| 분석 모델 | Unreal 사용률 추정 | 비고 |
|-----------|----------------|------|
| Sonnet 4.6 | 한국 SMB 200~360개사 | Unreal 기반 SMB 절대수 추정 |
| Haiku 4.5 | UE5 전환 대형사 4개 확인 | 넥슨, 크래프톤, 카카오게임즈, 넷마블 |
| Opus 4.6 | Unity 630~780개사 대비 Unreal 200~360개사 | Unity 대비 1/3 수준 |
| Codex xhigh | 한국 상용 프로젝트 전체 15~25%, PC/콘솔 지향 35~50% | 추정 |
| Codex spark | SOM 목표 75~300개사 (1~3년) | 조건부 |

**합의 범위: 한국 Unreal 기반 SMB 약 200~360개사** (Unity 630~780개사의 약 1/3~1/2 수준)

#### UE5 전환 가속 사례 (공개 정보)

| 스튜디오 | 프로젝트 | 엔진 | 출시 현황 |
|---------|--------|------|---------|
| 넥슨게임즈 | The First Descendant | UE5 | 2024-06-30 출시 |
| 크래프톤 | inZOI | UE5 | 2025-03-28 얼리액세스 |
| 카카오게임즈/Chrono Studio | Chrono Odyssey | UE5 | 출시 예정 |
| 넷마블 | The Seven Deadly Sins: Origin | UE5 | 2026-01-28 PS5 출시 |

#### Fab Marketplace 현황

Unreal 에코시스템의 핵심 배포 채널은 Fab Marketplace(구 Epic Games Marketplace)다. 수수료 구조는 **88/12** (개발자 88%, Epic 12%)로, Unity Asset Store의 70/30 대비 개발자에 유리하다. Unreal 플러그인 검색/설치 채널로 Fab 등재가 GTM 핵심 축이 된다.

### 2.2 타겟 시장 규모 (TAM / SAM / SOM)

5개 분석 모델의 TAM/SAM/SOM 추정치를 공통 계산 템플릿으로 교차검증한 결과이다.

#### 공통 계산 템플릿

**계산식**: `타겟 스튜디오 수 × 유료 좌석 수 × 월 단가 × 12개월`

#### Unreal 단독 TAM / SAM / SOM

| 구간 | 스튜디오 수 | 좌석 수 | 월 단가 | 연간 산출 | 결과 |
|------|-----------|---------|---------|----------|------|
| TAM 하한 | 200개사 | 8석 | 10,000원 | 200 × 8 × 10,000 × 12 | **1.92억 원** |
| TAM 상한 | 360개사 | 15석 | 15,000원 | 360 × 15 × 15,000 × 12 | **9.72억 원** |

- **Unreal TAM**: 연 **1.92억~9.72억 원** (한국 Unreal SMB 200~360개사 × 유료 좌석 8~15석 × 월 10,000~15,000원)
- **SAM**: TAM의 25~35% → 연 **0.48억~3.40억 원** (초기 2~3년 실질 접근 가능)
- **SOM 3년**: 20~50개사 확보 시 **연 0.19억~1.35억 원 ARR**
  - 산출: 20개사 × 8석 × 10,000원 × 12 = 0.19억, 50개사 × 15석 × 15,000원 × 12 = 1.35억

> **Codex xhigh 분석 참고**: 단독 TAM ~30.8억 원/년 추정치는 기술 직군 비율(55%)과 Unreal 비중(20~30%)을 곱한 추정이며, 실질 Jira Cloud 교집합(25~40%)을 반영하면 위 수치가 더 보수적이고 합리적이다.

#### Unity + Unreal 통합 TAM / SAM / SOM

| 구간 | 설명 | 연간 추정 |
|------|------|---------|
| 통합 TAM | Unity TAM(3.84억~15.12억) + Unreal TAM(1.92억~9.72억) | **5.76억~24.84억 원** |
| 통합 SAM | 교차 판매(cross-sell) 및 CAC 절감 효과 포함 | **1.92억~8.93억 원** |
| 통합 SOM 3년 | 60~130개사 확보 기준 | **연 0.93억~3.08억 원** |

> Codex xhigh 분석: Unity+Unreal 통합 시 TAM이 Unity 단독 대비 **2.5~4배** 확대된다. 이는 크로스셀/CAC 절감 시너지 효과를 포함한 추정치다.

#### TAM 정오 주의사항

Codex xhigh 최초 분석에서 KOCCA 기준 종사자 51,783명 전체를 스튜디오 수로 혼용하여 TAM 20.5~30.8억 원을 산출한 바 있다. 이는 종사자(개인) 수와 사업체(스튜디오) 수를 혼동한 계산이다. 본 PRD에서는 사업체 기준(200~360개사)으로 교정하여 산출하였다.

#### 시나리오별 수익 전망 (Unreal 단독 기준)

| 시나리오 | 조건 | 1년차 ARR | 2년차 ARR | 3년차 ARR |
|---------|------|----------|----------|----------|
| **보수적** | 한국 단독, 자연 성장 | $8K (약 1,100만 원) | $22K (약 3,200만 원) | $60K (약 8,700만 원) |
| **기본** | 한국 + Fab Marketplace | $15K (약 2,200만 원) | $50K (약 7,200만 원) | $140K (약 2.0억 원) |
| **낙관적** | Unity 고객 크로스셀 + 글로벌 | $30K (약 4,300만 원) | $100K (약 1.4억 원) | $300K+ (약 4.4억 원+) |

> 주: Unity+Unreal 통합 기준 ARR은 Unity PRD v2.1 수치와 합산하여 별도 판단.

### 2.3 경쟁 환경 분석

#### Unreal 특화 경쟁 도구 상세 분석

Unity 대비 Unreal 경쟁 환경은 다르다. BetaHub 등 클라우드 기반 통합 도구가 이미 존재하며, 이는 Unity PRD v2.1에서 "블루오션"으로 평가한 것과 달리 **Unreal 영역은 경쟁자가 실재한다**.

| 도구 | 유형 | Unreal 지원 | 가격 | 핵심 약점 | Bug-OneTouch-unreal 차별화 |
|------|------|------------|------|---------|----------------|
| **BetaHub** | 클라우드형, Unity/Unreal 통합 | UE 플러그인 제공 | 무료(기본) | 클라우드 업로드 기본 → 보안 민감 스튜디오 도입 장벽 | 로컬-퍼스트 아키텍처 |
| **FiexIt** | 클라우드형 + Jira/GitLab 연동 | Unreal 지원 | 유료 | 클라우드 중심, 온프레미스/오프라인 대응 제한 | 영상 데이터 로컬 저장 |
| **Oplix** | 티켓 자동 생성 | Unreal 통합 | 유료 | 세부 기술 공개 제한, 포지셔닝 불명확 | 번들 무결성 + 재시도 큐 |
| **LadyBug** | Trello 연동 | Unreal 일부 | 유료 | Trello 전용, Jira Cloud 미지원 | Jira Cloud 즉시 주입 |
| **BugSplat** | 크래시 분석 | UE 크래시 리포터 연동 | 유료(크래시 건수 기반) | 크래시 후 분석 전용, 60초 재현 영상 부재 | 재현 영상 + 상태 스냅샷 |
| **Sentry Unreal SDK** | 범용 에러 모니터링 | UE 4.27+ 지원 | $26/월(Team) | 게임 QA 현장 워크플로우 비특화 | 핫키 + 오버레이 UX |
| **Backtrace Unreal** | UE Crash Reporter 연동 | 콘솔 포함 다중 플랫폼 | 유료 | Jira 연동 있으나 원클릭 Jira 이슈 생성 UX 부재 | UE_LOG + World 상태 번들 |
| **Unreal 내장 도구** | Crash Reporter / Visual Logger / Unreal Insights | 내장 | 무료 | "QA가 원클릭으로 Jira 티켓 생성"까지 기본 제공 없음 | 핫키 → Jira 즉시 주입 |
| **인하우스 도구** | 자체 개발 | 일부 대형사 | 개발 비용 | 개발/유지 비용 높음 | 월 정액 SaaS로 유지보수 부담 제거 |

#### BetaHub: 실질적 위협 분석 (Opus-Codex 토론 결과)

5개 모델 토론에서 **BetaHub가 가장 실질적인 경쟁 위협**으로 합의되었다:

- Unity + Unreal 동시 지원 (Bug-OneTouch-unreal의 멀티엔진 차별화를 무력화 가능)
- F12 핫키 + 60초 영상 기본 제공 (Bug-OneTouch-unreal과 동일한 핵심 기능)
- 무료 플랜 존재 → 가격 저항 극복
- 단순 기능 복제로는 BetaHub 대비 차별화 불가

**BetaHub 대응 전략**:

1. **데이터 주권**: BetaHub는 클라우드 업로드 기본 → Bug-OneTouch-unreal은 영상/로그가 외부 서버 경유 없이 로컬에 저장. 사전 공개 빌드 보안, 내부망 환경 스튜디오에 명확한 우위.
2. **버그 처리 시간 단축 시스템**: 단순 "캡처 툴"이 아닌 "버그 처리 시간 단축 시스템"으로 포지셔닝 — UE_LOG 카테고리 분류, World/Level/Actor 상태 자동 수집, Jira 이슈 즉시 주입의 통합 워크플로우.
3. **Jira Cloud 즉시 주입**: BetaHub의 Jira 연동이 완성도 높지 않다면, Jira Cloud OAuth + 이슈 생성 품질에서 차별화.

#### 핵심 경쟁 구도

**Bug-OneTouch-unreal의 진짜 경쟁 상대는 "현상 유지(수작업)"와 "BetaHub"다.** BetaHub 대비 로컬-퍼스트 아키텍처와 Jira Cloud 즉시 주입 완성도로 차별화해야 한다.

### 2.4 차별화 포인트

Bug-OneTouch-unreal Unreal 버전의 핵심 차별화는 4박자 조합이다:

1. **Unreal 특화**: UE_LOG 카테고리/World/Level/Actor 상태 자동 수집, .uplugin 방식 배포
2. **로컬-퍼스트**: 영상/로그 외부 서버 경유 최소화 → 사전 공개 빌드 보안 민감 스튜디오에 유리
3. **60초 영상 링버퍼**: SceneCapture2D + RenderTarget 또는 플랫폼 네이티브 인코더 기반 커스텀 구현
4. **버그 처리 시간 단축 시스템**: 단순 캡처 툴이 아닌, 발견→리포트→Jira까지 전 과정의 마찰 제거

---

## 3. 목표 / 비목표

### 3.1 목표

1. **리포팅 시간 단축**: "발견 → 리포트 제출"까지 평균 시간을 크게 줄인다.
2. **재현 불가 감소**: 최소한 "증거(영상/스크린샷) + UE_LOG + World 상태"를 강제하여 재현 실패 확률을 낮춘다.
3. **누구나 리포팅 가능**: QA뿐 아니라 기획/개발도 쉽게 리포팅할 수 있게 한다.
4. **Jira Cloud에 즉시 주입**: 팀이 이미 쓰는 BTS에 바로 이슈가 생성되게 한다.
5. **보안/도입장벽 최소화**: 웹 포털을 없애고, 버그 데이터는 로컬 및 고객 BTS에만 존재하게 한다.
6. **글로벌 설계 내재화**: MVP부터 영어 기반으로 개발하여 추후 글로벌 확장 마찰을 최소화한다.

### 3.2 비목표 (MVP에서 하지 않는다)

- 웹 포털(리포트 리스트/검색/대시보드) 제공
- 타 이슈 트래커 연동 (Jira Cloud 단독 집중)
- DemoNetDriver 기반 Replay System 활용 — 60초 클립에 부적합하므로 제외 (기술 판단 섹션 8.1.2 참조)
- 콘솔 플랫폼(PS5, Xbox) 완전 지원 — MVP는 Windows Standalone + Editor 우선, 콘솔은 Phase B 이후
- Unreal Visual Logger / Unreal Insights 데이터 완전 통합 — Phase B 이후 확장
- 크래시 후 분석 전용 기능 (Backtrace/BugSplat 대체) — Bug-OneTouch-unreal은 "크래시 전" 재현 영상 보존에 집중
- 멀티플레이어/서버 사이드 로그 자동 수집 — 복잡도 高, Phase B 이후

---

## 4. 타겟 사용자 / 페르소나

### 4.1 1차 타겟 시장

- **한국 중소 → 중견 → 대형** Unreal Engine 기반 게임 개발사
- 이미 **Jira Cloud**를 사용하고 있으며, 리포팅 품질/속도 개선 니즈가 존재
- **UE5** 기반 PC/콘솔/모바일 개발팀 (AAA 타이틀 개발사, 인디 팀 포함)

### 4.2 타겟 세그먼트별 특성

| 세그먼트 | 팀 규모 | 특성 | Bug-OneTouch-unreal 적합성 |
|---------|--------|------|----------------|
| 인디/소규모 | 1~5인 | UE5 무료 플랜(매출 $1M 미만), 도구 예산 극소 | Free 티어로 진입 |
| 중소 스튜디오 | 5~20인 | QA팀 1~3인, Jira 도입 의향, PC/콘솔 타이틀 | Pro 티어 주력 타겟 |
| 중견 개발사 | 20~80인 | QA 조직 분리, 보안 민감, 사전 공개 빌드 보호 | Studio 티어, 로컬-퍼스트 강조 |
| 대형 개발사 | 80인+ | 인하우스 도구 있음, SSO/SLA 요구 | Enterprise, 도입 결정 주기 김 |

### 4.3 페르소나

**페르소나 A — QA 테스터 (이수빈, 중소 Unreal 스튜디오 QA 2년차)**
- 재현 불가/정보 부족으로 리포트가 반려되는 경험이 많다.
- UE4 → UE5 전환 이후 Visual Logger를 쓰라는 지시를 받았지만 진입 장벽이 높다.
- 핫키 한 번으로 UE_LOG + 영상 + 상태가 자동 수집되길 원한다.

**페르소나 B — C++ 클라이언트 개발자 (박민호, 중소 Unreal 스튜디오 클라이언트 4년차)**
- QA 리포트에 로그가 없어 재현에 수 시간을 낭비한다. 특히 C++ 크래시는 UE_LOG가 없으면 원인 파악이 극도로 어렵다.
- World 상태(현재 Level, Actor 수, Game Mode)가 자동으로 포함되길 원한다.
- Blueprint 팀원이 남긴 리포트도 개발자가 즉시 재현할 수 있는 수준이길 원한다.

**페르소나 C — 기획자/PM (최지우, 중소 Unreal 스튜디오 기획 3년차)**
- Unreal 에디터는 낯설어 직접 Visual Logger를 켜는 것이 부담스럽다.
- 버그 발견 시 Slack으로만 알리고 등록은 QA에 위임하는 현실.
- 비개발자도 쉽게 캡처해서 Jira에 바로 올릴 수 있는 도구가 필요하다.

---

## 5. 핵심 가치 제안

1. **"재현 불가를 놓치지 않기"**: 직전 60초 영상 + UE_LOG + World/Actor 상태 스냅샷으로 컨텍스트 완전 보존
2. **"리포팅의 마찰 제거"**: 핫키 자동 수집 + 템플릿 + Jira 즉시 이슈 생성 — 비개발자도 가능
3. **"담당자가 원인을 빠르게 좁히게"**: 표준화된 UE 환경/빌드/UE_LOG 카테고리/상태 묶음 제공
4. **"데이터 주권 보장"**: 로컬-퍼스트 설계로 영상 데이터 외부 서버 경유 없음 → 사전 공개 빌드 보안

---

## 6. 사용자 경험 (UX) — 핵심 플로우

### 6.1 최초 설정 (1회)

1. Unreal 프로젝트에 Bug-OneTouch-unreal 플러그인 설치 (.uplugin 방식, Fab Marketplace 또는 직접 설치)
2. **(옵션) 계정 기반 로그인/활성화**
3. "Connect Jira" 버튼 → 브라우저에서 Jira OAuth 동의
4. Jira Site 선택(1개) → Jira Project 선택(수동, 1개) → Issue Type 선택
5. UE_LOG 카테고리 필터 설정 (기본: Warning 이상 모두 수집)
6. 기본 템플릿/옵션 저장

> MVP 제약: 사용자 1명은 Jira 1개 프로젝트만 사용한다고 가정.

### 6.2 버그 발견 → 캡처 (핫키)

- 사용자가 게임 플레이(또는 에디터 플레이) 중 결함 발견
- 핫키(예: F12) → **즉시 스크린샷 캡처** + **리포트 오버레이 UI 표시**
- 동시에 "직전 60초 영상"을 리포트 번들로 확정 저장(로컬)
- UE_LOG 링버퍼에서 최근 N분 로그를 카테고리별로 추출
- 현재 World/Level 이름, Actor 수, Game Mode, 프레임레이트, 메모리 등 Unreal 상태 스냅샷 자동 생성

### 6.3 리포트 작성 → 이슈 생성

1. 제목/설명/재현 스텝/기대·실제/심각도 입력 (오버레이 내)
2. 기본 첨부: 스크린샷 + UE_LOG(카테고리별) + 상태 스냅샷
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

Bug-OneTouch-unreal은 Unreal Engine의 두 가지 실행 컨텍스트에서 서로 다른 기능 집합을 활성화한다. 이는 UE5 플러그인 모듈 타입(`Runtime` vs `Editor`)과 직접 대응된다.

#### 플레이 모드 (PIE / Standalone Game) — 핵심 캡처 기능 활성

플레이어 또는 QA가 실제 게임플레이를 수행하는 컨텍스트. **버그 재현 증거 수집의 핵심 기능**이 이 모드에서 동작한다.

| 기능 | 설명 |
|------|------|
| 60초 영상 링버퍼 | SceneCapture2D + RenderTarget 기반 실시간 캡처 |
| 스크린샷 캡처 | FScreenshotRequest 기반 즉시 캡처 |
| UE_LOG 수집 | FOutputDeviceRedirector 통한 실시간 로그 링버퍼 |
| 상태 스냅샷 | UWorld/Actor/GameMode 상태 직렬화 |
| 핫키 트리거 | 사용자 지정 핫키로 버그 리포트 즉시 생성 |

> PIE(Play In Editor)와 Standalone Game 모두 지원하며, PIE에서는 에디터 프로세스 내에서 게임 월드가 실행되므로 UE_LOG 수집 경로가 동일하게 적용된다. Standalone은 별도 프로세스이므로 IPC 또는 공유 파일 방식의 로그 경로가 필요할 수 있다.

#### 에디터 모드 — 설정/관리 기능만 활성

게임이 실행되지 않은 상태(에디터가 열려 있지만 플레이 중이 아닌 상태). **캡처 기능은 비활성화**되며, 설정 및 관리 기능만 동작한다.

| 기능 | 설명 |
|------|------|
| Jira 연결 설정 | OAuth 3LO 인증 및 프로젝트 매핑 |
| 캡처 설정 | 핫키 변경, 링버퍼 크기, 해상도 설정 |
| 번들 관리 | 이전 캡처 번들 목록 조회, 재전송, 삭제 |
| 플러그인 설정 | 로그 마스킹 규칙, 보존 정책 설정 |

> 에디터 모드 전용 기능은 `BugOneTouchEditor` 모듈(`Editor` 타입)에 구현되며, 패키지 빌드에는 포함되지 않는다. 이를 통해 배포 빌드 크기 및 런타임 오버헤드를 최소화한다.

#### 모드별 기능 활성화 요약

| 기능 | 에디터 모드 | PIE | Standalone |
|------|:---:|:---:|:---:|
| 60초 영상 링버퍼 | - | O | O |
| 스크린샷 캡처 | - | O | O |
| UE_LOG 링버퍼 | - | O | O |
| 상태 스냅샷 | - | O | O |
| 핫키 트리거 | - | O | O |
| Jira 연결 설정 | O | - | - |
| 캡처/링버퍼 설정 | O | - | - |
| 번들 관리 패널 | O | - | - |
| 로그 마스킹 설정 | O | - | - |

### 6.6 크래시 자동 감지 및 복구 플로우

PIE 또는 Standalone 플레이 중 크래시가 발생하면, Bug-OneTouch-unreal은 사용자가 핫키를 누르지 않아도 직전 데이터를 자동으로 보존하고, 에디터 재실행 후 Jira 등록까지 안내하는 복구 플로우를 제공한다.

#### 6.6.1 크래시 시 데이터 보존 전략 (3중 레이어)

크래시 핸들러 내에서는 async-signal-safe 제약이 있으므로, 다층 보존 전략으로 데이터 손실을 최소화한다.

**레이어 1 — 주기적 플러시 (가장 신뢰할 수 있는 방법)** **(Must)**

크래시 여부와 무관하게 플레이 모드 동안 주기적으로 디스크에 저장한다.

- 플러시 주기: 로그 5초 / 상태 10초 / 영상 30초 슬라이딩 윈도우
- 저장 경로: `FPaths::ProjectSavedDir()/BugOneTouch/crash_bundles/`
- 플러시 대상:
  - UE_LOG 링버퍼 → `crash_log.txt`
  - World/Actor 상태 → `state_snapshot.json`
  - SceneCapture2D 영상 → `replay_buffer` 세그먼트 파일

**레이어 2 — 크래시 콜백 즉시 번들 생성** **(Should)**

- `FCoreDelegates::OnHandleSystemError` 델리게이트 바인딩으로 크래시 직후 인터셉트
- `FCoreDelegates::OnShutdownAfterError`에서 최종 플러시 시도
- 콜백 내에서는 async-signal-safe 함수만 사용: atomic 플래그 설정 + 저수준 `write()` / `FlushViewOfFile()` 한정

**레이어 3 — abnormal_exit.flag 비정상 종료 감지** **(Must)**

- `BeginPlay` 시 플래그 파일 생성 (`FFileHelper::SaveStringToFile`)
- 정상 종료(`EndPlay`) 시 플래그 삭제
- 다음 에디터 시작 시 `FEditorDelegates::OnEditorInitialized`에서 플래그 잔존 여부 확인 → 비정상 종료로 판단

#### 6.6.2 크래시 유형별 보존 범위

| 크래시 유형 | 감지 방법 | 데이터 보존율 | 비고 |
|------------|----------|:----------:|------|
| Ensure/Check 매크로 | OnHandleSystemError | ~95% | 콜백 실행 보장 |
| Native 크래시 (SIGSEGV) | OnHandleSystemError + flag | ~80% | Standalone에서 보존율 높음 |
| PIE 에디터 다운 | abnormal_exit.flag | ~70% | 콜백 실행 미보장 |
| OOM / GPU Hang | abnormal_exit.flag | ~50% | 플러시 실행 불가할 수 있음 |

#### 6.6.3 에디터 재실행 후 복구 UI

에디터가 재실행되면 `FEditorDelegates::OnEditorInitialized`에 바인딩된 스캐너가 자동 실행된다.

1. `crash_bundles` 폴더 스캔 → `manifest.json`의 `registered: false` 항목 필터링
2. 미등록 번들 발견 시 Slate 알림 팝업 표시
3. 크래시 리포트 목록 UI (SWindow + Slate):
   - 시간순 정렬 (최신 먼저)
   - 각 항목: `yyyy-mm-dd hh:mm:ss — 크래시 리포트` + 크래시 메시지 1줄
   - 데이터 보존 상태 표시: 영상(초) / 로그(완전|부분|없음) / 상태(완전|부분|없음)
   - 썸네일: `last_screenshot.png` 프리뷰
   - 액션 버튼: [Jira에 등록] [로컬에서 열기] [삭제]
4. [Jira에 등록] 시 기존 버그 리포트 작성 UI 재활용:
   - 제목 자동 생성: `[Crash] {crash_type}: {crash_message 첫 50자}`
   - 설명에 스택 트레이스 + 시스템 정보 자동 삽입
   - 첨부파일: 번들 내 영상 / 스크린샷 / 로그

#### 6.6.4 크래시 번들 구조

```
crash_{yyyy-MM-dd_HH-mm-ss}.bot-unreal/
├── manifest.json          # 크래시 메타데이터
├── crash_log.txt          # 스택 트레이스 + 직전 500줄 UE_LOG
├── last_screenshot.png    # 크래시 직전 마지막 프레임
├── replay_buffer.mp4      # 보존된 영상 (최대 60초)
├── state_snapshot.json    # 마지막 World/Actor 상태 스냅샷
└── system_info.json       # OS/GPU/UE버전/프로젝트 정보
```

> 크래시 번들은 일반 버그 리포트 번들(`Reports/`)과 별도 경로(`crash_bundles/`)에 저장한다. 번들 최대 보관 수는 10개(FIFO), 보관 기간은 30일(기본값, 설정 가능)이다.

#### 6.6.5 manifest.json 크래시 전용 필드

일반 번들의 `manifest.json` 필드에 더해 아래 크래시 전용 필드가 추가된다.

- `crash_type`: `"ensure_check"` | `"native_crash"` | `"out_of_memory"` | `"gpu_hang"` | `"unknown"`
- `crash_message`: 크래시 메시지 첫 줄
- `stack_trace`: 전체 스택 트레이스 (`FPlatformStackWalk` 결과)
- `data_integrity`: 파일별 보존 상태 (`"complete"` | `"partial"` | `"missing"`)
- `auto_saved`: `true`
- `registered`: `false` (Jira 등록 완료 시 `true`로 변경)
- `jira_issue_key`: string | Jira 등록 완료 시 기록 (예: `"GAME-123"`), 미등록 시 `null`
- `registered_at`: string | ISO 8601 형식 Jira 등록 완료 시각, 미등록 시 `null`

#### 6.6.6 PIE vs Standalone 크래시 차이

| 항목 | PIE | Standalone |
|------|-----|-----------|
| 크래시 콜백 실행 | 미보장 (에디터 프로세스 공유) | 높음 (독립 프로세스) |
| 데이터 보존 전략 | 주기적 플러시 의존 | 콜백 + 플러시 병용 |
| 복구 UI 진입 | 에디터 재실행 후 자동 | 에디터에서 번들 폴더 수동 임포트 |

---

## 7. 제품 범위 (Scope)

### 7.1 Unreal 플러그인 (클라이언트)

**핵심 구성 (.uplugin 모듈 기반)**

```
BugOneTouchUnreal.uplugin
  Modules:
    BugOneTouchCore        - 핵심 로직, 로그 수집, 번들 관리 (Runtime)
    BugOneTouchCapture     - SceneCapture2D, 스크린샷, 영상 링버퍼 (Runtime)
    BugOneTouchJira        - Jira Cloud API 통신, OAuth 토큰 관리 (Runtime)
    BugOneTouchEditor      - 에디터 UI, 설정 패널, 테스트 도구 (Editor)
```

- **Capture Engine**: 스크린샷/영상 링버퍼/UE_LOG/상태 스냅샷
- **Local Bundle Manager**: 저장/보관/재시도
- **Report UI**: 오버레이/설정 (Slate/UMG 기반)
- **Jira Cloud API Client**: REST API + 첨부 업로드
- **Auth / Connection UI**: 브로커 연동 및 상태 표시

### 7.2 Auth Broker (Supabase) — Unity PRD v2.1과 공유

인증 모델 원칙:

1. **사용자(개인) 단위 1:1 인증** — Jira 연결은 프로젝트/팀 단위가 아닌 개인이 OAuth 3LO로 직접 인증한다. 팀 내 여러 사용자는 각자 자신의 Jira 계정으로 인증하며, 이슈는 해당 사용자 명의로 생성된다.
2. **토큰 로컬 저장** — refresh token을 포함한 OAuth 토큰은 로컬 머신의 `Saved/Bug-OneTouch-unreal/auth.enc`에 AES-256 암호화 저장한다.
3. **Auth Broker 역할 한정** — Broker는 Jira Cloud OAuth 3LO의 code→token 교환 및 refresh token 갱신(회전) 처리만 중계한다. 사용자 데이터(영상/스크린샷/로그 포함)를 저장하지 않는다.
4. **버그 데이터 저장 없음** — 영상/스크린샷/로그는 브로커로 업로드하지 않는다.
5. Unity Bug-OneTouch-unreal과 동일한 Auth Broker 인프라 재사용 가능 → 통합 운영 시 비용 절감

---

## 8. 상세 요구사항

### 8.1 캡처 요구사항 (MUST)

#### 8.1.1 스크린샷

- 캡처 트리거 시점의 화면을 PNG/JPG로 저장
- 기본 해상도: 현재 렌더링 해상도(필요 시 다운스케일 옵션)
- Unreal 구현: `FScreenshotRequest::RequestScreenshot()` 트리거 + `UGameViewportClient::OnScreenshotCaptured()` 델리게이트로 픽셀 버퍼 수신 (UE5.4+ 기준 검증된 경로; `FHighResScreenshotConfig`는 에디터 전용이므로 런타임 미사용)
  <!-- 구현 시 검증 필요: UE 버전별 OnScreenshotCaptured 시그니처 확인 -->

- 저장 위치: 로컬 번들 폴더 내 `screenshot.png`

#### 8.1.2 영상 (링버퍼 기반) — 로컬 저장 기본, Unreal 구현 판단

**기술 판단 근거**: Unreal Replay System(DemoNetDriver)은 "네트워크 복제 데이터 재생" 방식으로, 60초 영상 클립 생성에 적합하지 않다. 다음 이유로 제외한다:

1. DemoNetDriver는 NetMode가 없는 싱글플레이어/에디터 플레이 환경에서 작동이 제한된다.
2. 복제 데이터 기반이므로 "화면 영상" 생성이 아닌 "게임 상태 재생"이라, 재현성은 높지만 일반 사용자가 바로 볼 수 있는 MP4가 아니다.
3. 60초 링버퍼 기반 실시간 인코딩에는 별도 구현이 필요하다.

**채택 구현 방식 (하이브리드)**:

| 방식 | 설명 | 장단점 |
|------|------|--------|
| **SceneCapture2D + RenderTarget** | `USceneCaptureComponent2D` + `UTextureRenderTarget2D` → `FRenderTarget::ReadPixels()` 로 픽셀 추출 후 링버퍼 저장 및 인코딩 (UE5.4+ 기준) | 범용, 성능 영향 있음 <!-- 구현 시 검증 필요: ReadPixels 비동기 경로(EnqueueRenderCommand) 활용 권장 --> |
| **FMovieSceneCapture 기반** | Unreal Sequencer 캡처 파이프라인 활용 | 에디터 전용, 런타임 미지원 |
| **플랫폼 네이티브 인코더** | Windows: Media Foundation / Desktop Duplication API | 성능 최적, Windows 전용 |
| **OBS Virtual Camera SDK (조사)** | 외부 의존성 증가 | Phase B 검토 |

**MVP 선택**: Windows 환경에서는 **Desktop Duplication API** 기반 링버퍼 구현을 우선 채택. 에디터 모드에서는 FMovieSceneCapture 파이프라인 검토 병행.

- 기본값: **캡처 시점 기준 직전 60초**를 파일로 확정 저장
- 저장 기본, 첨부는 옵션
- 기본 품질 프리셋:
  - 720p / 30fps / H.264 / 목표 비트레이트 8~12 Mbps
  - 옵션: 1080p 프리셋
- 목표: 일반적인 60초 영상이 **200MB 이하**가 되도록 비트레이트 캡 제공
- 성능 요구:
  - 게임 플레이 중 프레임 드랍 최소화 (링버퍼 비동기 인코딩)
  - 캡처 시점 저장 확정은 빠르게 완료 (사용자 체감 3초 이내)

> 플랫폼 지원(영상):
> - MVP: Windows Standalone + Editor 우선
> - 차후: Android/iOS/콘솔은 플랫폼별 인코더 적용(Phase B 확장)

#### 8.1.3 UE_LOG 수집

- **Unreal 특화**: Unity의 Console 로그와 달리 Unreal은 `UE_LOG(LogCategory, Level, ...)` 카테고리 체계를 사용한다.
- 최소 포함:
  - 카테고리별 필터링 가능한 링버퍼 (기본: Warning/Error 이상 모두, 설정으로 Verbose 포함 가능)
  - `Saved/Logs/` 디렉토리의 최신 로그 파일 전체 첨부 옵션
  - 로그 인터셉트 구현: `FOutputDeviceRedirector::Get()->AddOutputDevice(&CustomDevice)` 로 커스텀 `FOutputDevice` 서브클래스를 전역 리디렉터에 등록 (UE5.4+ 기준)
  - 크래시 직전 UE_LOG flush 보장 (비정상 종료 시에도 마지막 로그 보존; 크래시 핸들러에서 best-effort flush 구현)
- 전송 전 처리:
  - 로그 압축(zip) 옵션 (기본 ON)
  - 민감정보 마스킹(간단한 정규식 기반, MVP에서는 최소 수준)
- 저장 위치: `logs/` 폴더 및 `logs.zip`

#### 8.1.4 Unreal 상태 스냅샷 (State Snapshot)

MVP 기본 수집 (Unreal 특화 항목 포함):

- **앱/빌드 정보**: 게임 버전, 빌드 번호, Git commit hash(가능 시), Unreal Engine 버전 (4.27 / 5.x 구분)
- **런타임 환경**: OS/디바이스/CPU/GPU/메모리/해상도/품질 설정 (`GSystemSettings`, `GEngine` 활용)
- **Unreal 실행 컨텍스트**:
  - 현재 World 이름: `GEngine->GetWorldContexts()` 순회 → `UWorld::GetName()` (UE5.4+ 기준)
  - 현재 Level 이름: `UWorld::GetCurrentLevel()` <!-- 구현 시 검증 필요: GetCurrentLevel()은 에디터 컨텍스트 전용일 수 있으므로, 런타임에서는 GetOuter() 또는 PersistentLevel 참조로 대체 검토 -->
  - 전체 액터 순회/집계: `UGameplayStatics::GetAllActorsOfClass(World, AActor::StaticClass(), OutActors)` 후 `OutActors.Num()` (UE5.4+ 기준)
  - Game Mode 클래스명: `UWorld::GetAuthGameMode()` → `GetClass()->GetName()` <!-- 구현 시 검증 필요: 클라이언트 빌드에서는 GameMode가 null일 수 있음 -->
  - Player Controller 상태 (위치, 회전)
  - 프레임레이트 샘플 (FApp::GetDeltaTime 기반)
  - GPU / CPU 프레임 시간 (Unreal Stats 연동 가능 시)
- **네트워크 상태** (멀티플레이어 프로젝트, 선택):
  - 연결 타입 (Listen Server / Dedicated Server / Client)
  - Ping (가능 시, MVP Optional)

확장 포인트(개발자 주입):

- `UBug-OneTouch-unrealContext::Add(Key, Value)` C++ API로 커스텀 K/V 추가
- `IBug-OneTouch-unrealContextProvider` 인터페이스로 프로젝트별 컨텍스트 프로바이더 등록 (Blueprint 노출 포함)

저장 위치: `state/state.json`

#### 8.1.5 크래시 자동 캡처 (MUST)

크래시 발생 시 사용자 개입 없이 직전 데이터를 자동으로 수집하고 번들로 보존한다. 섹션 6.6 플로우를 구현하기 위한 구체적 요구사항이다.

- **주기적 플러시 간격 설정 가능**: 기본값 — 로그 5초 / 상태 10초 / 영상 30초. 설정 UI에서 조정 가능.
- **크래시 콜백 바인딩 필수**: `FCoreDelegates::OnHandleSystemError` 반드시 바인딩. `FCoreDelegates::OnShutdownAfterError`에서 최종 플러시 시도.
- **abnormal_exit.flag**: `BeginPlay`에서 플래그 파일 생성, 정상 종료 시 삭제. 에디터 재시작 시 잔존 여부로 비정상 종료 판단.
- **크래시 번들 보관 정책**: 최대 10개(FIFO), 30일 보관(기본값, 설정 가능). `crash_bundles/` 폴더는 일반 번들(`Reports/`)과 분리.
- **크래시 핸들러 내 금지 사항**: 메모리 할당(`new`/`malloc`) 금지, `std::mutex` 잠금 금지, `UE_LOG` 출력 금지 (데드락 위험). atomic 플래그 설정과 저수준 `write()`/`FlushViewOfFile()`만 허용.
- **스택 트레이스 수집**: `FPlatformStackWalk::StackWalkAndDump()` 결과를 `crash_log.txt`에 포함.

#### 8.1.6 Crash Reporter 연동 (Should)

- Unreal 내장 Crash Reporter는 크래시 후 로그/콜스택/minidump를 수집한다.
- Bug-OneTouch-unreal은 Crash Reporter 엔드포인트(`CrashReportClient`)를 후킹하여 크래시 번들에 60초 영상 링버퍼를 자동 포함할 수 있다.
- **MVP 범위**: 크래시 발생 시 이미 저장된 링버퍼 파일을 크래시 번들에 복사하는 수준 (Crash Reporter 완전 대체 아님)
- **Phase B 확장**: Crash Reporter 커스텀 엔드포인트 → Bug-OneTouch-unreal 번들 자동 Jira 이슈 생성

---

### 8.2 로컬 번들 요구사항 (MUST)

#### 8.2.1 번들 구조 (예시)

```
Bug-OneTouch-unreal/
  Reports/
    2026-02-27_153012_AB12CD/
      manifest.json
      screenshot.png
      video.mp4               # optional (기본 저장)
      logs/
        ue_log_filtered.txt   # 카테고리 필터링된 로그
        full_log.zip          # optional (전체 로그 파일)
      state/
        state.json            # Unreal 상태 스냅샷 (World/Level/Actor 등 포함)
      attachments/            # optional
```

#### 8.2.2 manifest.json (MUST)

필수 필드 (Unreal 특화 필드 포함):

- `report_id` (UUID)
- `created_at`
- `engine` (값: `"unreal"`)
- `engine_version` (예: `"5.4.2"`)
- `app_version` / `build_number`
- `platform` / `device` / `os`
- `world_name` / `level_name` / `game_mode`
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

이슈 본문 템플릿 (Unreal 특화 섹션 포함):

- Summary
- Repro Steps
- Expected vs Actual
- **Unreal Environment**: Engine Version / World / Level / Game Mode / Actor Count / FPS
- Build Info: App Version / Build Number / Git Hash (가능 시)
- Device Info: OS / CPU / GPU / Memory / Resolution
- Artifacts: 스크린샷/로그/영상 목록 및 크기

필드:

- labels(기본: `bug-onetouch-unreal`, `unreal`, `ue5`)
- priority/severity 매핑(간단 매핑)
- component(선택)

#### 8.3.3 첨부 (Attachments)

- 기본 첨부: screenshot.png, ue_log_filtered.zip, state.json
- 옵션 첨부: video.mp4, full_log.zip
- 업로드 전:
  - Jira 업로드 제한(meta API) 확인 후 초과 시 영상 자동 제외 (또는 다운스케일은 Phase 확장)
- 업로드 실패:
  - 번들은 로컬에 유지
  - 에러 메시지 표시(권한/제한 안내 최소)

#### 8.3.4 토큰 갱신 (Refresh)

- OAuth 토큰은 **개인 사용자 단위 로컬 파일(`Saved/Bug-OneTouch-unreal/auth.enc`, AES-256 암호화)**에 저장된다.
- refresh token은 회전(rotating)이므로, refresh 응답에서 새 refresh token이 오면 반드시 `auth.enc`에 교체 저장한다.
- 연속 refresh 경쟁 방지를 위해 "연결 단위 락" 적용(브로커에서 처리)
- Auth Broker는 code→token 교환 및 refresh 갱신만 중계하며, 토큰을 서버 측에 보관하지 않는다.

---

### 8.4 Auth Broker (Supabase) 요구사항 (MUST) — Unity PRD v2.1과 동일

Auth Broker 전체 요구사항은 Unity PRD v2.1 섹션 8.4를 따른다. Unreal 특화 차이점만 아래에 기술한다.

#### 8.4.1 인증/토큰 모델 원칙 (Unreal 확정)

1. **사용자(개인) 단위 1:1 인증** — Jira 연결은 개인이 OAuth 3LO로 직접 인증한다. 팀 내 여러 사용자는 각자 자신의 Jira 계정으로 인증하며, 이슈는 해당 사용자 명의로 생성된다.
2. **토큰 로컬 저장** — refresh token을 포함한 OAuth 토큰은 로컬 머신의 `Saved/Bug-OneTouch-unreal/auth.enc`에 AES-256 암호화 저장한다. Supabase Vault는 사용하지 않는다.
3. **Auth Broker 역할 한정** — Broker는 code→token 교환 및 refresh token 갱신(회전)만 중계한다. 사용자 데이터를 보관하지 않는다.
4. **이슈 생성 명의** — 제출 시 인증된 사용자의 Jira 계정으로 이슈를 생성한다. "팀 공용 계정" 방식은 지원하지 않는다.

#### 8.4.2 Unreal 특화 차이점

- **클라이언트 인증**: Unity 계정 기반 식별자 대신, 초기 MVP에서는 이메일/매직링크 또는 간단한 API Key 기반으로 대체 가능 (Unreal 에디터에 Unity 계정 개념이 없음)
- **OAuth 토큰 저장**: `Saved/Bug-OneTouch-unreal/auth.enc` (AES-256 암호화 로컬 파일). Unity EditorPrefs 암호화 방식과 다름. UE 플랫폼별 Keychain/Credential Store는 Phase B 확장 검토.
- **테넌트 구분**: Unity+Unreal 통합 운영 시 동일 Supabase 인스턴스 내 `engine` 필드로 구분 가능

#### 8.4.3 데이터 모델 추가

기존 Unity PRD v2.1 `private.oauth_connections` 테이블에 다음 필드 추가:

- `engine` (varchar: `"unity"` / `"unreal"`)
- `ue_version` (varchar, optional: `"5.4"`)

---

### 8.5 Unreal 플러그인 기술 아키텍처

#### 8.5.1 플러그인 구조 (.uplugin)

```json
{
  "FileVersion": 3,
  "Version": 1,
  "VersionName": "1.0.0",
  "FriendlyName": "Bug-OneTouch-unreal",
  "Description": "로컬-퍼스트 버그 리포팅 플러그인 for Unreal Engine",
  "Category": "QA Tools",
  "Modules": [
    {
      "Name": "BugOneTouchCore",
      "Type": "Runtime",
      "LoadingPhase": "Default"
    },
    {
      "Name": "BugOneTouchCapture",
      "Type": "Runtime",
      "LoadingPhase": "Default"
    },
    {
      "Name": "BugOneTouchJira",
      "Type": "Runtime",
      "LoadingPhase": "Default"
    },
    {
      "Name": "BugOneTouchEditor",
      "Type": "Editor",
      "LoadingPhase": "PostEngineInit"
    }
  ]
}
```

#### 8.5.2 UE 버전 호환성 매트릭스

| UE 버전 | 지원 여부 | 비고 |
|---------|---------|------|
| UE 5.4 | MVP 우선 지원 | 2024년 공식 릴리즈, 안정 버전 |
| UE 5.5 | MVP 지원 | 최신 LTS 검토 |
| UE 5.6+ | Phase B | 출시 시점 대응 |
| UE 5.3 | 요청 시 지원 | 디자인 파트너 요청 기반 |
| UE 4.27 | Phase B 이후 | LTS 유지보수 버전, 요청 기반 |

#### 8.5.3 UE_LOG 링버퍼 구현 방식

```cpp
// 개념 구현 (비요구사항)
class FBug-OneTouch-unrealLogDevice : public FOutputDevice
{
public:
    virtual void Serialize(const TCHAR* V, ELogVerbosity::Type Verbosity,
                           const FName& Category) override
    {
        // 링버퍼에 로그 엔트리 추가 (카테고리/Level 필터 적용)
        if (ShouldCapture(Verbosity, Category))
        {
            RingBuffer.Enqueue(FLogEntry{V, Verbosity, Category, FDateTime::Now()});
        }
    }
private:
    TCircularBuffer<FLogEntry> RingBuffer; // 링버퍼, N분 분량
};
```

---

## 9. 가격 전략

### 9.1 가격 구조

5개 분석 모델의 권고를 종합한 SMB 친화형 좌석 기반(seat-based) 구독 구조:

| 티어 | 가격 | 대상 | 포함 기능 |
|------|------|------|---------|
| **Free** | 무료 | 인디/1~3인 팀, 월 50건 이하 | 월 50건 리포트, 30초 영상, 기본 Jira 연동 |
| **Pro** | $24~29/seat/월 (약 35,000~42,000원) | 중소 팀 (5~20인) | 무제한 리포트, 60초 영상, 전체 Jira 연동, 이메일 지원 |
| **Studio** | $49/seat/월 (약 71,000원) | 중견 개발사 (20~80인) | Pro 기능 + SSO, 감사 로그, 우선 지원, SLA |
| **Enterprise** | 협의 | 대형 개발사 (80인+) | 커스텀 배포, 온프레미스 옵션, 전담 CS, 커스텀 계약 |

**연간 결제 할인**: 월 기준 대비 20% 할인 (연간 일시 결제 시)

**Unreal 가격 설정 근거**:
- Unity Pro 가격($9/seat/월) 대비 Unreal Pro를 높게 설정하는 이유:
  - Unreal SMB는 수가 적지만 "버그 재현 불가 1건"의 비용이 더 크다 (PC/콘솔 타이틀 개발)
  - C++ 기반 기술 스택으로 플러그인 기술 지원 난도가 높음 → 프리미엄 정당화
  - BetaHub 등 경쟁사 대비 로컬-퍼스트 차별화로 지불의사 높은 세그먼트 타겟
- Codex xhigh 분석 권고 가격대: Starter 9.9만원/프로젝트/월, Studio 29.9만원/프로젝트/월 → 좌석 기반으로 전환 시 $24~29/seat 수준

**Unity+Unreal 통합 라이선스**:
- Unity Pro + Unreal Pro 동시 구독 시 **15% 번들 할인** (Phase B 이후 제공)

### 9.2 PLG (Product-Led Growth) 전환 설계

Free → Pro 전환 트리거:

- 팀 규모 증가 (3인 초과 시 Pro 필요)
- 월 리포트 한도 초과 (50건)
- 영상 길이 한도 초과 (30초 → 60초)
- UE_LOG 카테고리 필터 고급 설정 필요
- 우선 지원 요청

Pro → Studio 전환 트리거:

- SSO 요구
- 감사 로그 요구
- SLA 요구
- 팀 단위 관리 기능 요구

### 9.3 수익화 로드맵

| 단계 | 시기 | 목표 | KPI |
|------|------|------|-----|
| **1단계: 레퍼런스 확보** | 0~6개월 | Free 티어 5~10개 Unreal 스튜디오 레퍼런스 | 재현 불가 버그 감소 정량 지표 확보 |
| **2단계: 유료 전환** | 6~18개월 | Pro 전환 15~30개사 | MRR $5K+ (Unreal 단독) |
| **3단계: 업셀** | 18개월+ | Studio/Enterprise 계약 + Unity 크로스셀 | ARR $100K+ (Unity+Unreal 통합) |

---

## 10. Go-to-Market 전략

### 10.1 기본 시나리오: Unity 출시 이후 Unreal 확장

5개 모델 토론 합의 결과, **Unity 먼저 출시 후 Unreal 확장**이 기본 시나리오다:

- Unreal 단독 성공률: 20~30%
- Unity+Unreal 통합 성공률: 40~45%
- Unity 고객 기반을 레버리지로 활용한 Unreal 크로스셀이 CAC 절감에 유리

**타임라인 (기본 시나리오)**:

| 단계 | 시기 | 내용 |
|------|------|------|
| Unity MVP | 0~6개월 | Unity PRD v2.1 기준 출시 |
| Unity 안정화 | 6~12개월 | 레퍼런스 확보, MRR $2K+ |
| Unreal Alpha | 12~18개월 | 3~5개 Unreal 디자인 파트너와 공동 개발 |
| Unreal Beta | 18~24개월 | Fab Marketplace 등재, 공개 베타 |
| Unreal GA | 24개월+ | Unreal Fest Seoul 시즌 맞춤 정식 런칭 |

### 10.2 Phase 1: Unreal 시장 진입 준비 (0~6개월) — Unity 출시 병행

**목표**: Unreal 디자인 파트너 3~5개사 확보, 기술 검증

**채널 전략**:
- **Unreal 커뮤니티 아웃리치**: Unreal Engine Korea 커뮤니티, Epic Korea 파트너 프로그램 접촉
- **디자인 파트너 직접 모집**: UE5 기반 중소 스튜디오 10개사 타겟 콜드 아웃리치
- **Unity 레퍼런스 활용**: Unity Bug-OneTouch-unreal 고객 중 Unreal 병행 사용사 크로스셀

**성공 기준**:
- Unreal 디자인 파트너 3개사 이상 확보
- 기술 PoC (Windows, UE 5.4 기준) 완료

### 10.3 Phase 2: Unreal MVP 출시 (6~18개월) — 한국 SMB 집중

**목표**: Unreal Pro 전환 15개사, MRR $3K+ (Unreal 단독)

**채널 전략**:
- **Fab Marketplace 등재**: Unreal 생태계 내 자연 검색 트래픽 확보 (88/12 수수료 유리)
- **Unreal Fest Seoul**: 한국 최대 Unreal 개발자 컨퍼런스 스피킹/부스 운영
- **Epic MegaGrants 신청**: Epic 지원 프로그램 활용한 인지도 확보 및 자금 지원
- **케이스 스터디 발행**: Unreal 디자인 파트너의 "재현 불가 버그 X% 감소" 정량 성과

**성공 기준**:
- Fab Marketplace 등재 완료
- Pro 이상 유료 팀 15개사 이상
- MRR $3,000 이상 (Unreal 단독)

### 10.4 Phase 3: 통합 확장 및 글로벌 (18개월+) — Unity+Unreal 통합

**목표**: Unity+Unreal 통합 ARR $100K+ 달성

**채널 전략**:
- **Atlassian Marketplace**: Jira 생태계 내 Unity+Unreal 통합 플러그인 등재
- **GDC (Game Developers Conference)**: 글로벌 Unreal 개발사 대상 영업
- **Epic Games 파트너십**: Verified for Unreal Engine 인증 획득
- **글로벌 Unreal 커뮤니티**: Unreal Forum, Reddit r/unrealengine 커뮤니티 마케팅

**글로벌 확장 로드맵 (Unreal 특화)**:

| 지역 | 시기 | 전략 |
|------|------|------|
| 한국 | 0~24개월 | 검증 거점, 레퍼런스 확보, Unreal Fest Seoul |
| 일본 | 24~36개월 | Unreal 기반 AAA 타이틀 강국, 한국 사례 활용 |
| 북미/유럽 | 30개월+ | GDC, Fab Marketplace, 영어 콘텐츠 |
| 동남아 | 36개월+ | Unreal 모바일 성장 시장 |

### 10.5 리소스 계획 (Unreal 추가 인력)

#### Unreal 추가 필요 역할

| 역할 | FTE | 시기 | 비고 |
|------|-----|------|------|
| Unreal C++ 개발자 | 1.5~2.0 | Phase 2 착수 | 캡처 엔진 + .uplugin + UE 버전 대응 |
| UE QA 엔지니어 | 0.5 | Phase 2 | UE 버전별 호환성 테스트 |
| **Unity+Unreal 추가 소계** | **2.0~2.5 FTE** | | Unity 팀과 백엔드/PM 공유 |

---

## 11. 성공 지표

### 11.1 제품 지표

- 캡처 실행 대비 번들 생성 성공률(%)
- "Submit" 대비 이슈 생성 성공률(%)
- 이슈 생성 평균 소요시간(버튼 클릭→완료)
- 영상 첨부 사용률(옵션 ON 비율)
- UE_LOG 카테고리 필터 커스터마이즈 비율 (Unreal 특화)
- "재현 불가" 라벨 비율 변화(고객사 협조 시)
- 월 활성 팀(MAT, Monthly Active Teams)
- 월 활성 리포트(MAR, Monthly Active Reports)

### 11.2 품질 지표

- 번들 손상률(파일 누락/깨짐)
- UE_LOG/상태 스냅샷 필드 누락률
- 프레임 드랍/성능 영향 (기본 시나리오에서 허용 범위: FPS 5% 이하 저하)
- UE 버전별 호환성 테스트 통과율 (UE 5.4 / 5.5 / 5.6)

### 11.3 운영 SLO / 지원 정책

| SLO 항목 | 목표 | 측정 방식 |
|---------|------|----------|
| Auth Broker 가용성 | 99.5% (월간) | Supabase 상태 모니터링 |
| 토큰 발급 응답시간 (p95) | < 2초 | Edge Function 로그 |
| OAuth 연결 성공률 | > 95% | connect/callback 성공/실패 비율 |
| 영상 링버퍼 저장 소요시간 | < 3초 | 클라이언트 측정 |

| 지원 정책 | Free | Pro | Studio | Enterprise |
|----------|------|-----|--------|-----------|
| 지원 채널 | GitHub Issues | 이메일 | 이메일 + 우선 큐 | 전담 CS |
| 응답 SLA | Best effort | 48시간 | 24시간 | 4시간 |
| UE 버전 지원 범위 | 최신 2개 | 최신 3개 | 최신 4개 + LTS | 협의 |

### 11.4 사업 지표

- MRR / ARR (Unreal 단독 + Unity+Unreal 통합 구분)
- Free → Pro 전환율
- NPS (Net Promoter Score) — 목표: 40+
- 유료 팀 수 (Customer Count)
- Unity 고객 → Unreal 크로스셀 전환율

---

## 12. MVP 범위 정의 (Must / Should / Could)

### Must (MVP 필수)

- Unreal 핫키 + 오버레이 UI (Slate/UMG 기반)
- 스크린샷 + UE_LOG(카테고리별) + Unreal 상태 스냅샷 자동 수집
- 직전 60초 영상 **로컬 저장** (Windows Desktop Duplication API 기반)
- 로컬 번들 관리(저장/재시도/제출 상태)
- Jira Cloud 연결(OAuth) + 이슈 생성 + 첨부(스크린샷/로그/상태) + 영상 옵션 첨부
- Supabase Auth Broker (토큰 교환 중계 전용) + OAuth 토큰 로컬 `Saved/Bug-OneTouch-unreal/auth.enc` AES-256 암호화 저장
- UE 5.4 / 5.5 지원
- Windows Standalone + Editor 지원
- 영어 기반 UI (글로벌 설계 내재화)
- .uplugin 방식 배포 (Fab Marketplace 등재 준비)
- **크래시 자동 감지 (Must)**: 주기적 플러시 (로그 5초 / 상태 10초 / 영상 30초) + `abnormal_exit.flag` 비정상 종료 감지
- **크래시 번들 목록 UI (Must)**: 에디터 재실행 후 미등록 번들 자동 스캔 + SWindow 목록 표시
- **크래시 Jira 등록 (Must)**: 크래시 번들에서 기존 버그 리포트 UI 재활용하여 Jira 이슈 생성

### Should (MVP+)

- 영상 품질 프리셋(720p/1080p) 및 파일 크기 타겟
- UE_LOG 카테고리 필터 편집 UI
- 제출 실패 시 더 친절한 원인 분류(권한/용량/레이트리밋)
- Fab Marketplace 정식 등재
- UE 4.27 지원 (요청 기반)
- **크래시 콜백 내 즉시 번들 생성 (mmap 기반 atomic 플러시)**: `FCoreDelegates::OnHandleSystemError` 콜백 내 atomic 플래그 + mmap 기반 즉시 번들 생성 (Standalone 우선) — 콜백 바인딩 자체는 Must이나, 번들 즉시 생성 로직은 Should
- **Standalone 크래시 번들 임포트**: 에디터 외부에서 발생한 Standalone 크래시 번들을 에디터 내에서 수동 임포트하여 Jira 등록

### Could (후속)

- Android/iOS/콘솔 플랫폼 영상 캡처 지원
- Unreal Visual Logger / Unreal Insights 데이터 통합
- Blueprint 노출 API 고도화
- 멀티플레이어/서버 사이드 로그 자동 수집
- Self-hosted 브로커 (대형사/보안 요구 대응)
- Unity+Unreal 통합 라이선스 번들
- 팀 단위 대시보드/정책 관리
- **크래시 일괄 등록**: 미등록 번들 전체를 한 번에 Jira에 등록하는 배치 기능
- **크래시 패턴 분석**: 동일 스택 트레이스 패턴의 번들을 자동으로 묶어 중복 이슈 방지
- **PIE/Standalone 통합 복구**: PIE 크래시와 Standalone 크래시를 단일 복구 UI에서 통합 관리

---

## 13. 리스크 매트릭스

### 13.1 전체 리스크 매트릭스

| 리스크 | 발생 가능성 | 영향도 | 심각도 | 대응 방안 |
|--------|-----------|--------|--------|---------|
| **BetaHub: Unity+Unreal 동시 지원으로 차별화 무력화** | 높음 | 높음 | 치명적 | 로컬-퍼스트 아키텍처 강조, "버그 처리 시간 단축 시스템"으로 포지셔닝 전환, BetaHub와 통합 방향 검토 |
| **Unreal 단독 TAM 한계 (Unity 대비 1/3 수준)** | 확정적 | 높음 | 치명적 | Unity+Unreal 통합 시나리오를 기본으로, 단독 진입은 부록 A 시나리오로 관리 |
| **C++/.uplugin 기술 난이도 (Unity 대비 2~3배)** | 확정적 | 높음 | 높음 | Unreal C++ 전문 인력 확보, UE 버전 매트릭스 관리, Phase B 이후 콘솔 확장 |
| **SceneCapture2D/링버퍼 성능 영향** | 높음 | 중간 | 높음 | Desktop Duplication API(Windows) 우선 채택, 성능 프로파일링 필수, FPS 5% 미만 목표 |
| **UE 버전 호환성 파편화 (5.3/5.4/5.5/5.6)** | 높음 | 중간 | 높음 | UE 5.4 우선 지원, 버전별 CI 파이프라인 구축, LTS 버전 별도 관리 |
| **SaaS 구독 저항 (Unreal 스튜디오)** | 중간 | 중간 | 중간 | Free 티어 제공, 디자인 파트너 무료 베타, 연간 결제 할인 |
| **Epic Games Marketplace 정책 변경 (Fab)** | 중간 | 중간 | 중간 | 직접 배포 채널 병행 유지, Atlassian Marketplace 보완 채널 |
| **크래시 직전 링버퍼 flush 실패** | 중간 | 높음 | 중간 | 비동기 flush 구현, 크래시 핸들러에서 최선 노력(best-effort) flush, 테스트 커버리지 확보 |
| **PIE 크래시 시 콜백 미실행으로 데이터 손실** | 높음 | 중간 | 중간 | 주기적 플러시 간격 5초 이하 유지 + 영상 세그먼트 5초 단위 분할로 최대 손실 범위 최소화 |
| **크래시 핸들러 내 데드락** | 중간 | 높음 | 높음 | async-signal-safe 함수만 사용 (`write()`, `FlushViewOfFile()`), atomic 플래그 기반 설계. `new`/`mutex`/`UE_LOG` 핸들러 내 사용 금지 코드 리뷰 필수 |
| **UE5 버전 업데이트 시 크래시 콜백 API 변경** | 중간 | 중간 | 중간 | `FCoreDelegates` 안정 API만 사용, 불안정 내부 API 의존 최소화, 버전별 조건부 분기(`#if ENGINE_MINOR_VERSION`) 활용 |
| **Jira 첨부 업로드 제한 (UE_LOG 파일 크기)** | 중간 | 낮음 | 낮음 | 업로드 전 파일 크기 체크, 로그 압축(zip) 기본 ON, 자동 잘라내기 옵션 |
| **인지도 부재 (Unreal 커뮤니티)** | 높음 | 중간 | 중간 | Unreal Fest Seoul 발표, Epic MegaGrants 신청, Unreal 커뮤니티 마케팅 |
| **refresh token 유출 (로컬 파일)** | 낮음 | 높음 | 중간 | `Saved/Bug-OneTouch-unreal/auth.enc` AES-256 암호화 + 플랫폼 파일 권한 제한 + 로그 레드랙션 + 키 회전 정책 |
| **멀티테넌시 데이터 혼선** | 낮음 | 중간 | 낮음 | 토큰이 로컬에 저장되므로 서버 측 혼선 리스크 최소. Auth Broker에서 user_id 소유권 검증으로 추가 보호. |

### 13.2 BetaHub 리스크 심층 분석

BetaHub는 다음 이유로 Bug-OneTouch-unreal Unreal 버전의 가장 심각한 위협이다:

| BetaHub 강점 | Bug-OneTouch-unreal 대응 |
|------------|--------------|
| Unity + Unreal 동시 지원 | Unity→Unreal 크로스셀 이점 상쇄 → 단독 기능이 아닌 "워크플로우 통합" 차별화 |
| F12 핫키 + 60초 영상 | 동일 기능 → Unreal 특화 UE_LOG 카테고리/World 상태 수집으로 차별화 |
| 무료 플랜 | 로컬-퍼스트 보안 + Jira Cloud 완성도 + 기술 지원 품질로 유료 전환 |
| 클라우드 업로드 기본 | 로컬-퍼스트 역설적 강점: 보안 민감 스튜디오에서 BetaHub 채택 불가 시나리오 발생 |

**전략적 판단**: BetaHub를 경쟁 상대로 두되, 로컬-퍼스트 + Jira Cloud 즉시 주입 완성도 + Unreal 상태 스냅샷 깊이에서 차별화. 장기적으로 BetaHub와의 협업/통합 가능성도 검토.

### 13.3 규제/법적 준수 고려사항

| 항목 | 설명 | MVP 대응 |
|------|------|---------|
| **개인정보 보호 (PIPA)** | 영상/스크린샷에 개인정보가 포함될 수 있음 | 로컬-퍼스트 아키텍처로 외부 전송 최소화. Jira 첨부 시 사용자 동의 기반. 로그 마스킹 기본 적용 |
| **영상 수집 고지** | QA 외 플레이어가 사용할 경우 영상 녹화에 대한 고지 필요 | MVP는 내부 개발/QA 도구로 한정. 외부 배포 시 녹화 고지 UI 추가 |
| **Unreal Engine 라이선스** | 매출 $1M 초과 시 5% 로열티 발생 (2024년 Epic 정책) | 플러그인은 UE 런타임을 배포하지 않으므로 직접 해당 없음. 단, 고객 게임의 UE 라이선스 준수는 고객 책임. Fab 등재 시 Epic 수수료 12% 별도. |
| **Jira OAuth 데이터 처리** | Atlassian 3rd party app 정책 준수 | OAuth scope 최소 권한 원칙. 토큰만 서버 저장, 사용자 데이터 미저장 |
| **GDPR (글로벌 확장 시)** | EU 사용자 대상 시 데이터 처리 동의 필요 | Phase C(글로벌 확장) 시 DPA 문서 및 데이터 처리 동의 UI 추가 |

### 13.4 리스크 우선순위 요약

```
[즉시 대응 필요 — RED]
1. BetaHub 위협 → 로컬-퍼스트 포지셔닝 강화, "버그 처리 시간 단축 시스템"으로 재포지셔닝
2. Unreal TAM 한계 → Unity+Unreal 통합 기본 시나리오 채택 (본 PRD 방침)
3. C++/.uplugin 기술 난이도 → Unreal 전문 인력 확보 계획 수립 (착수 전)

[중기 모니터링 — YELLOW]
4. UE 버전 호환성 → CI 버전 매트릭스 구축
5. 링버퍼 성능 영향 → 프로파일링 계획 수립
6. 인지도 부재 → Unreal Fest Seoul / Epic MegaGrants 신청
7. 크래시 핸들러 내 데드락 (심각도 높음) → async-signal-safe 함수만 사용, 코드 리뷰 필수
8. PIE 크래시 시 콜백 미실행 (발생확률 높음) → 주기적 플러시 5초 이하 유지, 영상 세그먼트 5초 단위 분할

[장기 관리 — GREEN]
9. Fab Marketplace 정책 변경 → 직접 배포 채널 병행
10. 보안 우려 → 문서화 및 감사 인증 (지속)
11. UE5 버전 업데이트 시 크래시 콜백 API 변경 → FCoreDelegates 안정 API만 사용, 버전별 조건부 분기 활용
```

---

## 14. 출시 이후 확장 로드맵

### Phase A — Unity 우선 출시 (현재, 0~12개월)

- Unity PRD v2.1 기준 출시
- 한국 인디/중소 Unity 레퍼런스 10~20개사 확보
- Unreal 디자인 파트너 3~5개사 발굴 및 기술 PoC

### Phase B — Unreal MVP 출시 (12~24개월)

- Unreal Windows MVP (UE 5.4/5.5 지원)
- Fab Marketplace 등재
- Unreal Fest Seoul 발표
- 5~10개 디자인 파트너 → 레퍼런스로 전환
- 크래시 링버퍼 연동, UE_LOG 카테고리 고도화
- Atlassian Marketplace 통합 등재 (Unity+Unreal)

### Phase C — 통합 플랫폼화 (24개월+)

- Unity+Unreal 통합 라이선스 번들
- 콘솔 플랫폼 (PS5, Xbox) 영상 캡처 Phase C 목표
- Visual Logger / Unreal Insights 데이터 통합
- AI 요약/자동 필드 채움
- 글로벌 확장 (일본 → 북미/유럽 → 동남아)
- Self-hosted 브로커 (대형사/보안 요구 대응)

---

## 15. Acceptance Criteria

### 15.1 핵심 기능 (Core)

1. 사용자가 Unreal Editor/PC dev build에서 핫키를 누르면 **5초 이내**(체감 기준) 오버레이가 뜨고, 번들이 생성된다.
2. 번들에는 최소 `manifest.json + screenshot + ue_log_filtered.zip + state.json`이 포함된다.
3. 영상은 기본으로 로컬에 저장되고, 제출 시 옵션으로 첨부 가능하다.
4. Jira Cloud 연결을 완료하면 "Submit to Jira" 한 번으로 이슈가 생성되고, 링크가 반환된다.
5. Jira 토큰이 만료/무효가 되면 "Re-auth" 플로우로 복구 가능하다.
6. 모든 실패 케이스에서 번들은 로컬에 남아 데이터 유실이 없다.
7. OAuth 토큰(refresh token 포함)은 로컬 머신의 `Saved/Bug-OneTouch-unreal/auth.enc`에 AES-256 암호화 저장되며, 평문 접근이 불가능하다. Auth Broker는 사용자 데이터를 보관하지 않는다.
8. UI는 영어로 작성되며, 한국어 로컬라이즈를 별도 레이어로 지원한다.

### 15.2 Unreal 특화 기능 (Unreal-specific)

9. `state.json`에는 `world_name`, `level_name`, `game_mode`, `actor_count`, `ue_version`, `fps` 필드가 포함된다.
10. UE_LOG 링버퍼는 최소 5분 분량의 로그를 카테고리별로 수집하며, Warning 이상 레벨이 기본 필터링된다.
11. 60초 영상 링버퍼 저장 완료까지 **3초 이내**에 처리된다 (Windows, UE 5.4 기준).
12. 캡처 중 FPS 드랍이 기준값 대비 **5% 이하**로 유지된다 (60fps 기준 57fps 이상 유지).
13. UE 5.4 및 5.5에서 플러그인 로드/언로드가 에러 없이 완료된다.
14. `.uplugin` 방식으로 프로젝트에 추가 후 빌드 시간이 기본 빌드 대비 **30초 이상 증가하지 않는다**.

### 15.3 오프라인/재시도 (Offline Queue)

15. 제출 실패 시 번들은 `status=pending` 상태로 유지되며, "Retry submit" 버튼으로 재제출이 가능하다.
16. 재제출 성공 시 `status=submitted`로 기록되고, 생성된 Jira 이슈 URL이 번들 메타데이터에 저장된다.

### 15.4 업로드 제한 대응

17. 제출 전 Jira 첨부 업로드 제한(meta API)을 확인하고, 제한 초과 시 영상 첨부를 자동 제외하며 사용자에게 알린다.
18. 업로드 실패 시 에러 유형(권한 부족/용량 제한/네트워크)을 구분하여 사용자에게 안내 메시지를 표시한다.

### 15.5 보안 (Security)

19. refresh token 회전(rotating) 시 새 refresh token의 `auth.enc` 교체 저장과 이전 token 폐기가 원자적(atomic)으로 처리되며, 동시 refresh 요청은 연결 단위 락으로 직렬화된다.
20. Auth Broker의 모든 API 호출은 세션 토큰(JWT) 기반 클라이언트 인증을 거치며, user_id 소유권이 서버 측에서 검증된다. Auth Broker는 사용자 데이터를 저장하지 않으며 토큰 교환 중계만 수행한다.
21. OAuth 토큰은 로컬 `Saved/Bug-OneTouch-unreal/auth.enc`에 AES-256 암호화 저장되며, 파일에 대한 평문 접근이 불가능하도록 플랫폼 파일 권한이 설정된다.

### 15.6 번들 무결성

22. `manifest.json`은 생성 시 필수 필드(`report_id`, `created_at`, `engine`, `engine_version`, `world_name`, `level_name`, `game_mode`, `app_version`, `platform`, `title`, `description`, `severity`, `artifacts`, `integrations`)가 모두 존재하며, 필수 필드 누락 시 번들 생성을 실패 처리하고 사용자에게 알린다.
23. `artifacts` 배열의 각 항목은 `sha256` 해시를 포함하며, 제출 시 파일 무결성 검증에 사용된다.

### 15.7 크래시 자동 감지 및 복구 (Crash Recovery)

24. 플레이 모드 진행 중 주기적 플러시가 설정된 간격(기본 로그 5초, 상태 10초, 영상 30초)대로 `crash_bundles/` 폴더에 임시 데이터를 기록한다.
25. Ensure/Check 매크로 트리거 시 `crash_{timestamp}.bot-unreal` 번들이 자동 생성되고, `manifest.json`의 `crash_type`이 `"ensure_check"`, `registered`가 `false`로 설정된다.
26. Standalone 크래시 후 에디터에서 해당 번들 폴더를 임포트하면 크래시 번들 목록 UI에 표시된다.
27. `abnormal_exit.flag`가 잔존한 상태로 에디터를 재시작하면 비정상 종료로 정확히 감지되고, 해당 시간대의 미완성 번들이 복구 대상으로 마킹된다.
28. 크래시 번들 목록이 `manifest.json`의 `created_at` 기준 최신 순(내림차순)으로 표시된다.
29. [Jira에 등록] 클릭 시 제목(`[Crash] {crash_type}: {crash_message 첫 50자}`), 설명(스택 트레이스 + 시스템 정보), 첨부파일(영상/스크린샷/로그)이 자동으로 채워진 Jira 등록 폼이 열린다.
30. Jira 등록 완료 후 해당 번들의 `manifest.json`에서 `registered` 필드가 `true`로, `jira_issue_key`에 발급된 이슈 키가 기록된다.
31. 크래시 번들이 최대 보관 수(10개)를 초과하면 가장 오래된 번들부터 자동 삭제(FIFO)된다.
32. `data_integrity` 필드가 각 파일의 실제 보존 상태(`complete` / `partial` / `missing`)를 정확히 반영하며, 복구 UI에서 파일별 상태 배지로 표시된다.
33. 크래시 핸들러(`OnHandleSystemError` 콜백) 내에서 `new`/`malloc`/`std::mutex`/`UE_LOG` 호출이 발생하지 않으며, 데드락 없이 완료된다.

---

## 16. 시장 분석 출처

### 한국 게임 시장 통계

1. **KOCCA (한국콘텐츠진흥원)** — 2024 대한민국 게임백서: 한국 게임산업 매출 22조 9,642억 원 (2023), 세계 4위
   - URL: https://www.kocca.kr/kocca/bbs/view/B0000146/2008086.do
   - 발행일: 2024년
2. **통계청** — 게임 제작/배급업 사업체 수: 약 1,287~1,334개
   - URL: https://kosis.kr (경제총조사 > 산업별 사업체수)
   - 참조 연도: 2022~2023년
3. **KOCCA** — 2024년 게임백서 보도자료
   - URL: https://welcon.kocca.kr/en/support/content-report/381

### Unreal Engine 시장 데이터

4. **VGI The Big Game Engines Report 2025** — Steam 2024 신작 기준 Unity 51%, Unreal 28%, UE5 비중 72%
   - URL: https://vginsights.com/assets/reports/The_Big_Game_Engines_Report_of_2025.pdf
   - 발행일: 2025년
5. **Epic Games** — The First Descendant(UE5), Steam 공개 정보
   - URL: https://store.steampowered.com/app/2074920/The_First_Descendant/
6. **KRAFTON** — inZOI (UE5), Chrono Odyssey (UE5)
   - URL: https://krafton.com/en/games/inzoi/
7. **PlayStation Blog** — The Seven Deadly Sins: Origin (UE5, PS5 2026-01-28)
   - URL: https://blog.playstation.com/2025/09/24/the-seven-deadly-sins-origin-launches-january-28-on-ps5/
8. **Unreal Engine** — 라이선스 정보 및 Fab Marketplace 수수료
   - URL: https://www.unrealengine.com/en-US/license
   - URL: https://dev.epicgames.com/documentation/en-us/unreal-engine/plugins-in-unreal-engine

### 경쟁사 분석

9. **BetaHub** — Unity/Unreal 통합 버그 리포팅, F12 핫키, 60초 영상
   - URL: https://betahub.io/features/game_plugins/
10. **Oplix** — Unreal 통합 티켓 자동 생성
    - URL: https://www.oplix.io/integrations/unreal
11. **BugSplat** — Unreal Engine 크래시 분석
    - URL: https://www.bugsplat.com/for/unrealengine
12. **Sentry Unreal** — UE 4.27+ SDK
    - URL: https://docs.sentry.io/platforms/unreal/install
13. **Backtrace Unreal** — UE Crash Reporter 연동
    - URL: https://docs.saucelabs.com/error-reporting/platform-integrations/unreal/setup/

### Unreal Engine 기술 문서

14. **Unreal Replay System** — DemoNetDriver
    - URL: https://dev.epicgames.com/documentation/en-us/unreal-engine/using-the-replay-system-in-unreal-engine
15. **Unreal Logging (UE_LOG)** — 카테고리 체계
    - URL: https://dev.epicgames.com/documentation/en-us/unreal-engine/logging-in-unreal-engine
16. **Unreal Crash Reporting** — Crash Reporter 커스터마이징
    - URL: https://dev.epicgames.com/documentation/en-us/unreal-engine/crash-reporting-in-unreal-engine
17. **Unreal Insights** — 성능 분석 도구
    - URL: https://dev.epicgames.com/documentation/en-us/unreal-engine/unreal-insights-in-unreal-engine
18. **Visual Logger** — 시각화 디버깅
    - URL: https://dev.epicgames.com/documentation/en-us/unreal-engine/visual-logger-in-unreal-engine

### 분석 메타 출처

19. **Codex xhigh 분석** — `/tmp/codex_unreal_1.md` (2026-02-27)
20. **Codex spark 분석** — `/tmp/codex_unreal_2.md` (2026-02-27)

---

## Appendix

### A. Unreal 독립 MVP 시나리오 (부록)

> 본 PRD의 기본 시나리오는 "Unity 출시 이후 Unreal 확장"이다. 그러나 팀 상황(Unreal 전문 인력 선확보, 파트너사 요청 등)에 따라 Unreal 독립 MVP가 우선될 수 있다. 이 경우 아래 조건을 충족해야 한다.

**Unreal 독립 MVP 착수 조건**:

1. Unreal C++ 전문 개발자 2명 이상 확보
2. 디자인 파트너 3개사 이상 사전 계약
3. MVP 예산: 인건비 기준 10~14주 × 3FTE (C++ 2명 + QA 1명) = 약 6,000~8,400만 원 (시장 단가 기준)
4. UE 5.4 기준 기술 PoC 완료 (영상 링버퍼, UE_LOG 수집)

**독립 출시 타임라인**:

| 단계 | 기간 | 산출물 |
|------|------|--------|
| PoC | 2~3주 | 영상 링버퍼 성능 검증, UE_LOG 수집 구현 |
| MVP 개발 | 10~14주 | 전체 기능 구현 (Windows, UE 5.4) |
| 안정화 | 8~12주 | 성능 최적화, 버전 매트릭스, 장애 대응 |
| **총 기간** | **5~7개월** | 알파 → 베타 → GA |

**성공 기준 (독립 시나리오)**:

- 6개월 내 Unreal Free 티어 활성 팀 5개사 이상
- 12개월 내 Pro 전환 10개사 이상, MRR $2K+
- 실패 판단 기준: 12개월 내 MRR $1K 미달 시 Unity 통합 전략으로 피벗

---

### B. 구현 참고 메모 (비요구사항)

- DemoNetDriver 기반 Replay System은 60초 영상 클립 생성에 부적합. `USceneCaptureComponent2D` + `UTextureRenderTarget2D` → `FRenderTarget::ReadPixels()` 또는 Desktop Duplication API 우선 채택.
- 스크린샷 캡처: `FScreenshotRequest::RequestScreenshot()` 트리거 + `UGameViewportClient::OnScreenshotCaptured()` 델리게이트 수신 (UE5.4+ 기준; 버전별 시그니처 검증 필요).
- UE_LOG 링버퍼: `FOutputDeviceRedirector::Get()->AddOutputDevice(...)` 로 커스텀 `FOutputDevice` 서브클래스 등록. 게임 스레드와 별도 스레드에서 처리 권장.
- World/Actor 상태: `GEngine->GetWorldContexts()` + `UWorld::GetName()`, `UGameplayStatics::GetAllActorsOfClass()` 조합 (UE5.4+). `GetCurrentLevel()`은 에디터 컨텍스트 확인 후 사용.
- Jira Cloud 3LO는 code grant 기반이며 refresh token은 회전(rotating) 방식임. 토큰은 `Saved/Bug-OneTouch-unreal/auth.enc` AES-256 암호화 로컬 저장.
- Auth Broker는 토큰 교환 중계만 수행하며 사용자 데이터를 보관하지 않음. Supabase Vault 의존 없음.
- Fab Marketplace 등재 시 Epic 검수 기간 최소 2~4주 예상. 검수 요건 사전 검토 필수.
- UE 버전별 API 변경(특히 5.3→5.4→5.5) 대응을 위해 버전별 조건부 컴파일(`#if ENGINE_MAJOR_VERSION == 5 && ENGINE_MINOR_VERSION >= 4`) 활용 권장.

---

### C. 시장 분석 메타데이터

| 항목 | 내용 |
|------|------|
| 분석 모델 수 | 5개 (Sonnet 4.6, Haiku 4.5, Opus 4.6, Codex xhigh, Codex spark) |
| 가중 평균 사업 가능성 점수 | 7.6/10 (5개 모델 종합) |
| Codex xhigh 단독 평가 | 8.0/10 |
| Codex spark 단독 평가 | 7.5/10 |
| 최종 Go/No-Go 판단 | Conditional GO (Unity+Unreal 통합 기본, Unreal 독립 부록) |
| Unreal 단독 성공률 | 20~30% |
| Unity+Unreal 통합 성공률 | 40~45% |
| 분석 기준일 | 2026년 2월 27일 |
| 미합의 사항 | 출시 순서 (Opus: Unity 먼저, Codex: Unreal 독립 가능) |

---

### D. Unity PRD v2.1 대비 주요 차이점 요약

| 항목 | Unity PRD v2.1 | Unreal PRD v1.0 |
|------|--------------|---------------|
| 대상 엔진 | Unity (C#, UPM) | Unreal Engine 5 (C++/.uplugin) |
| 영상 캡처 | 링버퍼 + 플랫폼별 인코더 | SceneCapture2D / Desktop Duplication API (DemoNetDriver 제외) |
| 로그 수집 | Unity Console + Player.log | UE_LOG 카테고리별 + Saved/Logs/ |
| 상태 스냅샷 | Scene 이름, Time, FPS | World, Level, Game Mode, Actor Count, UE 버전 |
| 배포 채널 | Unity Asset Store (UPM) | Fab Marketplace (.uplugin) |
| 수수료 구조 | Unity Asset Store 70/30 | Fab 88/12 (개발자 유리) |
| 가격 (Pro) | $9/seat/월 | $24~29/seat/월 (Unreal 기술 프리미엄) |
| 경쟁 환경 | 사실상 직접 경쟁자 부재 (블루오션) | BetaHub 등 실질 경쟁자 존재 |
| 기술 난이도 | 기준 (1x) | 2~3배 (C++/엔진 버전 대응) |
| MVP 예상 기간 | 10~14주 (2 FTE) | 10~14주 (3 FTE: C++ 2명 + QA 1명) |
| Auth Broker | Supabase (신규 구축) | Supabase (Unity와 공유 재사용) |
| 한국 SMB 수 | Unity 630~780개사 | Unreal 200~360개사 |
