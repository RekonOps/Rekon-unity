# Bug-OneTouch 프로젝트 — 대화 맥락 정리

> 작성일: 2026-03-01
> 목적: 이전 세션의 대화 흐름 및 주요 결정 사항을 새 세션에서도 맥락 손실 없이 이어갈 수 있도록 정리

---

## 1단계: 프로젝트 파악 + 시장 분석 + PRD 정비

### 프로젝트 시작점

사용자가 `docs/` 안의 PRD 파일을 읽고 프로젝트 전반을 파악한 뒤, "한국 시장 SMB 타겟" 가능성 분석을 요청하면서 대화가 시작되었다.

### Bug-OneTouch 프로젝트란?

Unity / Unreal 게임 엔진용 **로컬-퍼스트 버그 리포팅 플러그인**이다.

핵심 기능:
- 60초 리플레이 버퍼 + 스크린샷 + 로그 + 상태 스냅샷을 **핫키 하나로 캡처**
- Jira Cloud에 직접 이슈 등록
- 웹 포탈 없음, **로컬-퍼스트 아키텍처**
- **Auth Broker만** Supabase로 운영 (Jira OAuth 토큰 교환 중계만 담당, 버그 데이터는 서버에 미저장)

### 시장 분석 방식

5개 AI 모델을 병렬로 활용하여 독립 조사를 수행하고, Opus 4.6이 결과를 종합했다.

| 모델 | 역할 |
|------|------|
| Claude Opus 4.6 | 팀장, 결과 종합 |
| Claude Sonnet 4.6 | 실무 분석 |
| Claude Haiku 4.5 | 빠른 분석 |
| Codex gpt-5.3-codex (xhigh) | 심층 연구 |
| Codex gpt-5.3-codex-spark (xhigh) | 심층 연구 |

- 각 모델이 독립적으로 한국 게임 시장 및 SMB 타겟 분석을 수행
- Opus 4.6이 결과를 종합하여 최종 리포트 작성
- 최종 리포트 위치: `/tmp/bug-onetouch-analysis/final_report.md`
- 종합 점수: **7.0/10 — Conditional GO**

### Linear 제거 결정

게임 업계에서 Linear를 실질적으로 사용하지 않는다는 조사 결과에 따라, PRD에서 Linear 관련 내용을 전부 제거하고 **Jira Cloud만 지원**하는 방향으로 정비했다.

---

## 2단계: Unity PRD v2 작성 + QA

### PRD v2 작성

시장 분석 결과를 반영하여 새로운 PRD를 작성했다.

- 파일 위치: `/Users/mac/IdeaProjects/Bug-OneTouch/docs/Bug-OneTouch_PRD_v2.md`
- 추가된 내용: TAM/SAM/SOM, 경쟁 분석, 가격 전략, GTM 전략, 리스크 매트릭스

가격 전략 (당시 초안):
- Free / Pro $9 / Pro $15 / Enterprise

### Codex QA 리뷰 1차 — 6.4/10 (거부)

Critical 2건이 발견되어 거부되었다.

- **C-1: TAM/SAM/SOM 계산 오류**
  - 계산식: 500 × 8 × 8,000 × 12 = 3.84억
  - PRD에는 "38억"으로 잘못 기재되어 있었음 (10배 오류)

- **C-2: Auth Broker 보안 취약점**
  - `POST /token/jira` 엔드포인트에 서버 검증 없이 raw `tenant_id` / `user_id` 허용
  - 검증 로직 부재로 임의의 요청 처리 가능

### Opus 수정 후 재리뷰 — 8.7/10 (조건부 승인, v2.1)

두 Critical 항목 모두 수정 후 재리뷰에서 조건부 승인을 받았다.

### 한글화 작업

- 사용자가 "TL;DR"이 무엇인지 모르겠다고 하여 → "핵심 요약"으로 변경
- 나머지 영문 섹션명도 한글화 요청이 있었으며 일부 진행 후 중단

---

## 3단계: Unreal PRD 작성

### 배경

Unity PRD v2.1과 동일한 방향성으로 Unreal Engine 플러그인 PRD를 작성해달라는 요청이 있었다.

- 5개 모델 병렬 연구 수행 (Opus, Sonnet, Haiku, Codex xhigh, Codex spark)
- Opus 4.6 vs Codex 5.3 spark 간 토론 진행 ("우리의 제품이 성공할 수 있는 방향성")

### 5개 모델 연구 결과 핵심

