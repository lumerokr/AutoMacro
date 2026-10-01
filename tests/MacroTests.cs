using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;


namespace AutoMacro
{
    internal static class MacroTests
    {
        static void Check(bool condition, string name) { if (!condition) throw new Exception("Macro test failed: " + name); }
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Private).SetValue(owner, value); }
        static object Invoke(object owner, string name, params object[] values) { return owner.GetType().GetMethod(name, Private).Invoke(owner, values); }
        static void VerifyHoldWindow()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-hold-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new Preferences { HoldWindow = true }.Save(path);
                Check(Preferences.Load(path).HoldWindow, "hold preference roundtrip");
                DataNode legacy = DataStore.Load(path); legacy.Element("HoldWindow").Remove();
                legacy.Add(new DataNode("LiveCompatibility", true)); DataStore.Save(path, legacy, false);
                Check(!Preferences.Load(path).HoldWindow, "old probe does not enable fixed input");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            using (MainForm form = new MainForm(false))
            {
                form.Show(); Application.DoEvents();
                CheckBox toggle = Field<CheckBox>(form, "holdWindow");
                Check(!toggle.Checked, "hold initially off");
                toggle.Checked = true;
                Check(Field<Label>(form, "pinnedWindow").Text.Contains("고정"), "hold hint");
                Set(form, "running", true); Invoke(form, "UpdateControls");
                Check(!toggle.Enabled, "running settings locked");
                Set(form, "running", false); Invoke(form, "UpdateControls");
                Invoke(form, "SelectPage", true); Check(!toggle.Visible, "hold only in clicker");
                Invoke(form, "SelectPage", false); Check(toggle.Visible && toggle.Checked, "hold survives page switch");
                toggle.Checked = false;
                Check(Field<Label>(form, "pinnedWindow").Text.Contains("기존"), "normal mode restored");
                Check(Field<HelpBubble>(form, "holdTip").Message.Contains("비활성화 후 사용해주세요"), "requested help text");
                form.Close();
            }
        }
        sealed class ClickReceiver : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
            internal readonly AutoResetEvent Received = new AutoResetEvent(false);
            internal int DownCount, UpCount;
            internal int KeyDownCount, KeyUpCount;
            internal IntPtr LastPosition;
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x100) { Interlocked.Increment(ref KeyDownCount); return; }
                if (message.Msg == 0x101) { Interlocked.Increment(ref KeyUpCount); Received.Set(); return; }
                if (message.Msg == 0x201 || message.Msg == 0x204 || message.Msg == 0x207)
                { Interlocked.Increment(ref DownCount); LastPosition = message.LParam; return; }
                if (message.Msg == 0x202 || message.Msg == 0x205 || message.Msg == 0x208)
                { Interlocked.Increment(ref UpCount); LastPosition = message.LParam; Received.Set(); return; }
                base.WndProc(ref message);
            }
        }
        static void VerifyNativeClick()
        {
            using (ManualResetEvent ready = new ManualResetEvent(false))
            {
                ClickReceiver receiver = null; IntPtr handle = IntPtr.Zero;
                Thread thread = new Thread(delegate()
                {
                    using (receiver = new ClickReceiver { ClientSize = new Size(200, 120), ShowInTaskbar = false })
                    {
                        handle = receiver.Handle; ready.Set(); Application.Run();
                    }
                });
                thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
                Check(ready.WaitOne(3000), "native click receiver ready");
                ClickTarget target = null;
                try
                {
                    uint process; uint targetThread = CompatibilityProbe.GetWindowThreadProcessId(handle, out process);
                    target = new ClickTarget { Window = handle, Root = handle, Process = process, Thread = targetThread, Position = new ClickTarget.Point { X = 23, Y = 45 } };
                    for (int button = 0; button < 3; button++)
                    {
                        target.Click(button, false);
                        Check(receiver.Received.WaitOne(1500), "native click pair delivered");
                        Check(receiver.DownCount == button + 1 && receiver.UpCount == button + 1 && receiver.LastPosition == ClickTarget.Coordinates(23, 45), "native message count and coordinates");
                    }
                    bool blocked = false;
                    try { target.Click(0, true); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked && receiver.DownCount == 3, "minimized test rejects restored window without click");
                    target.Position.X = 300; blocked = false;
                    try { target.Click(0, false); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked && receiver.DownCount == 3, "out of bounds rejected without click");
                    target.Position.X = 23;
                    using (MainForm form = new MainForm(false))
                    {
                        int globalInputs = 0, captures = 0;
                        form.CaptureClickTarget = delegate { captures++; return target; };
                        form.SystemInput = delegate(Native.Input[] inputs) { Interlocked.Increment(ref globalInputs); return (uint)inputs.Length; };
                        form.Show(); Application.DoEvents(); Set(form, "hotkeysReady", true);
                        Field<CheckBox>(form, "holdWindow").Checked = true;
                        Field<NumericUpDown>(form, "interval").Value = 10;
                        Invoke(form, "StartMacro");
                        for (int i = 0; i < 3; i++) Check(receiver.Received.WaitOne(1500), "fixed worker repeats");
                        Invoke(form, "StopMacro", "검증 중지");
                        Check(captures == 1 && globalInputs == 0, "target captured once with no global input fallback");
                        Check(receiver.LastPosition == ClickTarget.Coordinates(23, 45), "repeat coordinates remain fixed");
                        Field<CheckBox>(form, "holdWindow").Checked = false;
                        Invoke(form, "StartMacro"); Thread.Sleep(50); Invoke(form, "StopMacro", "검증 중지");
                        Check(globalInputs > 0 && captures == 1, "off restores original input path");
                        form.Close();
                    }
                    // Drain any already delivered notification before checking a new pair.
                    while (receiver.Received.WaitOne(0)) { }
                    receiver.Invoke((Action)delegate { receiver.ShowInTaskbar = false; receiver.WindowState = FormWindowState.Minimized; receiver.Show(); });
                    Check(CompatibilityProbe.IsIconic(handle), "test target minimized");
                    target.Repeat(0, Keys.None);
                    Check(receiver.Received.WaitOne(1500), "fixed click still delivered while minimized");
                    target.Repeat(3, Keys.A);
                    Check(receiver.Received.WaitOne(1500) && receiver.KeyDownCount == 1 && receiver.KeyUpCount == 1, "fixed keyboard messages remain targeted");
                }
                finally
                {
                    receiver.BeginInvoke((Action)delegate { Application.ExitThread(); });
                    Check(thread.Join(3000), "native receiver terminated"); receiver.Received.Dispose();
                }
                bool closed = false; try { target.Validate(); } catch (InvalidOperationException) { closed = true; }
                Check(closed, "destroyed click target rejected");
            }
        }
        static void VerifyClickTest()
        {
            VerifyNativeClick();
            for (int button = 0; button < 3; button++)
            {
                List<uint> messages = new List<uint>(); List<ulong> flags = new List<ulong>();
                ClickTarget.SendPair(button, 123, 234, delegate(uint message, UIntPtr flag, IntPtr position)
                {
                    messages.Add(message); flags.Add(flag.ToUInt64());
                    Check(position.ToInt32() == ((234 << 16) | 123), "click client coordinates"); return true;
                });
                uint down = button == 0 ? 0x201u : button == 1 ? 0x204u : 0x207u;
                Check(messages.Count == 2 && messages[0] == down && messages[1] == down + 1, "single click down/up pair");
                Check(flags[0] == (button == 0 ? 1ul : button == 1 ? 2ul : 16ul) && flags[1] == 0, "click button flags");
            }
            int calls = 0; bool failed = false;
            try { ClickTarget.SendPair(0, 0, 0, delegate { calls++; return false; }); } catch (InvalidOperationException) { failed = true; }
            Check(failed && calls == 1, "failed down does not report success");
            calls = 0; failed = false;
            try { ClickTarget.SendPair(0, 0, 0, delegate { calls++; return calls == 1; }); } catch (InvalidOperationException) { failed = true; }
            Check(failed && calls == 3, "failed release retried and reported");
            using (ClickCompatibilityDialog dialog = new ClickCompatibilityDialog(2))
            {
                dialog.Show(); Application.DoEvents();
                Check(Field<ComboBox>(dialog, "button").SelectedIndex == 2, "selected clicker button carried into test");
                Check(!Field<Button>(dialog, "test").Enabled, "test requires target");
                Invoke(dialog, "Begin", 1);
                Check(Field<System.Windows.Forms.Timer>(dialog, "timer").Enabled, "selection countdown started");
                Field<Button>(dialog, "cancel").PerformClick();
                Check(!Field<System.Windows.Forms.Timer>(dialog, "timer").Enabled && Field<int>(dialog, "operation") == 0, "countdown canceled");
                Set(dialog, "sentCondition", 1); Invoke(dialog, "Confirm", true);
                string[] verdict = Field<string[]>(dialog, "verdict");
                Check(verdict[0] == "미확인" && verdict[1] == "작동 확인", "minimized verdict isolated");
                Field<ComboBox>(dialog, "button").SelectedIndex = 0;
                Check(verdict[0] == "미확인" && verdict[1] == "미확인", "changing click type resets verdicts");
                Invoke(dialog, "Begin", 1); dialog.Close();
            }
        }
        static void VerifyHelpHover()
        {
            Exception uiError = null;
            ThreadExceptionEventHandler handler = delegate(object sender, ThreadExceptionEventArgs args) { uiError = args.Exception; };
            Application.ThreadException += handler;
            try
            {
                using (MainForm form = new MainForm(false))
                {
                    form.Show(); Application.DoEvents();
                    HelpIcon icon = Field<HelpIcon>(form, "holdHelp"); HelpBubble tip = Field<HelpBubble>(form, "holdTip");
                    Check(!icon.CanSelect && !icon.TabStop && icon.Cursor == Cursors.Default, "help icon is decorative without help cursor");
                    Field<Button>(form, "pickToggle").Focus(); Control focused = form.ActiveControl;
                    int clicks = 0; icon.Click += delegate { clicks++; };
                    for (int i = 0; i < 5; i++)
                    {
                        Invoke(icon, "OnMouseEnter", EventArgs.Empty);
                        tip.ShowAbove(icon); tip.ShowAbove(icon);
                        Check(tip.Visible, "repeated help show is safe");
                        Rectangle anchor = icon.RectangleToScreen(icon.ClientRectangle);
                        Check(tip.Bottom < anchor.Top && Math.Abs(tip.Left + tip.Width / 2 - (anchor.Left + anchor.Width / 2)) <= 1, "help remains centered above icon");
                        Native.PostMessage(icon.Handle, 0x201, (IntPtr)1, ClickTarget.Coordinates(11, 11));
                        Native.PostMessage(icon.Handle, 0x202, IntPtr.Zero, ClickTarget.Coordinates(11, 11));
                        Application.DoEvents();
                        Check(form.ActiveControl == focused && clicks == 0 && uiError == null, "hover then click cannot focus or crash");
                        Invoke(icon, "OnMouseLeave", EventArgs.Empty); Check(!tip.Visible, "help hides on leave");
                    }
                    tip.ShowAbove(icon); Invoke(form, "SelectPage", true); Check(!tip.Visible, "page switch hides help");
                    Invoke(form, "SelectPage", false); tip.ShowAbove(icon);
                    form.Close(); tip.ShowAbove(icon); tip.Dismiss();
                }
                Check(uiError == null, "help lifecycle has no UI exceptions");
            }
            finally { Application.ThreadException -= handler; }
        }
        static void VerifyLanguages()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-language-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Check(L.Supported("ko") == "ko" && L.Supported("ja") == "ja" && L.Supported("fr") == "en", "language fallback");
                foreach (string language in new string[] { "ko", "en", "ja" })
                {
                    L.Save(path, language); L.Initialize(path); Check(L.Current == language, "saved language restored");
                }
                File.WriteAllText(path, "broken JSON"); L.Initialize(path); Check(L.Current == L.WindowsDefault(), "corrupt language uses Windows display language");
                L.Current = "ko";
                using (MainForm form = new MainForm(false))
                {
                    form.Show(); Application.DoEvents();
                    MacroWorkspace page = Field<MacroWorkspace>(form, "macroWorkspace");
                    Field<TextBox>(page, "name").Text = "설정 이름 日本語 English";
                    MacroLibrary library = Field<MacroLibrary>(page, "library");
                    SavedMacro item = new SavedMacro { Name = "설정", Duration = 100 };
                    library.Items.Add(item); Invoke(page, "RefreshList", item.Id);
                    foreach (string language in new string[] { "en", "ja", "ko", "en", "ja", "ko" })
                    {
                        form.ApplyLanguage(language); Application.DoEvents();
                        Check(Field<ComboBox>(form, "mode").Items[0].ToString() == L.T("마우스 왼쪽 클릭"), "input options switch immediately");
                        Check(Field<CheckBox>(form, "holdWindow").Text == L.T("해당 창에서 유지"), "main page switched");
                        Check(Field<HelpBubble>(form, "holdTip").Message == L.T("해당 창의 해당 위치에서만 클리커가 작동하도록 하는 기능입니다. 창을 최소화 할 경우 작동되지 않습니다.\n\n클릭이 발생하지 않는 경우 해당 창에서는 사용할 수 없는 기능이므로 비활성화 후 사용해주세요\n\n입력 종류에 따라 작동 여부가 달라질수 있습니다.\nex) 왼쪽 클릭 작동 O, 키보드 입력 작동 X"), "help switches immediately");
                        Check(Field<TextBox>(page, "name").Text == "설정 이름 日本語 English" && Field<DarkListView>(page, "list").Items[0].Text == "설정", "user macro names preserved");
                        Check(Field<ContextMenuStrip>(page, "options").Items[0].Text == L.T("내용 보기"), "macro menu switched");
                        using (ClickCompatibilityDialog dialog = new ClickCompatibilityDialog(0))
                            Check(dialog.Text == L.T("클릭 호환성 테스트"), "new dialogs use selected language");
                        using (LanguageDialog dialog = new LanguageDialog())
                        {
                            dialog.Show(form); Application.DoEvents();
                            Check(dialog.Controls.Count == 3, "three language choices");
                            ((Button)dialog.Controls[1]).PerformClick();
                            Check(dialog.SelectedLanguage == "en" && dialog.DialogResult == DialogResult.OK, "language choice click");
                        }
                    }
                    form.Close();
                }
            }
            finally { L.Current = "ko"; if (File.Exists(path)) File.Delete(path); }
        }
        static SavedMacro ManagementSample()
        {
            SavedMacro source = new SavedMacro { Name = "편집 원본", Duration = 300, RepeatCount = 3, RepeatDelayMs = 120 };
            source.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 10, X = -22, Y = 33 });
            source.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, At = 100, Code = 65, Scan = 30 });
            source.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, At = 200, Code = 65, Scan = 30 });
            return source;
        }
        static void VerifyEditing()
        {
            SavedMacro source = ManagementSample(), edited = MacroEditing.Copy(source, false);
            Check(MacroEditing.SetDelay(edited, 1, 250) && edited.Actions[1].At == 260 && edited.Actions[2].At == 360 && edited.Duration == 460, "delay shifts later actions and trailing duration");
            Check(source.Actions[1].At == 100 && source.Duration == 300, "editor deep copy preserves original");
            Check(!MacroEditing.SetDelay(edited, 1, 86400000) && edited.Actions[1].At == 260, "oversized delay rejected without mutation");
            Check(MacroEditing.SetDelay(edited, 0, 0) && edited.Actions[0].At == 0 && edited.Actions[1].At == 250 && edited.Duration == 450, "first delay can be zero");
            MacroEditing.Delete(edited, new int[] { 0, 0 });
            Check(edited.Actions.Count == 2 && edited.Actions[0].At == 250 && edited.Duration == 450, "delete preserves timing and ignores duplicate indices");
            SavedMacro copy = MacroEditing.Copy(source, true); copy.Actions[0].X = 900;
            Check(copy.Id != source.Id && copy.RepeatCount == 3 && copy.RepeatDelayMs == 120 && source.Actions[0].X == -22, "duplicate independent identity and actions");
            MacroLibrary names = new MacroLibrary(); source.Name = new string('a', 80); names.Items.Add(source);
            string unique = MacroEditing.UniqueName(names, source.Name);
            Check(unique.Length == 80 && names.CheckName(unique, null) == null, "name collision respects 80 character limit");
            using (ManualResetEvent cancel = new ManualResetEvent(false))
            {
                SavedMacro repeated = ManagementSample(); int waiting = 0;
                Check(Playback.RunRepeated(repeated, cancel, delegate { }, null, false, delegate(int pause) { waiting++; Check(pause == 120, "waiting callback reports delay"); }), "repeat waiting callback completes");
                Check(waiting == 2, "waiting callback absent after final repeat");
                repeated.RepeatForever = true;
                Check(!Playback.RunRepeated(repeated, cancel, delegate { }, null, false, delegate { cancel.Set(); }), "emergency cancellation during reported wait");
            }
        }
        static void VerifyManagementUI()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-management-" + Guid.NewGuid().ToString("N") + ".json");
            string exported = path + "-export.json";
            try
            {
                using (MainForm form = new MainForm(false))
                {
                    form.Show(); Application.DoEvents(); Invoke(form, "SelectPage", true);
                    MacroWorkspace page = Field<MacroWorkspace>(form, "macroWorkspace");
                    page.PlaybackSink = delegate { };
                    Set(page, "persistence", true); Set(page, "libraryPath", path);
                    MacroLibrary library = Field<MacroLibrary>(page, "library");
                    SavedMacro original = ManagementSample(); library.Items.Add(original); library.Save(path); Invoke(page, "RefreshList", original.Id);
                    DialogAction(form, "녹화 내용 편집", delegate(Form dialog)
                    {
                        MacroEditorDialog editor = (MacroEditorDialog)dialog;
                        editor.Actions.Items[1].Selected = true; Application.DoEvents(); editor.Delay.Value = 250; editor.ApplyDelay.PerformClick();
                        Check(editor.Edited.Actions[1].At == 260 && original.Actions[1].At == 100, "editor applies delay to draft only");
                        ((Button)editor.CancelButton).PerformClick();
                    }, delegate { Invoke(page, "EditSelected"); });
                    Check(library.Items[0] == original && MacroLibrary.Load(path).Items[0].Duration == 300, "cancel leaves memory and disk unchanged");
                    DialogAction(form, "녹화 내용 편집", delegate(Form dialog)
                    {
                        MacroEditorDialog editor = (MacroEditorDialog)dialog;
                        editor.Actions.Items[1].Selected = true; Application.DoEvents(); editor.Delay.Value = 250; editor.ApplyDelay.PerformClick();
                        editor.Actions.SelectedIndices.Clear(); editor.Actions.Items[0].Selected = true; editor.DeleteActions.PerformClick();
                        Check(editor.Edited.Actions.Count == 2 && editor.Edited.Actions[0].At == 260, "editor deletes selected row without changing remaining timestamps");
                        editor.Save.PerformClick();
                    }, delegate { Invoke(page, "EditSelected"); });
                    SavedMacro current = library.Items[0];
                    Check(current.Id == original.Id && current.Duration == 460 && MacroLibrary.Load(path).Items[0].Actions.Count == 2, "editor save persisted");
                    Invoke(page, "DuplicateSelected");
                    Check(library.Items.Count == 2 && library.Items[1].Id != current.Id && library.Items[1].Name != current.Name, "duplicate option creates named independent copy");
                    Check(page.ExportFile(current, exported), "single macro export");
                    Check(MacroLibrary.Load(exported).Items.Count == 1 && MacroLibrary.Load(exported).Items[0].RepeatDelayMs == 120, "export contains selected macro and repetition settings");
                    Check(!page.ExportFile(current, path) && MacroLibrary.Load(path).Items.Count == 2, "export cannot overwrite live library");
                    Check(!page.ExportFile(current, path + ".bak"), "export cannot overwrite library backup");
                    Keys record = library.RecordKey, play = library.PlayKey;
                    Check(page.ImportFile(exported) && page.ImportFile(exported), "import supports repeated files");
                    Check(library.Items.Count == 4 && library.Items[2].Name != current.Name && library.Items[2].Id != library.Items[3].Id && library.RecordKey == record && library.PlayKey == play, "import resolves names and IDs without changing shortcuts");
                    File.WriteAllText(exported, "{broken}");
                    Check(!page.ImportFile(exported) && library.Items.Count == 4 && MacroLibrary.Load(path).Items.Count == 4, "invalid import preserves memory and disk");
                    string oldPath = Field<string>(page, "libraryPath"); Set(page, "libraryPath", Path.Combine(path + "-missing", "library.json"));
                    SavedMacro replacement = MacroEditing.Copy(current, false); replacement.Duration = 1000;
                    Check(!(bool)Invoke(page, "ReplaceMacro", current, replacement) && library.Items[0] == current, "failed edit save restores original");
                    Check(!(bool)Invoke(page, "AddCopies", (object)new SavedMacro[] { current }) && library.Items.Count == 4, "failed copy save restores collection");
                    Set(page, "libraryPath", oldPath);
                    Invoke(page, "RefreshList", current.Id); current.RepeatCount = 2; current.RepeatDelayMs = 1500;
                    Invoke(page, "PlaySelected"); Stopwatch timeout = Stopwatch.StartNew();
                    while (Field<long>(page, "waitDeadline") == 0 && timeout.ElapsedMilliseconds < 2500) { Application.DoEvents(); Thread.Sleep(10); }
                    Invoke(page, "UpdateUI");
                    Check(Field<long>(page, "waitDeadline") > 0 && Field<Label>(page, "draftInfo").Text.Contains("1/2") && Field<Label>(page, "draftInfo").Text.Contains("다음 반복까지"), "progress shows repeat total and remaining wait");
                    Check(Field<MacroProgress>(page, "progressBar").Visible && Field<MacroProgress>(page, "progressBar").Fraction == .5, "progress bar reaches first completed repeat");
                    Invoke(page, "StopPlayback"); Check(!Field<MacroProgress>(page, "progressBar").Visible, "emergency stop clears progress");
                    using (MacroEditorDialog editor = new MacroEditorDialog(current))
                    {
                        editor.Show(form); Application.DoEvents();
                        for (int i = 0; i < editor.Actions.VirtualListSize; i++) editor.Actions.Items[i].Selected = true;
                        editor.DeleteActions.PerformClick(); Application.DoEvents();
                        Check(editor.Edited.Actions.Count == 0 && !editor.Save.Enabled, "empty edited macro cannot be saved"); editor.Close();
                    }
                    foreach (string language in new string[] { "en", "ja", "ko" })
                    {
                        form.ApplyLanguage(language);
                        using (MacroEditorDialog translated = new MacroEditorDialog(current))
                            Check(translated.Text == L.T("녹화 내용 편집") && translated.DeleteActions.Text == L.T("선택 동작 삭제"), "new editor translations");
                        ContextMenuStrip menu = Field<ContextMenuStrip>(page, "options");
                        Check(menu.Items[4].Text == L.T("녹화 내용 편집") && menu.Items[5].Text == L.T("복제") && menu.Items[6].Text == L.T("내보내기"), "new context menu translations");
                    }
                    DialogAction(form, "설정", delegate(Form dialog)
                    {
                        ((SettingsDialog)dialog).LanguageTab.PerformClick();
                        foreach (Control control in dialog.Controls)
                            if (control is Button && (string)control.Tag == "en") { ((Button)control).PerformClick(); break; }
                    }, delegate { Invoke(form, "ChooseLanguage"); });
                    Check(L.Current == "en", "settings language button changes main UI"); form.ApplyLanguage("ko");
                    using (SettingsDialog settings = new SettingsDialog())
                    {
                        settings.Show(form); Application.DoEvents(); bool hasVersion = false;
                        foreach (Control control in settings.Controls) if (control.Text.Contains(typeof(MainForm).Assembly.GetName().Version.ToString(3))) hasVersion = true;
                        Check(settings.Text == "설정" && !hasVersion, "version is shown only in update dialog");
                        foreach (Control control in settings.Controls)
                        {
                            if ((string)control.Tag == "en") Check(!control.Visible, "settings opens other settings first");
                            if (control.Text == L.T("업데이트")) Check(control.Visible, "update is visible on other settings page");
                        }
                        settings.LanguageTab.PerformClick();
                        foreach (Control control in settings.Controls)
                        {
                            if ((string)control.Tag == "en") Check(control.Visible, "language choices shown on language settings page");
                            if (control.Text == L.T("업데이트")) Check(!control.Visible, "other actions hidden on language page");
                        }
                        settings.OtherTab.PerformClick(); settings.Close();
                    }
                    form.Close();
                }
            }
            finally { foreach (string file in new string[] { path, path + ".bak", exported, exported + ".bak" }) if (File.Exists(file)) File.Delete(file); }
        }
        static void EditorButtonClick(MacroEditorDialog editor, int row)
        {
            editor.Actions.EnsureVisible(row); Application.DoEvents();
            Rectangle bounds = editor.Actions.Items[row].Bounds; bounds.Width = editor.Actions.Columns[0].Width;
            Rectangle button = DarkListView.ToggleBounds(bounds); IntPtr point = ClickTarget.Coordinates(button.Left + button.Width / 2, button.Top + button.Height / 2);
            Native.PostMessage(editor.Actions.Handle, 0x201, (IntPtr)1, point);
            Native.PostMessage(editor.Actions.Handle, 0x202, IntPtr.Zero, point);
            Application.DoEvents(); Application.DoEvents();
        }
        static void VerifyGroupedEditor()
        {
            SavedMacro macro = new SavedMacro { Name = "편집 이동 묶음", Duration = 10000 };
            for (int i = 0; i < 600; i++) macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = i * 10, X = i });
            macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65, At = 6000 });
            macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65, At = 6005 });
            for (int i = 0; i < 400; i++) macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 6010 + i * 10, X = i });
            Exception uiError = null;
            ThreadExceptionEventHandler handler = delegate(object sender, ThreadExceptionEventArgs e) { uiError = e.Exception; };
            Application.ThreadException += handler;
            try
            {
                using (MacroEditorDialog editor = new MacroEditorDialog(macro))
                {
                    editor.Show(); Application.DoEvents();
                    Check(editor.Actions.MultiSelect && editor.Actions.VirtualListSize == 4 && editor.Model.Cells(0)[4] == "600개 로그", "editor initially groups movement logs with count");
                    editor.Actions.Items[0].Selected = true; Application.DoEvents();
                    Check(editor.Delay.Enabled && editor.Delay.Value == 0, "summary row can edit delay before first action");
                    editor.Delay.Value = 15; editor.ApplyDelay.PerformClick();
                    Check(editor.Edited.Actions[0].At == 15 && editor.Edited.Actions[599].At == 6005 && macro.Actions[0].At == 0, "summary edits group initial delay without modifying original");
                    editor.Actions.Items[1].Selected = true;
                    for (int i = 0; i < 5; i++)
                    {
                        EditorButtonClick(editor, 0);
                        Check(editor.Model.Groups[0].Expanded && editor.Actions.VirtualListSize == 604 && editor.Actions.SelectedIndices.Count == 2 && editor.Actions.Items[601].Selected, "button expands without losing other selection");
                        Check(editor.Actions.IsHighlighted(1, false) && editor.Actions.IsHighlighted(600, false) && !editor.Actions.IsHighlighted(602, false), "selected expanded summary highlights every child and no unrelated rows");
                        EditorButtonClick(editor, 0);
                        Check(!editor.Model.Groups[0].Expanded && editor.Actions.VirtualListSize == 4 && editor.Actions.SelectedIndices.Count == 2 && editor.Actions.Items[1].Selected, "button collapses without losing selection");
                    }
                    Rectangle summary = editor.Actions.Items[0].Bounds;
                    IntPtr body = ClickTarget.Coordinates(300, summary.Top + summary.Height / 2);
                    Native.PostMessage(editor.Actions.Handle, 0x201, (IntPtr)1, body); Native.PostMessage(editor.Actions.Handle, 0x202, IntPtr.Zero, body);
                    Native.PostMessage(editor.Actions.Handle, 0x203, (IntPtr)1, body); Native.PostMessage(editor.Actions.Handle, 0x202, IntPtr.Zero, body);
                    Application.DoEvents();
                    Check(!editor.Model.Groups[0].Expanded && editor.Actions.Items[0].Selected, "row click and double click select without toggling");
                    editor.Actions.Items[1].Selected = true;
                    EditorButtonClick(editor, 0);
                    editor.Actions.Items[1].Selected = true; // Selected summary plus one child must delete the group only once.
                    Check(editor.Actions.SelectedIndices.Count == 3 && !editor.Delay.Enabled && editor.DeleteActions.Enabled, "multiple selections enable deletion and disable ambiguous delay editing");
                    editor.DeleteActions.PerformClick(); Application.DoEvents();
                    Check(editor.Edited.Actions.Count == 401 && editor.Edited.Actions[0].Kind == ActionKind.KeyUp && editor.Edited.Actions[0].At == 6020 && editor.Model.Rows.Count == 2, "mixed selected summary child and key delete correctly without duplicate removal");
                    Check(macro.Actions.Count == 1002 && editor.Edited.Duration == 10015, "deletion preserves original and remaining timing");
                    EditorButtonClick(editor, 1); Check(editor.Actions.VirtualListSize == 402, "remaining group still expands after deletion");
                    editor.Actions.Items[401].Selected = true; editor.Actions.Items[401].Focused = true; editor.Actions.EnsureVisible(401);
                    editor.DeleteActions.PerformClick(); Application.DoEvents();
                    Check(editor.Edited.Actions.Count == 400 && editor.Model.Groups[1].Expanded && editor.Actions.VirtualListSize == 401, "delete scrolled selected detail preserves remaining expansion");
                    EditorButtonClick(editor, 1); Check(editor.Actions.VirtualListSize == 2, "remaining group safely collapses");
                    editor.Actions.Items[0].Selected = true; editor.Actions.Items[1].Selected = true;
                    editor.DeleteActions.PerformClick(); Application.DoEvents();
                    Check(editor.Edited.Actions.Count == 0 && editor.Actions.VirtualListSize == 0 && !editor.Save.Enabled, "collapsed group selection deletes all contained logs");
                    Check(uiError == null, "grouped editor has no virtual list callback errors"); editor.Close();
                }
                using (MainForm form = new MainForm(false))
                    Check(Field<Button>(form, "languageButton") is SettingsButton && Field<Button>(form, "languageButton").AccessibleName == "설정", "settings gear button replaces globe");
            }
            finally { Application.ThreadException -= handler; }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr flags, IntPtr value);
        static void VerifyShiftEditorSelection()
        {
            SavedMacro macro = new SavedMacro { Name = "Shift 선택", Duration = 80 };
            for (int i = 0; i < 8; i++) macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65, At = i * 10 });
            byte[] original = new byte[256]; Check(GetKeyboardState(original), "read test thread keyboard state");
            try
            {
                byte[] shift = new byte[256]; shift[(int)Keys.ShiftKey] = shift[(int)Keys.LShiftKey] = 128;
                using (MacroEditorDialog editor = new MacroEditorDialog(macro))
                {
                    editor.Show(); Application.DoEvents(); editor.Actions.Focus(); editor.Actions.Items[0].Focused = true;
                    editor.Actions.SelectedIndices.Clear();
                    Check(!editor.DeleteActions.Enabled, "delete initially disabled without selection");
                    SetKeyboardState(shift);
                    Rectangle row = editor.Actions.Items[3].Bounds; IntPtr point = ClickTarget.Coordinates(300, row.Top + row.Height / 2);
                    // Native messages go only to this test list; no system input is generated.
                    Native.PostMessage(editor.Actions.Handle, 0x201, (IntPtr)5, point); Native.PostMessage(editor.Actions.Handle, 0x202, (IntPtr)4, point);
                    Application.DoEvents(); SetKeyboardState(original);
                    Check(editor.Actions.SelectedIndices.Count == 4 && editor.DeleteActions.Enabled && !editor.ApplyDelay.Enabled, "Shift click range enables multi-delete");
                    editor.DeleteActions.PerformClick(); Application.DoEvents();
                    Check(editor.Edited.Actions.Count == 4 && editor.Edited.Actions[0].At == 40, "Shift click deletes selected range only");
                    editor.Close();
                }
                using (MacroEditorDialog editor = new MacroEditorDialog(macro))
                {
                    editor.Show(); Application.DoEvents(); editor.Actions.Focus(); editor.Actions.Items[0].Focused = true;
                    editor.Actions.SelectedIndices.Clear(); SetKeyboardState(shift);
                    SendMessage(editor.Actions.Handle, 0x100, (IntPtr)Keys.Down, (IntPtr)1);
                    SendMessage(editor.Actions.Handle, 0x101, (IntPtr)Keys.Down, (IntPtr)0xC0000001);
                    SendMessage(editor.Actions.Handle, 0x100, (IntPtr)Keys.Down, (IntPtr)1);
                    SendMessage(editor.Actions.Handle, 0x101, (IntPtr)Keys.Down, (IntPtr)0xC0000001);
                    SetKeyboardState(original); Application.DoEvents();
                    Check(editor.Actions.SelectedIndices.Count == 3 && editor.DeleteActions.Enabled, "Shift arrow range enables multi-delete");
                    editor.DeleteActions.PerformClick(); Application.DoEvents();
                    Check(editor.Edited.Actions.Count == 5 && editor.Edited.Actions[0].At == 30 && macro.Actions.Count == 8, "Shift arrow deletes range and preserves original");
                    Check(editor.Actions.Top == 60, "editor description removed and list expanded upward"); editor.Close();
                }
                using (MacroLogDialog dialog = new MacroLogDialog(macro))
                {
                    foreach (Control control in dialog.Controls)
                        if (control is Label) Check(!control.Text.Contains("\n") && !control.Text.Contains("묶어서"), "log description removed");
                }
            }
            finally { SetKeyboardState(original); }
        }
        static void VerifyRecoveryAndUndo()
        {
            string directory = Path.Combine(Path.GetTempPath(), "AutoMacro-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string settings = Path.Combine(directory, "settings.json"), path = Path.Combine(directory, "MacroLibrary.json"), bad = Path.Combine(directory, "bad.bak");
            try
            {
                File.WriteAllText(settings, "{broken}"); Check(!SettingsStore.IsValid(settings), "damaged settings detected before startup");
                using (SettingsRecoveryDialog recovery = new SettingsRecoveryDialog(settings))
                {
                    recovery.Show(); Application.DoEvents();
                    foreach (Control c in recovery.Controls) if (c is Button && c.Text == "기본 설정으로 복구") { ((Button)c).PerformClick(); break; }
                    Check(recovery.DialogResult == DialogResult.OK, "startup recovery button succeeds");
                }
                Check(SettingsStore.IsValid(settings) && Preferences.Load(settings).Interval == 100 && !StartupNotice.IsAcknowledged(settings), "settings recovery restores defaults and requires notice acknowledgement");
                string[] backups = Directory.GetFiles(directory, "settings.json.recovery-*.bak");
                Check(backups.Length == 1 && File.ReadAllText(backups[0]) == "{broken}", "damaged original preserved verbatim");
                StartupNotice.SaveAcknowledgement(settings); L.Save(settings, "ja"); new Preferences { Interval = 77 }.Save(settings);
                SettingsStore.Reset(settings);
                Check(Preferences.Load(settings).Interval == 100 && !StartupNotice.IsAcknowledged(settings) && Directory.GetFiles(directory, "settings.json.recovery-*.bak").Length == 2, "manual reset preserves prior settings separately");
                Check(SettingsRecoveryDialog.AllowStartup(settings), "valid settings skip recovery dialog");
                MacroLibrary saved = new MacroLibrary { RecordKey = Keys.XButton1, PlayKey = Keys.XButton2 };
                SavedMacro item = ManagementSample(); saved.Items.Add(item); saved.Save(path); item.Name = "현재 목록"; saved.Save(path);
                string backupContents = File.ReadAllText(path + ".bak"), currentContents = File.ReadAllText(path);
                using (MainForm form = new MainForm(false))
                {
                    form.Show(); Application.DoEvents(); MacroWorkspace page = Field<MacroWorkspace>(form, "macroWorkspace");
                    Field<NumericUpDown>(form, "interval").Value = 77; Field<CheckBox>(form, "holdWindow").Checked = true;
                    DialogAction(form, "Auto Macro", delegate(Form dialog)
                    { foreach (Control c in dialog.Controls) if (c is Button && c.Text == L.T("예")) { ((Button)c).PerformClick(); break; } }, delegate { Invoke(form, "ResetSettings"); });
                    Check(Field<NumericUpDown>(form, "interval").Value == 100 && !Field<CheckBox>(form, "holdWindow").Checked, "manual reset applies clicker defaults immediately"); form.ApplyLanguage("ko");
                    File.WriteAllText(settings, "{broken}"); Set(form, "persistence", true); Set(form, "settingsPath", settings);
                    DialogAction(form, "설정", delegate(Form dialog)
                    { ((SettingsDialog)dialog).LanguageTab.PerformClick(); foreach (Control c in dialog.Controls) if (c is Button && (string)c.Tag == "en") { ((Button)c).PerformClick(); break; } }, delegate { Invoke(form, "ChooseLanguage"); });
                    Check(L.Current == "ko" && File.ReadAllText(settings) == "{broken}", "language save failure preserves damaged data without UI crash"); Set(form, "persistence", false);
                    Set(page, "persistence", true); Set(page, "libraryPath", path); Set(page, "library", MacroLibrary.Load(path)); Set(page, "libraryReadOnly", true);
                    Check(page.RestoreBackup(path + ".bak") && !Field<bool>(page, "libraryReadOnly"), "backup recovery unlocks a damaged read-only workspace");
                    Check(Field<MacroLibrary>(page, "library").Items[0].Name == "편집 원본" && Field<MacroLibrary>(page, "library").RecordKey == Keys.XButton1, "backup restores original names IDs and shortcuts");
                    Check(File.ReadAllText(path + ".bak") == backupContents && File.ReadAllText(Directory.GetFiles(directory, "MacroLibrary.json.recovery-*.bak")[0]) == currentContents, "backup source unchanged and overwritten library archived");
                    File.WriteAllText(bad, "{broken}"); string live = File.ReadAllText(path); MacroLibrary before = Field<MacroLibrary>(page, "library");
                    Check(!page.RestoreBackup(bad) && Field<MacroLibrary>(page, "library") == before && File.ReadAllText(path) == live, "invalid backup leaves original memory and file unchanged");
                    Set(page, "libraryPath", Path.Combine(directory, "missing", "library.json"));
                    Check(!page.RestoreBackup(path + ".bak") && Field<MacroLibrary>(page, "library") == before, "failed restore write leaves memory unchanged");
                    Set(page, "libraryPath", path); form.Close();
                }
                SavedMacro group = new SavedMacro { Name = "실행 취소 검사", Duration = 50 };
                for (int i = 0; i < 3; i++) group.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = i * 10, X = i });
                group.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, At = 40, Code = 65 });
                using (MacroEditorDialog editor = new MacroEditorDialog(group))
                {
                    editor.Show(); Application.DoEvents(); Check(!editor.Undo.Enabled, "undo initially disabled"); editor.ToggleGroup(0);
                    editor.Actions.Items[0].Selected = true; editor.DeleteActions.PerformClick();
                    Check(editor.Edited.Actions.Count == 1 && editor.Undo.Enabled, "group deletion is undoable"); editor.Undo.PerformClick(); Application.DoEvents();
                    Check(editor.Edited.Actions.Count == 4 && editor.Model.Groups[0].Expanded && editor.Actions.Items[0].Selected && editor.Actions.IsHighlighted(3, false), "undo restores group expansion selection and child highlighting");
                    editor.Actions.SelectedIndices.Clear(); editor.Actions.Items[2].Selected = true; editor.Delay.Value = 50; editor.ApplyDelay.PerformClick();
                    Check(editor.Edited.Actions[1].At == 50 && editor.Edited.Duration == 90, "edited delay recorded in history");
                    Invoke(editor, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.Z)); Application.DoEvents();
                    Check(editor.Edited.Actions[1].At == 10 && editor.Edited.Duration == 50 && !editor.Undo.Enabled, "Ctrl Z restores delay and duration");
                    editor.Actions.SelectedIndices.Clear(); editor.Actions.Items[0].Selected = true; editor.Actions.Items[4].Selected = true; editor.DeleteActions.PerformClick();
                    Check(!editor.Save.Enabled && editor.Undo.Enabled, "undo remains available after deleting all actions"); editor.Undo.PerformClick();
                    Check(editor.Save.Enabled && editor.Edited.Actions.Count == 4 && group.Actions.Count == 4, "undo restores empty edit without modifying source"); editor.Close();
                }
            }
            finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
        internal static void RunFreshStartup()
        {
            Check(!File.Exists(AppInfo.SettingsPath) && !File.Exists(AppInfo.LibraryPath), "fresh startup test requires an empty data directory");
            L.Initialize(AppInfo.SettingsPath); Check(L.Current == L.WindowsDefault(), "fresh startup follows Windows display language");
            Check(SettingsRecoveryDialog.AllowStartup(AppInfo.SettingsPath) && !StartupNotice.IsAcknowledged(AppInfo.SettingsPath), "fresh startup needs notice and no recovery");
            using (StartupNotice notice = new StartupNotice(AppInfo.SettingsPath))
            { notice.Show(); Application.DoEvents(); notice.Confirm.PerformClick(); Check(notice.DialogResult == DialogResult.OK, "first startup acknowledgement saved"); }
            L.Save(AppInfo.SettingsPath, "ja");
            new Preferences { Mode = 3, Macro = Keys.B, Interval = 41 }.Save(AppInfo.SettingsPath);
            MacroLibrary library = new MacroLibrary(); library.Items.Add(ManagementSample()); library.Save(AppInfo.LibraryPath);
            L.Initialize(AppInfo.SettingsPath);
            using (MainForm form = new MainForm())
            {
                form.Show(); Application.DoEvents();
                Check(L.Current == "ja" && Field<ComboBox>(form, "mode").SelectedIndex == 3 && Field<NumericUpDown>(form, "interval").Value == 41, "relaunch restores language and clicker settings");
                MacroWorkspace page = Field<MacroWorkspace>(form, "macroWorkspace");
                Check(Field<MacroLibrary>(page, "library").Items.Count == 1 && Field<MacroLibrary>(page, "library").Items[0].Name == "편집 원본", "relaunch restores macro library independently");
                form.Close();
            }
            Check(StartupNotice.IsAcknowledged(AppInfo.SettingsPath) && SettingsStore.IsValid(AppInfo.SettingsPath), "relaunch preserves notice and valid settings");
        }
        internal static void RunUI()
        {
            UpdateTests.RunUI();
            VerifyLanguages();
            VerifyManagementUI();
            VerifyGroupedEditor();
            VerifyShiftEditorSelection();
            VerifyRecoveryAndUndo();
            VerifyHelpHover();
            VerifyClickTest();
            VerifyHoldWindow();
            VerifyStartupNotice();
            VerifyLogCollapse();
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-workflow-" + Guid.NewGuid().ToString("N") + ".json");
            using (MainForm form = new MainForm(false))
            {
                MacroWorkspace page = Field<MacroWorkspace>(form, "macroWorkspace");
                Set(page, "persistence", true); Set(page, "libraryPath", path);
                List<Native.Input> sent = new List<Native.Input>(); page.PlaybackSink = delegate(Native.Input input) { sent.Add(input); };
                form.Show(); Invoke(form, "SelectPage", true);
                Check(Field<InputMonitor>(page, "monitor") != null, "global monitor installation");
                // Disable actual hooks during synthetic recording tests; no user input is collected.
                Field<InputMonitor>(page, "monitor").Dispose();
                Invoke(page, "ToggleRecording");
                Check(Field<bool>(page, "recording"), "record start");
                Invoke(page, "CaptureAction", new MacroAction { Kind = ActionKind.KeyDown, Code = 65, Scan = 30 });
                Invoke(page, "CaptureAction", new MacroAction { Kind = ActionKind.KeyUp, Code = 65, Scan = 30 });
                Invoke(page, "EndRecording");
                Check(Field<SavedMacro>(page, "draft").Actions.Count == 2, "record stop retains draft");
                Field<TextBox>(page, "name").Text = "UI 녹화 저장";
                Check((bool)Invoke(page, "SaveDraft"), "save recorded draft");
                Check(MacroLibrary.Load(path).Items[0].Name == "UI 녹화 저장", "saved name persisted");
                Exception dialogError = null;
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
                {
                    timer.Interval = 50;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        try
                        {
                            Form dialog = null; foreach (Form candidate in Application.OpenForms) if (candidate.Text == "이름 변경") dialog = candidate;
                            Check(dialog != null, "rename dialog");
                            foreach (Control control in dialog.Controls) if (control is TextBox) control.Text = "UI 이름 변경";
                            ((Button)dialog.AcceptButton).PerformClick();
                        }
                        catch (Exception error) { dialogError = error; foreach (Form candidate in new List<Form>(OpenForms())) if (candidate != form) candidate.Close(); }
                    };
                    timer.Start(); Invoke(page, "RenameSelected");
                }
                if (dialogError != null) throw dialogError;
                Check(MacroLibrary.Load(path).Items[0].Name == "UI 이름 변경", "renamed macro persisted");
                Invoke(page, "PlaySelected");
                Stopwatch wait = Stopwatch.StartNew();
                while (Field<bool>(page, "playing") && wait.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(5); }
                Check(!Field<bool>(page, "playing") && sent.Count == 2, "selected item played once through test sink");
                // Verify direct-capture shortcut applies and persists without an extra Apply button.
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
                {
                    timer.Interval = 50;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        try
                        {
                            KeyPicker picker = null; foreach (Form candidate in Application.OpenForms) if (candidate is KeyPicker) picker = (KeyPicker)candidate;
                            Check(picker != null, "shortcut picker");
                            Invoke(picker, "AcceptKey", Keys.XButton1);
                            picker.DialogResult = DialogResult.OK; picker.Close();
                        }
                        catch (Exception error) { dialogError = error; foreach (Form candidate in new List<Form>(OpenForms())) if (candidate != form) candidate.Close(); }
                    };
                    timer.Start(); Invoke(page, "CaptureShortcut", 0);
                }
                if (dialogError != null) throw dialogError;
                Check(MacroLibrary.Load(path).RecordKey == Keys.XButton1, "shortcut immediately saved");
                VerifyOptions(page, form, path);
                Invoke(form, "SelectPage", false);
                Check(Field<InputMonitor>(page, "monitor") == null, "macro hooks removed on page switch");
                using (MacroWorkspace reopened = new MacroWorkspace(true, path))
                    Check(Field<MacroLibrary>(reopened, "library").Items[0].Name == "UI 이름 변경" && Field<MacroLibrary>(reopened, "library").RecordKey == Keys.XButton1, "restart restores list and shortcut");
                form.Close();
            }
            if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
        static IEnumerable<Form> OpenForms() { foreach (Form form in Application.OpenForms) yield return form; }
        static void VerifyStartupNotice()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-notice-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Check(!StartupNotice.IsAcknowledged(path), "first launch requires notice");
                using (StartupNotice notice = new StartupNotice(path))
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
                {
                    timer.Interval = 50; timer.Tick += delegate { timer.Stop(); notice.Close(); }; timer.Start();
                    Check(notice.ShowDialog() != DialogResult.OK && !File.Exists(path), "dismissal does not acknowledge notice");
                }
                using (StartupNotice notice = new StartupNotice(path))
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
                {
                    bool first = false, second = false;
                    foreach (Control c in notice.Controls) { first |= c.Text == StartupNotice.Responsibility; second |= c.Text == StartupNotice.Caution; }
                    Check(first && second && notice.Confirm.Text == "이해했습니다.", "requested exact notice text");
                    timer.Interval = 50; timer.Tick += delegate { timer.Stop(); notice.Confirm.PerformClick(); }; timer.Start();
                    Check(notice.ShowDialog() == DialogResult.OK && StartupNotice.IsAcknowledged(path), "acknowledgement persists");
                }
                Check(StartupNotice.AllowStartup(path), "subsequent startup skips notice");
                File.WriteAllText(path, "broken"); Check(!StartupNotice.IsAcknowledged(path), "invalid notice record requires acknowledgement");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        static void VerifyLogCollapse()
        {
            SavedMacro macro = new SavedMacro { Name = "로그 접기 검사" };
            for (int i = 0; i < 600; i++) macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = i * 10, X = i });
            macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65, At = 6000 });
            for (int i = 0; i < 400; i++) macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 6010 + i * 10, X = i });
            Exception callbackError = null;
            ThreadExceptionEventHandler handler = delegate(object sender, ThreadExceptionEventArgs e) { callbackError = e.Exception; };
            Application.ThreadException += handler;
            try
            {
                using (MacroLogDialog dialog = new MacroLogDialog(macro))
                {
                    dialog.Show(); Application.DoEvents();
                    for (int round = 0; round < 5; round++)
                    {
                        dialog.ToggleRow(0); dialog.ToggleRow(602);
                        int last = dialog.EventList.VirtualListSize - 1;
                        dialog.EventList.Items[last].Selected = true; dialog.EventList.Items[last].Focused = true;
                        dialog.EventList.EnsureVisible(last); dialog.EventList.Update();
                        dialog.ToggleRow(0); Application.DoEvents();
                        Check(dialog.Model.Rows.Count == 403, "collapse first group preserves second expansion");
                        dialog.ToggleRow(2); Application.DoEvents();
                        Check(dialog.Model.Rows.Count == 3 && dialog.EventList.VirtualListSize == 3, "scrolled selected log collapses to summaries");
                        if (callbackError != null) throw callbackError;
                    }
                    for (int click = 0; click < 4; click++)
                    {
                        Rectangle buttonRow = dialog.EventList.Items[0].Bounds;
                        Invoke(dialog.EventList, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 16, buttonRow.Top + buttonRow.Height / 2, 0));
                        Application.DoEvents();
                        Check(dialog.Model.Rows.Count == (click % 2 == 0 ? 603 : 3), "mouse button expand/collapse dispatch");
                    }
                    for (int click = 0; click < 4; click++)
                    {
                        Rectangle summaryRow = dialog.EventList.Items[0].Bounds;
                        int y = summaryRow.Top + summaryRow.Height / 2;
                        Invoke(dialog.EventList, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 300, y, 0));
                        Application.DoEvents();
                        Check(dialog.Model.Rows.Count == (click % 2 == 0 ? 3 : 603), "single summary click only selects");
                        Invoke(dialog.EventList, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, 300, y, 0));
                        Invoke(dialog.EventList, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 2, 300, y, 0));
                        Application.DoEvents();
                        Check(dialog.Model.Rows.Count == (click % 2 == 0 ? 603 : 3), "summary double click expands and collapses once");
                        if (click % 2 == 0)
                        {
                            Rectangle detail = dialog.EventList.Items[1].Bounds;
                            Invoke(dialog.EventList, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, 300, detail.Top + detail.Height / 2, 0));
                            Application.DoEvents();
                            Check(dialog.Model.Rows.Count == 603, "detail double click does not collapse summary");
                        }
                    }
                    Rectangle buttonSummary = dialog.EventList.Items[0].Bounds;
                    int buttonY = buttonSummary.Top + buttonSummary.Height / 2;
                    Invoke(dialog.EventList, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 16, buttonY, 0)); Application.DoEvents();
                    Invoke(dialog.EventList, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, 16, buttonY, 0));
                    Invoke(dialog.EventList, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 16, buttonY, 0)); Application.DoEvents();
                    Check(dialog.Model.Rows.Count == 603, "button double click does not undo first toggle");
                    dialog.ToggleRow(0); Application.DoEvents();
                    Invoke(dialog.EventList, "OnKeyDown", new KeyEventArgs(Keys.Enter)); Application.DoEvents();
                    Check(dialog.Model.Rows.Count == 603, "keyboard expand");
                    Invoke(dialog.EventList, "OnKeyDown", new KeyEventArgs(Keys.Space)); Application.DoEvents();
                    Check(dialog.Model.Rows.Count == 3, "keyboard collapse");
                    if (callbackError != null) throw callbackError;
                    dialog.Close();
                }
            }
            finally { Application.ThreadException -= handler; }
        }
        static void DialogAction(Form owner, string title, Action<Form> act, Action open)
        {
            Exception failure = null;
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            {
                timer.Interval = 60; timer.Tick += delegate
                {
                    timer.Stop(); Form dialog = null;
                    try { foreach (Form candidate in Application.OpenForms) if (candidate.Text == title) dialog = candidate; Check(dialog != null, title + " opens"); act(dialog); }
                    catch (Exception error) { failure = error; if (dialog != null) dialog.Close(); }
                };
                timer.Start(); open();
            }
            if (failure != null) throw failure;
        }
        static void VerifyOptions(MacroWorkspace page, MainForm form, string path)
        {
            MacroLibrary library = Field<MacroLibrary>(page, "library");
            SavedMacro second = new SavedMacro { Name = "우클릭 대상", Actions = new List<MacroAction>(library.Items[0].Actions), Duration = library.Items[0].Duration };
            library.Items.Add(second); library.Save(path); Invoke(page, "RefreshList", library.Items[0].Id);
            DarkListView list = Field<DarkListView>(page, "list");
            Rectangle row = list.Items[1].Bounds;
            Invoke(list, "OnMouseUp", new MouseEventArgs(MouseButtons.Right, 1, row.Left + 20, row.Top + 4, 0));
            ContextMenuStrip menu = Field<ContextMenuStrip>(page, "options");
            Check(menu.Visible && menu.Items.Count == 7 && list.SelectedItems[0].Tag == second, "right click targets clicked row");
            Check(menu.Items[0].Text == "내용 보기" && menu.Items[1].Text == "이름 변경" && menu.Items[2].Text == "반복 설정" && menu.Items[3].Text == "삭제", "context options");
            menu.Close();
            DialogAction(form, "반복 설정", delegate(Form dialog)
            {
                foreach (Control c in dialog.Controls)
                    if (c is NumericUpDown) ((NumericUpDown)c).Value = ((NumericUpDown)c).Minimum == 1 ? 3 : 20;
                ((Button)dialog.AcceptButton).PerformClick();
            }, delegate { ((ToolStripMenuItem)menu.Items[2]).PerformClick(); });
            Check(second.RepeatCount == 3 && second.RepeatDelayMs == 20 && MacroLibrary.Load(path).Items[1].RepeatCount == 3, "repeat dialog persists per item");
            Invoke(page, "PlaySelected");
            Check(list.Enabled && list.InputLocked, "playing keeps themed list enabled and selection locked");
            Invoke(page, "StopPlayback");
            string oldPath = Field<string>(page, "libraryPath"); Set(page, "libraryPath", Path.Combine(path + "-missing", "library.json"));
            Check(!(bool)Invoke(page, "SaveRepeat", second, 9, true, 500), "failed repeat save rejected");
            Check(second.RepeatCount == 3 && !second.RepeatForever && second.RepeatDelayMs == 20, "failed repeat save rollback");
            Check(!(bool)Invoke(page, "DeleteMacro", second) && library.Items.Contains(second), "failed deletion rolls back");
            Set(page, "libraryPath", oldPath);
            DialogAction(form, "매크로 삭제", delegate(Form dialog)
            {
                bool hasName = false; foreach (Control c in dialog.Controls) if (c.Text == second.Name) hasName = true;
                Check(hasName, "delete confirmation shows macro name"); ((Button)dialog.CancelButton).PerformClick();
            }, delegate { ((ToolStripMenuItem)menu.Items[3]).PerformClick(); });
            Check(library.Items.Contains(second), "cancel deletion retains macro");
            DialogAction(form, "매크로 삭제", delegate(Form dialog)
            {
                foreach (Control c in dialog.Controls) if (c is Button && c.Text == "삭제") { ((Button)c).PerformClick(); break; }
            }, delegate { ((ToolStripMenuItem)menu.Items[3]).PerformClick(); });
            Check(!library.Items.Contains(second) && MacroLibrary.Load(path).Items.Count == 1, "confirmed deletion persisted");
        }
        static void VerifyJsonStorage()
        {
            string prefix = Path.Combine(Path.GetTempPath(), "AutoMacro-json-" + Guid.NewGuid().ToString("N"));
            string settings = prefix + "-settings.json", libraryPath = prefix + "-library.json", notice = prefix + "-notice.json", language = prefix + "-language.json";
            string originalLanguage = L.Current;
            try
            {
                Preferences preferences = new Preferences { Mode = 3, Interval = 27, Macro = Keys.A, Toggle = Keys.XButton1, Stop = Keys.XButton2, HoldWindow = true };
                preferences.Save(settings);
                Preferences restored = Preferences.Load(settings);
                Check(restored.Mode == 3 && restored.Interval == 27 && restored.Macro == Keys.A && restored.Toggle == Keys.XButton1 && restored.Stop == Keys.XButton2 && restored.HoldWindow, "JSON preferences roundtrip");
                preferences.Interval = 42; preferences.Save(settings);
                Check(Preferences.Load(settings).Interval == 42, "JSON preferences update");

                MacroLibrary library = new MacroLibrary { RecordKey = Keys.XButton1, PlayKey = Keys.XButton2 };
                SavedMacro macro = new SavedMacro { Name = "한글 日本語 \\\"test\\\"", Duration = 100, RepeatCount = 7, RepeatForever = true, RepeatDelayMs = 1234 };
                macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 0, X = -123, Y = 456 });
                macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, At = 20, Code = 65, Scan = 30, Extended = true });
                macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, At = 100, Code = 65, Scan = 30, Extended = true });
                library.Items.Add(macro); library.Save(libraryPath);
                MacroLibrary loaded = MacroLibrary.Load(libraryPath);
                SavedMacro copy = loaded.Items[0];
                Check(copy.Name == macro.Name && copy.Id == macro.Id && copy.Created == macro.Created && copy.Duration == 100 && copy.RepeatCount == 7 && copy.RepeatForever && copy.RepeatDelayMs == 1234 && copy.Actions.Count == 3 && copy.Actions[0].X == -123 && copy.Actions[1].Extended && copy.Actions[1].Scan == 30, "JSON macro roundtrip preserves all data");
                foreach (string name in new string[] { "123", "true", "false" })
                {
                    macro.Name = name; library.Save(libraryPath);
                    Check(MacroLibrary.Load(libraryPath).Items[0].Name == name, "numeric and boolean names remain strings");
                }
                Check(MacroLibrary.Load(libraryPath + ".bak").Items[0].Name == "true", "JSON previous-save backup readable");
                File.WriteAllText(libraryPath, "{broken}"); bool rejected = false;
                try { MacroLibrary.Load(libraryPath); } catch { rejected = true; }
                Check(rejected && File.ReadAllText(libraryPath) == "{broken}", "corrupt JSON is preserved");
                File.Delete(libraryPath);
                Check(MacroLibrary.Load(libraryPath).Items.Count == 0, "missing JSON library starts empty");

                StartupNotice.SaveAcknowledgement(notice);
                Check(StartupNotice.IsAcknowledged(notice) && File.Exists(notice), "JSON notice stores acknowledgement");
                Check(StartupNotice.IsAcknowledged(notice), "JSON notice reload");
                L.Save(language, "ja"); L.Initialize(language);
                Check(L.Current == "ja" && File.Exists(language), "JSON language stores choice");
                L.Save(language, "en"); L.Initialize(language); Check(L.Current == "en", "JSON language reload");
            }
            finally
            {
                L.Current = originalLanguage;
                foreach (string path in new string[] { settings, libraryPath, notice, language })
                    foreach (string file in new string[] { path, path + ".bak", path + ".tmp" })
                        if (File.Exists(file)) File.Delete(file);
            }
        }
        static void VerifyUnifiedSettings()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-unified-" + Guid.NewGuid().ToString("N") + ".json");
            string language = L.Current;
            try
            {
                Check(!File.Exists(path) && !StartupNotice.IsAcknowledged(path), "fresh settings starts unacknowledged without creating file");
                StartupNotice.SaveAcknowledgement(path);
                Check(Preferences.Load(path).Interval == 100 && StartupNotice.IsAcknowledged(path), "notice first save also creates valid clicker defaults");
                L.Save(path, "ja");
                Preferences custom = new Preferences { Mode = 3, Interval = 37, Macro = Keys.B, Toggle = Keys.XButton1, Stop = Keys.XButton2, HoldWindow = true };
                custom.Save(path); L.Initialize(path);
                Check(L.Current == "ja" && StartupNotice.IsAcknowledged(path) && Preferences.Load(path).HoldWindow, "saving clicker preserves language and notice");
                L.Save(path, "en"); StartupNotice.SaveAcknowledgement(path); L.Initialize(path);
                Preferences restored = Preferences.Load(path);
                Check(L.Current == "en" && restored.Interval == 37 && restored.Mode == 3 && restored.Macro == Keys.B && restored.Toggle == Keys.XButton1 && restored.Stop == Keys.XButton2 && restored.HoldWindow, "saving language and notice preserves all clicker fields");
                DataNode root = DataStore.Load(path);
                Check(root.Name == "AutoMacro" && root.Element("Language") != null && root.Element("Acknowledged") != null && root.Element("NoticeVersion") != null, "single JSON contains all settings fields");
                File.WriteAllText(path, "{broken}"); bool rejected = false;
                try { custom.Save(path); } catch { rejected = true; }
                Check(rejected && File.ReadAllText(path) == "{broken}", "partial save cannot overwrite corrupted combined settings");
            }
            finally { L.Current = language; if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        }
        internal static void Run()
        {
            UpdateTests.Run();
            VerifyUnifiedSettings();
            VerifyJsonStorage();
            VerifyEditing();
            RecordingBuffer recording = new RecordingBuffer();
            recording.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65 }, 1);
            recording.Add(new MacroAction { Kind = ActionKind.Move, X = -100, Y = 20 }, 2);
            recording.Add(new MacroAction { Kind = ActionKind.Move, X = -99, Y = 20 }, 3);
            recording.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65, Scan = 30 }, 10);
            recording.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65, Scan = 30 }, 30);
            recording.Add(new MacroAction { Kind = ActionKind.MouseDown, Code = 1, X = 100, Y = 100 }, 40);
            recording.Add(new MacroAction { Kind = ActionKind.Move, X = 150, Y = 150 }, 60);
            SavedMacro macro = recording.Finish(100);
            Check(macro.Actions.Count == 6, "orphan release excluded / mouse move sampling");
            Check(macro.Actions[5].Kind == ActionKind.MouseUp && macro.Actions[5].X == 150 && macro.Actions[5].At == 100, "unfinished drag closed at last position");
            InputMonitor.MouseData raw = new InputMonitor.MouseData { x = -25, y = 33, data = 0xFF880000 };
            MacroAction wheel = InputMonitor.DecodeMouse(0x20A, raw);
            Check(wheel.Code == -120 && wheel.Kind == ActionKind.Wheel, "signed wheel delta");
            raw.data = 0x20000;
            Check(InputMonitor.DecodeMouse(0x20B, raw).Code == 5, "thumb decoding");
            Native.Input key = Playback.Convert(macro.Actions[1], new Rectangle(-1920, 0, 3840, 1080));
            Check(key.type == 1 && key.data.keyboard.scan == 30 && key.data.keyboard.flags == 8, "keyboard scan playback");
            Native.Input move = Playback.Convert(new MacroAction { Kind = ActionKind.Move, X = -1920, Y = 0 }, new Rectangle(-1920, 0, 3840, 1080));
            Check(move.data.mouse.dx == 0 && (move.data.mouse.flags & 0x4000) != 0, "negative monitor coordinate");
            Native.Input wheelInput = Playback.Convert(wheel, new Rectangle(0, 0, 1920, 1080));
            Check(unchecked((int)wheelInput.data.mouse.data) == -120 && (wheelInput.data.mouse.flags & 0x800) != 0, "wheel playback");
            MacroLibrary library = new MacroLibrary(); macro.Name = "테스트 <키보드> & 마우스"; library.Items.Add(macro);
            Check(library.CheckName("  ", null) != null && library.CheckName(macro.Name, null) != null && library.CheckName(macro.Name, macro) == null, "name validation");
            Check(library.ValidateShortcut(0, Keys.F9) != null && library.ValidateShortcut(0, Keys.XButton1) == null, "control key conflicts");
            library.RecordKey = Keys.XButton1; library.PlayKey = Keys.XButton2;
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-library-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                library.Save(path); macro.Name = "이름 변경 완료"; library.Save(path);
                MacroLibrary restored = MacroLibrary.Load(path);
                Check(restored.RecordKey == Keys.XButton1 && restored.PlayKey == Keys.XButton2 && restored.Items.Count == 1 && restored.Items[0].Name == macro.Name && restored.Items[0].Actions.Count == 6, "saved library / rename / shortcuts round trip");
                Check(MacroLibrary.Load(path + ".bak").Items[0].Name != macro.Name, "previous save backup");
                macro.RepeatCount = 4; macro.RepeatForever = true; macro.RepeatDelayMs = 1500; library.Save(path);
                SavedMacro repeated = MacroLibrary.Load(path).Items[0];
                Check(repeated.RepeatCount == 4 && repeated.RepeatForever && repeated.RepeatDelayMs == 1500, "repeat fields persisted");
                DataNode legacy = DataStore.Load(path); DataNode legacyMacro = legacy.Element("Macro");
                legacyMacro.Attribute("repeatCount").Remove(); legacyMacro.Attribute("repeatForever").Remove(); legacyMacro.Attribute("repeatDelayMs").Remove(); DataStore.Save(path, legacy, false);
                repeated = MacroLibrary.Load(path).Items[0];
                Check(repeated.RepeatCount == 1 && !repeated.RepeatForever && repeated.RepeatDelayMs == 0, "legacy macro defaults");
                File.WriteAllText(path, "<broken>"); bool rejected = false;
                try { MacroLibrary.Load(path); } catch { rejected = true; }
                Check(rejected, "corrupted library rejected");
            }
            finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
            SavedMacro movement = new SavedMacro();
            movement.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 100, X = 10 });
            movement.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 1100, X = 20 });
            movement.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, At = 1200, Code = 65 });
            movement.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 1300, X = 30 });
            MacroLogModel log = new MacroLogModel(movement);
            Check(log.Rows.Count == 3 && log.Cells(0)[3].Contains("0:00:01.000"), "consecutive moves collapsed with duration");
            log.Toggle(0); Check(log.Rows.Count == 5 && log.Cells(1)[2] == "100" && log.Cells(2)[4] == "20, 0", "expanded raw move logs unchanged");
            log.Toggle(0); Check(log.Rows.Count == 3, "collapse restored");
            using (MacroLogDialog dialog = new MacroLogDialog(movement))
            { dialog.CreateControl(); dialog.ToggleRow(0); Check(dialog.EventList.VirtualListSize == 5, "virtual log expansion"); }
            using (ManualResetEvent cancel = new ManualResetEvent(false))
            {
                List<Native.Input> sent = new List<Native.Input>();
                Check(Playback.Run(macro, cancel, delegate(Native.Input input) { sent.Add(input); }, null, false), "one pass complete");
                Check(sent.Count == macro.Actions.Count, "one pass exact input count");
                SavedMacro held = new SavedMacro { Duration = 100000 };
                held.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65, Scan = 30 });
                held.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65, Scan = 30, At = 100000 });
                sent.Clear();
                bool completed = Playback.Run(held, cancel, delegate(Native.Input input) { sent.Add(input); if (sent.Count == 1) cancel.Set(); }, null, true);
                Check(!completed && sent.Count == 2 && (sent[1].data.keyboard.flags & 2) != 0, "cancel interrupts waiting / releases key");
                cancel.Reset(); sent.Clear(); bool failure = false;
                try { Playback.Run(held, cancel, delegate(Native.Input input) { sent.Add(input); if (sent.Count == 1) throw new Exception("simulated failure"); }, null, false); } catch { failure = true; }
                Check(failure && sent.Count == 2 && (sent[1].data.keyboard.flags & 2) != 0, "input failure releases keys");
                cancel.Reset(); sent.Clear();
                SavedMacro repeated = new SavedMacro { RepeatCount = 3, RepeatDelayMs = 10 };
                repeated.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65 }); repeated.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65 });
                long iteration = 0;
                Check(Playback.RunRepeated(repeated, cancel, delegate(Native.Input input) { sent.Add(input); }, delegate(long n, int step) { iteration = n; }, false) && sent.Count == 6 && iteration == 3, "exact total repeat count");
                repeated.RepeatCount = 1; repeated.RepeatDelayMs = 10000; bool done = false;
                Thread one = new Thread(delegate() { done = Playback.RunRepeated(repeated, cancel, delegate(Native.Input input) { }, null, false); });
                one.Start(); bool noTail = one.Join(700); if (!noTail) { cancel.Set(); one.Join(); }
                Check(noTail && done, "no wait after final iteration");
                cancel.Reset(); repeated.RepeatForever = true;
                using (ManualResetEvent first = new ManualResetEvent(false))
                {
                    bool result = true; int sentCount = 0;
                    Thread loop = new Thread(delegate() { result = Playback.RunRepeated(repeated, cancel, delegate(Native.Input input) { if (++sentCount == 2) first.Set(); }, null, false); });
                    loop.Start(); bool started = first.WaitOne(1000); Thread.Sleep(30); cancel.Set(); bool stopped = loop.Join(700);
                    Check(started && stopped && !result && sentCount == 2, "infinite repeat cancelled during inter-repeat wait");
                }
            }
        }
    }
}
