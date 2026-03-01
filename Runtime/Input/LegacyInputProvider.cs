using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// Unity Legacy Input System 기반 핫키 제공자.
    /// Input.GetKeyDown을 사용하여 키 입력을 감지합니다.
    /// </summary>
    public class LegacyInputProvider : IHotkeyProvider
    {
        /// <summary>
        /// 이번 프레임에 지정된 키가 눌렸는지 반환합니다.
        /// </summary>
        /// <param name="key">감지할 키코드</param>
        /// <returns>이번 프레임에 눌렸으면 true</returns>
        public bool IsTriggered(KeyCode key)
        {
            return Input.GetKeyDown(key);
        }
    }
}
