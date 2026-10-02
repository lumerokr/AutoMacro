using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoMacro
{
    // Kept alive for the entire hook lifetime; callbacks run on the UI message loop.
    internal sealed class ThumbHook : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct MouseData
        { public int x, y; public uint mouseData, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        readonly HookProc callback;
        readonly Func<Keys, bool, bool> handler;
        IntPtr hook;
        internal ThumbHook(Func<Keys, bool, bool> action) { handler = action; callback = Callback; }
        internal bool Install() { hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); return hook != IntPtr.Zero; }
        internal static Keys Decode(uint data)
        { uint button = data >> 16; return button == 1 ? Keys.XButton1 : button == 2 ? Keys.XButton2 : Keys.None; }
        IntPtr Callback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && (message.ToInt32() == 0x020B || message.ToInt32() == 0x020C))
            {
                MouseData input = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                Keys key = Decode(input.mouseData);
                if ((input.flags & 1) == 0 && key != Keys.None && handler(key, message.ToInt32() == 0x020B)) return (IntPtr)1;
            }
            return CallNextHookEx(hook, code, message, data);
        }
        public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    }
}
