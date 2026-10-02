using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class MacroFeatureTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Macro feature test: " + message); }
        static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        static object Invoke(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        static SavedMacro Sample(string name)
        {
            SavedMacro item = new SavedMacro { Name = name, Duration = 200 };
            item.Actions.Add(new MacroAction { At = 100, Kind = ActionKind.KeyDown, Code = (int)Keys.A });
            item.Actions.Add(new MacroAction { At = 200, Kind = ActionKind.KeyUp, Code = (int)Keys.A });
            return item;
        }
        internal static void Run()
        {
            SavedMacro first = Sample("first"), second = Sample("second");
            MacroLibrary library = new MacroLibrary(); library.Items.Add(first); library.Items.Add(second);
            Check(library.ValidateRunKey(first, Keys.F10, new Keys[] { Keys.F6, Keys.F8, Keys.Space }) == null, "available run key");
            Check(library.ValidateRunKey(first, Keys.F7, null) != null && library.ValidateRunKey(first, Keys.F8, null) != null && library.ValidateRunKey(first, Keys.F9, null) != null, "global macro shortcuts reserved");
            Check(library.ValidateRunKey(first, Keys.F6, new Keys[] { Keys.F6 }) != null, "clicker shortcuts reserved");
            Check(library.ValidateRunKey(first, Keys.A, null) != null, "recorded key reserved");
            Check(library.ValidateRunKey(first, Keys.ControlKey, null) != null, "modifier alone rejected");
            first.RunKey = Keys.F10; first.SpeedPercent = 200;
            Check(library.ValidateRunKey(second, Keys.F10, null) != null && library.ValidateShortcut(0, Keys.F10) != null, "duplicates blocked in both directions");
            Check(library.ValidateRunKey(second, Keys.XButton1, null) == null, "thumb shortcut allowed");
            Check(MacroEditing.Copy(first, false).RunKey == Keys.F10 && MacroEditing.Copy(first, true).RunKey == Keys.None, "editing retains and copying clears shortcut");
            Check(MacroEditing.Copy(first, true).SpeedPercent == 200, "copy retains speed");
            Check(Playback.ScaledTime(1001, 200) == 501 && Playback.ScaledTime(100, 50) == 200, "speed scales absolute timing without early rounding");
            first.RepeatCount = 3; first.RepeatDelayMs = 70;
            Check(Playback.TotalMilliseconds(first) == 440, "speed leaves repeat delay unchanged");
            Check(Math.Abs(Playback.ProgressFraction(first, 100, false) - 100.0 / 440) < 0.000001, "progress includes unchanged repeat pauses at changed speed");
            Check(Playback.ProgressFraction(first, 440, false) < 1 && Playback.ProgressFraction(first, 9999, false) < 1 && Playback.ProgressFraction(first, 440, true) == 1, "100 percent requires successful worker completion");
            SavedMacro tail = Sample("trailing wait"); tail.Duration = 1000;
            Check(Playback.ProgressFraction(tail, 200, false) == 0.2, "last input does not complete trailing wait progress");
            tail.RepeatForever = true; tail.RepeatDelayMs = 500;
            Check(Math.Abs(Playback.ProgressFraction(tail, 1200, false) - 0.8) < 0.000001, "infinite iteration includes repeat wait");
            tail.Duration = 0; tail.RepeatForever = false;
            Check(Playback.ProgressFraction(tail, 0, false) == 0 && Playback.ProgressFraction(tail, 0, true) == 1, "zero-duration macro needs completion confirmation");
            first.Duration = 0; first.SpeedPercent = 1000; first.RepeatDelayMs = 0;
            Check(Playback.RepeatPause(first) == 10, "minimum repeat interval at high speed");
            first = Sample("first"); first.RunKey = Keys.F10; first.SpeedPercent = 50; library.Items[0] = first;
            string path = Path.Combine(AppInfo.DataDirectory, "feature-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                library.Save(path, false); SavedMacro restored = MacroLibrary.Load(path).Items[0];
                Check(restored.RunKey == Keys.F10 && restored.SpeedPercent == 50, "new settings persist");
                DataNode data = DataStore.Load(path);
                foreach (DataNode node in data.Elements("Macro")) { node.Attribute("runKey").Remove(); node.Attribute("speedPercent").Remove(); }
                DataStore.Save(path, data, false); restored = MacroLibrary.Load(path).Items[0];
                Check(restored.RunKey == Keys.None && restored.SpeedPercent == 100, "old library defaults");
                foreach (DataNode node in data.Elements("Macro")) node.Add(new DataField("speedPercent", 0));
                DataStore.Save(path, data, false); bool rejected = false; try { MacroLibrary.Load(path); } catch (FormatException) { rejected = true; }
                Check(rejected, "invalid speed rejected on import");
            }
            finally { File.Delete(path); }
            using (ManualResetEvent cancel = new ManualResetEvent(false))
            {
                List<Native.Input> sent = new List<Native.Input>();
                first.SpeedPercent = 1000;
                Check(!Playback.Run(first, cancel, delegate(Native.Input input) { sent.Add(input); cancel.Set(); }, null, true), "cancel timed playback");
                Check(sent.Count == 2 && (sent[1].data.keyboard.flags & 2) != 0, "cancellation releases held key at changed speed");
            }
            using (ManualResetEvent cancel = new ManualResetEvent(false))
            {
                first.SpeedPercent = 200; first.RepeatCount = 2; first.RepeatDelayMs = 70;
                Stopwatch clock = Stopwatch.StartNew(); int sent = 0, waits = 0;
                Check(Playback.RunRepeated(first, cancel, delegate { sent++; }, null, true, delegate(int delay) { Check(delay == 70, "repeat wait unchanged"); waits++; }), "timed repeated playback");
                Check(sent == 4 && waits == 1 && clock.ElapsedMilliseconds >= 240 && clock.ElapsedMilliseconds < 2000, "scaled timing and no final repeat wait");
            }
            VerifyCoordinatesAndSearch();
        }
        static void VerifyCoordinatesAndSearch()
        {
            SavedMacro macro = Sample("window"); macro.WindowRelative = macro.OriginKnown = true; macro.RecordedOriginX = 100; macro.RecordedOriginY = 200;
            Point origin = new Point(-400, 300); bool available = true;
            MacroTarget target = new MacroTarget(delegate { return available; }, delegate { return origin; }, delegate { return new Size(800, 600); });
            MacroAction move = new MacroAction { Kind = ActionKind.Move, X = 125, Y = 240 };
            MacroAction result = target.Resolve(macro, move);
            Check(result.X == -375 && result.Y == 340 && move.X == 125, "legacy coordinates translated without mutating recording");
            move.HasClientPosition = true; move.ClientX = 30; move.ClientY = 50;
            result = target.Resolve(macro, move); Check(result.X == -370 && result.Y == 350, "recorded client coordinates take precedence");
            origin = new Point(600, -100); result = target.Resolve(macro, move);
            Check(result.X == 630 && result.Y == -50, "window movement and negative monitor coordinates followed");
            move.ClientX = 900; bool rejected = false;
            try { target.Resolve(macro, move); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "outside-window input blocked");
            move.ClientX = 30; available = false; rejected = false;
            try { target.Resolve(macro, macro.Actions[0]); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "unavailable target also blocks keyboard input");
            available = true; macro.Duration = 5000; macro.Actions[0].At = 0; macro.Actions[1].At = 5000;
            using (ManualResetEvent cancel = new ManualResetEvent(false))
            {
                List<Native.Input> sent = new List<Native.Input>(); Stopwatch watch = Stopwatch.StartNew(); rejected = false;
                try { Playback.Run(macro, cancel, delegate(Native.Input input) { sent.Add(input); available = false; }, null, true, delegate(MacroAction action) { return target.Resolve(macro, action); }, target.CheckAvailable); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && sent.Count == 2 && (sent[1].data.keyboard.flags & 2) != 0 && watch.ElapsedMilliseconds < 500, "target loss interrupts waits and releases held key");
            }
            available = true; macro.Actions.Clear(); macro.Actions.Add(move); macro.Duration = 0; move.At = 0;
            string path = Path.Combine(AppInfo.DataDirectory, "coordinates-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                MacroLibrary library = new MacroLibrary(); library.Items.Add(macro); library.Save(path, false);
                SavedMacro restored = MacroLibrary.Load(path).Items[0];
                Check(restored.WindowRelative && restored.OriginKnown && restored.RecordedOriginX == 100 && restored.RecordedOriginY == 200 && restored.Actions[0].ClientX == 30 && restored.Actions[0].ClientY == 50, "coordinate settings and actions round trip");
                Check(MacroEditing.Copy(restored, true).OriginKnown && MacroEditing.Copy(restored, false).Actions[0].HasClientPosition, "copy/edit retain client coordinates");
                DataNode data = DataStore.Load(path);
                foreach (DataNode node in data.Elements("Macro")) foreach (DataNode action in node.Elements("Action")) action.Attribute("clientY").Remove();
                DataStore.Save(path, data, false); rejected = false; try { MacroLibrary.Load(path); } catch (FormatException) { rejected = true; }
                Check(rejected, "partial client coordinates rejected");
            }
            finally { File.Delete(path); }
            SavedMacro a = Sample("Zulu"), b = Sample("alpha"), c = Sample("beta");
            a.Modified = new DateTime(2026, 1, 1); b.Modified = new DateTime(2026, 2, 1); c.Modified = new DateTime(2026, 3, 1);
            List<SavedMacro> items = new List<SavedMacro> { a, b, c };
            Check(MacroWorkspace.VisibleItems(items, " ALP ", 0)[0] == b, "case-insensitive partial name search");
            Check(MacroWorkspace.VisibleItems(items, "", 1)[0] == b && MacroWorkspace.VisibleItems(items, "", 2)[0] == c, "name and modified sorting");
            Check(items[0] == a && MacroWorkspace.VisibleItems(items, "missing", 2).Count == 0, "sorting leaves source order and empty search intact");
        }
        internal static void RunUI()
        {
            using (MainForm form = new MainForm(false))
            {
                form.Show(); Invoke(form, "SelectPage", true); Application.DoEvents();
                MacroWorkspace page = Field<MacroWorkspace>(form, "macroWorkspace");
                MacroLibrary library = Field<MacroLibrary>(page, "library");
                VerifyTypingGuards(form, page, library);
                SavedMacro first = Sample("first"), second = Sample("second"); second.Duration = 5000; second.RunKey = Keys.F10; second.SpeedPercent = 200;
                library.Items.Add(first); library.Items.Add(second); Invoke(page, "RefreshList", first.Id);
                page.PlaybackSink = delegate { };
                Invoke(page, "HandleCommand", 5, Keys.F10); Application.DoEvents();
                Check(Field<bool>(page, "playing") && Field<SavedMacro>(page, "playingMacro") == second, "shortcut runs unselected macro");
                Check(Field<Label>(page, "message").Text.Contains(second.Name) && Field<DarkListView>(page, "list").SelectedItems[0].Tag == second, "shortcut makes the running macro clear");
                Invoke(page, "UpdateUI");
                Check(Field<Label>(page, "draftInfo").Text.Contains(L.T("예상 남은 시간 {0} · 현재 동작: {1}").Split('{')[0]), "remaining time displayed");
                page.RequestStop(); Stopwatch clock = Stopwatch.StartNew();
                while (page.Busy) { Application.DoEvents(); Thread.Sleep(10); Check(clock.ElapsedMilliseconds < 3000, "stop stays responsive"); }
                Check(!(bool)Invoke(page, "SaveRunKey", first, Keys.F6), "UI rejects clicker shortcut");
                Check((bool)Invoke(page, "SaveRunKey", first, Keys.XButton1), "UI accepts thumb shortcut");
                Check(Invoke(form, "ValidateCapture", 1, Keys.XButton1) != null, "clicker rejects assigned macro key");
                TextBox search = Field<TextBox>(page, "search"); ComboBox sort = Field<ComboBox>(page, "sort"); DarkListView list = Field<DarkListView>(page, "list");
                search.Text = "SECOND"; PumpSearch(page); Check(list.Items.Count == 1 && list.Items[0].Tag == second, "search selects correct underlying macro");
                search.Text = "missing"; PumpSearch(page); Check(list.Items.Count == 0 && !Field<Button>(page, "play").Enabled, "empty result cannot run selected macro");
                search.Text = ""; second.Modified = DateTime.Now.AddMinutes(1); sort.SelectedIndex = 2; Application.DoEvents();
                Check(list.Items[0].Tag == second && library.Items[0] == first, "UI recent sort preserves storage order");
                Invoke(page, "RefreshList", second.Id);
                Check(!(bool)Invoke(page, "SaveCoordinates", second, true, null), "old macro needs reference origin");
                Check((bool)Invoke(page, "SaveCoordinates", second, true, (Point?)new Point(100, 200)), "coordinate reference accepted");
                Check(second.WindowRelative && second.OriginKnown, "coordinate mode enabled");
                Check((bool)Invoke(page, "SaveCoordinates", second, false, null), "screen mode restored");
                using (Form targetWindow = new Form())
                {
                    targetWindow.Show(); Application.DoEvents(); Point before, after;
                    Check(MacroWindows.TryOrigin(targetWindow.Handle, out before), "real client origin available");
                    targetWindow.Location = new Point(targetWindow.Left + 40, targetWindow.Top + 35); Application.DoEvents();
                    Check(MacroWindows.TryOrigin(targetWindow.Handle, out after) && after.X == before.X + 40 && after.Y == before.Y + 35, "real window movement changes client origin");
                    targetWindow.WindowState = FormWindowState.Minimized; Application.DoEvents();
                    Check(!MacroWindows.TryOrigin(targetWindow.Handle, out after), "minimized window unavailable"); targetWindow.Close();
                }
                VerifyWindowPlayback(form, page);
                VerifyTimedProgress(page);
                foreach (string language in new string[] { "en", "ja", "ko" })
                {
                    form.ApplyLanguage(language);
                    ContextMenuStrip menu = Field<ContextMenuStrip>(page, "options");
                    Check(menu.Items[7].Text == L.T("실행 단축키 설정"), "shortcut menu localized");
                    Check(menu.Items[9].Text == L.T("좌표 기준 설정") && sort.Items[2].ToString() == L.T("최근 수정순"), "coordinates and sorting localized");
                }
                form.Close();
            }
            VerifySortPersistenceAndRollback();
        }
        static void VerifyTypingGuards(MainForm form, MacroWorkspace page, MacroLibrary library)
        {
            Keys record = library.RecordKey, play = library.PlayKey, stop = library.StopKey;
            Func<IntPtr> foreground = page.ForegroundWindow;
            try
            {
                library.RecordKey = Keys.B; library.PlayKey = Keys.C; library.StopKey = Keys.D;
                page.ForegroundWindow = delegate { return form.Handle; };
                foreach (TextBox entry in new TextBox[] { Field<TextBox>(page, "name"), Field<TextBox>(page, "search") })
                {
                    entry.Focus(); Application.DoEvents(); Check(entry.Focused, "entry focused for shortcut regression");
                    foreach (Keys key in new Keys[] { Keys.B, Keys.C })
                    {
                        Check(!(bool)Invoke(page, "ControlInput", key, true) && !(bool)Invoke(page, "ControlInput", key, false), "typing is not swallowed by recording/play shortcut");
                    }
                    Invoke(page, "HandleCommand", 1, Keys.B); Invoke(page, "HandleCommand", 2, Keys.C);
                    Check(!page.Busy, "posted recording/play commands cannot start while typing");
                    Check((bool)Invoke(page, "ControlInput", Keys.D, true), "emergency stop retained in text input");
                    Invoke(page, "HandleCommand", 3, Keys.D); Field<HashSet<Keys>>(page, "controlDown").Clear();
                    page.ForegroundWindow = delegate { return IntPtr.Zero; };
                    Check(!(bool)Invoke(page, "ControlInput", Keys.B, false), "focus change does not swallow an uncaptured key release");
                    Check((bool)Invoke(page, "ControlInput", Keys.B, true), "external application still accepts record shortcut");
                    Field<HashSet<Keys>>(page, "controlDown").Clear();
                    page.ForegroundWindow = delegate { return form.Handle; };
                }
            }
            finally
            {
                library.RecordKey = record; library.PlayKey = play; library.StopKey = stop;
                page.ForegroundWindow = foreground; Field<HashSet<Keys>>(page, "controlDown").Clear();
                Field<DarkListView>(page, "list").Focus();
            }
        }
        static void VerifyTimedProgress(MacroWorkspace page)
        {
            SavedMacro macro = Sample("timed progress"); macro.Actions[0].At = 0; macro.Actions[1].At = 20;
            macro.Duration = 700; macro.RepeatCount = 2; macro.RepeatDelayMs = 250;
            page.PlaybackSink = delegate { };
            Invoke(page, "PlayMacro", macro);
            MacroProgress progress = Field<MacroProgress>(page, "progressBar"); Stopwatch watch = Stopwatch.StartNew();
            while (Field<int>(page, "playCount") < 2) { Application.DoEvents(); Thread.Sleep(5); Check(watch.ElapsedMilliseconds < 3000, "early input completes"); }
            Invoke(page, "UpdateUI"); Check(progress.Fraction < 0.5 && page.Busy, "last action does not fill the bar while trailing wait remains");
            while (ReadDeadline(page) == 0) { Application.DoEvents(); Thread.Sleep(5); Check(watch.ElapsedMilliseconds < 3000, "repeat wait starts"); }
            Invoke(page, "UpdateUI"); double before = progress.Fraction;
            Stopwatch wait = Stopwatch.StartNew(); while (wait.ElapsedMilliseconds < 80) { Application.DoEvents(); Thread.Sleep(5); }
            Invoke(page, "UpdateUI"); Check(progress.Fraction > before && progress.Fraction < 1, "repeat waiting advances time-based progress");
            while (page.Busy) { Application.DoEvents(); Thread.Sleep(5); Check(watch.ElapsedMilliseconds < 4000, "timed playback completes"); }
            Check(progress.Fraction == 1, "success confirms full progress");
            Invoke(page, "PlayMacro", macro); page.RequestStop(); watch.Restart();
            while (page.Busy) { Application.DoEvents(); Thread.Sleep(5); Check(watch.ElapsedMilliseconds < 3000, "cancel progress test completes"); }
            Check(progress.Fraction < 1, "cancelled playback is not marked complete");
        }
        static long ReadDeadline(MacroWorkspace page) { return Field<long>(page, "waitDeadline"); }
        static void PumpSearch(MacroWorkspace page)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (Field<System.Windows.Forms.Timer>(page, "searchRefresh").Enabled)
            { Application.DoEvents(); Thread.Sleep(5); Check(watch.ElapsedMilliseconds < 3000, "search finishes after typing"); }
        }
        static void VerifyWindowPlayback(MainForm form, MacroWorkspace page)
        {
            SavedMacro macro = Sample("window playback"); macro.WindowRelative = macro.OriginKnown = true; macro.Duration = 5000;
            macro.Actions[0].At = 0; macro.Actions[1].At = 5000;
            Field<MacroLibrary>(page, "library").Items.Add(macro); Invoke(page, "RefreshList", macro.Id);
            int sent = 0; page.PlaybackSink = delegate { Interlocked.Increment(ref sent); };
            using (Form target = new Form())
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            {
                target.Show(); Application.DoEvents();
                // Sandboxed tests may have no foreground desktop. Isolate that OS boundary;
                // keep real window movement/minimization and playback scheduling below.
                page.WindowActivator = delegate(IntPtr handle) { return handle == target.Handle; };
                page.TargetFactory = delegate(IntPtr handle)
                {
                    return new MacroTarget(delegate { return CompatibilityProbe.IsWindow(handle) && !CompatibilityProbe.IsIconic(handle); },
                        delegate { Point point; Check(MacroWindows.TryOrigin(handle, out point), "test target origin"); return point; },
                        delegate { return MacroWindows.ClientSize(handle); });
                };
                timer.Interval = 30; timer.Tick += delegate
                {
                    MacroWindowPicker picker = null;
                    foreach (Form open in Application.OpenForms) if (open is MacroWindowPicker) { picker = (MacroWindowPicker)open; break; }
                    if (picker == null) return;
                    timer.Stop(); ListBox windows = Field<ListBox>(picker, "windows"); windows.Items.Clear();
                    windows.Items.Add(new MacroWindows.Choice { Handle = target.Handle, Title = "test target" }); windows.SelectedIndex = 0;
                    ((Button)picker.AcceptButton).PerformClick();
                };
                page.WindowActivator = delegate { return false; };
                timer.Start(); Invoke(page, "PlaySelected");
                Check(!page.Busy && sent == 0, "activation failure sends no input and restores idle state");
                page.WindowActivator = delegate(IntPtr handle) { return handle == target.Handle; };
                timer.Start(); Invoke(page, "PlaySelected");
                Stopwatch watch = Stopwatch.StartNew();
                while (Thread.VolatileRead(ref sent) == 0)
                {
                    Application.DoEvents(); Thread.Sleep(5);
                    Check(watch.ElapsedMilliseconds < 3000, "window picker activates target and starts playback; " + Field<Label>(page, "message").Text + "; playing=" + Field<bool>(page, "playing") + "; foreground=" + CompatibilityProbe.GetForegroundWindow() + "; target=" + target.Handle);
                }
                target.WindowState = FormWindowState.Minimized; Application.DoEvents(); watch.Restart();
                while (page.Busy) { Application.DoEvents(); Thread.Sleep(10); Check(watch.ElapsedMilliseconds < 2000, "minimization stops actual window playback during long wait"); }
                Check(sent == 2, "actual window playback releases held input after target minimizes"); target.Close();
            }
            page.WindowActivator = MacroWindows.Activate; page.TargetFactory = MacroTarget.ForWindow;
        }
        static void VerifySortPersistenceAndRollback()
        {
            string originalDirectory = AppInfo.TestDataDirectory, settings = AppInfo.SettingsPath;
            byte[] originalSettings = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            string path = Path.Combine(AppInfo.DataDirectory, "feature-view-" + Guid.NewGuid().ToString("N") + ".json");
            SavedMacro first = Sample("Zulu"), second = Sample("alpha");
            MacroLibrary library = new MacroLibrary(); library.Items.Add(first); library.Items.Add(second); library.Save(path, false);
            try
            {
                using (MacroWorkspace page = new MacroWorkspace(true, path))
                {
                    Field<ComboBox>(page, "sort").SelectedIndex = 1;
                    Check((int)SettingsStore.Read(settings).Element("MacroSort") == 1, "sort choice persisted in settings");
                }
                using (MacroWorkspace page = new MacroWorkspace(true, path))
                {
                    Check(Field<ComboBox>(page, "sort").SelectedIndex == 1 && Field<DarkListView>(page, "list").Items[0].Tag == Field<MacroLibrary>(page, "library").Items[1], "sort restored on relaunch");
                    SavedMacro selected = Field<MacroLibrary>(page, "library").Items[0]; DateTime oldModified = selected.Modified;
                    page.GetType().GetField("libraryPath", Private).SetValue(page, Path.Combine(path + "-missing", "library.json"));
                    Check(!(bool)Invoke(page, "SaveCoordinates", selected, true, (Point?)new Point(100, 200)), "failed coordinate save rejected");
                    Check(!selected.WindowRelative && !selected.OriginKnown && selected.Modified == oldModified, "coordinate and modified timestamp rollback");
                    Check(!(bool)Invoke(page, "SaveRunKey", selected, Keys.F10) && selected.RunKey == Keys.None && selected.Modified == oldModified, "failed shortcut save rollback");
                    AppInfo.TestDataDirectory = path + "-missing";
                    Field<ComboBox>(page, "sort").SelectedIndex = 2;
                    Check(Field<ComboBox>(page, "sort").SelectedIndex == 1, "failed sort setting save restores selection");
                    AppInfo.TestDataDirectory = originalDirectory;
                }
            }
            finally
            {
                AppInfo.TestDataDirectory = originalDirectory;
                if (originalSettings == null) File.Delete(settings); else File.WriteAllBytes(settings, originalSettings);
                File.Delete(path); File.Delete(path + ".bak");
            }
        }
    }
}
