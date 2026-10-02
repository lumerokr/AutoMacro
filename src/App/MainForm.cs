using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class MainForm : Form
    {
        readonly Panel viewport = new Panel();
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
        readonly System.Windows.Forms.Timer preferenceSave = new System.Windows.Forms.Timer { Interval = 300 };
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
        bool resourcesDisposed;
        readonly string settingsPath = AppInfo.SettingsPath;
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
            macroWorkspace.ReservedKeys = delegate { return new Keys[] { toggleKey, stopKey, macroKey }; };
            Controls.Add(macroWorkspace); macroWorkspace.Visible = false;
            macroWorkspace.StateChanged += delegate
            {
                languageButton.Enabled = !running && !macroWorkspace.Busy;
                if (macroPage) { badge.Text = macroWorkspace.StateText; badge.ForeColor = macroWorkspace.Busy ? Theme.Accent : Theme.Muted; }
            };
            LoadPreferences(); settingsReady = true; UpdatePinnedHint();
            mode.SelectedIndexChanged += delegate { UpdateControls(); SavePreferences(); };
            preferenceSave.Tick += delegate { SavePreferences(); };
            interval.ValueChanged += delegate { QueuePreferenceSave(); };
            refresh.Interval = 100; refresh.Tick += delegate
            {
                FinishClickerStop();
                if (closePending && !running && !macroWorkspace.Busy) { closePending = false; Close(); return; }
                if (pendingPage.HasValue && !running && !macroWorkspace.Busy)
                { bool page = pendingPage.Value; pendingPage = null; SelectPage(page); }
                counter.Text = L.T("완료한 입력  ") + Interlocked.Read(ref count).ToString("N0") + L.T("회");
                if (running && !stopping) status.Text = L.T("실행 중 · ") + InputRules.KeyName(toggleKey) + L.T(" 또는 ") + InputRules.KeyName(stopKey) + L.T("로 정지");
            };
            refresh.Start(); UpdateControls();
            Control[] content = new Control[Controls.Count]; Controls.CopyTo(content, 0); Controls.Clear();
            viewport.Dock = DockStyle.Fill; viewport.AutoScroll = true; viewport.AutoScrollMinSize = new Size(0, 878); viewport.BackColor = Theme.Background;
            viewport.Controls.AddRange(content); Controls.Add(viewport);
            viewport.Paint += PaintContent; viewport.Scroll += delegate { viewport.Invalidate(); };
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
                Preferences defaults = Preferences.ResetDefaults(macroWorkspace.SavedRunKeys());
                if (persistence) SettingsStore.Reset(settingsPath, defaults);
                bool ready = settingsReady; settingsReady = false;
                try
                {
                    mode.SelectedIndex = defaults.Mode; interval.Value = defaults.Interval;
                    holdWindow.Checked = false; macroKey = defaults.Macro; toggleKey = defaults.Toggle; stopKey = defaults.Stop;
                    macroWorkspace.ResetViewSettings();
                    ApplyLanguage(L.WindowsDefault());
                }
                finally { settingsReady = ready; }
                status.Text = L.T("설정을 초기화했습니다. 안내는 다음 실행에 다시 표시됩니다.");
                if (defaults.Macro != Keys.Space || defaults.Toggle != Keys.F6 || defaults.Stop != Keys.F8)
                    status.Text += "\n" + L.T("매크로 실행 단축키와 겹치지 않도록 Clicker 키를 조정했습니다.");
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
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true;
                preferenceSave.Dispose(); refresh.Dispose(); UnregisterKeys();
                if (macroWorkspace != null) macroWorkspace.Deactivate();
                holdTip.Dispose();
                if (worker != null) { worker.Dispose(); worker = null; }
            }
            base.Dispose(disposing);
        }
        void SetupDisplay(TextBox box, int x, int y, int width)
        {
            box.SetBounds(x, y, width, 30); box.ReadOnly = true; box.TabStop = false;
            box.BackColor = Theme.Field; box.ForeColor = Theme.Ink; box.BorderStyle = BorderStyle.FixedSingle;
        }
        void PaintContent(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState saved = e.Graphics.Save();
            e.Graphics.TranslateTransform(viewport.AutoScrollPosition.X, viewport.AutoScrollPosition.Y);
            e.Graphics.ScaleTransform(DeviceDpi / 96f, DeviceDpi / 96f);
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
            e.Graphics.Restore(saved);
        }
        internal static Size ViewportSize(Size desired, Size workingArea, Size border)
        { return new Size(Math.Max(1, Math.Min(desired.Width, workingArea.Width - border.Width)), Math.Max(1, Math.Min(desired.Height, workingArea.Height - border.Height))); }
        internal void FitWorkArea(Rectangle area)
        {
            float scale = DeviceDpi / 96f;
            Size desired = new Size((int)(620 * scale), (int)((macroPage ? 828 : 878) * scale));
            viewport.AutoScrollPosition = Point.Empty;
            viewport.AutoScrollMinSize = new Size(0, desired.Height);
            ClientSize = ViewportSize(desired, area.Size, new Size(Width - ClientSize.Width, Height - ClientSize.Height));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            viewport.Invalidate();
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
            FitWorkArea(Screen.FromControl(this).WorkingArea);
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
            preferenceSave.Stop();
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
            if (macroWorkspace != null && macroWorkspace.ValidateClickerKey(key) != null) return macroWorkspace.ValidateClickerKey(key);
            if (target == 0)
                return InputRules.ValidateKeys(toggleKey, stopKey, key);
            if (!InputRules.ValidShortcut(key))
                return L.T("단축키는 Ctrl / Alt / Shift / Windows 키 외의 키 하나를 눌러주세요.");
            return target == 1 ? InputRules.ValidateKeys(key, stopKey, macroKey) : InputRules.ValidateKeys(toggleKey, key, macroKey);
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
            FitWorkArea(Screen.FromControl(this).WorkingArea);
            hotkeysReady = !macroPage && RegisterKeys(toggleKey, stopKey); UpdateControls();
            if (!hotkeysReady) status.Text = L.T("단축키 등록 실패: 다른 키로 설정하세요.");
            base.OnShown(e);
        }
        void UnregisterKeys()
        {
            if (IsHandleCreated) { Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2); }
            if (controlMouseHook != null) { controlMouseHook.Dispose(); controlMouseHook = null; }
            hotkeysReady = false;
        }
        bool RegisterKeys(Keys toggle, Keys emergency)
        {
            bool first = InputRules.IsThumb(toggle) || Native.RegisterHotKey(Handle, 1, 0x4000, (uint)toggle);
            bool second = InputRules.IsThumb(emergency) || Native.RegisterHotKey(Handle, 2, 0x4000, (uint)emergency);
            bool mouseReady = true;
            if (first && second && (InputRules.IsThumb(toggle) || InputRules.IsThumb(emergency)))
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
                status.Text = target == 0 ? L.T("매크로 키 설정됨: ") + InputRules.KeyName(macroKey) : L.T("설정됨 · 시작·중지: ") + InputRules.KeyName(toggleKey) + L.T(" / 긴급 정지: ") + InputRules.KeyName(stopKey);
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
            keyDisplay.Text = InputRules.KeyName(macroKey); toggleDisplay.Text = InputRules.KeyName(toggleKey); stopDisplay.Text = InputRules.KeyName(stopKey);
            start.Text = L.T("시작  ") + InputRules.KeyName(toggleKey); stop.Text = L.T("정지  ") + InputRules.KeyName(stopKey);
            keyDisplay.Enabled = pick.Enabled = !running && mode.SelectedIndex == 3;
            start.Enabled = !running && hotkeysReady; stop.Enabled = running;
        }
        void StartMacro()
        {
            if (running || closePending || macroWorkspace.Busy || !hotkeysReady || !Enabled || capturing || macroPage) return;
            string conflict = InputRules.ValidateKeys(toggleKey, stopKey, macroKey);
            if (conflict != null) { status.Text = conflict; return; }
            // Commit and clamp manually typed text before capturing the interval.
            decimal parsed;
            if (decimal.TryParse(interval.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out parsed))
                interval.Value = Math.Max(interval.Minimum, Math.Min(interval.Maximum, parsed));
            int delay = InputRules.ClampInterval((int)interval.Value);
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
            status.Text = L.T("실행 중 · ") + InputRules.KeyName(toggleKey) + L.T(" 또는 ") + InputRules.KeyName(stopKey) + L.T("로 정지");
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
        void QueuePreferenceSave()
        {
            if (!persistence || !settingsReady) return;
            preferenceSave.Stop(); preferenceSave.Start();
        }
        internal void ShowUpdateNotes(ReleaseUpdate cached)
        {
            if (IsDisposed || Disposing) return;
            capturing = true; holdTip.Dismiss();
            if (!macroPage) UnregisterKeys();
            try { using (UpdateNotesDialog dialog = new UpdateNotesDialog(cached)) dialog.ShowDialog(this); }
            finally
            {
                capturing = false;
                if (!IsDisposed && !macroPage) hotkeysReady = RegisterKeys(toggleKey, stopKey);
                if (!IsDisposed) UpdateControls();
            }
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
                if (m.WParam.ToInt32() == 2) StopMacro(L.T("정지됨 (") + InputRules.KeyName(stopKey) + ")");
                else if (m.WParam.ToInt32() == 1) { if (running) StopMacro(L.T("정지됨 (") + InputRules.KeyName(toggleKey) + ")"); else StartMacro(); }
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
            SavePreferences(); preferenceSave.Stop();
            StopMacro(L.T("종료됨")); refresh.Stop();
            UnregisterKeys();
            base.OnFormClosed(e);
        }
    }
}
