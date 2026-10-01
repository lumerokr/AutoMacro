using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class MacroEditing
    {
        internal static SavedMacro Copy(SavedMacro source, bool newIdentity)
        {
            SavedMacro result = new SavedMacro { Id = newIdentity ? Guid.NewGuid().ToString("N") : source.Id,
                Name = source.Name, Created = newIdentity ? DateTime.Now : source.Created, Duration = source.Duration,
                RepeatCount = source.RepeatCount, RepeatForever = source.RepeatForever, RepeatDelayMs = source.RepeatDelayMs };
            foreach (MacroAction action in source.Actions) result.Actions.Add(action.Copy());
            return result;
        }
        internal static string UniqueName(MacroLibrary library, string original)
        {
            string candidate = original; int number = 2;
            while (library.CheckName(candidate, null) != null)
            {
                string suffix = " (" + number++ + ")";
                candidate = original.Substring(0, Math.Min(original.Length, 80 - suffix.Length)) + suffix;
            }
            return candidate;
        }
        internal static bool SetDelay(SavedMacro macro, int index, long delay)
        {
            if (index < 0 || index >= macro.Actions.Count || delay < 0 || delay > 86400000) return false;
            long previous = index == 0 ? 0 : macro.Actions[index - 1].At;
            long delta = delay - (macro.Actions[index].At - previous);
            if (macro.Duration + delta < 0 || macro.Duration + delta > 86400000) return false;
            for (int i = index; i < macro.Actions.Count; i++) macro.Actions[i].At += delta;
            macro.Duration += delta; return true;
        }
        internal static void Delete(SavedMacro macro, IList<int> indices)
        {
            // Keep the timing of remaining actions, including the original trailing wait.
            HashSet<int> selected = new HashSet<int>(indices);
            List<MacroAction> remaining = new List<MacroAction>();
            for (int i = 0; i < macro.Actions.Count; i++)
                if (!selected.Contains(i)) remaining.Add(macro.Actions[i]);
            macro.Actions = remaining;
        }
    }
    internal sealed class MacroEditList : DarkListView
    {
        internal Func<int, bool> IsSummary;
        internal Action<int> ToggleRequested;
        int pressedRow = -1;
        bool skipRelease;
        int ButtonAt(Point point)
        {
            ListViewHitTestInfo hit = HitTest(point);
            if (hit.Item == null || hit.SubItem != hit.Item.SubItems[0] || IsSummary == null || !IsSummary(hit.Item.Index)) return -1;
            Rectangle bounds = hit.Item.Bounds; bounds.Width = Columns[0].Width;
            return ToggleBounds(bounds).Contains(point) ? hit.Item.Index : -1;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x201 || m.Msg == 0x203)
            {
                Point point = new Point((short)(m.LParam.ToInt64() & 65535), (short)((m.LParam.ToInt64() >> 16) & 65535));
                int row = ButtonAt(point);
                if (row >= 0) { pressedRow = row; skipRelease = m.Msg == 0x203; Capture = true; return; }
            }
            if (m.Msg == 0x202 && pressedRow >= 0)
            {
                int row = pressedRow; pressedRow = -1; Capture = false;
                Point point = new Point((short)(m.LParam.ToInt64() & 65535), (short)((m.LParam.ToInt64() >> 16) & 65535));
                if (!skipRelease && ButtonAt(point) == row && ToggleRequested != null) ToggleRequested(row);
                skipRelease = false; return;
            }
            if (m.Msg == 0x215) pressedRow = -1;
            base.WndProc(ref m);
        }
    }
    internal sealed class MacroEditorDialog : Form
    {
        internal readonly SavedMacro Edited;
        internal readonly MacroEditList Actions = new MacroEditList();
        internal MacroLogModel Model;
        bool rebuilding, toggleQueued;
        internal readonly NumericUpDown Delay = new NumericUpDown();
        internal readonly Button ApplyDelay = new ModernButton(), DeleteActions = new ModernButton(), Save = new ModernButton();
        internal readonly Button Undo = new ModernButton();
        sealed class Snapshot
        {
            internal SavedMacro Macro;
            internal readonly List<int> Expanded = new List<int>(), Selected = new List<int>();
        }
        readonly List<Snapshot> history = new List<Snapshot>();
        readonly Label status = new Label();
        internal MacroEditorDialog(SavedMacro source)
        {
            Edited = MacroEditing.Copy(source, false); Model = new MacroLogModel(Edited);
            Text = L.T("녹화 내용 편집"); ClientSize = new Size(820, 520);
            BackColor = Theme.Background; ForeColor = Theme.Ink; Font = new Font("맑은 고딕", 10);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            Label title = new Label { Text = source.Name, AutoEllipsis = true }; title.SetBounds(20, 18, 780, 26); Controls.Add(title);
            Actions.SetBounds(20, 60, 780, 314); Actions.View = View.Details; Actions.VirtualMode = true; Actions.MultiSelect = true;
            Actions.Columns.Add("", 40); Actions.Columns.Add(L.T("순서"), 88); Actions.Columns.Add(L.T("시간 (ms)"), 112);
            Actions.Columns.Add(L.T("입력"), 326); Actions.Columns.Add(L.T("화면 위치"), 220); Controls.Add(Actions);
            Actions.RetrieveVirtualItem += delegate(object sender, RetrieveVirtualItemEventArgs e)
            {
                e.Item = new ListViewItem(e.ItemIndex >= 0 && e.ItemIndex < Model.Rows.Count ? Model.Cells(e.ItemIndex) : new string[] { "", "", "", "", "" });
            };
            Actions.IsSummary = delegate(int index) { return index >= 0 && index < Model.Rows.Count && Model.Rows[index].Summary; };
            Actions.ToggleRequested = QueueToggle;
            Actions.SelectedIndexChanged += delegate { UpdateSelection(); };
            // Virtual lists notify range selections separately (Shift-click and Shift-arrow).
            Actions.VirtualItemsSelectionRangeChanged += delegate { UpdateSelection(); };
            Actions.ItemSelectionChanged += delegate { UpdateSelection(); };
            Actions.SelectionOverride = delegate(int index)
            {
                if (index < 0 || index >= Model.Rows.Count) return false;
                LogRow row = Model.Rows[index];
                return Actions.SelectedIndices.Contains(index) || row.Group.IsGroup && Actions.SelectedIndices.Contains(row.Group.SummaryRow);
            };
            Label wait = new Label { Text = L.T("이 동작 전 대기 (ms)") }; wait.SetBounds(20, 392, 205, 28); Controls.Add(wait);
            Delay.Minimum = 0; Delay.Maximum = 86400000; Delay.ThousandsSeparator = true; Delay.BackColor = Theme.Field; Delay.ForeColor = Theme.Ink;
            Delay.SetBounds(224, 389, 170, 30); Controls.Add(Delay);
            Add(ApplyDelay, L.T("시간 적용"), 408, 386, 120, false); Add(DeleteActions, L.T("선택 동작 삭제"), 542, 386, 258, false);
            status.ForeColor = Theme.Muted; status.SetBounds(20, 432, 780, 30); Controls.Add(status);
            Add(Undo, L.T("실행 취소"), 20, 470, 160, false); Undo.Enabled = false;
            Undo.Click += delegate { UndoEdit(); }; KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Control && e.KeyCode == Keys.Z) { UndoEdit(); e.Handled = true; e.SuppressKeyPress = true; } };
            Add(Save, L.T("저장"), 552, 470, 118, true);
            Button cancel = new ModernButton { DialogResult = DialogResult.Cancel }; Add(cancel, L.T("취소"), 682, 470, 118, false); CancelButton = cancel;
            ApplyDelay.Click += delegate
            {
                if (Actions.SelectedIndices.Count != 1) return;
                Delay.Validate(); int index = Model.Rows[Actions.SelectedIndices[0]].Index;
                if ((long)Delay.Value == Edited.Actions[index].At - (index == 0 ? 0 : Edited.Actions[index - 1].At)) return;
                Snapshot before = CaptureSnapshot();
                if (!MacroEditing.SetDelay(Edited, index, (long)Delay.Value)) { status.Text = L.T("전체 길이는 24시간을 넘을 수 없습니다."); return; }
                Remember(before);
                Actions.Invalidate(); status.Text = L.T("시간을 수정했습니다. 저장을 눌러 반영하세요."); UpdateSelection();
            };
            DeleteActions.Click += delegate
            {
                HashSet<int> selected = new HashSet<int>();
                foreach (int rowIndex in Actions.SelectedIndices)
                {
                    LogRow row = Model.Rows[rowIndex];
                    if (row.Summary) for (int i = row.Group.First; i <= row.Group.Last; i++) selected.Add(i);
                    else selected.Add(row.Index);
                }
                if (selected.Count == 0) return;
                Remember(CaptureSnapshot());
                List<MacroAction> expanded = new List<MacroAction>();
                foreach (LogGroup group in Model.Groups) if (group.Expanded) expanded.Add(Edited.Actions[group.First]);
                RebuildList(delegate
                {
                    MacroEditing.Delete(Edited, new List<int>(selected)); Model = new MacroLogModel(Edited);
                    // Restore expanded groups that still have their original first action.
                    foreach (MacroAction first in expanded)
                    {
                        int row = Model.Rows.FindIndex(delegate(LogRow item) { return item.Summary && Edited.Actions[item.Index] == first; });
                        if (row >= 0) Model.Toggle(row);
                    }
                });
                Actions.Invalidate(); status.Text = L.T("동작을 삭제했습니다. 저장을 눌러 반영하세요."); UpdateSelection();
            };
            Save.Click += delegate { if (Edited.Actions.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
            Actions.VirtualListSize = Model.Rows.Count; UpdateSelection();
        }
        Snapshot CaptureSnapshot()
        {
            Snapshot snapshot = new Snapshot { Macro = MacroEditing.Copy(Edited, false) };
            foreach (LogGroup group in Model.Groups) if (group.Expanded) snapshot.Expanded.Add(group.First);
            foreach (int row in Actions.SelectedIndices) snapshot.Selected.Add(row);
            return snapshot;
        }
        void Remember(Snapshot snapshot)
        {
            history.Add(snapshot); long actions = 0; foreach (Snapshot item in history) actions += item.Macro.Actions.Count;
            while (history.Count > 1 && (history.Count > 20 || actions > 500000)) { actions -= history[0].Macro.Actions.Count; history.RemoveAt(0); }
            Undo.Enabled = true;
        }
        internal void UndoEdit()
        {
            if (history.Count == 0) return;
            Snapshot previous = history[history.Count - 1]; history.RemoveAt(history.Count - 1);
            RebuildList(delegate
            {
                Edited.Actions = previous.Macro.Actions; Edited.Duration = previous.Macro.Duration; Model = new MacroLogModel(Edited);
                foreach (int first in previous.Expanded)
                {
                    int row = Model.Rows.FindIndex(delegate(LogRow item) { return item.Summary && item.Index == first; });
                    if (row >= 0) Model.Toggle(row);
                }
            });
            foreach (int row in previous.Selected) if (row < Model.Rows.Count) Actions.Items[row].Selected = true;
            Undo.Enabled = history.Count > 0; status.Text = L.T("이전 편집으로 되돌렸습니다."); UpdateSelection();
        }
        void RebuildList(Action change)
        {
            rebuilding = true; Actions.BeginUpdate();
            try
            {
                Actions.SelectedIndices.Clear(); ListViewItem focused = Actions.FocusedItem; if (focused != null) focused.Focused = false;
                Actions.VirtualListSize = 0; change(); Actions.VirtualListSize = Model.Rows.Count;
            }
            finally { Actions.EndUpdate(); rebuilding = false; }
            Actions.Invalidate(); UpdateSelection();
        }
        void QueueToggle(int index)
        {
            if (toggleQueued || !Actions.IsSummary(index)) return;
            LogGroup group = Model.Rows[index].Group; toggleQueued = true;
            BeginInvoke((Action)delegate
            {
                toggleQueued = false; if (IsDisposed || Actions.IsDisposed) return;
                int current = Model.Rows.FindIndex(delegate(LogRow row) { return row.Summary && row.Group == group; });
                if (current >= 0) ToggleGroup(current);
            });
        }
        internal void ToggleGroup(int index)
        {
            if (!Actions.IsSummary(index)) return;
            List<LogRow> selected = new List<LogRow>(); foreach (int row in Actions.SelectedIndices) selected.Add(Model.Rows[row]);
            LogGroup target = Model.Rows[index].Group;
            RebuildList(delegate { Model.Toggle(index); });
            // Preserve other selections; collapsed selected details become the group's summary.
            foreach (LogRow old in selected)
            {
                int row = Model.Rows.FindIndex(delegate(LogRow item)
                { return item.Group == old.Group && (old.Summary ? item.Summary : item.Summary && !item.Group.Expanded || !item.Summary && item.Index == old.Index); });
                if (row >= 0) Actions.Items[row].Selected = true;
            }
            int summary = Model.Rows.FindIndex(delegate(LogRow row) { return row.Summary && row.Group == target; });
            if (summary >= 0) Actions.EnsureVisible(summary);
            UpdateSelection();
        }
        void Add(Button button, string text, int x, int y, int width, bool primary)
        { button.Text = text; button.SetBounds(x, y, width, 34); Theme.Button(button, primary); Controls.Add(button); }
        void UpdateSelection()
        {
            if (rebuilding) return;
            Actions.Invalidate();
            bool single = Actions.SelectedIndices.Count == 1;
            Delay.Enabled = ApplyDelay.Enabled = single; DeleteActions.Enabled = Actions.SelectedIndices.Count > 0; Save.Enabled = Edited.Actions.Count > 0;
            if (single)
            {
                int row = Actions.SelectedIndices[0];
                int index = row < Model.Rows.Count ? Model.Rows[row].Index : -1;
                if (index >= 0 && index < Edited.Actions.Count) Delay.Value = Edited.Actions[index].At - (index == 0 ? 0 : Edited.Actions[index - 1].At);
            }
        }
    }
    internal sealed class MacroProgress : Control
    {
        internal double Fraction;
        internal MacroProgress() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Field);
            using (Brush brush = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * Math.Max(0, Math.Min(1, Fraction))), Height);
        }
    }
}
