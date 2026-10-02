using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;

namespace AutoMacro
{
    internal static class MaintenanceTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Private).GetValue(value); }
        static void Check(bool value, string message) { if (!value) throw new Exception("Maintenance: " + message); }
        static void Pump(int ms) { Stopwatch watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); } }
        internal static void Run()
        {
            VerifyValidationAndText();
            VerifyOffscreenInput();
            VerifyResetKeys();
            Check(MacroWorkspace.LengthText(86400000) == "24:00:00.000" && MacroWorkspace.LengthText(3661001) == "01:01:01.001", "elapsed hours do not wrap at 24 hours");
            string path = Path.Combine(AppInfo.DataDirectory, "unchanged-settings.json");
            try
            {
                new Preferences().Save(path); DateTime stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(path, stamp);
                new Preferences().Save(path);
                Check(File.GetLastWriteTimeUtc(path) == stamp, "unchanged preferences do not rewrite file");
                SettingsStore.Update(path, new DataNode("Language", "en")); new Preferences { Interval = 101 }.Save(path);
                Check((string)SettingsStore.Read(path).Element("Language") == "en" && Preferences.Load(path).Interval == 101, "partial save preserves unrelated fields");
            }
            finally { File.Delete(path); }
        }
        static void VerifyValidationAndText()
        {
            Check(!InputRules.ValidShortcut(Keys.ControlKey) && !InputRules.ValidShortcut(Keys.LShiftKey) && !InputRules.ValidShortcut(Keys.A | Keys.Control) && InputRules.ValidShortcut(Keys.XButton1) && MacroLibrary.ValidShortcut(Keys.F6), "shared shortcut rules");
            Check(TextLimits.Truncate("ab\uD83D\uDE00", 3) == "ab" && TextLimits.Truncate("abe\u0301", 3) == "ab", "name truncation preserves surrogate pairs and combining marks");
            MacroLibrary library = new MacroLibrary();
            SavedMacro macro = new SavedMacro { Name = new string('a', 75) + "\uD83D\uDE00", Duration = 10 };
            macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65, At = 0 });
            macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyUp, Code = 65, At = 10 }); library.Items.Add(macro);
            string copy = MacroEditing.UniqueName(library, macro.Name);
            Check(copy.Length <= 80 && !copy.Contains("\uD83D"), "duplicate suffix does not split a surrogate pair at the limit");
            string path = Path.Combine(AppInfo.DataDirectory, "shared-validation.json");
            try
            {
                library.Save(path); string original = File.ReadAllText(path);
                Action[] invalidate = {
                    delegate { macro.Duration = -1; }, delegate { macro.RepeatCount = 0; },
                    delegate { macro.Actions[1].At = -1; }, delegate { macro.Actions[0].Code = 7; },
                    delegate { macro.SpeedPercent = 0; }, delegate { macro.WindowRelative = true; }
                };
                foreach (Action change in invalidate)
                {
                    change(); bool rejected = false;
                    try { library.Save(path); } catch (FormatException) { rejected = true; }
                    Check(rejected && File.ReadAllText(path) == original && MacroLibrary.Load(path).Items.Count == 1, "invalid save preserves readable original data");
                    macro.Duration = 10; macro.RepeatCount = 1; macro.Actions[1].At = 10; macro.Actions[0].Code = 65; macro.SpeedPercent = 100; macro.WindowRelative = false;
                }
            }
            finally { foreach (string file in new string[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(file)) File.Delete(file); }
        }
        internal static void RunUI()
        {
            using (RichTextBox box = new RichTextBox())
            {
                ReleaseNotesFormat.Render(box, "### Changes\n- **Fixed** interval 0.10\\~10\n- Unicode 한국어 日本語");
                Check(box.Text.StartsWith("Changes\n• Fixed") && !box.Text.Contains("###") && box.Text.Contains("0.10~10"), "readable Markdown headings and bullets");
                box.Select(0, 7); Check(box.SelectionFont.Bold && box.SelectionColor == Theme.Accent, "heading visually styled");
            }
            VerifyNotesReopen(); VerifyPreferenceBatch(); VerifyCompactUndo(); VerifyBackgroundCleanup(); VerifyDirectDisposal(); VerifyViewport(); VerifyForcedFileDialog(); VerifyResetUI();
        }
        static void VerifyResetKeys()
        {
            Preferences ordinary = Preferences.ResetDefaults(new Keys[0]);
            Check(ordinary.Macro == Keys.Space && ordinary.Toggle == Keys.F6 && ordinary.Stop == Keys.F8, "unreserved reset keeps ordinary defaults");
            Keys[] reserved = { Keys.Space, Keys.F6, Keys.F8, Keys.A, Keys.F1 };
            Preferences adjusted = Preferences.ResetDefaults(reserved);
            HashSet<Keys> used = new HashSet<Keys>(reserved);
            foreach (Keys key in new Keys[] { adjusted.Macro, adjusted.Toggle, adjusted.Stop })
                Check(InputRules.ValidShortcut(key) && used.Add(key), "reset chooses distinct keys outside saved run shortcuts");
            string path = Path.Combine(AppInfo.DataDirectory, "reset-keys.json");
            try
            {
                new Preferences { Interval = 222 }.Save(path); StartupNotice.SaveAcknowledgement(path);
                SettingsStore.Reset(path, adjusted); Preferences loaded = Preferences.Load(path);
                Check(loaded.Macro == adjusted.Macro && loaded.Toggle == adjusted.Toggle && loaded.Stop == adjusted.Stop && loaded.Interval == 100 && !StartupNotice.IsAcknowledged(path), "adjusted reset keys persist with other defaults");
            }
            finally { foreach (string file in Directory.GetFiles(AppInfo.DataDirectory, "reset-keys.json*")) File.Delete(file); }
        }
        static void VerifyResetUI()
        {
            string original = AppInfo.TestDataDirectory;
            string directory = Path.Combine(original, "reset-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            AppInfo.TestDataDirectory = directory;
            try
            {
                MacroLibrary library = new MacroLibrary { StopKey = Keys.F12 };
                foreach (Keys key in new Keys[] { Keys.Space, Keys.F6, Keys.F8 })
                {
                    SavedMacro macro = new SavedMacro { Name = key.ToString(), RunKey = key, Duration = 10 };
                    macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = 0, X = 10, Y = 10 }); library.Items.Add(macro);
                }
                library.Save(AppInfo.LibraryPath); string contents = File.ReadAllText(AppInfo.LibraryPath);
                new Preferences { Macro = Keys.A, Toggle = Keys.F10, Stop = Keys.F11 }.Save(AppInfo.SettingsPath);
                using (MainForm form = new MainForm(true))
                using (System.Windows.Forms.Timer confirm = new System.Windows.Forms.Timer { Interval = 20 })
                {
                    form.Show(); Application.DoEvents();
                    confirm.Tick += delegate
                    {
                        foreach (Form dialog in Application.OpenForms) if (dialog != form)
                            foreach (Control control in dialog.Controls) if (control is Button && ((Button)control).DialogResult == DialogResult.Yes)
                            { confirm.Stop(); ((Button)control).PerformClick(); return; }
                    };
                    confirm.Start(); form.GetType().GetMethod("ResetSettings", Private).Invoke(form, null);
                    Preferences reset = Preferences.Load(AppInfo.SettingsPath);
                    Check(reset.Macro != Keys.Space && reset.Toggle != Keys.F6 && reset.Stop != Keys.F8, "UI reset persists collision-free clicker keys");
                    Check(File.ReadAllText(AppInfo.LibraryPath) == contents, "reset leaves saved macro data byte-for-byte unchanged"); form.Close();
                }
                using (MainForm reopened = new MainForm(true))
                {
                    MacroWorkspace page = Field<MacroWorkspace>(reopened, "macroWorkspace");
                    MacroLibrary loaded = Field<MacroLibrary>(page, "library");
                    foreach (SavedMacro macro in loaded.Items)
                        Check((bool)page.GetType().GetMethod("RunKeyAvailable", Private).Invoke(page, new object[] { macro }), "saved macro shortcut remains available after reopening");
                }
            }
            finally { AppInfo.TestDataDirectory = original; Directory.Delete(directory, true); }
        }
        static void VerifyOffscreenInput()
        {
            Rectangle screen = new Rectangle(-1920, 0, 3840, 1080);
            Rectangle[] monitors = { new Rectangle(-1920, 0, 1920, 1080), new Rectangle(0, 300, 1920, 1080) };
            Check(Playback.OnMonitor(-100, 20, monitors) && !Playback.OnMonitor(100, 20, monitors), "monitor gaps are not treated as visible screen positions");
            foreach (Point point in new Point[] { new Point(-1921, 0), new Point(1920, 0), new Point(0, -1), new Point(0, 1080) })
            {
                bool rejected = false;
                try { Playback.Convert(new MacroAction { Kind = ActionKind.MouseDown, Code = 1, X = point.X, Y = point.Y }, screen); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "offscreen input is rejected instead of clamped");
            }
            Check(Playback.Convert(new MacroAction { Kind = ActionKind.Move, X = -1920, Y = 0 }, screen).data.mouse.dx == 0, "negative monitor coordinates remain valid");
            SavedMacro macro = new SavedMacro();
            macro.Actions.Add(new MacroAction { Kind = ActionKind.KeyDown, Code = 65 });
            macro.Actions.Add(new MacroAction { Kind = ActionKind.MouseDown, Code = 1, X = Int32.MinValue, Y = 0 });
            List<Native.Input> sent = new List<Native.Input>(); bool stopped = false;
            using (ManualResetEvent cancel = new ManualResetEvent(false))
                try { Playback.Run(macro, cancel, delegate(Native.Input input) { sent.Add(input); }, null, false); }
                catch (InvalidOperationException) { stopped = true; }
            Check(stopped && sent.Count == 2 && sent[0].type == 1 && (sent[1].data.keyboard.flags & 2) != 0, "offscreen mouse input stops playback and releases already held keyboard input");
        }
        static void VerifyViewport()
        {
            using (MainForm form = new MainForm(false))
            {
                form.Show(); Application.DoEvents(); form.FitWorkArea(new Rectangle(0, 0, 800, 600)); Application.DoEvents();
                Panel viewport = Field<Panel>(form, "viewport");
                Check(form.Height <= 600 && viewport.AutoScroll && viewport.VerticalScroll.Visible, "small working area fits the window and enables scrolling");
                viewport.AutoScrollPosition = new Point(0, 1000); Application.DoEvents();
                Button stop = Field<Button>(form, "stop");
                Check(stop.Bottom <= viewport.ClientSize.Height && stop.Top >= 0, "bottom clicker controls can be scrolled into view");
                form.Close();
            }
            Check(MainForm.ViewportSize(new Size(930, 1317), new Size(1280, 720), new Size(16, 40)).Height == 680, "scaled window respects available screen height");
            string previous = L.Current;
            try
            {
                foreach (string language in new string[] { "en", "ja" })
                {
                    L.Current = language;
                    using (SettingsRecoveryDialog recovery = new SettingsRecoveryDialog("unused"))
                        foreach (Control control in recovery.Controls)
                            if (control is Button && ((Button)control).DialogResult == DialogResult.Cancel)
                                Check(control.Text == (language == "en" ? "Exit" : "終了"), "recovery exit button is translated");
                }
            }
            finally { L.Current = previous; }
        }
        static void VerifyForcedFileDialog()
        {
            using (Form owner = new Form())
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ManualResetEvent finished = new ManualResetEvent(false))
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                int entered = 0; owner.Show();
                timer.Tick += delegate
                {
                    if (Thread.VolatileRead(ref entered) == 0) return;
                    foreach (Form form in Application.OpenForms) if (form is FileWorkDialog)
                    {
                        timer.Stop(); ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(100); release.Set(); }); form.Dispose(); return;
                    }
                };
                timer.Start();
                int result = FileWorkDialog.Run(owner, delegate { Interlocked.Exchange(ref entered, 1); if (!release.WaitOne(3000)) throw new TimeoutException(); finished.Set(); return 42; });
                Check(result == 42 && finished.WaitOne(0), "disposed progress dialog waits for the real file operation result"); owner.Close();
            }
        }
        static void VerifyDirectDisposal()
        {
            MainForm form = new MainForm(false); form.Show(); Application.DoEvents();
            System.Windows.Forms.Timer timer = Field<System.Windows.Forms.Timer>(form, "refresh");
            form.Dispose(); form.Dispose(); Pump(200);
            Check(form.IsDisposed && !timer.Enabled, "direct disposal stops timers and is safe to repeat");
            MainForm hidden = new MainForm(false); hidden.Dispose(); Check(!hidden.IsHandleCreated, "disposing an unopened form does not create a window handle");
        }
        static void VerifyNotesReopen()
        {
            using (UpdateNotesDialog seed = new UpdateNotesDialog(new ReleaseUpdate { Version = new Version(AppInfo.Version), Notes = "### Changes\n- cached offline changes" })) { seed.Show(); Application.DoEvents(); seed.Close(); }
            using (UpdateDialog parent = new UpdateDialog())
            using (System.Windows.Forms.Timer close = new System.Windows.Forms.Timer { Interval = 30 })
            {
                bool seen = false;
                close.Tick += delegate
                {
                    foreach (Form form in Application.OpenForms) if (form is UpdateNotesDialog)
                    {
                        Check(form.Text == L.T("업데이트 내용"), "reopened notes title does not claim a new update");
                        Check(Field<RichTextBox>(form, "content").Text.Contains("cached offline changes"), "cached notes reopen offline");
                        seen = true; close.Stop(); form.Close(); break;
                    }
                };
                parent.Show(); close.Start(); parent.Notes.PerformClick(); Check(seen, "update screen reopens current release notes"); parent.Close();
            }
        }
        static void VerifyPreferenceBatch()
        {
            string original = AppInfo.TestDataDirectory;
            string directory = Path.Combine(original, "batch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            AppInfo.TestDataDirectory = directory;
            try
            {
                using (MainForm form = new MainForm(true))
                {
                    form.Show(); NumericUpDown interval = Field<NumericUpDown>(form, "interval");
                    for (int i = 101; i <= 120; i++) interval.Value = i;
                    Check(!File.Exists(AppInfo.SettingsPath), "rapid edits are not immediately written twenty times");
                    Pump(450); Check(Preferences.Load(AppInfo.SettingsPath).Interval == 120, "settled value is persisted");
                    DateTime stamp = File.GetLastWriteTimeUtc(AppInfo.SettingsPath); interval.Value = 121; interval.Value = 122;
                    Check(File.GetLastWriteTimeUtc(AppInfo.SettingsPath) == stamp, "second edit burst deferred");
                    form.Close(); Check(Preferences.Load(AppInfo.SettingsPath).Interval == 122, "closing flushes pending value");
                }
            }
            finally { AppInfo.TestDataDirectory = original; Directory.Delete(directory, true); }
        }
        static void VerifyCompactUndo()
        {
            SavedMacro macro = new SavedMacro { Name = "large edit", Duration = 30000 };
            for (int i = 0; i < 20000; i++) macro.Actions.Add(new MacroAction { Kind = ActionKind.Move, At = i, X = i % 100, Y = 1 });
            using (MacroEditorDialog editor = new MacroEditorDialog(macro))
            {
                editor.Show(); Application.DoEvents(); MacroAction first = editor.Edited.Actions[0], last = editor.Edited.Actions[19999];
                editor.Actions.Items[0].Selected = true; editor.Delay.Value = 10; editor.ApplyDelay.PerformClick();
                Check(editor.Edited.Duration == 30010 && editor.Edited.Actions[19999].At == 20009, "large delay edit applied");
                editor.Undo.PerformClick(); Check(editor.Edited.Duration == 30000 && editor.Edited.Actions[19999].At == 19999 && Object.ReferenceEquals(first, editor.Edited.Actions[0]) && Object.ReferenceEquals(last, editor.Edited.Actions[19999]), "delay undo preserves action objects and restores timing");
                editor.Actions.Items[0].Selected = true; editor.DeleteActions.PerformClick(); Check(editor.Edited.Actions.Count == 0, "large grouped deletion");
                editor.Undo.PerformClick(); Check(editor.Edited.Actions.Count == 20000 && Object.ReferenceEquals(first, editor.Edited.Actions[0]) && Object.ReferenceEquals(last, editor.Edited.Actions[19999]), "deleted actions restored without replacing unaffected objects"); editor.Close();
            }
        }
        static void VerifyBackgroundCleanup()
        {
            int entered = 0, finished = 0, callbacks = 0;
            using (Form owner = new Form())
            using (UiBackgroundWork work = new UiBackgroundWork(owner))
            {
                owner.Show();
                Check(work.Run(delegate(CancellationToken token)
                {
                    Interlocked.Exchange(ref entered, 1);
                    try { token.WaitHandle.WaitOne(2500); token.ThrowIfCancellationRequested(); }
                    finally { work.Post(delegate { callbacks++; }); Interlocked.Exchange(ref finished, 1); }
                }), "background work starts");
                Stopwatch watch = Stopwatch.StartNew();
                while (entered == 0) { Pump(10); Check(watch.ElapsedMilliseconds < 3000, "worker starts"); }
                work.Dispose(); work.Dispose(); owner.Close();
                while (finished == 0) { Pump(10); Check(watch.ElapsedMilliseconds < 3000, "cancelled worker finishes without disposed-token errors"); }
                Pump(50); Check(callbacks == 0 && !work.Run(delegate { callbacks++; }), "closed owner cannot receive late work or callbacks");
            }
        }
    }
}
