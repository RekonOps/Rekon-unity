// SampleContextProvider.cs
// Rekon BasicDemo 샘플
//
// IContextProvider 인터페이스를 구현하여 동적 컨텍스트를 제공하는 예제입니다.
// ContextProviderRegistry에 등록하면 버그 캡처 시 자동으로 GetContext()가 호출됩니다.
//
// RekonContext.Add() 와의 차이:
//   - RekonContext: 정적 API, 직접 키-값 추가/제거
//   - IContextProvider: 캡처 시점에 GetContext()를 호출하여 데이터 수집
//                       (게임 오브젝트 참조, 씬 상태 등을 캡처 시점에 수집할 때 유용)

using System;
using System.Collections.Generic;
using UnityEngine;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Samples
{
    /// <summary>
    /// IContextProvider 구현 예제.
    /// 이 컴포넌트를 Scene의 게임 오브젝트에 추가하면
    /// ContextProviderRegistry에 자동으로 등록/해제됩니다.
    /// </summary>
    public class SampleContextProvider : MonoBehaviour, IContextProvider
    {
        // ──────────────────────────────────────────────────────────────
        // Unity 생명주기
        // ──────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            // Rekon 시스템이 초기화된 후에 등록해야 합니다.
            // 실제 프로젝트에서는 Rekon.Instance가 null인지 확인하세요.
            try
            {
                // 참고: Rekon.Instance.ContextRegistry에 등록하는 것이 정석이지만,
                //       이 샘플에서는 직접 ContextProviderRegistry 인스턴스 생성 방법을 보여줍니다.
                Debug.Log("[SampleContextProvider] IContextProvider 등록 준비 완료.");
                Debug.Log("실제 프로젝트에서는 Rekon.Instance.ContextRegistry.Register(this)를 호출하세요.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SampleContextProvider] 등록 실패: {ex.Message}");
            }
        }

        private void OnDisable()
        {
            // 컴포넌트 비활성화 시 등록 해제
            // Rekon.Instance.ContextRegistry.Unregister(this);
            Debug.Log("[SampleContextProvider] IContextProvider 등록 해제.");
        }

        // ──────────────────────────────────────────────────────────────
        // IContextProvider 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 버그 캡처 시점에 호출됩니다.
        /// 현재 게임 상태를 딕셔너리로 반환합니다.
        /// </summary>
        /// <returns>컨텍스트 키-값 데이터</returns>
        public Dictionary<string, string> GetContext()
        {
            // 주의: GetContext()는 캡처 스레드에서 호출될 수 있습니다.
            //       Unity API(예: FindObjectOfType, Camera.main) 중 일부는
            //       메인 스레드에서만 안전하게 호출됩니다.
            //       Thread Safety가 중요한 경우 미리 캐시된 값을 반환하세요.

            var context = new Dictionary<string, string>();

            // 씬 정보
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            context["scene_name"]       = activeScene.name;
            context["scene_build_index"] = activeScene.buildIndex.ToString();
            context["scene_is_loaded"]  = activeScene.isLoaded.ToString();

            // 시스템 정보
            context["platform"]         = Application.platform.ToString();
            context["unity_version"]    = Application.unityVersion;
            context["device_model"]     = SystemInfo.deviceModel;
            context["os_version"]       = SystemInfo.operatingSystem;
            context["memory_size_mb"]   = SystemInfo.systemMemorySize.ToString();
            context["graphics_device"]  = SystemInfo.graphicsDeviceName;

            // 런타임 정보
            context["frame_count"]      = Time.frameCount.ToString();
            context["real_time"]        = Time.realtimeSinceStartup.ToString("F2");
            context["fps"]              = (1.0f / Time.smoothDeltaTime).ToString("F1");

            // 메모리 사용량
            long totalMemory = GC.GetTotalMemory(forceFullCollection: false);
            context["gc_memory_mb"]     = (totalMemory / 1024f / 1024f).ToString("F2");

            return context;
        }
    }
}