**BetaHub 발견**: 무료 + Unity/Unreal 동시 지원 + F12 핫키 + 60초 영상 → 블루오션이 아님

| 분석 항목 | 결과 |
|-----------|------|
| Unreal 단독 종합 점수 | 6.3 ~ 8.0/10 (모델별 차이) |
| Unity+Unreal 통합 점수 | 7.5 ~ 8.5/10 |
| 한국 Unreal SMB 대상 기업 수 | 200 ~ 360개사 |
| 한국 Unity SMB 대상 기업 수 | 630 ~ 780개사 (Unreal의 약 2배) |
| Unreal 기술 난이도 | Unity 대비 2 ~ 3배 (C++ 개발) |

### Opus vs Codex 토론 결과

**합의 사항:**
- 포지셔닝: "캡처 툴"이 아닌 **"버그 처리 시간 단축 시스템"**
- BetaHub는 실질적 위협으로 인정
- 기술 전략: 하이브리드 (Replay System + 커스텀)
- GTM 전략: 한국 → 글로벌 순서
- 성공률 추정: Unreal 단독 20~30%, Unity+Unreal 통합 40~45%

**미합의 사항:**
- 출시 순서 — Opus: Unity 먼저(비협상) / Codex: Unreal MVP 먼저 가능

### Unreal PRD v1.0 작성

- 총 1,082줄, AC 22개

**Codex QA 결과: 7.4/10, Critical 3건 발견**

| 항목 | 내용 |
|------|------|
| C-1 | UE API 명세 오류 |
| C-2 | 모듈 스펙 불일치 |
| C-3 | 토큰 모델 혼재 |

총 16곳 수정 후 **v1.1로 업그레이드** 완료.

---

## 4단계: 엔진별 동작 모드 구분 + 크래시 복구

### 플레이 모드 / 에디터 모드 구분

게임 엔진 플러그인 특성상 두 가지 모드를 명확히 구분해야 한다는 논의가 있었다.

| 모드 | 활성 기능 |
|------|-----------|
| 플레이 모드 (PIE / Standalone) | 캡처 전체 활성 — 영상, 스크린샷, 로그, 상태, 핫키 |
| 에디터 모드 | 설정/관리만 활성 — Jira 연결, 캡처 설정, 번들 관리 |

양쪽 PRD에 **6.5절**로 반영 완료.

### 크래시 시 자동 저장 — 기술 조사

#### Unreal 조사 결과 (`/tmp/unreal_crash_research.md`)

- `FCoreDelegates::OnHandleSystemError`, `OnShutdownAfterError` 콜백 활용 가능
- **메모리 매핑 파일(mmap) + 5~10초 주기 백그라운드 플러시**가 현실적 최선
- 크래시 보존율 추정:
  - Standalone 크래시: ~95%
  - PIE 크래시: ~70%
  - OS 강제 종료: 마지막 플러시 시점까지만 보존
- 기존 도구(BugSplat, Sentry)는 크래시 후 스택 트레이스만 수집 → **크래시 직전 영상/로그 보존은 명확한 차별화 포인트**

#### Unity 조사 결과 (`/tmp/unity_crash_research.md`)

- `Application.logMessageReceived` (Managed 예외), `AppDomain.UnhandledException` 등 활용
- **Native 크래시 시 C# 코드 실행 불가** → 주기적 플러시가 유일하게 신뢰할 수 있는 전략
- `MemoryMappedFile` — Unity 2022.3.22f1 이전 버전에서 macOS/Linux 에디터 크래시 버그 존재
- IL2CPP + ScriptCallOptimization 환경에서 `UnhandledException` 미호출 공식 버그

### 크래시 복구 플로우 설계 (`/tmp/crash_recovery_flow.md`)

사용자의 아이디어를 기반으로 아래 흐름을 설계했다.

```
플레이 모드 중 — 3중 레이어 보존:

  레이어 1 (Must): 주기적 플러시
    - 로그:   5초 주기
    - 상태:  10초 주기
    - 영상:  30초 주기

  레이어 2 (Should): 크래시 즉시 번들 생성
    - OnHandleSystemError / Application.logMessageReceived 콜백 활용

  레이어 3 (Must): abnormal_exit.flag
    - 비정상 종료 감지 플래그 파일

              ↓

에디터 재실행 후:

  crash_bundles/ 폴더 자동 스캔
              ↓
  미등록 크래시 번들 알림 팝업
              ↓
  "yyyy-mm-dd hh:mm:ss — 크래시 리포트" 목록 표시
              ↓
  [Jira에 등록] 버튼 → 기존 버그 리포트 UI 재활용
```

