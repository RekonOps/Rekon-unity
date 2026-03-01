# Bug-OneTouch BasicDemo 샘플

Bug-OneTouch 플러그인의 핵심 기능을 실제 코드로 보여주는 샘플입니다.

---

## 포함된 파일

| 파일 | 설명 |
|------|------|
| `BasicDemoScene.unity.meta` | 샘플 씬 메타 파일 |
| `Scripts/SampleBugReporter.cs` | `BugOneTouchContext` 정적 API 사용 예제 |
| `Scripts/SampleContextProvider.cs` | `IContextProvider` 인터페이스 구현 예제 |

---

## 설치 방법

### UPM Package Manager에서 샘플 가져오기

1. `Window` → `Package Manager` 열기
2. 목록에서 **Bug-OneTouch** 패키지 선택
3. 우측 `Samples` 탭 클릭
4. **BasicDemo** 항목 옆 `Import` 버튼 클릭
5. `Assets/Samples/Bug-OneTouch/0.1.0/BasicDemo/` 폴더에 파일이 복사됩니다

---

## 사용 방법

### SampleBugReporter 사용

1. 씬에 빈 게임 오브젝트 생성
2. `SampleBugReporter` 컴포넌트 추가
3. Inspector에서 `Current Level`, `Score`, `Player Hp` 값 설정
4. Play Mode 진입 후 `F12` 눌러 버그 리포트 제출
5. 리포트에 다음 데이터가 자동 포함됩니다:
   - `level` - 현재 레벨
   - `score` - 점수
   - `player_hp` - 플레이어 HP
   - `scene` - 현재 씬 이름
   - `frame` - 현재 프레임 수
   - `time` - 게임 경과 시간

### SampleContextProvider 사용

1. 씬에 빈 게임 오브젝트 생성
2. `SampleContextProvider` 컴포넌트 추가
3. 실제 프로젝트에서 `BugOneTouch.Instance.ContextRegistry.Register(this)` 호출 코드 추가
4. Play Mode에서 버그 리포트 시 `GetContext()`가 자동으로 호출되어 씬 정보, 시스템 정보, 런타임 정보가 수집됩니다

---

## 주요 개념

### BugOneTouchContext (정적 API)

```csharp
// 데이터 추가/업데이트
BugOneTouchContext.Add("key", "value");

// 데이터 제거
BugOneTouchContext.Remove("key");

// 전체 초기화
BugOneTouchContext.Clear();
```

씬 전환 없이 지속적으로 업데이트되는 단순한 값(레벨, 점수 등)에 적합합니다.

### IContextProvider (동적 프로바이더)

```csharp
public class MyProvider : IContextProvider
{
    public Dictionary<string, string> GetContext()
    {
        return new Dictionary<string, string>
        {
            { "fps", (1f / Time.smoothDeltaTime).ToString("F1") },
        };
    }
}

// 등록
BugOneTouch.Instance.ContextRegistry.Register(new MyProvider());
```

버그 캡처 시점의 최신 상태를 수집해야 하거나, 여러 시스템이 독립적으로 컨텍스트를 관리해야 할 때 적합합니다.

---

## 참고 문서

- [사용자 가이드](../../docs/user-guide.md)
- [API 레퍼런스](../../docs/api-reference.md)
