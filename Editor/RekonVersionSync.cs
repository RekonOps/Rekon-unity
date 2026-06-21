using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using RekonSdk = RekonOps.Rekon.Rekon;
// PackageInfo 는 UnityEditor.PackageInfo 와 이름이 충돌하므로 alias 로 명시.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace RekonOps.Rekon.Editor
{
    /// <summary>
    /// <see cref="RekonSdk.Version"/> 상수를 package.json 의 version 과 자동 동기화한다.
    ///
    /// 동작:
    ///   - 에디터 로드/도메인 리로드 시([InitializeOnLoadMethod]) PackageInfo.version(=package.json)
    ///     을 읽어 Runtime/Rekon.cs 의 `Version = "..."` 리터럴을 갱신한다.
    ///   - 값이 이미 일치하면 아무것도 안 한다(재컴파일 루프 방지).
    ///   - 패키지가 immutable(PackageCache, git URL 설치)이거나 쓰기 실패 시 조용히 skip
    ///     → 릴리스 시점에 동기화된 리터럴이 그대로 쓰인다(fail-safe).
    ///
    /// Rekon.Version 은 여전히 const 이므로, 이 스크립트가 한 번도 실행되지 않아도
    /// 빌드/런타임은 정상 동작한다(최악의 경우 = 리터럴이 stale, 빌드 깨짐 없음).
    /// 또한 Editor 어셈블리라 플레이어 빌드에는 포함되지 않는다.
    /// </summary>
    internal static class RekonVersionSync
    {
        // 예: `public const string Version = "0.5.0";` → 그룹2(값)만 교체
        private static readonly Regex s_VersionLiteral = new Regex(
            "(public\\s+const\\s+string\\s+Version\\s*=\\s*\")([^\"]*)(\";)");

        [InitializeOnLoadMethod]
        private static void OnEditorLoad() => TrySync();

        private static void TrySync()
        {
            try
            {
                var pkg = PackageInfo.FindForAssembly(typeof(RekonSdk).Assembly);
                if (pkg == null || string.IsNullOrEmpty(pkg.version)) return;

                string fullPath = Path.Combine(pkg.resolvedPath, "Runtime", "Rekon.cs");
                if (!File.Exists(fullPath)) return;

                string src = File.ReadAllText(fullPath);
                Match m = s_VersionLiteral.Match(src);
                if (!m.Success) return;
                if (m.Groups[2].Value == pkg.version) return; // 이미 일치 → no-op (루프 방지)

                string updated = s_VersionLiteral.Replace(src, "${1}" + pkg.version + "${3}", 1);
                File.WriteAllText(fullPath, updated);

                // AssetDatabase 상대 경로로 reimport (mutable 패키지에서만 의미)
                AssetDatabase.ImportAsset(pkg.assetPath + "/Runtime/Rekon.cs");
                Debug.Log($"[Rekon] Rekon.Version 동기화: {m.Groups[2].Value} → {pkg.version} (package.json 기준)");
            }
            catch (System.Exception)
            {
                // immutable 패키지(PackageCache) 또는 쓰기 권한 없음 → skip (fail-safe).
                // 릴리스 시 커밋된 리터럴이 그대로 사용된다.
            }
        }
    }
}
