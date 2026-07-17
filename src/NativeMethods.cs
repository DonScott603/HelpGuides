using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PsrClone
{
    /// <summary>
    /// Win32 interop: global low-level mouse/keyboard hooks, key translation and
    /// window / process helpers used by the recording engine.
    /// </summary>
    internal static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;

        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;      // ALT
        public const int VK_CAPITAL = 0x14;   // CAPS LOCK

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        public const uint GA_ROOT = 2;

        // ---- Synthetic input (used only by the --recordtest verification harness) ----
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        /// <summary>Returns the top-level window title for a given point on screen.</summary>
        public static string GetTopWindowTitle(int x, int y, out uint processId)
        {
            processId = 0;
            try
            {
                POINT p = new POINT { x = x, y = y };
                IntPtr hwnd = WindowFromPoint(p);
                if (hwnd == IntPtr.Zero) return string.Empty;
                IntPtr root = GetAncestor(hwnd, GA_ROOT);
                if (root == IntPtr.Zero) root = hwnd;
                GetWindowThreadProcessId(root, out processId);
                var sb = new StringBuilder(512);
                GetWindowText(root, sb, sb.Capacity);
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        /// <summary>Best-effort translation of a virtual key to a human-readable token.</summary>
        public static string KeyToText(uint vkCode, uint scanCode)
        {
            switch (vkCode)
            {
                case 0x08: return "{Backspace}";
                case 0x09: return "{Tab}";
                case 0x0D: return "{Enter}";
                case 0x1B: return "{Esc}";
                case 0x20: return " ";
                case 0x2E: return "{Delete}";
                case 0x2D: return "{Insert}";
                case 0x24: return "{Home}";
                case 0x23: return "{End}";
                case 0x21: return "{PageUp}";
                case 0x22: return "{PageDown}";
                case 0x25: return "{Left}";
                case 0x26: return "{Up}";
                case 0x27: return "{Right}";
                case 0x28: return "{Down}";
                case 0x2C: return "{PrintScreen}";
                case 0x5B:
                case 0x5C: return "{Win}";
                case 0x10:
                case 0x11:
                case 0x12:
                case 0x14: return string.Empty; // modifiers alone
            }
            if (vkCode >= 0x70 && vkCode <= 0x7B) return "{F" + (vkCode - 0x6F) + "}";

            byte[] keyState = new byte[256];
            keyState[VK_SHIFT] = (byte)((GetKeyState(VK_SHIFT) & 0x8000) != 0 ? 0x80 : 0);
            keyState[VK_CONTROL] = (byte)((GetKeyState(VK_CONTROL) & 0x8000) != 0 ? 0x80 : 0);
            keyState[VK_MENU] = (byte)((GetKeyState(VK_MENU) & 0x8000) != 0 ? 0x80 : 0);
            keyState[VK_CAPITAL] = (byte)((GetKeyState(VK_CAPITAL) & 0x0001) != 0 ? 0x01 : 0);

            // If Ctrl or Alt (without AltGr) is held, represent as a shortcut token.
            bool ctrl = keyState[VK_CONTROL] != 0;
            bool alt = keyState[VK_MENU] != 0;

            IntPtr layout = GetKeyboardLayout(0);
            var sb = new StringBuilder(8);
            int rc = ToUnicodeEx(vkCode, scanCode, keyState, sb, sb.Capacity, 0, layout);
            string ch = rc > 0 ? sb.ToString() : string.Empty;

            if (ctrl || alt)
            {
                string mods = (ctrl ? "Ctrl+" : string.Empty) + (alt ? "Alt+" : string.Empty);
                string label = ch;
                if (string.IsNullOrEmpty(label) || char.IsControl(label.Length > 0 ? label[0] : ' '))
                {
                    // fall back to VK letter/number
                    if (vkCode >= 0x30 && vkCode <= 0x5A) label = ((char)vkCode).ToString();
                }
                return "{" + mods + label + "}";
            }

            if (!string.IsNullOrEmpty(ch) && !char.IsControl(ch[0])) return ch;
            return string.Empty;
        }
    }
}
