using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class ClickTarget
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        internal IntPtr Window, Root;
        internal uint Process, Thread;
        internal Point Position;
        internal string Name;
        internal static ClickTarget Capture()
        {
            Point point;
            if (!GetCursorPos(out point)) throw new InvalidOperationException(L.T("마우스 위치를 읽을 수 없습니다."));
            IntPtr window = WindowFromPoint(point); uint process;
            uint thread = CompatibilityProbe.GetWindowThreadProcessId(window, out process);
            if (window == IntPtr.Zero || process == 0 || process == (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                throw new InvalidOperationException(L.T("Auto Macro 이외의 대상 창 위에 마우스를 놓으세요."));
            IntPtr root = GetAncestor(window, 2);
            if (!ScreenToClient(window, ref point)) throw new InvalidOperationException(L.T("창 내부 위치를 읽을 수 없습니다."));
            ClickTarget target = new ClickTarget { Window = window, Root = root, Position = point, Process = process, Thread = thread, Name = CompatibilityProbe.Title(root) };
            target.Validate(); return target;
        }
        internal void Validate()
        {
            uint process; uint thread = CompatibilityProbe.GetWindowThreadProcessId(Window, out process);
            if (!CompatibilityProbe.IsWindow(Window) || !CompatibilityProbe.IsWindow(Root) || process != Process || thread != Thread || GetAncestor(Window, 2) != Root)
                throw new InvalidOperationException(L.T("대상 창이 변경되거나 종료되었습니다. 다시 지정하세요."));
            Rect rect;
            // Minimized windows may report a zero client size; use the captured coordinates.
            if (CompatibilityProbe.IsIconic(Root)) return;
            if (!GetClientRect(Window, out rect) || Position.X < 0 || Position.Y < 0 || Position.X >= rect.Right || Position.Y >= rect.Bottom || Position.X > 32767 || Position.Y > 32767)
                throw new InvalidOperationException(L.T("지정 위치가 창 내부를 벗어났습니다. 다시 지정하세요."));
        }
        internal static IntPtr Coordinates(int x, int y) { return new IntPtr(unchecked((int)(((uint)(ushort)y << 16) | (ushort)x))); }
        internal static void SendPair(int button, int x, int y, Func<uint, UIntPtr, IntPtr, bool> send)
        {
            uint down = button == 0 ? 0x201u : button == 1 ? 0x204u : 0x207u;
            UIntPtr flag = new UIntPtr(button == 0 ? 1u : button == 1 ? 2u : 16u);
            IntPtr coordinates = Coordinates(x, y);
            if (!send(down, flag, coordinates)) throw new InvalidOperationException(L.T("클릭 누름 전송 실패: 권한 또는 대상 창을 확인하세요."));
            if (!send(down + 1, UIntPtr.Zero, coordinates))
            {
                send(down + 1, UIntPtr.Zero, coordinates);
                throw new InvalidOperationException(L.T("클릭 해제 전송 실패: 대상 창 상태를 확인하세요."));
            }
        }
        internal void Repeat(int mode, Keys key)
        {
            Validate();
            if (mode < 3)
                SendPair(mode, Position.X, Position.Y, delegate(uint message, UIntPtr flag, IntPtr coordinates) { return PostMessage(Window, message, flag, coordinates); });
            else
            {
                uint data = 1u | ((MapVirtualKey((uint)key, 0) & 255u) << 16) | (Native.IsExtended(key) ? 1u << 24 : 0u);
                if (!PostMessage(Window, 0x100, new UIntPtr((uint)key), new IntPtr(unchecked((int)data))))
                    throw new InvalidOperationException(L.T("키 누름 전송 실패: 권한 또는 대상 창을 확인하세요."));
                if (!PostMessage(Window, 0x101, new UIntPtr((uint)key), new IntPtr(unchecked((int)(data | 0xC0000000u)))))
                {
                    PostMessage(Window, 0x101, new UIntPtr((uint)key), new IntPtr(unchecked((int)(data | 0xC0000000u))));
                    throw new InvalidOperationException(L.T("키 해제 전송 실패: 대상 창을 확인하세요."));
                }
            }
        }
        internal void Click(int button, bool minimized)
        {
            Validate();
            if (CompatibilityProbe.IsIconic(Root) != minimized)
                throw new InvalidOperationException(minimized ? L.T("대상 창을 최소화한 뒤 다시 테스트하세요.") : L.T("대상 창의 최소화를 해제한 뒤 다시 테스트하세요."));
            if (GetAncestor(CompatibilityProbe.GetForegroundWindow(), 2) == Root)
                throw new InvalidOperationException(L.T("다른 창을 활성화한 뒤 다시 테스트하세요."));
            SendPair(button, Position.X, Position.Y, delegate(uint message, UIntPtr flag, IntPtr coordinates) { return PostMessage(Window, message, flag, coordinates); });
        }
    }

    internal sealed class ClickCompatibilityDialog : Form
    {
        readonly Label targetLabel = new Label(), state = new Label(), results = new Label();
        readonly Button choose = new ModernButton(), test = new ModernButton(), cancel = new ModernButton(), yes = new ModernButton(), no = new ModernButton();
        readonly ComboBox button = new ComboBox(), condition = new ComboBox();
        readonly Timer timer = new Timer();
        ClickTarget target;
        int seconds, operation, sentCondition;
        readonly string[] verdict = { L.T("미확인"), L.T("미확인") };
        internal ClickCompatibilityDialog(int selectedButton)
        {
            Text = L.T("클릭 호환성 테스트"); ClientSize = new Size(564, 440);
            Font = new Font("맑은 고딕", 10); BackColor = Theme.Background; ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Label hint = new Label { Text = L.T("1. 대상 지정 후 3초 안에 클릭할 위치에 마우스를 놓으세요.\n2. 테스트를 누르면 3초 뒤 그 위치에 클릭을 한 번 보냅니다.\n누르면 문제가 없는 위치를 선택하고 실제 반응을 확인하세요.") };
            hint.SetBounds(24, 20, 516, 74); Controls.Add(hint);
            choose.Text = L.T("대상 · 위치 지정 (3초)"); choose.SetBounds(24, 104, 516, 34);
            targetLabel.Text = L.T("지정된 창 없음"); targetLabel.SetBounds(24, 148, 516, 44); targetLabel.AutoEllipsis = true; Controls.Add(targetLabel);
            button.Items.AddRange(new object[] { L.T("왼쪽 클릭"), L.T("오른쪽 클릭"), L.T("휠 클릭") });
            condition.Items.AddRange(new object[] { L.T("백그라운드 상태"), L.T("최소화 상태") });
            button.SetBounds(24, 200, 246, 28); condition.SetBounds(286, 200, 254, 28);
            foreach (ComboBox box in new ComboBox[] { button, condition })
            {
                box.DropDownStyle = ComboBoxStyle.DropDownList; box.BackColor = Theme.Field; box.ForeColor = Theme.Ink;
                box.FlatStyle = FlatStyle.Flat; box.DrawMode = DrawMode.OwnerDrawFixed; box.ItemHeight = 24;
                box.DrawItem += delegate(object sender, DrawItemEventArgs e)
                {
                    ComboBox combo = (ComboBox)sender;
                    using (Brush brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(49, 62, 81) : Theme.Field)) e.Graphics.FillRectangle(brush, e.Bounds);
                    if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, combo.Items[e.Index].ToString(), combo.Font, e.Bounds, Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                };
                Controls.Add(box);
            }
            button.SelectedIndex = selectedButton < 3 ? selectedButton : 0; condition.SelectedIndex = 0;
            test.Text = L.T("테스트 클릭 (3초 뒤 1회)"); test.SetBounds(24, 242, 346, 36);
            cancel.Text = L.T("예약 취소"); cancel.SetBounds(382, 242, 158, 36);
            state.SetBounds(24, 288, 516, 44); state.Text = L.T("대상 창을 지정하세요."); Controls.Add(state);
            yes.Text = L.T("반응 있었음"); yes.SetBounds(24, 338, 246, 34);
            no.Text = L.T("반응 없었음"); no.SetBounds(286, 338, 254, 34);
            foreach (Button item in new Button[] { choose, test, cancel, yes, no }) { Theme.Button(item, item == test); Controls.Add(item); }
            results.SetBounds(24, 386, 516, 44); results.ForeColor = Theme.Muted; Controls.Add(results);
            choose.Click += delegate { Begin(1); }; test.Click += delegate { Begin(2); };
            cancel.Click += delegate { timer.Stop(); operation = 0; state.Text = L.T("예약이 취소되었습니다."); UpdateButtons(); };
            yes.Click += delegate { Confirm(true); }; no.Click += delegate { Confirm(false); };
            button.SelectedIndexChanged += delegate { ResetResults(); }; condition.SelectedIndexChanged += delegate { yes.Enabled = no.Enabled = false; };
            timer.Interval = 1000; timer.Tick += delegate { Tick(); };
            ResetResults(); UpdateButtons();
        }
        void ResetResults() { verdict[0] = verdict[1] = L.T("미확인"); yes.Enabled = no.Enabled = false; ShowResults(); }
        void ShowResults() { results.Text = L.T("이번 대상 · 선택한 클릭 종류의 사용자 확인 결과\n백그라운드: ") + verdict[0] + L.T("  /  최소화: ") + verdict[1]; }
        void Confirm(bool worked)
        {
            verdict[sentCondition] = worked ? L.T("작동 확인") : L.T("반응 없음"); yes.Enabled = no.Enabled = false; ShowResults();
            state.Text = L.T("결과를 기록했습니다. 다른 상태도 따로 테스트할 수 있습니다.");
        }
        void Begin(int next)
        {
            if (next == 2) { verdict[condition.SelectedIndex] = L.T("미확인"); ShowResults(); }
            operation = next; seconds = 3; yes.Enabled = no.Enabled = false; UpdateButtons(); Countdown(); timer.Start();
        }
        void Countdown() { state.Text = seconds + L.T("초 뒤 ") + (operation == 1 ? L.T("대상 지정 · 원하는 위치에 마우스를 놓으세요.") : L.T("1회 클릭 · 다른 창을 활성화하거나 대상을 최소화하세요.")); }
        void Tick()
        {
            if (--seconds > 0) { Countdown(); return; }
            timer.Stop(); int action = operation; operation = 0;
            try
            {
                if (action == 1)
                {
                    target = null; ResetResults(); target = ClickTarget.Capture();
                    targetLabel.Text = target.Name + L.T("\n클릭 위치 (대상 컨트롤 기준): ") + target.Position.X + ", " + target.Position.Y;
                    state.Text = L.T("대상 지정 완료. 창 크기와 내부 배치를 유지하고 테스트하세요.");
                }
                else
                {
                    target.Click(button.SelectedIndex, condition.SelectedIndex == 1); sentCondition = condition.SelectedIndex;
                    verdict[sentCondition] = L.T("미확인"); ShowResults();
                    state.Text = L.T("클릭 메시지 전송 완료. 실제 반응을 확인하고 아래에서 선택하세요.");
                    yes.Enabled = no.Enabled = true;
                }
            }
            catch (Exception error) { state.Text = error.Message; if (target == null) targetLabel.Text = L.T("지정된 창 없음"); }
            UpdateButtons();
        }
        void UpdateButtons()
        {
            choose.Enabled = button.Enabled = condition.Enabled = operation == 0;
            test.Enabled = operation == 0 && target != null; cancel.Enabled = operation != 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
