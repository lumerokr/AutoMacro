using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mouse;
            [FieldOffset(0)] public KeyInput keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint type; public InputUnion data; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

        internal static Input[] MakeInputs(int mode, Keys key)
        {
            Input[] inputs = new Input[2];
            if (mode < 3)
            {
                uint down = mode == 0 ? 0x0002u : mode == 1 ? 0x0008u : 0x0020u;
                inputs[0].data.mouse.flags = down;
                inputs[1].data.mouse.flags = down << 1;
            }
            else
            {
                uint extended = IsExtended(key) ? 1u : 0u;
                for (int i = 0; i < 2; i++)
                {
                    inputs[i].type = 1;
                    inputs[i].data.keyboard.key = (ushort)key;
                    inputs[i].data.keyboard.flags = extended | (i == 1 ? 2u : 0u);
                }
            }
            return inputs;
        }
        internal static bool IsExtended(Keys key)
        {
            return key == Keys.RControlKey || key == Keys.RMenu || key == Keys.Insert || key == Keys.Delete ||
                key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown ||
                key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
                key == Keys.NumLock || key == Keys.Divide || key == Keys.PrintScreen ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.Apps;
        }
    }
}
