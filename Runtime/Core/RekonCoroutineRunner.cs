using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 코루틴 실행 전용 MonoBehaviour.
    /// ScreenshotCapturer 등 비-MonoBehaviour 클래스에서 WaitForEndOfFrame을 사용하기 위한 브릿지.
    /// </summary>
    internal class RekonCoroutineRunner : MonoBehaviour { }
}
