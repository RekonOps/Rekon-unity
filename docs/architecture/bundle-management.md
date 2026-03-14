# 번들 관리 아키텍처 (M2)

## 개요

BugBeacon Unity 플러그인의 M2 마일스톤은 캡처 파이프라인(M1)에서 생성된 아티팩트를
로컬 디스크에 번들 단위로 패키징하고 관리하는 시스템을 구현합니다.

---

## 디렉토리 구조

```
{Application.persistentDataPath}/
└── BugBeacon/
    └── bundles/
        └── {bundle-id}/              # GUID 기반 고유 번들 디렉토리
            ├── manifest.json          # 번들 메타데이터 (상태, 아티팩트 목록 등)
            ├── screenshot.png         # 스크린샷 (PNG)
            ├── logs.zip               # 로그 (ZIP 압축)
            ├── state.json             # 상태 스냅샷 (JSON)
            └── video/                 # 영상 세그먼트 (옵션)
                └── ...
```

---

## manifest.json 스키마

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "created_at": "2024-01-15T10:30:00.000Z",
  "plugin_version": "0.1.0",
  "unity_version": "2022.3.22f1",
  "title": "",
  "description": "",
  "artifacts": [
    {
      "type": "Screenshot",
      "file_name": "screenshot.png",
      "size_bytes": 102400,
      "sha256_hash": "abc123..."
    },
    {
      "type": "Log",
      "file_name": "logs.zip",
      "size_bytes": 5120,
      "sha256_hash": "def456..."
    },
    {
      "type": "State",
      "file_name": "state.json",
      "size_bytes": 2048,
      "sha256_hash": "ghi789..."
    },
    {
      "type": "Video",
      "file_name": "video",
      "size_bytes": 10485760,
      "sha256_hash": ""
    }
  ],
  "total_size_bytes": 10594816,
  "state": "pending",
  "jira_issue_key": null,
  "registered_at": null
}
```

### 필드 설명

| 필드              | 타입     | 설명                                                |
|-------------------|----------|-----------------------------------------------------|
| `id`              | string   | GUID v4 기반 고유 식별자                            |
| `created_at`      | string   | ISO 8601 UTC 타임스탬프 (예: `2024-01-15T10:30:00Z`) |
| `plugin_version`  | string   | 플러그인 버전 (package.json의 version 필드)         |
| `unity_version`   | string   | Unity 에디터/런타임 버전                            |
| `title`           | string   | 버그 제목 (사용자가 나중에 입력)                    |
| `description`     | string   | 버그 설명 (사용자가 나중에 입력)                    |
| `artifacts`       | array    | 포함된 아티팩트 목록                                |
| `total_size_bytes`| long     | 모든 아티팩트의 총 크기 (바이트)                    |
| `state`           | string   | 번들 상태 (아래 상태 머신 참조)                     |
| `jira_issue_key`  | string?  | Jira 이슈 키 (예: `BUG-123`). 미등록 시 null       |
| `registered_at`   | string?  | Jira 등록 완료 시각. 미등록 시 null                 |

### BundleArtifact 필드

| 필드          | 타입   | 설명                                    |
|---------------|--------|-----------------------------------------|
| `type`        | string | 아티팩트 종류: `Screenshot`, `Log`, `State`, `Video` |
| `file_name`   | string | 번들 디렉토리 내 파일/폴더명            |
| `size_bytes`  | long   | 파일 크기 (바이트)                      |
| `sha256_hash` | string | SHA-256 해시 (hex string). 디렉토리는 빈 문자열 |

---

## 번들 상태 머신

```
         캡처 완료
            │
            ▼
        ┌─────────┐
        │ Created │   ← BundleWriter가 생성 직후 초기 상태
        └────┬────┘
             │ 사용자가 제출 승인
             ▼
        ┌─────────┐
        │ Pending │   ← 제출 대기 중 (로컬에 저장됨)
        └────┬────┘
             │ 제출 시작
             ▼
        ┌────────────┐
        │ Submitting │ ← Jira API 호출 진행 중
        └─────┬──────┘
         ┌────┴────┐
         │         │
         ▼         ▼
    ┌─────────┐ ┌────────┐
    │Submitted│ │ Failed │ ← 네트워크 오류 등
    └─────────┘ └───┬────┘
                    │ 재시도 (최대 3회)
                    └──────────► Pending
