using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoMacro
{
    // Shared window identification helpers for pinned input and the manual click test.
    internal static class CompatibilityProbe
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
        internal static string Title(IntPtr window)
        {
            StringBuilder text = new StringBuilder(512);
            GetWindowText(window, text, text.Capacity);
            return text.Length == 0 ? L.T("(제목 없는 창)") : text.ToString();
        }
    }
}
