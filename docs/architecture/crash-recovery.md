# M6: 크래시 복구 설계 문서

## 개요

Bug-OneTouch Unity 플러그인의 크래시 복구 시스템은 게임이 비정상 종료(크래시, Managed Exception, 강제 종료)
될 경우 직전까지의 로그·상태·영상을 보존하고, 다음 실행 시 Jira 이슈로 자동 제출할 수 있도록
지원합니다.

---

## 1. 3중 레이어 아키텍처

```
┌──────────────────────────────────────────────────────────────┐
│ 레이어 1: 주기적 플러시 (PeriodicFlushManager)               │
│   • 로그: 5초마다  → active/logs_flush.zip                   │
│   • 상태: 10초마다 → active/state_flush.json                 │
│   • 영상: 30초마다 → active/video_flush/                     │
├──────────────────────────────────────────────────────────────┤
│ 레이어 2: Managed Exception 감지 (ManagedExceptionHandler)   │
│   • Application.logMessageReceived 구독                      │
│   • LogType.Exception 수신 시 즉시 크래시 번들 생성          │
│   • 30초 쿨다운으로 중복 방지                                │
├──────────────────────────────────────────────────────────────┤
│ 레이어 3: 비정상 종료 감지 (AbnormalExitDetector)            │
│   • Play 시작: abnormal_exit.flag 생성                       │
│   • 정상 종료: abnormal_exit.flag 삭제                       │
│   • 다음 시작: flag 존재 → 크래시 번들 생성                  │
└──────────────────────────────────────────────────────────────┘
```

---

## 2. 디렉토리 구조

```
{persistentDataPath}/BugOneTouch/
├── crash_recovery/
│   ├── active/                     # 레이어 1 주기적 플러시 데이터
│   │   ├── logs_flush.zip          # 최신 로그 스냅샷
│   │   ├── state_flush.json        # 최신 상태 스냅샷
│   │   └── video_flush/            # 최신 영상 세그먼트
│   └── abnormal_exit.flag          # 레이어 3 비정상 종료 감지 플래그
│
└── crash_bundles/
    └── {timestamp}/                # 크래시 번들 (YYYYMMDD_HHmmss_fff 형식)
        ├── manifest.json           # 크래시 번들 매니페스트
        ├── logs_flush.zip          # 복사된 로그
        ├── state_flush.json        # 복사된 상태
        ├── video_flush/            # 복사된 영상
        └── crash_info.json         # 크래시 원인 정보 (exception type, stack trace)
```

---

## 3. 크래시 번들 구조 (manifest.json)

```json
{
  "id": "20240315_143022_123",
  "type": "crash",
  "created_at": "2024-03-15T14:30:22.123Z",
  "plugin_version": "1.0.0",
  "unity_version": "2022.3.22f1",
  "crash_type": "managed_exception",
  "exception_type": "System.NullReferenceException",
  "exception_message": "Object reference not set...",
  "stack_trace": "at Player.Update() in ...",
  "data_integrity": {
    "logs_ok": true,
    "state_ok": true,
    "video_ok": false,
    "overall": "partial"
  },
  "jira_issue_key": null,
  "registered_at": null
}
```

### data_integrity.overall 값
| 값 | 의미 |
|---|---|
| `"ok"` | 모든 데이터 무결성 검증 성공 |
| `"partial"` | 일부 데이터만 존재 또는 검증 실패 |
| `"missing"` | 유효한 데이터가 없음 |

---

## 4. 상태 머신

### AbnormalExitDetector 상태

```
[앱 시작]
    │
    ▼
flag 파일 존재?
    ├─ Yes → 크래시 감지! → CrashBundleWriter.BuildAsync() 호출
    │                       → Editor: CrashBundleScanner 알림
    └─ No  → 정상 시작
    │
    ▼
flag 파일 생성 (abnormal_exit.flag)
    │
    ▼
[Play 중 로그/상태/영상 주기적 플러시]
    │
    ├─ Managed Exception → ManagedExceptionHandler → 즉시 번들 생성
    │
    └─ 정상 종료 (OnApplicationQuit) → flag 파일 삭제
```

### ManagedExceptionHandler 상태

```
[Exception 수신]
    │
    ▼
쿨다운 중?
    ├─ Yes → 무시
    └─ No  → 쿨다운 시작 (30초)
               │
               ▼
           CrashBundleWriter.BuildAsync()
               │
               ▼
           크래시 번들 생성 완료
```

---

## 5. 구성 요소 목록

### Runtime (GaoZombie.BugOneTouch)

| 파일 | 역할 |
|---|---|
| `Runtime/CrashRecovery/PeriodicFlushManager.cs` | 레이어 1 주기적 플러시 MonoBehaviour |
| `Runtime/CrashRecovery/MappedFileWriter.cs` | 원자적 파일 쓰기 유틸리티 |
| `Runtime/CrashRecovery/AbnormalExitDetector.cs` | 레이어 3 비정상 종료 감지 |
| `Runtime/CrashRecovery/CrashBundleWriter.cs` | 크래시 번들 생성 |
| `Runtime/CrashRecovery/ManagedExceptionHandler.cs` | 레이어 2 Managed Exception 감지 |
| `Runtime/CrashRecovery/CrashBundleRetentionPolicy.cs` | 번들 보존 정책 (FIFO + 기간) |

### Editor (GaoZombie.BugOneTouch.Editor)

| 파일 | 역할 |
|---|---|
| `Editor/CrashRecovery/CrashBundleScanner.cs` | 에디터 시작 시 번들 스캔 |
| `Editor/CrashRecovery/CrashRecoveryWindow.cs` | 크래시 복구 UI 창 |
| `Editor/CrashRecovery/CrashJiraSubmitter.cs` | 크래시 → Jira 자동 제출 |

### Tests

| 파일 | 역할 |
|---|---|
| `Tests/Runtime/CrashRecovery/PeriodicFlushTests.cs` | 플러시 매니저 단위 테스트 |
| `Tests/Runtime/CrashRecovery/AbnormalExitDetectorTests.cs` | 감지기 단위 테스트 |
| `Tests/Runtime/CrashRecovery/CrashBundleWriterTests.cs` | 번들 생성 단위 테스트 |
| `Tests/Editor/CrashRecovery/CrashRecoveryWindowTests.cs` | 복구 UI 단위 테스트 |

---

## 6. 설계 원칙

1. **최소 I/O**: 플러시 데이터는 버퍼링 후 원자적 쓰기 (temp → rename)
2. **예외 격리**: 플러시 실패는 게임에 영향을 주지 않음 (로그만 남김)
3. **스레드 안전**: 코루틴/메인 스레드에서만 Unity API 호출
4. **Managed Only**: Native crash는 Unity Crash Reporter 영역, C# Exception만 처리
5. **DontDestroyOnLoad**: PeriodicFlushManager는 씬 전환에도 지속
6. **Editor 분리**: [InitializeOnLoad]는 Editor asmdef에서만 사용