```

### 상태 전환 규칙

| 현재 상태    | 다음 상태    | 조건                              |
|-------------|-------------|-----------------------------------|
| Created     | Pending     | 사용자 제출 요청                   |
| Pending     | Submitting  | SubmissionQueue가 처리 시작        |
| Submitting  | Submitted   | Jira API 성공 응답                 |
| Submitting  | Failed      | 네트워크 오류 / API 오류           |
| Failed      | Pending     | 재시도 큐에 의해 재시도 (최대 3회) |
| Failed      | (삭제)      | 최대 재시도 횟수 초과              |

---

## 주요 컴포넌트

### Phase 2.2: 번들 생성

| 클래스               | 파일                               | 역할                                     |
|---------------------|------------------------------------|------------------------------------------|
| `BundleManifest`    | `Runtime/Bundle/BundleManifest.cs` | 번들 메타데이터 데이터 모델              |
| `BundleArtifact`    | `Runtime/Bundle/BundleManifest.cs` | 개별 아티팩트 메타데이터                 |
| `ManifestGenerator` | `Runtime/Bundle/ManifestGenerator.cs` | `CaptureResult` → `BundleManifest` 변환 |
| `SHA256HashUtility` | `Runtime/Bundle/SHA256HashUtility.cs` | 파일 SHA-256 해시 계산 유틸리티         |
| `BundleWriter`      | `Runtime/Bundle/BundleWriter.cs`   | 번들 디렉토리에 아티팩트 복사 및 저장   |

### Phase 2.3: 번들 저장소

| 클래스                  | 파일                                      | 역할                                   |
|------------------------|-------------------------------------------|----------------------------------------|
| `BundleRepository`     | `Runtime/Bundle/BundleRepository.cs`      | 디스크 스캔, 번들 목록 조회, 상태 변경 |
| `BundleRetentionPolicy`| `Runtime/Bundle/BundleRetentionPolicy.cs` | FIFO 기반 보관 정책 (200개/5GB)        |

### Phase 2.4: 재시도 큐

| 클래스             | 파일                               | 역할                                        |
|-------------------|------------------------------------|---------------------------------------------|
| `SubmissionQueue` | `Runtime/Bundle/SubmissionQueue.cs`| 실패 번들 자동 재시도 (지수 백오프)          |

---

## 보관 정책 (Retention Policy)

- **최대 번들 수**: 200개 (BugBeaconSettings.maxBundles)
- **최대 디스크 사용량**: 5,120MB / 5GB (BugBeaconSettings.maxDiskUsageMB)
- **삭제 전략**: FIFO (가장 오래된 번들부터 삭제)
- **삭제 조건**: 둘 중 하나라도 초과 시 삭제 진행

---

## 재시도 정책 (Retry Policy)

- **최대 재시도 횟수**: 3회
- **재시도 간격**: 지수 백오프
  - 1차 재시도: 5초 후
  - 2차 재시도: 15초 후
  - 3차 재시도: 45초 후
- **재시도 불가 조건**: 최대 횟수 초과 시 Failed 상태 유지 및 삭제 대상 지정

---

## 원자적 쓰기 (Atomic Write)

`manifest.json` 업데이트는 원자적 쓰기를 사용하여 손상(corruption)을 방지합니다:

1. 임시 파일(`.tmp`)에 새 내용 쓰기
2. 기존 `manifest.json`을 임시 파일로 교체 (`File.Move` with overwrite)

---

## 관련 설정 (BugBeaconSettings)

```csharp
[Header("Bundle")]
public int maxBundles = 200;        // 최대 번들 수
public int maxDiskUsageMB = 5120;   // 최대 디스크 사용량 (MB)
```
