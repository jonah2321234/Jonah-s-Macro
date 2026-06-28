using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClicker
{
    /// <summary>
    /// P/Invoke wrappers for registering a global toggle hotkey and for
    /// simulating real keyboard key presses, text typing, and mouse clicks
    /// via SendInput so synthesized input behaves like hardware input to
    /// the app/game in focus (important for games like Roblox, which read
    /// raw scan codes rather than virtual-key-only events).
    /// </summary>
    internal static class NativeMethods
    {
        // ---- Hotkey registration (used for the single Start/Stop hotkey) ----
        public const int WM_HOTKEY = 0x0312;
        public const int ToggleHotkeyId = 9100;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const uint MOD_NONE = 0x0000;

        // Useful virtual-key codes
        public const ushort VK_SLASH = 0xBF;   // '/' key (US layout)
        public const ushort VK_RETURN = 0x0D;  // Enter

        public static ushort VkForDigit(int digit) => (ushort)(0x30 + digit);

        // ---- High-resolution timer (improves Task.Delay accuracy for small delays) ----
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint uMilliseconds);

        // ---- SendInput plumbing ----
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint MAPVK_VK_TO_VSC = 0;

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public INPUTUNION U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

        /// <summary>Simulates a single left mouse click at the current cursor position.</summary>
        public static void SendLeftClick()
        {
            var down = new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
            var up = new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
            SendInput(1, new[] { down }, InputSize);
            SendInput(1, new[] { up }, InputSize);
        }

        /// <summary>
        /// Simulates a key press + release for the given virtual key code.
        /// Sent as a hardware scan code (KEYEVENTF_SCANCODE) rather than a bare
        /// virtual-key event, since most games read raw scan codes and ignore
        /// virtual-key-only synthetic input.
        /// </summary>
        public static void SendKeyPress(ushort vk)
        {
            ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

            var down = new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = KEYEVENTF_SCANCODE } } };
            var up = new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP } } };

            SendInput(1, new[] { down }, InputSize);
            Thread.Sleep(15);
            SendInput(1, new[] { up }, InputSize);
        }

        /// <summary>
        /// Types text by sending each character as a real hardware-style scan code
        /// (handling Shift where needed), the same approach as SendKeyPress. This is
        /// more reliable for game chat boxes than Unicode key events.
        /// </summary>
        public static void TypeText(string text)
        {
            foreach (char c in text)
            {
                short vkScan = VkKeyScan(c);
                if (vkScan == -1) continue;

                byte vk = (byte)(vkScan & 0xFF);
                bool needsShift = ((vkScan >> 8) & 1) != 0;
                ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
                ushort shiftScan = (ushort)MapVirtualKey(0x10, MAPVK_VK_TO_VSC); // VK_SHIFT

                if (needsShift)
                    SendInput(1, new[] { new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wScan = shiftScan, dwFlags = KEYEVENTF_SCANCODE } } } }, InputSize);

                SendInput(1, new[] { new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE } } } }, InputSize);
                Thread.Sleep(8);
                SendInput(1, new[] { new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP } } } }, InputSize);

                if (needsShift)
                    SendInput(1, new[] { new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wScan = shiftScan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP } } } }, InputSize);

                Thread.Sleep(15);
            }
        }

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        /// <summary>
        /// Sends the "//swords" chat command: opens Roblox's chat box (pressing "/"
        /// opens chat and inputs the first "/"), types the remaining "/swords", then
        /// presses Enter to submit it.
        /// </summary>
        public static void SendSwordsCommand()
        {
            SendKeyPress(VK_SLASH);   // opens chat box with "/" already typed
            Thread.Sleep(150);        // give the chat box time to open and gain focus
            TypeText("/swords");      // completes "//swords"
            Thread.Sleep(80);
            SendKeyPress(VK_RETURN);  // submit
        }
    }
}
