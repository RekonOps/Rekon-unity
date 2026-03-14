using System;
using System.Collections.Generic;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 버그 리포트에 포함되는 시스템/애플리케이션 상태 스냅샷.
    /// JsonUtility로 직렬화할 수 있도록 [Serializable] 태그를 사용합니다.
    ///
    /// 주의: JsonUtility는 Dictionary를 직렬화하지 못하므로
    /// custom_context는 SerializableKeyValuePair 리스트로 저장합니다.
    /// </summary>
    [Serializable]
    public class StateSnapshot
    {
        // ── 엔진 정보 ──────────────────────────────────────────────────
        /// <summary>엔진 이름 (Unity)</summary>
        public string engine;

        /// <summary>Unity 버전 (예: 2022.3.22f1)</summary>
        public string engine_version;

        /// <summary>애플리케이션 버전 (Application.version)</summary>
        public string app_version;

        /// <summary>빌드 번호 (정의된 경우)</summary>
        public string build_number;

        // ── 플랫폼/디바이스 정보 ───────────────────────────────────────
        /// <summary>플랫폼 (WindowsPlayer, IPhonePlayer 등)</summary>
        public string platform;

        /// <summary>디바이스 모델명 (SystemInfo.deviceModel)</summary>
        public string device;

        /// <summary>운영체제 버전 (SystemInfo.operatingSystem)</summary>
        public string os;

        /// <summary>CPU 이름 (SystemInfo.processorType)</summary>
        public string cpu;

        /// <summary>GPU 이름 (SystemInfo.graphicsDeviceName)</summary>
        public string gpu;

        /// <summary>시스템 메모리(MB) (SystemInfo.systemMemorySize)</summary>
        public int memory_mb;

        // ── 화면 정보 ──────────────────────────────────────────────────
        /// <summary>화면 너비(픽셀)</summary>
        public int screen_width;

        /// <summary>화면 높이(픽셀)</summary>
        public int screen_height;

        /// <summary>전체 화면 여부</summary>
        public bool fullscreen;

        // ── 런타임 정보 ────────────────────────────────────────────────
        /// <summary>현재 씬 이름</summary>
        public string scene;

        /// <summary>앱 시작 후 경과 시간(초)</summary>
        public float time_since_startup;

        /// <summary>총 렌더링 프레임 수</summary>
        public int frame_count;

        /// <summary>측정 시점의 초당 프레임 수 (1 / Time.unscaledDeltaTime)</summary>
        public float fps;

        /// <summary>현재 품질 설정 레벨</summary>
        public int quality_level;

        // ── 커스텀 컨텍스트 ────────────────────────────────────────────
        /// <summary>
        /// ContextProviderRegistry에서 수집된 커스텀 K/V 데이터.
        /// JsonUtility 호환을 위해 리스트 형태로 저장합니다.
        /// </summary>
        public List<SerializableKeyValue> custom_context = new List<SerializableKeyValue>();

        /// <summary>
        /// 스냅샷이 수집된 UTC 시각 (ISO 8601 형식)
        /// </summary>
        public string captured_at;

        /// <summary>
        /// custom_context를 Dictionary로 변환하여 반환합니다.
        /// </summary>
        public Dictionary<string, string> GetCustomContextDictionary()
        {
            var result = new Dictionary<string, string>();
            if (custom_context == null)
                return result;

            foreach (var kv in custom_context)
            {
                if (!string.IsNullOrEmpty(kv.key))
                    result[kv.key] = kv.value;
            }
            return result;
        }

        /// <summary>
        /// Dictionary를 custom_context 리스트로 설정합니다.
        /// </summary>
        public void SetCustomContextDictionary(Dictionary<string, string> dict)
        {
            custom_context = new List<SerializableKeyValue>();
            if (dict == null)
                return;

            foreach (var kvp in dict)
                custom_context.Add(new SerializableKeyValue { key = kvp.Key, value = kvp.Value });
        }
    }

    /// <summary>
    /// JsonUtility 직렬화를 위한 Key-Value 쌍 구조체.
    /// </summary>
    [Serializable]
    public class SerializableKeyValue
    {
        public string key;
        public string value;
    }
}
