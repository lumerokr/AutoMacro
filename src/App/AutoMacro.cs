using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.IO;

using System.Drawing.Drawing2D;
using System.Collections.Generic;
[assembly: System.Reflection.AssemblyTitle("Auto Macro")]
[assembly: System.Reflection.AssemblyProduct("Auto Macro")]


namespace AutoMacro
{
    internal sealed class StartupNotice : Form
    {
        internal static string Responsibility { get { return L.T("매크로 프로그램 사용의 책임은 온전히 사용자 본인에게 있습니다."); } }
        internal static string Caution { get { return L.T("안티치트, 보안 프로그램 등이 가동중인 곳에서는 사용을 자제해주세요.\n이 프로그램은 어떠한 우회 기능도 제공하지 않습니다."); } }
        readonly string noticePath;
        readonly Label error = new Label();
        internal readonly Button Confirm = new ModernButton();
        internal StartupNotice(string path)
        {
            noticePath = path;
            Text = L.T("Auto Macro · 사용 안내"); ClientSize = new Size(580, 340);
            Font = new Font("맑은 고딕", 10); BackColor = Theme.Background; ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            using (Stream stream = typeof(StartupNotice).Assembly.GetManifestResourceStream("AutoMacro.ico"))
                if (stream != null) using (Icon icon = new Icon(stream, new Size(48, 48))) Icon = (Icon)icon.Clone();
            Label title = new Label { Text = L.T("사용 전 안내"), Font = new Font("맑은 고딕", 19, FontStyle.Bold), AutoSize = true };
            title.Location = new Point(28, 24); Controls.Add(title);
            Label first = new Label { Text = Responsibility }; first.SetBounds(30, 91, 520, 46); Controls.Add(first);
            Label second = new Label { Text = Caution }; second.SetBounds(30, 143, 520, 86); Controls.Add(second);
            error.SetBounds(30, 240, 520, 34); error.ForeColor = Theme.Muted; Controls.Add(error);
            Confirm.Text = L.T("이해했습니다."); Confirm.SetBounds(30, 284, 520, 38); Theme.Button(Confirm, true); Controls.Add(Confirm);
            Confirm.Click += delegate
            {
                try { SaveAcknowledgement(noticePath); DialogResult = DialogResult.OK; Close(); }
                catch (Exception) { error.Text = L.T("확인 여부를 저장하지 못했습니다. 쓰기 가능한 폴더에서 실행해주세요."); }
            };
            AcceptButton = Confirm;
        }
        internal static bool IsAcknowledged(string path)
        {
            try
            {
                if (!DataStore.Exists(path)) return false;
                DataNode root = SettingsStore.Read(path);
                bool accepted = (int?)root.Element("NoticeVersion") == 1 && (bool?)root.Element("Acknowledged") == true;
                
                return accepted;
            }
            catch (Exception) { return false; }
        }
        internal static void SaveAcknowledgement(string path)
        {
            SettingsStore.Update(path, new DataNode("NoticeVersion", 1), new DataNode("Acknowledged", true));
        }
        internal static bool AllowStartup(string path)
        {
            if (IsAcknowledged(path)) return true;
            using (StartupNotice notice = new StartupNotice(path)) return notice.ShowDialog() == DialogResult.OK;
        }
    }
    internal sealed class Preferences
    {
        internal int Mode = 0, Interval = 100;
        internal bool HoldWindow;
        internal Keys Macro = Keys.Space, Toggle = Keys.F6, Stop = Keys.F8;
        internal static Preferences Load(string path)
        {
            if (!DataStore.Exists(path)) return new Preferences();
            DataNode root = DataStore.Load(path);
            if (root.Name != "AutoMacro" || (int?)root.Attribute("version") != 1) throw new FormatException("Unsupported preferences");
            Preferences value = new Preferences();
            value.HoldWindow = (bool?)root.Element("HoldWindow") ?? false;
            value.Mode = (int)root.Element("Mode"); value.Interval = MainForm.ClampInterval((int)root.Element("Interval"));
            value.Macro = (Keys)(int)root.Element("Macro"); value.Toggle = (Keys)(int)root.Element("Toggle"); value.Stop = (Keys)(int)root.Element("Stop");
            if (value.Mode < 0 || value.Mode > 3 || (int)value.Macro < 8 || (int)value.Macro > 254 ||
                !ValidControl(value.Toggle) || !ValidControl(value.Stop) || MainForm.ValidateKeys(value.Toggle, value.Stop, value.Macro) != null)
                throw new FormatException("Invalid preferences");
            
            return value;
        }
        static bool ValidControl(Keys key)
        {
            Keys n = MainForm.NormalizeKey(key);
            return MainForm.IsThumb(key) || ((int)key >= 8 && (int)key <= 254 && n != Keys.ControlKey && n != Keys.ShiftKey && n != Keys.Menu && key != Keys.LWin && key != Keys.RWin);
        }
        internal void Save(string path)
        {
            SettingsStore.Update(path, new DataNode("Mode", Mode), new DataNode("Interval", Interval),
                new DataNode("Macro", (int)Macro), new DataNode("Toggle", (int)Toggle), new DataNode("Stop", (int)Stop), new DataNode("HoldWindow", HoldWindow));
        }
    }
    internal static class Theme
    {
        internal static readonly Color Background = Color.FromArgb(15, 19, 28), Card = Color.FromArgb(24, 30, 42),
            Field = Color.FromArgb(34, 42, 57), Ink = Color.FromArgb(234, 241, 251), Muted = Color.FromArgb(147, 162, 184),
            Accent = Color.FromArgb(103, 232, 202);
        internal static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void Button(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand;
            button.BackColor = primary ? Accent : Field; button.ForeColor = primary ? Background : Ink;
            button.Font = new Font("맑은 고딕", 10, FontStyle.Bold);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(146, 248, 222) : Color.FromArgb(49, 62, 81);
        }
    }
    internal sealed class ModernButton : Button
    {
        bool hover;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = !Enabled ? Theme.Field : hover ? FlatAppearance.MouseOverBackColor : BackColor;
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Theme.Accent, fill);
        }
    }
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

    internal sealed class KeyPicker : Form
    {
        internal Keys SelectedKey;
        readonly Func<Keys, string> validate;
        readonly Label hint = new Label();
        bool accepted;
        readonly bool allowMouse;
        ThumbHook mouseHook;
        internal KeyPicker(string purpose, Func<Keys, string> validateKey, bool mouse = false)
        {
            allowMouse = mouse;
            validate = validateKey;
            Text = purpose; ClientSize = new Size(470, 145); Font = new Font("맑은 고딕", 10);
            BackColor = Theme.Card; ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            hint.Text = (allowMouse ? L.T("키보드 키 또는 마우스 4·5번 버튼을 눌렀다가 떼세요.") : L.T("지정할 키를 한 번 눌렀다가 떼세요.")) + L.T("\n다른 기능에 지정된 키는 사용할 수 없습니다.\n취소하려면 창의 X 버튼을 누르세요.");
            hint.SetBounds(20, 20, 430, 110); Controls.Add(hint);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!allowMouse) return;
            mouseHook = new ThumbHook(delegate(Keys key, bool down)
            {
                if (down && !accepted) AcceptKey(key);
                else if (!down && accepted && SelectedKey == key)
                    BeginInvoke((Action)delegate { DialogResult = DialogResult.OK; Close(); });
                return true;
            });
            if (!mouseHook.Install()) hint.Text = L.T("마우스 버튼 감지 등록에 실패했습니다.\n창을 닫고 다시 시도하거나 키보드 키를 지정하세요.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && mouseHook != null) { mouseHook.Dispose(); mouseHook = null; }
            base.Dispose(disposing);
        }
        void AcceptKey(Keys key)
        {
            string error = validate(key);
            if (error != null) { hint.Text = error + L.T("\n다른 키를 누르세요. 취소: 창의 X 버튼"); return; }
            SelectedKey = key; accepted = true; hint.Text = MainForm.KeyName(key) + L.T(" 버튼/키를 떼면 설정됩니다.");
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (accepted) return true;
            AcceptKey(key); return true;
        }
        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == 0x0101 || m.Msg == 0x0105) && accepted && (Keys)m.WParam.ToInt32() == SelectedKey)
            { DialogResult = DialogResult.OK; Close(); return; }
            base.WndProc(ref m);
        }
        internal bool VerifyCaptureRelease()
        {
            Message down = Message.Create(Handle, 0x0100, (IntPtr)(int)Keys.F6, IntPtr.Zero);
            ProcessCmdKey(ref down, Keys.F6);
            if (accepted) return false;
            down = Message.Create(Handle, 0x0100, (IntPtr)(int)Keys.A, IntPtr.Zero);
            ProcessCmdKey(ref down, Keys.A);
            if (!accepted || SelectedKey != Keys.A || DialogResult != DialogResult.None) return false;
            Message up = Message.Create(Handle, 0x0101, (IntPtr)(int)Keys.A, IntPtr.Zero);
            WndProc(ref up);
            return DialogResult == DialogResult.OK;
        }
    }

    internal sealed class MainForm : Form
    {
        readonly CheckBox holdWindow = new CheckBox();
        readonly Button languageButton = new SettingsButton();
        readonly Button testCompatibility = new ModernButton();
        readonly Label pinnedWindow = new Label();
        readonly HelpIcon holdHelp = new HelpIcon();
        readonly HelpBubble holdTip = new HelpBubble(L.T("해당 창의 해당 위치에서만 클리커가 작동하도록 하는 기능입니다. 창을 최소화 할 경우 작동되지 않습니다.\n\n클릭이 발생하지 않는 경우 해당 창에서는 사용할 수 없는 기능이므로 비활성화 후 사용해주세요\n\n입력 종류에 따라 작동 여부가 달라질수 있습니다.\nex) 왼쪽 클릭 작동 O, 키보드 입력 작동 X"));
        internal Func<ClickTarget> CaptureClickTarget = ClickTarget.Capture;
        internal Func<Native.Input[], uint> SystemInput = delegate(Native.Input[] items) { return Native.SendInput((uint)items.Length, items, Marshal.SizeOf(typeof(Native.Input))); };
        readonly ComboBox mode = new ComboBox();
        readonly TextBox keyDisplay = new TextBox();
        readonly Button pick = new ModernButton();
        readonly NumericUpDown interval = new NumericUpDown();
        readonly Button start = new ModernButton();
        readonly Button stop = new ModernButton();
        readonly Label status = new Label();
        readonly Label counter = new Label();
        readonly TextBox toggleDisplay = new TextBox();
        readonly TextBox stopDisplay = new TextBox();
        readonly Button pickToggle = new ModernButton();
        readonly Button pickStop = new ModernButton();
        Keys toggleKey = Keys.F6, stopKey = Keys.F8, macroKey = Keys.Space;
        bool capturing;
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        bool stopping, closePending;
        bool? pendingPage;
        string stopMessage;
        InputJob worker;
        bool running, hotkeysReady;
        long count;
        ThumbHook controlMouseHook;
        readonly Label saveStatus = new Label(), badge = new Label();
        readonly Button clickerMenu = new ModernButton(), macroMenu = new ModernButton();
        readonly List<Control> clickerControls = new List<Control>();
        readonly MacroWorkspace macroWorkspace;
        bool macroPage;
        readonly bool persistence;
        bool settingsReady;
        readonly string settingsPath = AppInfo.SettingsPath;
        internal static bool IsThumb(Keys key) { return key == Keys.XButton1 || key == Keys.XButton2; }
        internal static string KeyName(Keys key)
        { return key == Keys.XButton1 ? L.T("마우스 4") : key == Keys.XButton2 ? L.T("마우스 5") : key.ToString(); }

        internal static int ClampInterval(int value) { return Math.Max(10, Math.Min(3600000, value)); }
        internal static Keys NormalizeKey(Keys key)
        {
            if (key == Keys.LControlKey || key == Keys.RControlKey) return Keys.ControlKey;
            if (key == Keys.LShiftKey || key == Keys.RShiftKey) return Keys.ShiftKey;
            if (key == Keys.LMenu || key == Keys.RMenu) return Keys.Menu;
            return key & Keys.KeyCode;
        }
        internal static string ValidateKeys(Keys toggle, Keys emergency, Keys macro)
        {
            if (NormalizeKey(toggle) == NormalizeKey(emergency)) return L.T("시작·중지와 긴급 정지 단축키는 달라야 합니다.");
            if (NormalizeKey(macro) == NormalizeKey(toggle) || NormalizeKey(macro) == NormalizeKey(emergency))
                return L.T("매크로 키와 제어 단축키는 중복될 수 없습니다.");
            return null;
        }
        internal MainForm() : this(true) { }
        internal MainForm(bool savePreferences)
        {
            persistence = savePreferences;
            Text = "Auto Macro"; ClientSize = new Size(620, 878);
            Font = new Font("맑은 고딕", 10); BackColor = Theme.Background; ForeColor = Theme.Ink;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("AutoMacro.ico"))
                if (stream != null) using (Icon icon = new Icon(stream, new Size(48, 48))) Icon = (Icon)icon.Clone();
            PictureBox mark = new PictureBox { Location = new Point(28, 26), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Image = Icon.ToBitmap() };
            Controls.Add(mark);
            AddLabel("Auto Macro", 90, 26, 24, true);
            badge.SetBounds(470, 38, 120, 29); badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.BackColor = Theme.Field; badge.ForeColor = Theme.Accent; Controls.Add(badge);

            AddLabel(L.T("반복 입력"), 48, 119, 11, true);
            AddLabel(L.T("입력 종류"), 48, 165, 10, false);
            mode.SetBounds(190, 161, 380, 30); mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.FlatStyle = FlatStyle.Flat; mode.BackColor = Theme.Field; mode.ForeColor = Theme.Ink;
            mode.DrawMode = DrawMode.OwnerDrawFixed; mode.ItemHeight = 24;
            mode.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                using (Brush brush = new SolidBrush(Theme.Field)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, mode.Items[e.Index].ToString(), mode.Font,
                    e.Bounds, Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            mode.Items.AddRange(new object[] { L.T("마우스 왼쪽 클릭"), L.T("마우스 오른쪽 클릭"), L.T("마우스 휠 클릭 (가운데 버튼)"), L.T("키보드 키") });
            mode.SelectedIndex = 0; Controls.Add(mode);
            AddLabel(L.T("키보드 키"), 48, 215, 10, false);
            SetupDisplay(keyDisplay, 190, 211, 266); Controls.Add(keyDisplay);
            pick.Text = L.T("설정"); pick.SetBounds(470, 207, 100, 36); Controls.Add(pick);
            pick.Click += delegate { CaptureKey(0); };
            AddLabel(L.T("입력 간격"), 48, 265, 10, false);
            interval.SetBounds(190, 261, 180, 30); interval.Minimum = 10; interval.Maximum = 3600000;
            interval.Value = 100; interval.Increment = 10; interval.ThousandsSeparator = true;
            interval.BackColor = Theme.Field; interval.ForeColor = Theme.Ink; interval.BorderStyle = BorderStyle.FixedSingle; Controls.Add(interval);
            AddLabel(L.T("ms  /  최소 10ms"), 389, 265, 10, false);

            AddLabel(L.T("실행 제어"), 48, 339, 11, true);
            AddLabel(L.T("시작 · 중지"), 48, 385, 10, false);
            AddLabel(L.T("긴급 정지"), 48, 435, 10, false);
            SetupDisplay(toggleDisplay, 190, 381, 266); SetupDisplay(stopDisplay, 190, 431, 266);
            Controls.Add(toggleDisplay); Controls.Add(stopDisplay);
            pickToggle.Text = pickStop.Text = L.T("설정");
            pickToggle.SetBounds(470, 377, 100, 36); pickStop.SetBounds(470, 427, 100, 36);
            Controls.Add(pickToggle); Controls.Add(pickStop);
            pickToggle.Click += delegate { CaptureKey(1); }; pickStop.Click += delegate { CaptureKey(2); };
            start.SetBounds(28, 506, 348, 52); stop.SetBounds(390, 506, 202, 52); stop.Enabled = false;
            Controls.Add(start); Controls.Add(stop);
            foreach (Button button in new Button[] { pick, pickToggle, pickStop, start, stop }) Theme.Button(button, button == start);
            start.Click += delegate { StartMacro(); }; stop.Click += delegate { StopMacro(L.T("정지됨")); };
            status.SetBounds(30, 574, 562, 40); status.Text = L.T("대상 창에서 시작 단축키를 누르세요.");
            status.ForeColor = Theme.Muted; Controls.Add(status);
            counter.SetBounds(30, 628, 290, 24); counter.ForeColor = Theme.Muted; counter.Text = L.T("완료한 입력  0회"); Controls.Add(counter);
            saveStatus.SetBounds(332, 628, 260, 24); saveStatus.TextAlign = ContentAlignment.TopRight;
            saveStatus.Font = new Font("맑은 고딕", 9); saveStatus.ForeColor = Theme.Muted; saveStatus.Text = L.T("설정 자동 저장"); Controls.Add(saveStatus);
            foreach (Control control in Controls)
            {
                if (control.Top >= 100) clickerControls.Add(control);
                control.Top += 48;
            }
            foreach (Control control in new Control[] { start, stop, status, counter, saveStatus }) control.Top += 160;
            AddLabel(L.T("실험적 기능"), 48, 558, 11, true);
            clickerControls.Add(Controls[Controls.Count - 1]);
            holdWindow.Text = L.T("해당 창에서 유지"); holdWindow.SetBounds(48, 589, 220, 26);
            holdWindow.BackColor = Theme.Card; holdWindow.ForeColor = Theme.Ink;
            holdWindow.AutoSize = true;
            holdHelp.SetBounds(200, 591, 22, 22);
            holdHelp.AccessibleName = L.T("해당 창에서 유지 도움말");
            holdHelp.MouseEnter += delegate { holdTip.ShowAbove(holdHelp); };
            holdHelp.MouseLeave += delegate { holdTip.Dismiss(); };
            Deactivate += delegate { holdTip.Dismiss(); };
            LocationChanged += delegate { holdTip.Dismiss(); };
            holdWindow.SizeChanged += delegate { holdHelp.Location = new Point(holdWindow.Right + 6, holdWindow.Top + (holdWindow.Height - holdHelp.Height) / 2); };
            testCompatibility.Text = L.T("호환 테스트"); testCompatibility.SetBounds(440, 586, 124, 28); Theme.Button(testCompatibility, false);
            testCompatibility.Click += delegate { TestCompatibility(); };
            pinnedWindow.SetBounds(48, 624, 516, 56); pinnedWindow.AutoEllipsis = true;
            pinnedWindow.BackColor = Theme.Card; pinnedWindow.ForeColor = Theme.Muted;
            foreach (Control control in new Control[] { holdWindow, holdHelp, testCompatibility, pinnedWindow })
            { Controls.Add(control); clickerControls.Add(control); }
            holdHelp.Location = new Point(holdWindow.Right + 6, holdWindow.Top + (holdWindow.Height - holdHelp.Height) / 2);
            holdWindow.CheckedChanged += delegate { UpdatePinnedHint(); SavePreferences(); };
            clickerMenu.Text = "Clicker"; macroMenu.Text = "Macro";
            clickerMenu.SetBounds(28, 8, 110, 34); macroMenu.SetBounds(146, 8, 110, 34);
            Theme.Button(clickerMenu, false); Theme.Button(macroMenu, false);
            clickerMenu.ForeColor = Theme.Accent;
            Controls.Add(clickerMenu); Controls.Add(macroMenu);
            languageButton.SetBounds(558, 8, 34, 34); languageButton.Cursor = Cursors.Hand;
            languageButton.AccessibleName = L.T("설정"); Controls.Add(languageButton);
            languageButton.Click += delegate { ChooseLanguage(); };
            FitLanguageLayout();
            clickerMenu.Click += delegate { SelectPage(false); };
            macroMenu.Click += delegate { SelectPage(true); };
            macroWorkspace = new MacroWorkspace(persistence); macroWorkspace.Location = new Point(28, 148);
            Controls.Add(macroWorkspace); macroWorkspace.Visible = false;
            macroWorkspace.StateChanged += delegate
            {
                languageButton.Enabled = !running && !macroWorkspace.Busy;
                if (macroPage) { badge.Text = macroWorkspace.StateText; badge.ForeColor = macroWorkspace.Busy ? Theme.Accent : Theme.Muted; }
            };
            LoadPreferences(); settingsReady = true; UpdatePinnedHint();
            mode.SelectedIndexChanged += delegate { UpdateControls(); SavePreferences(); };
            interval.ValueChanged += delegate { SavePreferences(); };
            refresh.Interval = 100; refresh.Tick += delegate
            {
                FinishClickerStop();
                if (closePending && !running && !macroWorkspace.Busy) { closePending = false; Close(); return; }
                if (pendingPage.HasValue && !running && !macroWorkspace.Busy)
                { bool page = pendingPage.Value; pendingPage = null; SelectPage(page); }
                counter.Text = L.T("완료한 입력  ") + Interlocked.Read(ref count).ToString("N0") + L.T("회");
                if (running && !stopping) status.Text = L.T("실행 중 · ") + KeyName(toggleKey) + L.T(" 또는 ") + KeyName(stopKey) + L.T("로 정지");
            };
            refresh.Start(); UpdateControls();
        }
        void ChooseLanguage()
        {
            if (running || macroWorkspace.Busy || capturing) return;
            capturing = true; holdTip.Dismiss();
            if (!macroPage) UnregisterKeys();
            PreparedUpdate prepared = null;
            try
            {
                using (SettingsDialog dialog = new SettingsDialog(ResetSettings, macroWorkspace.RestoreBackupDialog))
                {
                    DialogResult choice = dialog.ShowDialog(this);
                    if (choice == DialogResult.Abort) prepared = dialog.Prepared;
                    if (choice == DialogResult.OK)
                    {
                        try
                        {
                            if (persistence) L.Save(settingsPath, dialog.SelectedLanguage);
                            ApplyLanguage(dialog.SelectedLanguage);
                        }
                        catch (Exception) { status.Text = L.T("언어 설정을 저장하지 못했습니다. 설정 초기화로 복구하거나 폴더 권한을 확인하세요."); }
                    }
                }
            }
            finally
            {
                capturing = false;
                if (!macroPage) hotkeysReady = RegisterKeys(toggleKey, stopKey);
                UpdateControls();
            }
            CompleteUpdate(prepared);
        }
        internal bool CanNotifyUpdate { get { return !running && !macroWorkspace.Busy && !capturing; } }
        internal void ShowStartupUpdate(ReleaseUpdate release)
        {
            if (!StartupUpdateCheck.IsNewer(release)) return;
            capturing = true; holdTip.Dismiss();
            if (!macroPage) UnregisterKeys();
            PreparedUpdate prepared = null;
            try
            {
                using (UpdateDialog dialog = new UpdateDialog(release, true))
                    if (dialog.ShowDialog(this) == DialogResult.OK) prepared = dialog.Prepared;
            }
            finally
            {
                capturing = false;
                if (!macroPage) hotkeysReady = RegisterKeys(toggleKey, stopKey);
                UpdateControls();
            }
            CompleteUpdate(prepared);
        }
        void CompleteUpdate(PreparedUpdate prepared)
        {
            if (prepared != null)
            {
                UpdateInstaller.Pending = prepared;
                Close();
                if (!IsDisposed) { UpdateInstaller.Pending = null; prepared.Discard(); }
            }
        }
        void ResetSettings()
        {
            if (L.Confirm(this, L.T("클리커 설정·언어·안내 확인을 초기화할까요? 현재 설정은 백업하고 저장된 매크로는 유지합니다."), false) != DialogResult.Yes) return;
            try
            {
                if (persistence) SettingsStore.Reset(settingsPath);
                bool ready = settingsReady; settingsReady = false;
                try
                {
                    Preferences defaults = new Preferences(); mode.SelectedIndex = defaults.Mode; interval.Value = defaults.Interval;
                    holdWindow.Checked = false; macroKey = defaults.Macro; toggleKey = defaults.Toggle; stopKey = defaults.Stop;
                    ApplyLanguage(L.WindowsDefault());
                }
                finally { settingsReady = ready; }
                status.Text = L.T("설정을 초기화했습니다. 안내는 다음 실행에 다시 표시됩니다.");
            }
            catch (Exception) { status.Text = L.T("복구하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요."); }
        }
        internal void ApplyLanguage(string language)
        {
            string previous = L.Current; L.Current = L.Supported(language);
            SuspendLayout();
            try
            {
                L.Apply(this, previous); holdTip.Message = L.Convert(holdTip.Message, previous);
                languageButton.AccessibleName = L.T("설정");
                macroWorkspace.LanguageChanged(previous);
                FitLanguageLayout();
                UpdatePinnedHint(); UpdateControls(); Invalidate(true);
            }
            finally { ResumeLayout(true); }
        }
        void FitLanguageLayout()
        {
            int width = Math.Max(124, TextRenderer.MeasureText(testCompatibility.Text, testCompatibility.Font).Width + 20);
            testCompatibility.SetBounds(564 - width, 586, width, 28);
        }
        void TestCompatibility()
        {
            if (running || capturing || macroPage) return;
            capturing = true; UnregisterKeys();
            try { using (ClickCompatibilityDialog dialog = new ClickCompatibilityDialog(mode.SelectedIndex)) dialog.ShowDialog(this); }
            finally
            {
                capturing = false; hotkeysReady = RegisterKeys(toggleKey, stopKey);
                UpdateControls(); UpdatePinnedHint();
                if (!hotkeysReady) status.Text = L.T("단축키 등록 실패: 다른 키로 설정하세요.");
            }
        }
        void UpdatePinnedHint()
        {
            if (!running) pinnedWindow.Text = holdWindow.Checked
                ? L.T("대상 위치에 마우스를 놓고 시작 단축키를 누르세요.\n시작할 때의 창과 위치를 고정합니다.")
                : L.T("꺼짐 · 기존 클리커 방식으로 입력합니다.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { holdTip.Dispose(); if (worker != null) worker.Dispose(); }
            base.Dispose(disposing);
        }
        void SetupDisplay(TextBox box, int x, int y, int width)
        {
            box.SetBounds(x, y, width, 30); box.ReadOnly = true; box.TabStop = false;
            box.BackColor = Theme.Field; box.ForeColor = Theme.Ink; box.BorderStyle = BorderStyle.FixedSingle;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle[] cards = macroPage ? new Rectangle[0] :
                new Rectangle[] { new Rectangle(28, 148, 564, 210), new Rectangle(28, 368, 564, 166), new Rectangle(28, 544, 564, 154) };
            foreach (Rectangle rect in cards)
                using (GraphicsPath p = Theme.Round(rect, 16)) using (Brush brush = new SolidBrush(Theme.Card)) e.Graphics.FillPath(brush, p);
            using (Pen pen = new Pen(Theme.Field))
            {
                e.Graphics.DrawLine(pen, 28, 47, 592, 47);
                if (!macroPage) e.Graphics.DrawLine(pen, 30, 826, 592, 826);
            }
            using (Pen pen = new Pen(Theme.Accent, 3)) e.Graphics.DrawLine(pen, macroPage ? 146 : 28, 45, macroPage ? 256 : 138, 45);
        }
        void SelectPage(bool showMacro)
        {
            if (macroPage == showMacro || capturing) return;
            if (running || macroWorkspace.Busy)
            {
                pendingPage = showMacro;
                StopMacro(L.T("정지됨")); macroWorkspace.RequestStop();
                return;
            }
            holdTip.Dismiss();
            if (macroPage) macroWorkspace.Deactivate();
            StopMacro(L.T("정지됨"));
            if (IsHandleCreated) UnregisterKeys();
            macroPage = showMacro;
            foreach (Control control in clickerControls) control.Visible = !macroPage;
            macroWorkspace.Visible = macroPage;
            ClientSize = new Size(620, macroPage ? 794 : 878);
            if (macroPage && Visible) macroWorkspace.Activate(Handle);
            clickerMenu.ForeColor = macroPage ? Theme.Muted : Theme.Accent;
            macroMenu.ForeColor = macroPage ? Theme.Accent : Theme.Muted;
            if (!macroPage && Visible)
            {
                hotkeysReady = RegisterKeys(toggleKey, stopKey);
                if (!hotkeysReady) status.Text = L.T("단축키 등록 실패: 다른 키로 설정하세요.");
            }
            UpdateControls(); UpdatePinnedHint(); Invalidate();
        }
        void AddLabel(string text, int x, int y, int size, bool bold)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent,
                ForeColor = bold ? Theme.Ink : Theme.Muted, Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular) });
        }
        void LoadPreferences()
        {
            if (!persistence) return;
            try
            {
                Preferences p = Preferences.Load(settingsPath);
                mode.SelectedIndex = p.Mode; interval.Value = p.Interval;
                holdWindow.Checked = p.HoldWindow;
                macroKey = p.Macro; toggleKey = p.Toggle; stopKey = p.Stop;
                if (File.Exists(settingsPath)) saveStatus.Text = L.T("저장된 설정 불러옴");
            }
            catch (Exception) { saveStatus.Text = L.T("설정 읽기 실패 · 기본값 사용"); }
        }
        void SavePreferences()
        {
            if (!persistence || !settingsReady) return;
            try
            {
                new Preferences { Mode = mode.SelectedIndex, Interval = (int)interval.Value, Macro = macroKey, Toggle = toggleKey, Stop = stopKey, HoldWindow = holdWindow.Checked }.Save(settingsPath);
                saveStatus.Text = L.T("설정 저장됨");
            }
            catch (Exception) { saveStatus.Text = L.T("저장 실패 · 폴더 권한 확인"); }
        }
        internal bool VerifyConflictGuards()
        {
            if (ValidateCapture(0, Keys.F6) == null || ValidateCapture(0, Keys.F8) == null) return false;
            if (ValidateCapture(1, Keys.Space) == null || ValidateCapture(2, Keys.Space) == null) return false;
            if (ValidateCapture(1, Keys.F8) == null || ValidateCapture(2, Keys.F6) == null) return false;
            if (ValidateCapture(1, Keys.ControlKey) == null) return false;
            if (ValidateCapture(1, Keys.XButton1) != null || ValidateCapture(2, Keys.XButton2) != null) return false;
            toggleKey = Keys.XButton1;
            if (ValidateCapture(2, Keys.XButton1) == null || ValidateCapture(2, Keys.XButton2) != null) return false;
            toggleKey = Keys.F6;
            ApplyCapturedKey(1, Keys.Space);
            if (toggleKey != Keys.F6) return false;
            ApplyCapturedKey(1, Keys.F8);
            if (toggleKey != Keys.F6 || stopKey != Keys.F8) return false;
            toggleKey = Keys.F9; stopKey = Keys.F10;
            return ValidateCapture(0, Keys.F6) == null && ValidateCapture(0, Keys.F9) != null;
        }
        string ValidateCapture(int target, Keys key)
        {
            if (key == Keys.None) return L.T("키를 다시 눌러주세요.");
            if (target == 0)
                return ValidateKeys(toggleKey, stopKey, key);
            Keys normalized = NormalizeKey(key);
            if (normalized == Keys.ControlKey || normalized == Keys.ShiftKey || normalized == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return L.T("단축키는 Ctrl / Alt / Shift / Windows 키 외의 키 하나를 눌러주세요.");
            return target == 1 ? ValidateKeys(key, stopKey, macroKey) : ValidateKeys(toggleKey, key, macroKey);
        }
        void CaptureKey(int target)
        {
            if (running || capturing) return;
            capturing = true; UnregisterKeys();
            Keys? selected = null;
            try
            {
                string title = target == 0 ? L.T("매크로 키 지정") : target == 1 ? L.T("시작·중지 키 지정") : L.T("긴급 정지 키 지정");
                using (KeyPicker dialog = new KeyPicker(title, delegate(Keys key) { return ValidateCapture(target, key); }, target != 0))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        selected = dialog.SelectedKey;
                    }
                }
            }
            finally
            {
                capturing = false;
                if (selected.HasValue) ApplyCapturedKey(target, selected.Value);
                else
                {
                    hotkeysReady = RegisterKeys(toggleKey, stopKey); UpdateControls();
                    if (!hotkeysReady) status.Text = L.T("단축키 등록 실패: 다른 키로 설정하세요.");
                }
            }
        }
        protected override void OnShown(EventArgs e)
        {
            hotkeysReady = !macroPage && RegisterKeys(toggleKey, stopKey); UpdateControls();
            if (!hotkeysReady) status.Text = L.T("단축키 등록 실패: 다른 키로 설정하세요.");
            base.OnShown(e);
        }
        void UnregisterKeys()
        {
            Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2);
            if (controlMouseHook != null) { controlMouseHook.Dispose(); controlMouseHook = null; }
            hotkeysReady = false;
        }
        bool RegisterKeys(Keys toggle, Keys emergency)
        {
            bool first = IsThumb(toggle) || Native.RegisterHotKey(Handle, 1, 0x4000, (uint)toggle);
            bool second = IsThumb(emergency) || Native.RegisterHotKey(Handle, 2, 0x4000, (uint)emergency);
            bool mouseReady = true;
            if (first && second && (IsThumb(toggle) || IsThumb(emergency)))
            {
                controlMouseHook = new ThumbHook(delegate(Keys key, bool down)
                {
                    if (!hotkeysReady || capturing || (key != toggle && key != emergency)) return false;
                    if (!down) Native.PostMessage(Handle, 0x8001, (IntPtr)(key == toggle ? 1 : 2), (IntPtr)(int)key);
                    return true;
                });
                mouseReady = controlMouseHook.Install();
            }
            if (first && second && mouseReady) return true;
            UnregisterKeys(); return false;
        }
        void ApplyCapturedKey(int target, Keys key)
        {
            if (running || capturing) return;
            Keys nextToggle = target == 1 ? key : toggleKey, nextStop = target == 2 ? key : stopKey;
            string error = ValidateCapture(target, key);
            if (error != null) { status.Text = error; return; }
            UnregisterKeys();
            if (RegisterKeys(nextToggle, nextStop))
            {
                toggleKey = nextToggle; stopKey = nextStop; hotkeysReady = true;
                if (target == 0) macroKey = key;
                status.Text = target == 0 ? L.T("매크로 키 설정됨: ") + KeyName(macroKey) : L.T("설정됨 · 시작·중지: ") + KeyName(toggleKey) + L.T(" / 긴급 정지: ") + KeyName(stopKey);
                SavePreferences();
            }
            else
            {
                hotkeysReady = RegisterKeys(toggleKey, stopKey);
                status.Text = hotkeysReady ? L.T("등록 실패: 다른 앱이 사용하거나 지원하지 않는 키입니다. 기존 설정을 유지합니다.") : L.T("단축키 등록 실패: 다른 키로 설정해야 시작할 수 있습니다.");
            }
            UpdateControls();
        }
        void UpdateControls()
        {
            languageButton.Enabled = !running && !capturing && (macroWorkspace == null || !macroWorkspace.Busy);
            testCompatibility.Enabled = holdWindow.Enabled = !running && !capturing;
            mode.Enabled = !running; interval.Enabled = !running;
            pickToggle.Enabled = pickStop.Enabled = !running;
            badge.Text = macroPage ? macroWorkspace.StateText : stopping ? L.T("정지 중…") : running ? L.T("●  실행 중") : L.T("●  대기 중");
            badge.ForeColor = running ? Theme.Accent : Theme.Muted;
            keyDisplay.Text = KeyName(macroKey); toggleDisplay.Text = KeyName(toggleKey); stopDisplay.Text = KeyName(stopKey);
            start.Text = L.T("시작  ") + KeyName(toggleKey); stop.Text = L.T("정지  ") + KeyName(stopKey);
            keyDisplay.Enabled = pick.Enabled = !running && mode.SelectedIndex == 3;
            start.Enabled = !running && hotkeysReady; stop.Enabled = running;
        }
        void StartMacro()
        {
            if (running || closePending || macroWorkspace.Busy || !hotkeysReady || !Enabled || capturing || macroPage) return;
            string conflict = ValidateKeys(toggleKey, stopKey, macroKey);
            if (conflict != null) { status.Text = conflict; return; }
            // Commit and clamp manually typed text before capturing the interval.
            decimal parsed;
            if (decimal.TryParse(interval.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out parsed))
                interval.Value = Math.Max(interval.Minimum, Math.Min(interval.Maximum, parsed));
            int delay = ClampInterval((int)interval.Value);
            int selectedMode = mode.SelectedIndex; Keys selectedKey = macroKey;
            ClickTarget target = null;
            if (holdWindow.Checked)
            {
                try { target = CaptureClickTarget(); }
                catch (Exception error) { status.Text = error.Message + L.T(" 대상 위치에서 시작 단축키를 누르세요."); return; }
                pinnedWindow.Text = L.T("고정된 창: ") + target.Name + L.T("\n위치: ") + target.Position.X + ", " + target.Position.Y;
            }
            Native.Input[] inputs = Native.MakeInputs(selectedMode, selectedKey);
            Interlocked.Exchange(ref count, 0); running = true; stopping = false;
            UpdateControls();
            status.Text = L.T("실행 중 · ") + KeyName(toggleKey) + L.T(" 또는 ") + KeyName(stopKey) + L.T("로 정지");
            worker = new InputJob(delegate(WaitHandle cancel)
            {
                while (!cancel.WaitOne(0))
                {
                    if (target != null)
                    {
                        try { target.Repeat(selectedMode, selectedKey); }
                        catch (Exception error)
                        {
                            string message = L.T("고정 입력 중지: ") + error.Message;
                            if (!cancel.WaitOne(0)) Interlocked.Exchange(ref stopMessage, message);
                            return;
                        }
                    }
                    else
                    {
                        uint sent = SystemInput(inputs);
                        if (sent != 2)
                        {
                            // Release a possible partially injected press before stopping.
                            SystemInput(new Native.Input[] { inputs[1] });
                            if (!cancel.WaitOne(0)) Interlocked.Exchange(ref stopMessage, L.T("입력 실패: 대상 앱의 권한 또는 입력 허용 여부를 확인하세요."));
                            return;
                        }
                    }
                    Interlocked.Increment(ref count);
                    if (cancel.WaitOne(delay)) return;
                }
            });
        }
        void StopMacro(string message)
        {
            if (worker != null)
            {
                if (!stopping) { stopping = true; stopMessage = message; }
                worker.Stop(); status.Text = L.T("정지 중…"); UpdateControls();
                FinishClickerStop(); return;
            }
            running = false; UpdateControls(); status.Text = message;
            counter.Text = L.T("완료한 입력: ") + Interlocked.Read(ref count).ToString("N0") + L.T("회");
        }
        void FinishClickerStop()
        {
            if (worker == null || !worker.Completed) return;
            if (worker.Error != null) stopMessage = L.T("입력 실패: 대상 앱의 권한 또는 입력 허용 여부를 확인하세요.");
            worker.Dispose(); worker = null; running = false; stopping = false;
            UpdateControls(); status.Text = stopMessage ?? L.T("정지됨"); stopMessage = null;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x8002 && macroPage && macroWorkspace != null)
            { macroWorkspace.HandleCommand(m.WParam.ToInt32(), (Keys)m.LParam.ToInt32()); return; }
            bool mouseControl = m.Msg == 0x8001 && (Keys)m.LParam.ToInt32() == (m.WParam.ToInt32() == 1 ? toggleKey : stopKey);
            if ((m.Msg == 0x0312 || mouseControl) && !capturing && hotkeysReady)
            {
                if (m.WParam.ToInt32() == 2) StopMacro(L.T("정지됨 (") + KeyName(stopKey) + ")");
                else if (m.WParam.ToInt32() == 1) { if (running) StopMacro(L.T("정지됨 (") + KeyName(toggleKey) + ")"); else StartMacro(); }
            }
            base.WndProc(ref m);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopMacro(L.T("종료됨")); macroWorkspace.RequestStop();
            if (running || macroWorkspace.Busy) { closePending = true; e.Cancel = true; return; }
            if (!macroWorkspace.PrepareClose()) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            macroWorkspace.Deactivate();
            decimal parsed;
            if (decimal.TryParse(interval.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out parsed))
                interval.Value = Math.Max(interval.Minimum, Math.Min(interval.Maximum, parsed));
            SavePreferences();
            StopMacro(L.T("종료됨")); refresh.Stop(); refresh.Dispose();
            UnregisterKeys();
            base.OnFormClosed(e);
        }
    }

    internal static class Program
    {
        internal static void AfterMainShown(Form form, Action ready)
        {
            form.Shown += delegate
            {
                form.BeginInvoke((Action)delegate
                {
                    if (form.IsDisposed || form.Disposing || !form.Visible) return;
                    try { ready(); } catch { form.Close(); }
                });
            };
        }
        static bool PrepareStartup(string updateFolder)
        {
            if (updateFolder != null) UpdateInstaller.SetAwaitingUser(updateFolder, true);
            try
            {
                AppInfo.InitializeDataDirectory();
                if (!SettingsRecoveryDialog.AllowStartup(AppInfo.SettingsPath)) return false;
                L.Initialize(AppInfo.SettingsPath);
                return StartupNotice.AllowStartup(AppInfo.SettingsPath);
            }
            finally { if (updateFolder != null) UpdateInstaller.SetAwaitingUser(updateFolder, false); }
        }
        static void RunMain(MainForm form)
        {
            // Attach only after the usage notice has been accepted.
            using (StartupUpdateCheck updates = new StartupUpdateCheck(form, delegate { return form.CanNotifyUpdate; }, form.ShowStartupUpdate))
                Application.Run(form);
        }
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--apply-update") return UpdateInstaller.Run(args);
            bool created;
            L.Initialize(AppInfo.SettingsPath);
            using (Mutex mutex = new Mutex(true, "Local\\SimpleAutoMacroApp", out created))
            {
                if (!created) { MessageBox.Show(L.T("Auto Macro가 이미 실행 중입니다."), "Auto Macro"); return 0; }
                if (args.Length == 2 && args[0] == "--update-ready")
                {
                    try { if (!PrepareStartup(args[1])) return 0; } catch { return 1; }
                    // Confirm only after OnShown initialization and the first UI message turn.
                    using (MainForm updated = new MainForm())
                    {
                        AfterMainShown(updated, delegate { UpdateInstaller.SignalReady(args[1]); });
                        RunMain(updated);
                    }
                }
                else
                {
                    UpdateInstaller.CleanCompleted();
                    try { if (!PrepareStartup(null)) return 0; }
                    catch { MessageBox.Show(L.T(UpdateService.Storage) + "\n\n" + AppInfo.DataDirectory, "Auto Macro", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                    RunMain(new MainForm());
                }
            }
            UpdateInstaller.StartPending();
            return 0;
        }
    }
}





