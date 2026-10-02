using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class MacroWindows
    {
        delegate bool EnumWindowProc(IntPtr window, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        internal static bool TryOrigin(IntPtr window, out Point origin)
        { origin = Point.Empty; return CompatibilityProbe.IsWindow(window) && !CompatibilityProbe.IsIconic(window) && ClientToScreen(window, ref origin); }
        internal static Size ClientSize(IntPtr window)
        { Rect rect; if (!GetClientRect(window, out rect)) throw new InvalidOperationException(L.T("대상 창을 사용할 수 없어 재생을 중단했습니다.")); return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top); }
        internal static IntPtr RecordingWindow(MacroAction action)
        {
            IntPtr window = action.Kind == ActionKind.KeyDown || action.Kind == ActionKind.KeyUp ? CompatibilityProbe.GetForegroundWindow() : WindowFromPoint(new Point(action.X, action.Y));
            window = GetAncestor(window, 2); uint process;
            CompatibilityProbe.GetWindowThreadProcessId(window, out process);
            return process == (uint)Process.GetCurrentProcess().Id ? IntPtr.Zero : window;
        }
        internal static bool Activate(IntPtr window)
        { if (CompatibilityProbe.GetForegroundWindow() == window) return true; SetForegroundWindow(window); return CompatibilityProbe.GetForegroundWindow() == window; }
        internal sealed class Choice
        {
            internal IntPtr Handle;
            internal string Title;
            public override string ToString() { return Title; }
        }
        internal static List<Choice> Choices()
        {
            List<Choice> result = new List<Choice>(); uint own = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr window, IntPtr data)
            {
                uint process; CompatibilityProbe.GetWindowThreadProcessId(window, out process);
                if (process != own && IsWindowVisible(window) && !CompatibilityProbe.IsIconic(window))
                { Rect rect; if (GetClientRect(window, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top) result.Add(new Choice { Handle = window, Title = CompatibilityProbe.Title(window) }); }
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(Choice a, Choice b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title); });
            return result;
        }
    }
    internal sealed class MacroTarget
    {
        readonly Func<bool> available;
        readonly Func<Point> origin;
        readonly Func<Size> size;
        internal MacroTarget(Func<bool> available, Func<Point> origin, Func<Size> size)
        { this.available = available; this.origin = origin; this.size = size; }
        internal static MacroTarget ForWindow(IntPtr window)
        {
            uint process; uint thread = CompatibilityProbe.GetWindowThreadProcessId(window, out process);
            return new MacroTarget(delegate
            {
                uint current; uint currentThread = CompatibilityProbe.GetWindowThreadProcessId(window, out current);
                return CompatibilityProbe.IsWindow(window) && !CompatibilityProbe.IsIconic(window) && current == process && currentThread == thread && CompatibilityProbe.GetForegroundWindow() == window;
            }, delegate { Point point; if (!MacroWindows.TryOrigin(window, out point)) throw new InvalidOperationException(L.T("대상 창을 사용할 수 없어 재생을 중단했습니다.")); return point; }, delegate { return MacroWindows.ClientSize(window); });
        }
        internal MacroAction Resolve(SavedMacro macro, MacroAction action)
        {
            CheckAvailable();
            if (action.Kind == ActionKind.KeyDown || action.Kind == ActionKind.KeyUp) return action;
            long x = action.HasClientPosition ? action.ClientX : (long)action.X - macro.RecordedOriginX;
            long y = action.HasClientPosition ? action.ClientY : (long)action.Y - macro.RecordedOriginY;
            Size bounds = size();
            if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height) throw new InvalidOperationException(L.T("입력 위치가 대상 창 밖이어서 재생을 중단했습니다."));
            Point start = origin(); long screenX = start.X + x, screenY = start.Y + y;
            if (screenX < Int32.MinValue || screenX > Int32.MaxValue || screenY < Int32.MinValue || screenY > Int32.MaxValue) throw new InvalidOperationException();
            MacroAction resolved = action.Copy(); resolved.X = (int)screenX; resolved.Y = (int)screenY; return resolved;
        }
        internal void CheckAvailable()
        { if (!available()) throw new InvalidOperationException(L.T("대상 창이 닫히거나 최소화되었거나 다른 창으로 전환되어 재생을 중단했습니다.")); }
    }
    internal sealed class MacroWindowPicker : Form
    {
        internal IntPtr SelectedWindow;
        readonly ListBox windows = new ListBox();
        readonly Label hint = new Label();
        internal MacroWindowPicker()
        {
            Text = L.T("대상 창 선택"); ClientSize = new Size(480, 330); BackColor = Theme.Card; ForeColor = Theme.Ink; Font = new Font("맑은 고딕", 10);
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            windows.SetBounds(20, 20, 440, 188); windows.BackColor = Theme.Field; windows.ForeColor = Theme.Ink; windows.BorderStyle = BorderStyle.None; Controls.Add(windows);
            hint.Text = L.T("창 내부 기준 재생은 대상 창을 활성화합니다. 백그라운드 입력 기능이 아닙니다."); hint.SetBounds(20, 220, 440, 46); Controls.Add(hint);
            Button reload = new ModernButton { Text = L.T("새로 고침") }, ok = new ModernButton { Text = L.T("선택") }, cancel = new ModernButton { Text = L.T("취소"), DialogResult = DialogResult.Cancel };
            reload.SetBounds(20, 280, 120, 34); ok.SetBounds(242, 280, 100, 34); cancel.SetBounds(356, 280, 104, 34); Theme.Button(reload, false); Theme.Button(ok, true); Theme.Button(cancel, false);
            Controls.Add(reload); Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
            reload.Click += delegate { Reload(); }; Shown += delegate { Reload(); };
            ok.Click += delegate
            {
                MacroWindows.Choice selected = windows.SelectedItem as MacroWindows.Choice; Point point;
                if (selected == null || !MacroWindows.TryOrigin(selected.Handle, out point)) { hint.Text = L.T("사용할 수 있는 대상 창을 선택하세요."); return; }
                SelectedWindow = selected.Handle; DialogResult = DialogResult.OK; Close();
            };
        }
        void Reload()
        {
            windows.Items.Clear();
            try { foreach (MacroWindows.Choice choice in MacroWindows.Choices()) windows.Items.Add(choice); }
            catch { hint.Text = L.T("사용할 수 있는 대상 창을 선택하세요."); }
            if (windows.Items.Count > 0) windows.SelectedIndex = 0;
        }
    }
}
