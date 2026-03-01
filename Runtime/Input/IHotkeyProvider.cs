using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 핫키 입력 감지 전략 인터페이스.
    /// Legacy Input / New Input System 중 하나를 주입합니다.
    /// </summary>
    public interface IHotkeyProvider
    {
        /// <summary>
        /// 이번 프레임에 지정된 키가 눌렸는지 반환합니다.
        /// </summary>
        /// <param name="key">감지할 키코드</param>
        /// <returns>이번 프레임에 눌렸으면 true</returns>
        bool IsTriggered(KeyCode key);
    }
}