#### 크래시 번들 구조

```
crash_{timestamp}.bot-unity/
├── manifest.json          # 크래시 메타데이터 (8개 필드 + jira_issue_key + registered_at)
├── crash_log.txt          # 스택 트레이스 + 직전 500줄 로그
├── last_screenshot.png    # 크래시 직전 마지막 프레임
├── replay_buffer.mp4      # 보존된 영상 (최대 60초)
├── state_snapshot.json    # 마지막 상태 스냅샷
└── system_info.json       # OS / GPU / 엔진 버전 / 프로젝트 정보
```

### 양쪽 PRD 반영 항목

크래시 복구 관련 내용을 Unity PRD(v2.2)와 Unreal PRD(v1.1) 양쪽에 동일하게 반영했다.

| 섹션 | 내용 |
|------|------|
| 6.6 | 크래시 자동 감지 및 복구 플로우 |
| 8.1.5 | 크래시 자동 캡처 (MUST) |
| 12 | MVP 범위에 크래시 복구 분류 |
| 13 | 리스크 매트릭스에 크래시 리스크 추가 |
| 15 | 크래시 관련 AC 추가 (Unity 8개, Unreal 10개) |

### 최종 검증 + 불일치 수정

양쪽 PRD의 크래시 복구 반영 상태를 교차 검증하여 불일치 4건씩 발견하고 수정했다.

수정 내역:
1. `manifest.json`에 `jira_issue_key` / `registered_at` 필드 누락 → 추가
2. Jira 첨부 파일 형식 불일치 → 통일
3. 레이어별 Must / Should 레이블 미표시 → 추가
4. 리스크 요약 누락 / 표현 모호 → 수정

---

## 현재 산출물 목록

| 파일 경로 | 설명 | 버전 |
|-----------|------|------|
| `docs/Bug-OneTouch_PRD_v2.md` | Unity PRD | v2.2 |
| `docs/Bug-OneTouch_PRD_Unreal_v1.md` | Unreal PRD | v1.1 |
| `/tmp/bug-onetouch-analysis/final_report.md` | Unity 시장 분석 종합 리포트 | - |
| `/tmp/codex_unreal_1.md` | Codex xhigh Unreal 연구 결과 | - |
| `/tmp/codex_unreal_2.md` | Codex spark Unreal 연구 결과 | - |
| `/tmp/unreal_crash_research.md` | Unreal 크래시 기술 조사 | - |
| `/tmp/unity_crash_research.md` | Unity 크래시 기술 조사 | - |
| `/tmp/crash_recovery_flow.md` | 크래시 복구 플로우 설계 | - |

---

## 주요 기술 결정 사항

| 항목 | 결정 내용 |
|------|-----------|
| 아키텍처 | 로컬-퍼스트 (웹 포탈 없음, Auth Broker만 서버) |
| 인증 방식 | Jira Cloud OAuth 3LO, 사용자 단위 1:1, 토큰은 로컬 AES-256 암호화 |
| 이슈 트래커 | Jira Cloud만 지원 (Linear 제거) |
| 출시 전략 | Unity 먼저 → Unreal 확장 (토론 결과 합의) |
| 포지셔닝 | "캡처 툴"이 아닌 "버그 처리 시간 단축 시스템" |
| 주요 경쟁자 | BetaHub (무료, Unity+Unreal 동시 지원, 실질적 위협) |
| 가격 전략 | Free / Pro $24~29/seat / Enterprise 연간 계약 |
| 크래시 복구 | 3중 레이어 보존 + 에디터 재실행 후 복구 UI + Jira 등록 연동 |

---

## 작업 방식 및 에이전트 운영 규칙

| 역할 | 모델 | 담당 업무 |
|------|------|-----------|
| 팀장 / 관리자 | Claude Opus 4.6 | 계획 수립, 작업 분배, 결과 취합 |
| 실무 담당 | Claude Sonnet 4.6 | 코드 작성, 문서 작성 |
| QA / 연구 | Codex gpt-5.3-codex / spark | 코드 리뷰, 기술 연구, 토론 |

- 모든 작업은 **subagent를 통해 수행** (CLAUDE.md 규칙 준수)
- Codex CLI 실행: `codex exec` 명령어로 비대화형 연구 실행
- Codex 설정 파일 위치: `~/.codex/config.toml`
- Task tool 호출 시 반드시 `model: "sonnet"` 파라미터 지정
