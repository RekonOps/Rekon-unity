# BugBeacon API 레퍼런스

> 네임스페이스: `GaoZombie.BugBeacon`

---

## 목차

1. [BugBeaconContext](#bugonетouchcontext)
2. [IContextProvider](#icontextprovider)
3. [ContextProviderRegistry](#contextproviderregistry)
4. [BugBeaconSettings](#bugonетouchsettings)
5. [CaptureOrchestrator](#captureorchestrator)
6. [LogMasker](#logmasker)
7. [MaskingRuleLoader](#maskingruleloader)

---

## BugBeaconContext

`Runtime/Capture/BugBeaconContext.cs`

버그 리포트에 포함될 커스텀 키-값 데이터를 관리하는 정적 API 클래스입니다. 스레드 안전하게 구현되어 있으며, 어느 스레드에서도 호출할 수 있습니다.

### 메서드

#### `Add(string key, string value)`

```csharp
public static void Add(string key, string value)
```

컨텍스트 데이터를 추가하거나 기존 키의 값을 업데이트합니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `key` | `string` | 데이터 키. `null` 또는 빈 문자열이면 예외를 던집니다. |
| `value` | `string` | 데이터 값. `null`이면 빈 문자열로 저장됩니다. |

**예외:**

- `ArgumentNullException`: `key`가 `null` 또는 빈 문자열인 경우

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

void OnLevelStart(int levelIndex)
{
    BugBeaconContext.Add("current_level", levelIndex.ToString());
    BugBeaconContext.Add("difficulty", GameSettings.Difficulty.ToString());
}
```

---

#### `Remove(string key)`

```csharp
public static void Remove(string key)
```

지정한 키의 컨텍스트 데이터를 제거합니다. 키가 존재하지 않으면 아무 동작도 하지 않습니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `key` | `string` | 제거할 키. `null` 또는 빈 문자열이면 무시됩니다. |

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

void OnLevelEnd()
{
    // 레벨 종료 시 해당 레벨의 컨텍스트 제거
    BugBeaconContext.Remove("current_level");
}
```

---

#### `Clear()`

```csharp
public static void Clear()
```

등록된 모든 컨텍스트 데이터를 제거합니다.

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

void OnSceneUnloaded(Scene scene)
{
    // 씬 전환 시 이전 씬의 컨텍스트 초기화
    BugBeaconContext.Clear();
}
```

---

#### `GetSnapshot()`

```csharp
public static Dictionary<string, string> GetSnapshot()
```

현재 컨텍스트 데이터의 복사본을 반환합니다. 원본 딕셔너리는 변경되지 않습니다.

**반환값:** `Dictionary<string, string>` - 현재 컨텍스트 데이터의 스냅샷

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

void DebugPrintContext()
{
    var snapshot = BugBeaconContext.GetSnapshot();
    foreach (var kvp in snapshot)
    {
        Debug.Log($"[Context] {kvp.Key}: {kvp.Value}");
    }
}
```

---

#### `AsProvider()`

```csharp
public static IContextProvider AsProvider()
```

`BugBeaconContext`를 `IContextProvider`로 감싸는 어댑터를 반환합니다. `ContextProviderRegistry`에 등록할 때 사용합니다.

**반환값:** `IContextProvider` - BugBeaconContext를 감싸는 프로바이더

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

// ContextProviderRegistry에 정적 컨텍스트 등록
registry.Register(BugBeaconContext.AsProvider());
```

---

### 프로퍼티

#### `Count`

```csharp
public static int Count { get; }
```

현재 등록된 컨텍스트 항목 수를 반환합니다.

**반환값:** `int` - 등록된 항목 수

---

## IContextProvider

`Runtime/Capture/IContextProvider.cs`

버그 리포트에 포함될 동적 컨텍스트 데이터를 제공하는 인터페이스입니다. 게임 상태를 버그 리포트에 자동으로 포함시키려면 이 인터페이스를 구현하고 `ContextProviderRegistry`에 등록합니다.

### 인터페이스 정의

```csharp
namespace GaoZombie.BugBeacon
{
    public interface IContextProvider
    {
        Dictionary<string, string> GetContext();
    }
}
```

### 메서드

#### `GetContext()`

```csharp
Dictionary<string, string> GetContext()
```

현재 컨텍스트 데이터를 반환합니다. 버그 캡처 시점에 호출됩니다.

**반환값:** `Dictionary<string, string>` - 컨텍스트 키-값 데이터. `null`을 반환하면 해당 프로바이더의 데이터는 무시됩니다.

**주의사항:**

- 같은 키를 여러 프로바이더가 제공하면 나중에 등록된 프로바이더의 값이 우선합니다.
- `GetContext()` 내부에서 예외가 발생하면 해당 프로바이더는 건너뛰고 나머지 프로바이더를 계속 처리합니다.
- 성능에 민감한 경우, `GetContext()` 내부에서 무거운 연산을 피하고 미리 캐시된 값을 반환하는 것을 권장합니다.

**구현 예:**

```csharp
using System.Collections.Generic;
using GaoZombie.BugBeacon;

public class GameStateContextProvider : IContextProvider
{
    public Dictionary<string, string> GetContext()
    {
        return new Dictionary<string, string>
        {
            { "level",          GameManager.CurrentLevel.ToString() },
            { "score",          GameManager.Score.ToString() },
            { "player_hp",      PlayerController.Hp.ToString() },
            { "enemy_count",    EnemyManager.ActiveCount.ToString() },
            { "scene",          UnityEngine.SceneManagement.SceneManager
                                    .GetActiveScene().name },
        };
    }
}
```

---

## ContextProviderRegistry

`Runtime/Capture/ContextProviderRegistry.cs`

`IContextProvider` 구현체를 등록/해제하고, 등록된 모든 프로바이더에서 데이터를 수집하여 병합하는 레지스트리 클래스입니다. 모든 공개 메서드는 스레드 안전합니다.

### 메서드

#### `Register(IContextProvider provider)`

```csharp
public void Register(IContextProvider provider)
```

컨텍스트 프로바이더를 등록합니다. 이미 등록된 프로바이더는 중복 등록되지 않습니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `provider` | `IContextProvider` | 등록할 프로바이더. `null`이면 예외를 던집니다. |

**예외:**

- `ArgumentNullException`: `provider`가 `null`인 경우

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

public class GameBootstrapper : MonoBehaviour
{
    private GameStateContextProvider _contextProvider;

    void Awake()
    {
        _contextProvider = new GameStateContextProvider();
        BugBeacon.Instance.ContextRegistry.Register(_contextProvider);
    }

    void OnDestroy()
    {
        BugBeacon.Instance.ContextRegistry.Unregister(_contextProvider);
    }
}
```

---

#### `Unregister(IContextProvider provider)`

```csharp
public void Unregister(IContextProvider provider)
```

등록된 컨텍스트 프로바이더를 해제합니다. 등록되지 않은 프로바이더는 무시됩니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `provider` | `IContextProvider` | 해제할 프로바이더. `null`이면 예외를 던집니다. |

---

#### `CollectAll()`

```csharp
public Dictionary<string, string> CollectAll()
```

등록된 모든 프로바이더에서 컨텍스트 데이터를 수집하고 병합하여 반환합니다.

**반환값:** `Dictionary<string, string>` - 병합된 컨텍스트 데이터. Key 충돌 시 나중에 등록된 프로바이더의 값이 우선합니다.

**주의사항:**

- 프로바이더에서 예외가 발생하면 해당 프로바이더는 건너뛰고 나머지를 계속 처리합니다.
- 예외 발생 시 `Debug.LogWarning`으로 경고 메시지가 출력됩니다.

---

#### `Clear()`

```csharp
public void Clear()
```

등록된 모든 프로바이더를 해제합니다.

---

### 프로퍼티

#### `Count`

```csharp
public int Count { get; }
```

현재 등록된 프로바이더 수를 반환합니다.

---

## BugBeaconSettings

`Runtime/Settings/BugBeaconSettings.cs`

플러그인 동작을 제어하는 ScriptableObject 설정 클래스입니다. Unity 에디터에서 `Project Settings` → `BugBeacon` 패널을 통해 관리합니다.

### 생성

```csharp
// 에디터 메뉴: Assets > Create > BugBeacon > Settings
[CreateAssetMenu(fileName = "BugBeaconSettings", menuName = "BugBeacon/Settings")]
```

### 주요 프로퍼티

#### Hotkey 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `captureHotkey` | `KeyCode` | `KeyCode.F12` | 버그 캡처를 시작하는 키 |

#### Screenshot 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `screenshotDownscale` | `int` | `1` | 스크린샷 축소 배율 (1 = 원본 해상도) |

#### Video 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `videoEnabled` | `bool` | `true` | 영상 링 버퍼 활성화 여부 |
| `videoWidth` | `int` | `1280` | 녹화 해상도 가로 픽셀 |
| `videoHeight` | `int` | `720` | 녹화 해상도 세로 픽셀 |
| `videoFps` | `int` | `30` | 초당 프레임 수 (15~60) |
| `videoBufferSeconds` | `int` | `60` | 영상 버퍼 유지 시간(초) (10~120) |
| `videoBitrateMbps` | `float` | `10f` | 목표 비트레이트 Mbps (2~20) |

#### Log 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `logBufferSize` | `int` | `500` | 링 버퍼에 보관할 최대 로그 줄 수 (100~5000) |
| `maskingRulesPath` | `string` | `""` | 커스텀 마스킹 규칙 JSON 파일의 절대 경로 |

#### Crash Recovery 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `logFlushInterval` | `float` | `5f` | 로그 디스크 플러시 주기(초) (1~30) |
| `stateFlushInterval` | `float` | `10f` | 상태 스냅샷 플러시 주기(초) (1~60) |
| `videoFlushInterval` | `float` | `30f` | 영상 플러시 주기(초) (10~120) |
| `maxCrashBundles` | `int` | `10` | 보관할 최대 크래시 번들 수 (1~50) |
| `crashBundleRetentionDays` | `int` | `30` | 크래시 번들 보관 기간(일) (1~365) |

#### Bundle 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `maxBundles` | `int` | `200` | 최대 번들 수 (10~1000) |
| `maxDiskUsageMB` | `int` | `5120` | 최대 디스크 사용량 MB (500~20000) |

#### Auth Broker 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `authBrokerUrl` | `string` | `""` | Auth Broker 서버 기본 URL |

#### Jira 설정

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `defaultLabels` | `string[]` | `["bugbeacon-unity", "unity"]` | 이슈 생성 시 자동으로 추가할 Jira 레이블 목록 |

---

## CaptureOrchestrator

`Runtime/Core/CaptureOrchestrator.cs`

캡처 파이프라인 전체를 조율하는 오케스트레이터 클래스입니다. 핫키 트리거 이벤트를 구독하고, 스크린샷/로그/상태/영상을 병렬로 수집합니다.

### 이벤트

#### `OnProgress`

```csharp
public event Action<CaptureProgressEvent> OnProgress
```

캡처 각 단계의 진행 상황을 알리는 이벤트입니다.

**이벤트 데이터: `CaptureProgressEvent`**

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `Stage` | `string` | 진행 단계 (`"screenshot"`, `"logs"`, `"state"`, `"video"`, `"complete"`) |
| `Progress` | `float` | 진행률 (0.0 ~ 1.0) |
| `ErrorMessage` | `string` | 오류 발생 시 메시지. 정상이면 `null` |

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

public class CaptureProgressDisplay : MonoBehaviour
{
    void Start()
    {
        var orchestrator = BugBeacon.Instance.Orchestrator;
        orchestrator.OnProgress += HandleProgress;
    }

    void OnDestroy()
    {
        if (BugBeacon.Instance != null)
        {
            BugBeacon.Instance.Orchestrator.OnProgress -= HandleProgress;
        }
    }

    private void HandleProgress(CaptureProgressEvent evt)
    {
        Debug.Log($"[캡처 진행] {evt.Stage}: {evt.Progress * 100:F0}%");

        if (evt.ErrorMessage != null)
        {
            Debug.LogWarning($"[캡처 오류] {evt.Stage}: {evt.ErrorMessage}");
        }
    }
}
```

---

#### `OnCaptureCompleted`

```csharp
public event Action<CaptureResult> OnCaptureCompleted
```

캡처가 완료되면 발행되는 이벤트입니다. `CaptureResult`에 수집된 파일 경로가 포함됩니다.

**이벤트 데이터: `CaptureResult`**

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `Timestamp` | `DateTime` | 캡처 시작 시각 (UTC) |
| `ScreenshotPath` | `string` | 스크린샷 PNG 파일 경로. 실패 시 `null` |
| `LogsPath` | `string` | 로그 ZIP 파일 경로. 실패 시 `null` |
| `StatePath` | `string` | 상태 스냅샷 JSON 파일 경로. 실패 시 `null` |
| `VideoPath` | `string` | 영상 디렉토리 경로. 비활성화되거나 실패 시 `null` |

---

### 메서드

#### `StartAsync()`

```csharp
public async Task<CaptureResult> StartAsync()
```

캡처 파이프라인을 수동으로 시작합니다. 이미 캡처 중이면 `null`을 반환합니다.

**반환값:** `Task<CaptureResult>` - 완료된 캡처 결과. 이미 진행 중인 경우 `null`

**주의사항:**

- 최대 5초의 타임아웃이 적용됩니다. 타임아웃 시 수집된 아티팩트만 포함한 결과를 반환합니다.
- 핫키 트리거 시 자동으로 호출됩니다. 수동 호출은 디버그 또는 자동화 테스트용으로만 사용하는 것을 권장합니다.

---

#### `BindHotkeyManager(HotkeyManager hotkeyManager)`

```csharp
public void BindHotkeyManager(HotkeyManager hotkeyManager)
```

HotkeyManager를 등록하고 `OnCaptureTrigger` 이벤트를 구독합니다. 초기화 시 BugBeacon 시스템이 자동으로 호출합니다.

---

## LogMasker

`Runtime/Security/LogMasker.cs`

로그 문자열에서 민감 정보를 마스킹하는 유틸리티 클래스입니다. 기본적으로 이메일, IPv4 주소, 토큰/시크릿을 마스킹하며, 커스텀 규칙을 추가할 수 있습니다.

### 정적 메서드 (기본 마스킹)

#### `MaskEmail(string input)`

```csharp
public static string MaskEmail(string input)
```

이메일 주소를 `[MASKED:EMAIL]`로 치환합니다.

**사용 예:**

```csharp
string masked = LogMasker.MaskEmail("user@example.com 에게 메일을 보냈습니다.");
// 결과: "[MASKED:EMAIL] 에게 메일을 보냈습니다."
```

---

#### `MaskIp(string input)`

```csharp
public static string MaskIp(string input)
```

IPv4 주소를 `[MASKED:IP]`로 치환합니다.

**사용 예:**

```csharp
string masked = LogMasker.MaskIp("서버 192.168.1.100에 연결했습니다.");
// 결과: "서버 [MASKED:IP]에 연결했습니다."
```

---

#### `MaskToken(string input)`

```csharp
public static string MaskToken(string input)
```

`token=`, `secret=`, `password=`, `api_key=`, `access_key=`, `auth=` 패턴의 값을 `[MASKED:TOKEN]`으로 치환합니다.

**사용 예:**

```csharp
string masked = LogMasker.MaskToken("api_key=sk_live_abc123xyz");
// 결과: "api_key=[MASKED:TOKEN]"
```

---

### 인스턴스 메서드

#### `MaskAll(string input)`

```csharp
public string MaskAll(string input)
```

모든 기본 마스킹 규칙(이메일, IP, 토큰)과 등록된 커스텀 규칙을 순서대로 적용합니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `input` | `string` | 마스킹할 원본 문자열. `null` 또는 빈 문자열이면 그대로 반환합니다. |

**반환값:** `string` - 민감 정보가 마스킹된 문자열

---

#### `AddRule(MaskingRule rule)`

```csharp
public void AddRule(MaskingRule rule)
```

커스텀 마스킹 규칙을 추가합니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `rule` | `MaskingRule` | 추가할 마스킹 규칙. `null`이면 예외를 던집니다. |

**`MaskingRule` 구조:**

```csharp
public class MaskingRule
{
    public string Name        { get; set; }  // 규칙 이름 (디버그용)
    public string Pattern     { get; set; }  // 정규식 패턴 (필수)
    public string Replacement { get; set; }  // 대체 문자열
    public bool   Enabled     { get; set; }  // 활성화 여부
}
```

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

var masker = new LogMasker();
masker.AddRule(new LogMasker.MaskingRule
{
    Name        = "인앱 결제 영수증",
    Pattern     = @"receipt=[A-Za-z0-9+/=]+",
    Replacement = "receipt=[MASKED:RECEIPT]",
    Enabled     = true
});

string masked = masker.MaskAll("purchase receipt=abc123def456");
// 결과: "purchase receipt=[MASKED:RECEIPT]"
```

---

#### `ClearRules()`

```csharp
public void ClearRules()
```

등록된 모든 커스텀 마스킹 규칙을 제거합니다. 기본 규칙(이메일, IP, 토큰)은 영향을 받지 않습니다.

---

### 프로퍼티

#### `RuleCount`

```csharp
public int RuleCount { get; }
```

현재 등록된 커스텀 마스킹 규칙 수를 반환합니다.

---

## MaskingRuleLoader

`Runtime/Security/MaskingRuleLoader.cs`

JSON 파일에서 커스텀 마스킹 규칙을 로드하여 `LogMasker`에 주입하는 정적 유틸리티 클래스입니다.

### JSON 스키마

```json
{
  "rules": [
    {
      "name": "규칙 이름",
      "pattern": "정규식 패턴",
      "replacement": "대체 문자열",
      "enabled": true
    }
  ]
}
```

### 메서드

#### `LoadFromFile(LogMasker masker, string filePath)`

```csharp
public static int LoadFromFile(LogMasker masker, string filePath)
```

지정 경로의 JSON 파일에서 커스텀 마스킹 규칙을 로드하여 `masker`에 추가합니다. 파일이 없거나 파싱에 실패하면 경고 로그를 출력하고 0을 반환합니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `masker` | `LogMasker` | 규칙을 주입할 LogMasker 인스턴스. `null`이면 예외를 던집니다. |
| `filePath` | `string` | JSON 파일의 절대 경로. 빈 문자열이면 경고 후 0을 반환합니다. |

**반환값:** `int` - 성공적으로 로드된 규칙 수

**사용 예:**

```csharp
using GaoZombie.BugBeacon;

var masker = new LogMasker();
string rulesPath = "/path/to/custom-masking-rules.json";
int loadedCount = MaskingRuleLoader.LoadFromFile(masker, rulesPath);
Debug.Log($"{loadedCount}개의 커스텀 마스킹 규칙이 로드되었습니다.");
```

---

#### `LoadFromJson(LogMasker masker, string json)`

```csharp
public static int LoadFromJson(LogMasker masker, string json)
```

JSON 문자열에서 커스텀 마스킹 규칙을 파싱하여 `masker`에 추가합니다.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `masker` | `LogMasker` | 규칙을 주입할 LogMasker 인스턴스. `null`이면 예외를 던집니다. |
| `json` | `string` | 규칙 JSON 문자열. 빈 문자열이면 경고 후 0을 반환합니다. |

**반환값:** `int` - 성공적으로 로드된 규칙 수

---

#### `GetDefaultRules()`

```csharp
public static IReadOnlyList<LogMasker.MaskingRule> GetDefaultRules()
```

내장 기본 마스킹 규칙 3종(이메일, IPv4, 토큰/시크릿)의 목록을 반환합니다.

**반환값:** `IReadOnlyList<LogMasker.MaskingRule>` - 기본 마스킹 규칙 목록

---

#### `GetDefaultRulesFilePath()`

```csharp
public static string GetDefaultRulesFilePath()
```

패키지 기본 마스킹 규칙 JSON 파일의 경로를 반환합니다. 패키지 캐시 경로, 개발 환경 경로, 로컬 경로 순서로 탐색합니다.

**반환값:** `string` - 마스킹 규칙 JSON 파일의 절대 경로

---

## 변경 이력

| 버전 | 날짜 | 변경 내용 |
|------|------|-----------|
| 0.1.0 | 2026-03-01 | 최초 릴리즈 |
