using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class MacroWorkspace : UserControl
    {
        readonly bool persistence;
        readonly string libraryPath;
        MacroLibrary library = new MacroLibrary();
        readonly Button record = new ModernButton(), stop = new ModernButton(), save = new ModernButton(), play = new ModernButton();
        readonly TextBox name = new TextBox();
        readonly Button import = new ModernButton();
        readonly MacroProgress progressBar = new MacroProgress();
        readonly TextBox[] keyNames = { new TextBox(), new TextBox(), new TextBox() };
        readonly Button[] keyButtons = { new ModernButton(), new ModernButton(), new ModernButton() };
        readonly DarkListView list = new DarkListView();
        readonly ContextMenuStrip options = new ContextMenuStrip();
        readonly Label draftInfo = new Label(), message = new Label();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        readonly HashSet<Keys> controlDown = new HashSet<Keys>();
        readonly Stopwatch recordClock = new Stopwatch();
        InputMonitor monitor;
        RecordingBuffer buffer;
        SavedMacro draft;
        InputJob player;
        bool stopping;
        string playbackResult;
        bool active, recording, playing, capturing, libraryReadOnly, limitQueued, fileBusy;
        int playCount;
        long playIteration, waitDeadline;
        int playTotal, actionTotal;
        bool playForever;
        IntPtr ownerHandle;
        internal Action<Native.Input> PlaybackSink = Playback.Send;
        internal event Action StateChanged;
        internal string StateText { get { return fileBusy ? L.T("처리 중") : recording ? L.T("●  녹화 중") : stopping ? L.T("정지 중…") : playing ? L.T("▶  재생 중") : L.T("●  대기 중"); } }
        internal bool Busy { get { return recording || playing || fileBusy; } }

        internal MacroWorkspace(bool persist) : this(persist, AppInfo.LibraryPath) { }
        internal MacroWorkspace(bool persist, string path)
        {
            persistence = persist; libraryPath = path; BackColor = Theme.Card; ForeColor = Theme.Ink; Size = new Size(564, 622);
            Font = new Font("맑은 고딕", 10);
            LabelAt(L.T("녹화"), 20, 14, true);
            AddButton(record, L.T("녹화 시작"), 20, 44, 254, true); AddButton(stop, L.T("긴급 정지"), 288, 44, 256, false);
            name.SetBounds(20, 98, 390, 29); name.BackColor = Theme.Field; name.ForeColor = Theme.Ink; name.MaxLength = 80; name.BorderStyle = BorderStyle.FixedSingle;
            name.Text = NextName(); Controls.Add(name);
            AddButton(save, L.T("녹화 저장"), 424, 94, 120, false);
            draftInfo.SetBounds(20, 134, 524, 28); draftInfo.ForeColor = Theme.Muted; Controls.Add(draftInfo);
            LabelAt(L.T("저장된 매크로"), 20, 179, true);
            AddButton(import, L.T("가져오기"), 424, 173, 120, false); import.Height = 30;
            import.Click += delegate { ImportSelectedFile(); };
            progressBar.SetBounds(20, 164, 524, 5); progressBar.Visible = false; Controls.Add(progressBar);
            list.SetBounds(20, 216, 524, 112); list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.BackColor = Theme.Field; list.ForeColor = Theme.Ink; list.BorderStyle = BorderStyle.None; list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add(L.T("이름"), 264); list.Columns.Add(L.T("길이"), 132); list.Columns.Add(L.T("동작 수"), 128); Controls.Add(list);
            options.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()); options.BackColor = Theme.Card; options.ForeColor = Theme.Ink;
            options.Font = Font; options.ShowImageMargin = false;
            options.Items.Add(L.T("내용 보기"), null, delegate { InspectSelected(); });
            options.Items.Add(L.T("이름 변경"), null, delegate { RenameSelected(); });
            options.Items.Add(L.T("반복 설정"), null, delegate { ConfigureRepeat(); });
            options.Items.Add(L.T("삭제"), null, delegate { DeleteSelected(); });
            options.Items.Add(L.T("녹화 내용 편집"), null, delegate { EditSelected(); });
            options.Items.Add(L.T("복제"), null, delegate { DuplicateSelected(); });
            options.Items.Add(L.T("내보내기"), null, delegate { ExportSelected(); });
            list.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right || Busy || capturing) return;
                ListViewItem row = list.GetItemAt(e.X, e.Y); if (row == null) return;
                list.SelectedItems.Clear(); row.Selected = true; row.Focused = true;
                for (int i = 1; i < options.Items.Count; i++) options.Items[i].Enabled = i == 6 || !libraryReadOnly;
                options.Show(list, e.Location);
            };
            AddButton(play, L.T("선택한 매크로 실행"), 20, 342, 524, true);
            string[] titles = { L.T("녹화 시작 · 종료"), L.T("선택 매크로 실행"), L.T("긴급 정지") };
            for (int i = 0; i < 3; i++)
            {
                int target = i, y = 403 + i * 44;
                LabelAt(titles[i], 20, y + 5, false);
                keyNames[i].SetBounds(188, y, 222, 29); keyNames[i].ReadOnly = true; keyNames[i].TabStop = false; keyNames[i].BackColor = Theme.Field; keyNames[i].ForeColor = Theme.Ink;
                keyNames[i].BorderStyle = BorderStyle.FixedSingle; Controls.Add(keyNames[i]);
                AddButton(keyButtons[i], L.T("설정"), 424, y - 3, 120, false); keyButtons[i].Click += delegate { CaptureShortcut(target); };
            }
            message.SetBounds(20, 546, 524, 60); message.ForeColor = Theme.Muted; Controls.Add(message);
            message.Text = L.T("대상 앱에서 녹화 단축키를 누르세요. 마우스 위치와 키보드 입력을 기록합니다.");
            record.Click += delegate { ToggleRecording(); };
            stop.Click += delegate { EmergencyStop(); };
            save.Click += delegate { SaveDraft(); };
            play.Click += delegate { PlaySelected(); };
            list.SelectedIndexChanged += delegate { UpdateUI(); };
            list.DoubleClick += delegate { InspectSelected(); };
            if (persistence)
            {
                try { library = FileWorkDialog.Run<MacroLibrary>(null, delegate { return MacroLibrary.Load(libraryPath); }); }
                catch (Exception) { libraryReadOnly = true; message.Text = L.T("매크로 파일을 읽지 못했습니다. 원본을 보존했습니다. MacroLibrary.json과 .bak 파일을 확인하세요."); }
            }
            RefreshList(null);
            refresh.Interval = 150; refresh.Tick += delegate { FinishPlayback(); UpdateUI(); };
            refresh.Start(); UpdateUI();
        }
        internal void LanguageChanged(string previous)
        {
            foreach (ToolStripItem item in options.Items) item.Text = L.Convert(item.Text, previous);
            UpdateUI(); list.Invalidate();
        }
        string NextName()
        {
            string root = L.T("매크로 ") + DateTime.Now.ToString("yyyy-MM-dd HHmmss"), value = root; int suffix = 2;
            while (library.CheckName(value, null) != null) value = root + " (" + suffix++ + ")";
            return value;
        }
        void LabelAt(string text, int x, int y, bool bold)
        { Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = bold ? Theme.Ink : Theme.Muted, Font = new Font("맑은 고딕", bold ? 11 : 10, bold ? FontStyle.Bold : FontStyle.Regular) }); }
        void AddButton(Button button, string text, int x, int y, int width, bool primary)
        { button.Text = text; button.SetBounds(x, y, width, 36); Theme.Button(button, primary); Controls.Add(button); }
        SavedMacro Selected { get { return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as SavedMacro; } }
        static string LengthText(long ms) { return TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss") + "." + (ms % 1000).ToString("000"); }
        void RefreshList(string selectedId)
        {
            list.BeginUpdate(); list.Items.Clear();
            foreach (SavedMacro item in library.Items)
            {
                ListViewItem row = new ListViewItem(new string[] { item.Name, LengthText(item.Duration), item.Actions.Count.ToString("N0") }); row.Tag = item; list.Items.Add(row);
                if (item.Id == selectedId) row.Selected = true;
            }
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0) list.Items[0].Selected = true;
            list.EndUpdate(); UpdateUI();
        }
        void UpdateUI()
        {
            record.Text = recording ? L.T("■ 녹화 종료") : L.T("● 녹화 시작");
            record.Enabled = active && monitor != null && !playing && !capturing && !libraryReadOnly;
            stop.Enabled = recording || playing;
            save.Enabled = draft != null && !Busy && !libraryReadOnly;
            name.Enabled = !Busy; list.InputLocked = Busy;
            import.Enabled = !Busy && !capturing && !libraryReadOnly;
            progressBar.Visible = playing;
            play.Enabled = active && monitor != null && Selected != null && !Busy && !capturing;
            for (int i = 0; i < 3; i++) keyButtons[i].Enabled = !Busy && !capturing && !libraryReadOnly;
            keyNames[0].Text = MainForm.KeyName(library.RecordKey); keyNames[1].Text = MainForm.KeyName(library.PlayKey); keyNames[2].Text = MainForm.KeyName(library.StopKey);
            if (recording) draftInfo.Text = L.T("녹화 중  ") + LengthText(recordClock.ElapsedMilliseconds) + "  ·  " + buffer.Actions.Count.ToString("N0") + L.T("개 동작");
            else if (playing)
            {
                long iteration = Interlocked.Read(ref playIteration), deadline = Interlocked.Read(ref waitDeadline);
                int completed = Thread.VolatileRead(ref playCount);
                double remaining = deadline == 0 ? 0 : Math.Max(0, (deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
                draftInfo.Text = String.Format(L.T("{0}/{1}회 · {2}/{3} 동작"), iteration.ToString("N0"), playForever ? "∞" : playTotal.ToString("N0"), completed.ToString("N0"), actionTotal.ToString("N0"));
                if (deadline != 0) draftInfo.Text += " · " + String.Format(L.T("다음 반복까지 {0}초"), remaining.ToString("0.0"));
                progressBar.Fraction = playForever ? completed / (double)Math.Max(1, actionTotal) : ((iteration - 1) + completed / (double)Math.Max(1, actionTotal)) / Math.Max(1, playTotal);
                progressBar.Invalidate();
            }
            else draftInfo.Text = draft == null ? L.T("새 녹화를 만들고 이름을 입력해 저장하세요.") : L.T("저장 전 녹화  ") + LengthText(draft.Duration) + "  ·  " + draft.Actions.Count.ToString("N0") + L.T("개 동작");
            if (StateChanged != null) StateChanged();
        }
        internal void Activate(IntPtr handle)
        { active = true; ownerHandle = handle; InstallMonitor(); UpdateUI(); }
        void InstallMonitor()
        {
            if (monitor != null) monitor.Dispose();
            controlDown.Clear();
            monitor = new InputMonitor(ControlInput, CaptureAction);
            if (!monitor.Install()) { monitor.Dispose(); monitor = null; message.Text = L.T("입력 감지를 시작하지 못했습니다. Clicker로 이동한 후 Macro를 다시 열어주세요."); }
        }
        internal void Deactivate()
        {
            EndRecording(); StopPlayback(); active = false;
            if (monitor != null) { monitor.Dispose(); monitor = null; }
            controlDown.Clear(); UpdateUI();
        }
        bool ControlInput(Keys key, bool down)
        {
            int command = key == library.RecordKey ? 1 : key == library.PlayKey ? 2 : key == library.StopKey ? 3 : 0;
            if (command == 0 || !active || capturing) return false;
            if (down) controlDown.Add(key);
            else if (controlDown.Remove(key)) Native.PostMessage(ownerHandle, 0x8002, (IntPtr)command, (IntPtr)(int)key);
            return true;
        }
        internal void HandleCommand(int command, Keys key)
        {
            if (!active || capturing || options.Visible || !FindForm().Enabled) return;
            if (command == 4) { if (recording && limitQueued) { EndRecording(); message.Text = L.T("녹화 한도에 도달해 종료했습니다. 녹화 내용을 저장하세요."); } return; }
            Keys expected = command == 1 ? library.RecordKey : command == 2 ? library.PlayKey : library.StopKey;
            if (key != expected) return;
            if (command == 1) ToggleRecording(); else if (command == 2) PlaySelected(); else if (command == 3) EmergencyStop();
        }
        void CaptureAction(MacroAction action)
        {
            if (!recording || limitQueued) return;
            long at = recordClock.ElapsedMilliseconds;
            if (buffer.Actions.Count >= 200000 || at >= 86400000)
            { limitQueued = true; Native.PostMessage(ownerHandle, 0x8002, (IntPtr)4, IntPtr.Zero); return; }
            buffer.Add(action, at);
        }
        void ToggleRecording()
        {
            if (!active || monitor == null || playing || capturing || libraryReadOnly) return;
            if (recording) { EndRecording(); return; }
            if (draft != null && L.Confirm(FindForm(), L.T("아직 저장하지 않은 녹화가 있습니다. 새 녹화로 바꿀까요?"), false) != DialogResult.Yes) return;
            draft = null; buffer = new RecordingBuffer(); limitQueued = false; name.Text = NextName();
            recordClock.Restart(); recording = true;
            message.Text = L.T("녹화 중입니다. ") + MainForm.KeyName(library.RecordKey) + L.T("로 종료한 뒤 이름을 입력하고 저장하세요."); UpdateUI();
        }
        void EndRecording()
        {
            if (!recording) return;
            recordClock.Stop(); recording = false;
            SavedMacro captured = buffer.Finish(Math.Min(86400000, recordClock.ElapsedMilliseconds));
            draft = captured.Actions.Count > 0 ? captured : null;
            message.Text = draft == null ? L.T("기록된 동작이 없습니다. 대상 앱에서 입력한 뒤 다시 종료하세요.") : L.T("녹화가 끝났습니다. 이름을 입력하고 ‘녹화 저장’을 누르세요.");
            UpdateUI();
        }
        bool SaveLibrary()
        {
            if (libraryReadOnly) return false;
            try
            {
                if (!persistence) { library.ValidateCapacity(); return true; }
                return RunFileWork(delegate { library.Save(libraryPath); return true; });
            }
            catch (LibraryLimitException error) { message.Text = error.Message; return false; }
            catch (Exception) { message.Text = L.T("저장하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요. 변경 사항은 적용하지 않았습니다."); return false; }
        }
        T RunFileWork<T>(Func<T> work)
        {
            if (fileBusy) throw new InvalidOperationException();
            bool suspended = active && monitor != null;
            if (suspended) { monitor.Dispose(); monitor = null; }
            fileBusy = true; UpdateUI();
            try { return FileWorkDialog.Run(FindForm(), work); }
            finally { fileBusy = false; if (suspended && active && !capturing) InstallMonitor(); UpdateUI(); }
        }
        bool SaveDraft()
        {
            if (draft == null || Busy) return false;
            string error = library.CheckName(name.Text, null);
            if (error != null) { message.Text = error; return false; }
            draft.Name = name.Text.Trim(); library.Items.Add(draft);
            if (!SaveLibrary()) { library.Items.Remove(draft); return false; }
            string id = draft.Id; draft = null; RefreshList(id); message.Text = L.T("저장했습니다. 목록에서 선택하고 실행할 수 있습니다."); return true;
        }
        void RenameSelected()
        {
            SavedMacro selected = Selected; if (selected == null || Busy || libraryReadOnly) return;
            using (Form dialog = new Form())
            {
                dialog.Text = L.T("이름 변경"); dialog.ClientSize = new Size(440, 142); dialog.Font = Font; dialog.BackColor = Theme.Card; dialog.ForeColor = Theme.Ink;
                dialog.StartPosition = FormStartPosition.CenterParent; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = dialog.MinimizeBox = false;
                TextBox input = new TextBox { Text = selected.Name, MaxLength = 80, BackColor = Theme.Field, ForeColor = Theme.Ink };
                input.SetBounds(20, 22, 400, 30); dialog.Controls.Add(input);
                Label error = new Label { ForeColor = Theme.Muted }; error.SetBounds(20, 60, 280, 60); dialog.Controls.Add(error);
                Button ok = new ModernButton { Text = L.T("저장") }; ok.SetBounds(320, 86, 100, 36); Theme.Button(ok, true); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                ok.Click += delegate
                {
                    string invalid = library.CheckName(input.Text, selected);
                    if (invalid != null) { error.Text = invalid; return; }
                    string previous = selected.Name; selected.Name = input.Text.Trim();
                    if (!SaveLibrary()) { selected.Name = previous; error.Text = L.T("저장하지 못했습니다. 다시 시도하세요."); return; }
                    dialog.DialogResult = DialogResult.OK; dialog.Close();
                };
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { RefreshList(selected.Id); message.Text = L.T("이름을 변경했습니다."); }
            }
        }
        void InspectSelected()
        {
            SavedMacro selected = Selected; if (selected == null || Busy) return;
            using (MacroLogDialog dialog = new MacroLogDialog(selected)) dialog.ShowDialog(FindForm());
        }
        internal bool RestoreBackup(string path)
        {
            if (Busy || capturing || !File.Exists(path) || String.Equals(Path.GetFullPath(path), Path.GetFullPath(libraryPath), StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                MacroLibrary restored = RunFileWork(delegate
                {
                    MacroLibrary value = MacroLibrary.Load(path);
                    if (persistence) { RecoveryFiles.Preserve(libraryPath); value.Save(libraryPath, false); }
                    return value;
                });
                library = restored; libraryReadOnly = false; controlDown.Clear(); RefreshList(null);
                message.Text = L.T("매크로 백업을 복원했습니다."); return true;
            }
            catch (Exception) { message.Text = L.T("백업을 복원하지 못했습니다. 기존 파일과 목록은 유지됩니다."); return false; }
            finally { UpdateUI(); }
        }
        internal void RestoreBackupDialog()
        {
            if (Busy || capturing) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = L.T("매크로 백업 복원"), Filter = "Backup (*.bak)|*.bak|JSON (*.json)|*.json", FileName = Path.GetFileName(libraryPath) + ".bak", InitialDirectory = Path.GetDirectoryName(libraryPath), CheckFileExists = true })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (L.Confirm(FindForm(), L.T("현재 매크로 목록과 단축키를 백업 내용으로 바꿀까요? 현재 파일은 별도로 보존합니다."), false) != DialogResult.Yes) return;
                RestoreBackup(dialog.FileName);
            }
        }
        bool ReplaceMacro(SavedMacro selected, SavedMacro edited)
        {
            if (Busy || libraryReadOnly || edited.Actions.Count == 0 || MacroEditing.PairError(edited) != null) return false;
            int index = library.Items.IndexOf(selected); if (index < 0) return false;
            library.Items[index] = edited;
            if (!SaveLibrary()) { library.Items[index] = selected; return false; }
            RefreshList(edited.Id); return true;
        }
        void EditSelected()
        {
            SavedMacro selected = Selected; if (selected == null || Busy || libraryReadOnly) return;
            using (MacroEditorDialog dialog = new MacroEditorDialog(selected))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (ReplaceMacro(selected, dialog.Edited)) message.Text = L.T("녹화 내용을 저장했습니다.");
            }
        }
        bool AddCopies(IList<SavedMacro> sources)
        {
            if (Busy || libraryReadOnly || sources.Count == 0) return false;
            List<SavedMacro> added;
            try { added = RunFileWork(delegate { return MacroEditing.CopyBatch(library, sources); }); }
            catch (LibraryLimitException error) { message.Text = error.Message; return false; }
            catch (Exception) { message.Text = L.T("매크로 JSON 파일을 읽지 못했습니다. 기존 목록은 유지됩니다."); return false; }
            library.Items.AddRange(added);
            if (!SaveLibrary()) { foreach (SavedMacro copy in added) library.Items.Remove(copy); return false; }
            RefreshList(added[0].Id); return true;
        }
        void DuplicateSelected()
        {
            SavedMacro selected = Selected; if (selected == null || Busy || libraryReadOnly) return;
            SavedMacro copy = new SavedMacro { Name = selected.Name, Actions = selected.Actions, Duration = selected.Duration,
                RepeatCount = selected.RepeatCount, RepeatForever = selected.RepeatForever, RepeatDelayMs = selected.RepeatDelayMs };
            copy.Name = selected.Name.Substring(0, Math.Min(selected.Name.Length, 73)) + L.T(" 복사본");
            if (copy.Name.Length > 80) copy.Name = copy.Name.Substring(0, 80);
            if (AddCopies(new SavedMacro[] { copy })) message.Text = L.T("매크로를 복제했습니다.");
        }
        internal bool ImportFile(string path)
        {
            if (Busy || libraryReadOnly || !File.Exists(path)) return false;
            try
            {
                MacroLibrary incoming = RunFileWork(delegate { return MacroLibrary.Load(path); });
                if (incoming.Items.Count == 0) { message.Text = L.T("파일에 저장된 매크로가 없습니다."); return false; }
                if (!AddCopies(incoming.Items)) return false;
                message.Text = String.Format(L.T("{0}개 매크로를 가져왔습니다."), incoming.Items.Count); return true;
            }
            catch (LibraryLimitException error) { message.Text = error.Message; return false; }
            catch (Exception) { message.Text = L.T("매크로 JSON 파일을 읽지 못했습니다. 기존 목록은 유지됩니다."); return false; }
            finally { UpdateUI(); }
        }
        void ImportSelectedFile()
        {
            if (Busy || libraryReadOnly) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = L.T("매크로 가져오기"), Filter = "JSON (*.json)|*.json", CheckFileExists = true })
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) ImportFile(dialog.FileName);
        }
        internal bool ExportFile(SavedMacro selected, string path)
        {
            if (selected == null || Busy) return false;
            try
            {
                string destination = Path.GetFullPath(path);
                string directory = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string protectedPath in new string[] { libraryPath, AppInfo.SettingsPath })
                    if (String.Equals(destination, Path.GetFullPath(protectedPath), StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(destination, Path.GetFullPath(protectedPath) + ".bak", StringComparison.OrdinalIgnoreCase))
                    { message.Text = L.T("사용자 데이터 파일에는 내보낼 수 없습니다."); return false; }
                RunFileWork(delegate { MacroLibrary exported = new MacroLibrary(); exported.Items.Add(MacroEditing.Copy(selected, false)); exported.Save(path); return true; });
                message.Text = L.T("매크로를 내보냈습니다."); return true;
            }
            catch (Exception) { message.Text = L.T("내보내지 못했습니다. 폴더 권한과 여유 공간을 확인하세요."); return false; }
            finally { UpdateUI(); }
        }
        void ExportSelected()
        {
            SavedMacro selected = Selected; if (selected == null || Busy) return;
            string filename = selected.Name;
            foreach (char c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
            using (SaveFileDialog dialog = new SaveFileDialog { Title = L.T("매크로 내보내기"), Filter = "JSON (*.json)|*.json", DefaultExt = "json", AddExtension = true, FileName = filename + ".json", OverwritePrompt = true })
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) ExportFile(selected, dialog.FileName);
        }
        bool SaveRepeat(SavedMacro selected, int count, bool forever, int delay)
        {
            if (Busy || libraryReadOnly || count < 1 || count > 1000000 || delay < 0 || delay > 86400000) return false;
            int oldCount = selected.RepeatCount, oldDelay = selected.RepeatDelayMs; bool oldForever = selected.RepeatForever;
            selected.RepeatCount = count; selected.RepeatForever = forever; selected.RepeatDelayMs = delay;
            if (!SaveLibrary()) { selected.RepeatCount = oldCount; selected.RepeatForever = oldForever; selected.RepeatDelayMs = oldDelay; return false; }
            message.Text = L.T("반복 설정을 저장했습니다."); return true;
        }
        void ConfigureRepeat()
        {
            SavedMacro selected = Selected; if (selected == null || Busy || libraryReadOnly) return;
            using (Form dialog = new Form())
            {
                dialog.Text = L.T("반복 설정"); dialog.ClientSize = new Size(460, 310); dialog.Font = Font; dialog.BackColor = Theme.Card; dialog.ForeColor = Theme.Ink;
                dialog.StartPosition = FormStartPosition.CenterParent; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = dialog.MinimizeBox = false;
                Label title = new Label { Text = selected.Name, AutoEllipsis = true }; title.SetBounds(22, 20, 416, 28); dialog.Controls.Add(title);
                Label countLabel = new Label { Text = L.T("반복 횟수 설정"), AutoSize = true }; countLabel.Location = new Point(22, 74); dialog.Controls.Add(countLabel);
                NumericUpDown count = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = selected.RepeatCount, ThousandsSeparator = true, BackColor = Theme.Field, ForeColor = Theme.Ink };
                count.SetBounds(224, 70, 180, 30); dialog.Controls.Add(count);
                CheckBox forever = new CheckBox { Text = L.T("계속 반복"), Checked = selected.RepeatForever, AutoSize = true }; forever.Location = new Point(224, 111); dialog.Controls.Add(forever);
                count.Enabled = !forever.Checked; forever.CheckedChanged += delegate { count.Enabled = !forever.Checked; };
                Label waitLabel = new Label { Text = L.T("반복 사이 대기 시간 (ms)") }; waitLabel.SetBounds(22, 162, 190, 44); dialog.Controls.Add(waitLabel);
                NumericUpDown delay = new NumericUpDown { Minimum = 0, Maximum = 86400000, Value = selected.RepeatDelayMs, ThousandsSeparator = true, BackColor = Theme.Field, ForeColor = Theme.Ink };
                delay.SetBounds(224, 158, 180, 30); dialog.Controls.Add(delay);
                Label hint = new Label { Text = L.T("전체 재생 횟수입니다. 마지막 재생 뒤에는 대기하지 않습니다."), ForeColor = Theme.Muted }; hint.SetBounds(22, 208, 416, 42); dialog.Controls.Add(hint);
                Button ok = new ModernButton { Text = L.T("저장") }, cancelButton = new ModernButton { Text = L.T("취소"), DialogResult = DialogResult.Cancel };
                ok.SetBounds(224, 258, 100, 36); cancelButton.SetBounds(338, 258, 100, 36); Theme.Button(ok, true); Theme.Button(cancelButton, false);
                dialog.Controls.Add(ok); dialog.Controls.Add(cancelButton); dialog.AcceptButton = ok; dialog.CancelButton = cancelButton;
                ok.Click += delegate
                {
                    count.Validate(); delay.Validate();
                    if (!SaveRepeat(selected, (int)count.Value, forever.Checked, (int)delay.Value)) { hint.Text = L.T("저장하지 못했습니다. 설정을 확인하고 다시 시도하세요."); return; }
                    dialog.DialogResult = DialogResult.OK; dialog.Close();
                };
                dialog.ShowDialog(FindForm());
            }
        }
        bool DeleteMacro(SavedMacro selected)
        {
            if (Busy || libraryReadOnly) return false;
            int index = library.Items.IndexOf(selected); if (index < 0) return false;
            library.Items.RemoveAt(index);
            if (!SaveLibrary()) { library.Items.Insert(index, selected); return false; }
            RefreshList(null); message.Text = L.T("매크로를 삭제했습니다."); return true;
        }
        void DeleteSelected()
        {
            SavedMacro selected = Selected; if (selected == null || Busy || libraryReadOnly) return;
            using (Form dialog = new Form())
            {
                dialog.Text = L.T("매크로 삭제"); dialog.ClientSize = new Size(460, 216); dialog.Font = Font; dialog.BackColor = Theme.Card; dialog.ForeColor = Theme.Ink;
                dialog.StartPosition = FormStartPosition.CenterParent; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = dialog.MinimizeBox = false;
                Label question = new Label { Text = L.T("다음 매크로를 정말 삭제할까요?"), AutoSize = true }; question.Location = new Point(22, 22); dialog.Controls.Add(question);
                Label itemName = new Label { Text = selected.Name, ForeColor = Theme.Accent }; itemName.SetBounds(22, 60, 416, 75); dialog.Controls.Add(itemName);
                Label error = new Label { ForeColor = Theme.Muted }; error.SetBounds(22, 139, 416, 24); dialog.Controls.Add(error);
                Button confirm = new ModernButton { Text = L.T("삭제") }, cancelButton = new ModernButton { Text = L.T("취소"), DialogResult = DialogResult.Cancel };
                confirm.SetBounds(224, 172, 100, 32); cancelButton.SetBounds(338, 172, 100, 32); Theme.Button(confirm, false); Theme.Button(cancelButton, true); confirm.ForeColor = Color.FromArgb(255, 139, 148);
                dialog.Controls.Add(confirm); dialog.Controls.Add(cancelButton); dialog.AcceptButton = cancelButton; dialog.CancelButton = cancelButton;
                confirm.Click += delegate
                {
                    if (!DeleteMacro(selected)) { error.Text = L.T("삭제 내용을 저장하지 못했습니다. 다시 시도하세요."); return; }
                    dialog.DialogResult = DialogResult.OK; dialog.Close();
                };
                dialog.ShowDialog(FindForm());
            }
        }
        void PlaySelected()
        {
            SavedMacro selected = Selected; if (!active || monitor == null || selected == null || Busy || capturing) return;
            playing = true; playbackResult = null; playCount = 0; playIteration = 1; waitDeadline = 0; playTotal = selected.RepeatCount; actionTotal = selected.Actions.Count; playForever = selected.RepeatForever;
            message.Text = (selected.RepeatForever ? L.T("계속 반복합니다.") : selected.RepeatCount.ToString("N0") + L.T("회 재생합니다.")) + L.T(" 반복 사이 ") + selected.RepeatDelayMs.ToString("N0") + L.T("ms 대기 · ") + MainForm.KeyName(library.StopKey) + L.T("로 정지"); UpdateUI();
            player = new InputJob(delegate(WaitHandle cancelled)
            {
                string result;
                try { result = Playback.RunRepeated(selected, cancelled, PlaybackSink, delegate(long iteration, int n) { Interlocked.Exchange(ref playIteration, iteration); Interlocked.Exchange(ref playCount, n); Interlocked.Exchange(ref waitDeadline, 0); }, true, delegate(int pause) { Interlocked.Exchange(ref waitDeadline, Stopwatch.GetTimestamp() + (long)pause * Stopwatch.Frequency / 1000); }) ? L.T("재생을 완료했습니다.") : L.T("재생을 정지했습니다."); }
                catch (Exception error) { result = L.T("재생 중단: ") + error.Message; }
                playbackResult = result;
            });
        }
        void StopPlayback()
        {
            if (player != null) { stopping = true; player.Stop(); message.Text = L.T("정지 중…"); }
            FinishPlayback(); UpdateUI();
        }
        void FinishPlayback()
        {
            if (player == null || !player.Completed) return;
            player.Dispose(); player = null; playing = false; stopping = false;
            message.Text = playbackResult ?? L.T("재생을 정지했습니다."); UpdateUI();
        }
        internal void RequestStop() { EmergencyStop(); }
        void EmergencyStop() { EndRecording(); StopPlayback(); }
        void CaptureShortcut(int target)
        {
            if (Busy || capturing || libraryReadOnly) return;
            capturing = true; if (monitor != null) { monitor.Dispose(); monitor = null; } controlDown.Clear();
            try
            {
                using (KeyPicker dialog = new KeyPicker(L.T("Macro 단축키 설정"), delegate(Keys key) { return library.ValidateShortcut(target, key); }, true))
                {
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        Keys old = target == 0 ? library.RecordKey : target == 1 ? library.PlayKey : library.StopKey;
                        SetShortcut(target, dialog.SelectedKey);
                        if (!SaveLibrary()) SetShortcut(target, old); else message.Text = L.T("단축키를 설정하고 저장했습니다.");
                    }
                }
            }
            finally { capturing = false; if (active) InstallMonitor(); UpdateUI(); }
        }
        void SetShortcut(int target, Keys key)
        { if (target == 0) library.RecordKey = key; else if (target == 1) library.PlayKey = key; else library.StopKey = key; }
        internal bool PrepareClose()
        {
            EndRecording(); StopPlayback();
            if (draft == null) return true;
            DialogResult choice = L.Confirm(FindForm(), L.T("저장하지 않은 녹화가 있습니다. 현재 이름으로 저장하고 종료할까요?"), true);
            if (choice == DialogResult.Cancel) return false;
            if (choice == DialogResult.No) return true;
            return SaveDraft();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                active = false; recording = false; StopPlayback();
                if (monitor != null) { monitor.Dispose(); monitor = null; }
                refresh.Stop(); refresh.Dispose(); if (player != null) player.Dispose();
                options.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

