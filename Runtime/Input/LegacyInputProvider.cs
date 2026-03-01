using UnityEngine;

namespace RekonOps.BugOneTouch
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

        /// <summary>
        /// Ctrl(Windows) 또는 Cmd(Mac) 키가 현재 눌려있는지 반환합니다.
        /// </summary>
        public bool IsCtrlOrCmdHeld()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
#else
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
        }

        /// <summary>
        /// Shift 키가 현재 눌려있는지 반환합니다.
        /// </summary>
        public bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>
        /// Alt(Windows) 또는 Option(Mac) 키가 현재 눌려있는지 반환합니다.
        /// </summary>
        public bool IsAltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }
    }
}
