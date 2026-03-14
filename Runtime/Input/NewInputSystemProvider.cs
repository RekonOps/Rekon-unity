using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// Unity New Input System 기반 핫키 제공자.
    /// ENABLE_INPUT_SYSTEM 심볼이 정의된 경우에만 컴파일됩니다.
    /// </summary>
#if ENABLE_INPUT_SYSTEM
    public class NewInputSystemProvider : IHotkeyProvider
    {
        // New Input System의 KeyCode → Key 매핑 테이블
        // 모든 KeyCode를 커버하지는 않으며, 누락된 경우 false를 반환합니다.
        private static readonly System.Collections.Generic.Dictionary<KeyCode, UnityEngine.InputSystem.Key> s_KeyMap
            = new System.Collections.Generic.Dictionary<KeyCode, UnityEngine.InputSystem.Key>
        {
            { KeyCode.F1,  UnityEngine.InputSystem.Key.F1  },
            { KeyCode.F2,  UnityEngine.InputSystem.Key.F2  },
            { KeyCode.F3,  UnityEngine.InputSystem.Key.F3  },
            { KeyCode.F4,  UnityEngine.InputSystem.Key.F4  },
            { KeyCode.F5,  UnityEngine.InputSystem.Key.F5  },
            { KeyCode.F6,  UnityEngine.InputSystem.Key.F6  },
            { KeyCode.F7,  UnityEngine.InputSystem.Key.F7  },
            { KeyCode.F8,  UnityEngine.InputSystem.Key.F8  },
            { KeyCode.F9,  UnityEngine.InputSystem.Key.F9  },
            { KeyCode.F10, UnityEngine.InputSystem.Key.F10 },
            { KeyCode.F11, UnityEngine.InputSystem.Key.F11 },
            { KeyCode.F12, UnityEngine.InputSystem.Key.F12 },
            { KeyCode.Space,       UnityEngine.InputSystem.Key.Space      },
            { KeyCode.Return,      UnityEngine.InputSystem.Key.Enter      },
            { KeyCode.Escape,      UnityEngine.InputSystem.Key.Escape     },
            { KeyCode.BackQuote,   UnityEngine.InputSystem.Key.Backquote  },
            { KeyCode.Tab,         UnityEngine.InputSystem.Key.Tab        },
            { KeyCode.LeftShift,   UnityEngine.InputSystem.Key.LeftShift  },
            { KeyCode.RightShift,  UnityEngine.InputSystem.Key.RightShift },
            { KeyCode.LeftControl, UnityEngine.InputSystem.Key.LeftCtrl   },
            { KeyCode.RightControl,UnityEngine.InputSystem.Key.RightCtrl  },
            { KeyCode.LeftAlt,     UnityEngine.InputSystem.Key.LeftAlt    },
            { KeyCode.RightAlt,    UnityEngine.InputSystem.Key.RightAlt   },
            { KeyCode.Alpha0, UnityEngine.InputSystem.Key.Digit0 },
            { KeyCode.Alpha1, UnityEngine.InputSystem.Key.Digit1 },
            { KeyCode.Alpha2, UnityEngine.InputSystem.Key.Digit2 },
            { KeyCode.Alpha3, UnityEngine.InputSystem.Key.Digit3 },
            { KeyCode.Alpha4, UnityEngine.InputSystem.Key.Digit4 },
            { KeyCode.Alpha5, UnityEngine.InputSystem.Key.Digit5 },
            { KeyCode.Alpha6, UnityEngine.InputSystem.Key.Digit6 },
            { KeyCode.Alpha7, UnityEngine.InputSystem.Key.Digit7 },
            { KeyCode.Alpha8, UnityEngine.InputSystem.Key.Digit8 },
            { KeyCode.Alpha9, UnityEngine.InputSystem.Key.Digit9 },
            { KeyCode.A, UnityEngine.InputSystem.Key.A },
            { KeyCode.B, UnityEngine.InputSystem.Key.B },
            { KeyCode.C, UnityEngine.InputSystem.Key.C },
            { KeyCode.D, UnityEngine.InputSystem.Key.D },
            { KeyCode.E, UnityEngine.InputSystem.Key.E },
            { KeyCode.F, UnityEngine.InputSystem.Key.F },
            { KeyCode.G, UnityEngine.InputSystem.Key.G },
            { KeyCode.H, UnityEngine.InputSystem.Key.H },
            { KeyCode.I, UnityEngine.InputSystem.Key.I },
            { KeyCode.J, UnityEngine.InputSystem.Key.J },
            { KeyCode.K, UnityEngine.InputSystem.Key.K },
            { KeyCode.L, UnityEngine.InputSystem.Key.L },
            { KeyCode.M, UnityEngine.InputSystem.Key.M },
            { KeyCode.N, UnityEngine.InputSystem.Key.N },
            { KeyCode.O, UnityEngine.InputSystem.Key.O },
            { KeyCode.P, UnityEngine.InputSystem.Key.P },
            { KeyCode.Q, UnityEngine.InputSystem.Key.Q },
            { KeyCode.R, UnityEngine.InputSystem.Key.R },
            { KeyCode.S, UnityEngine.InputSystem.Key.S },
            { KeyCode.T, UnityEngine.InputSystem.Key.T },
            { KeyCode.U, UnityEngine.InputSystem.Key.U },
            { KeyCode.V, UnityEngine.InputSystem.Key.V },
            { KeyCode.W, UnityEngine.InputSystem.Key.W },
            { KeyCode.X, UnityEngine.InputSystem.Key.X },
            { KeyCode.Y, UnityEngine.InputSystem.Key.Y },
            { KeyCode.Z, UnityEngine.InputSystem.Key.Z },
        };

        /// <summary>
        /// 이번 프레임에 지정된 키가 눌렸는지 반환합니다.
        /// New Input System의 Keyboard를 사용합니다.
        /// </summary>
        /// <param name="key">감지할 키코드 (Legacy KeyCode 형식)</param>
        /// <returns>이번 프레임에 눌렸으면 true. 매핑이 없거나 키보드가 없으면 false</returns>
        public bool IsTriggered(KeyCode key)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return false;

            if (!s_KeyMap.TryGetValue(key, out var inputKey))
                return false;

            return keyboard[inputKey].wasPressedThisFrame;
        }

        /// <summary>
        /// Ctrl(Windows) 또는 Cmd(Mac) 키가 현재 눌려있는지 반환합니다.
        /// </summary>
        public bool IsCtrlOrCmdHeld()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return false;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;
#else
            return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
#endif
        }

        /// <summary>
        /// Shift 키가 현재 눌려있는지 반환합니다.
        /// </summary>
        public bool IsShiftHeld()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return false;
            return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        /// <summary>
        /// Alt(Windows) 또는 Option(Mac) 키가 현재 눌려있는지 반환합니다.
        /// </summary>
        public bool IsAltHeld()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return false;
            return keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
        }
    }
#else
    /// <summary>
    /// New Input System이 비활성화된 경우의 더미 구현.
    /// 항상 false를 반환합니다.
    /// </summary>
    public class NewInputSystemProvider : IHotkeyProvider
    {
        public bool IsTriggered(KeyCode key)
        {
            return false;
        }

        public bool IsCtrlOrCmdHeld()
        {
            return false;
        }

        public bool IsShiftHeld()
        {
            return false;
        }

        public bool IsAltHeld()
        {
            return false;
        }
    }
#endif
}
