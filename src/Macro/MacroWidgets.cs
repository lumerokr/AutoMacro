using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoMacro
{
    internal class DarkListView : ListView
    {
        internal bool InputLocked;
        internal Func<int, bool> SelectionOverride;
        internal bool IsHighlighted(int index, bool selected) { return selected || SelectionOverride != null && SelectionOverride(index); }
        internal static readonly Color Selection = Color.FromArgb(39, 79, 82);
        internal DarkListView()
        {
            DoubleBuffered = true; OwnerDraw = true; BackColor = Theme.Field; ForeColor = Theme.Ink;
            BorderStyle = BorderStyle.None; FullRowSelect = true; HideSelection = false;
            DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (Brush brush = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(brush, e.Bounds);
                Rectangle text = Rectangle.Inflate(e.Bounds, -8, 0);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, text, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using (Pen pen = new Pen(Color.FromArgb(54, 67, 85))) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };
            DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { if (View != View.Details) e.DrawDefault = true; };
            DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                bool selected = IsHighlighted(e.ItemIndex, e.Item.Selected);
                Color fill = selected ? Selection : Theme.Field;
                using (Brush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.ColumnIndex == 0 && (e.SubItem.Text == "+" || e.SubItem.Text == "−"))
                {
                    Rectangle button = ToggleBounds(e.Bounds);
                    using (Pen pen = new Pen(Theme.Muted)) e.Graphics.DrawRectangle(pen, button);
                    // Draw the symbol geometrically so font padding cannot shift its center.
                    float centerX = button.Left + button.Width / 2f, centerY = button.Top + button.Height / 2f;
                    using (Pen symbol = new Pen(Theme.Accent, 2))
                    {
                        e.Graphics.DrawLine(symbol, centerX - 4, centerY, centerX + 4, centerY);
                        if (e.SubItem.Text == "+") e.Graphics.DrawLine(symbol, centerX, centerY - 4, centerX, centerY + 4);
                    }
                    return;
                }
                Rectangle text = Rectangle.Inflate(e.Bounds, -8, 0);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, selected ? Theme.Accent : Theme.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        internal static Rectangle ToggleBounds(Rectangle bounds)
        { return new Rectangle(bounds.Left + (bounds.Width - 18) / 2, bounds.Top + (bounds.Height - 16) / 2, 18, 16); }
        void FillHeader()
        {
            if (Columns.Count == 0) return;
            int used = 0; for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
            int width = Math.Max(80, ClientSize.Width - used);
            if (Columns[Columns.Count - 1].Width != width) Columns[Columns.Count - 1].Width = width;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FillHeader(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FillHeader(); }
        protected override void WndProc(ref Message m)
        {
            // Keep the control enabled to prevent Windows painting a white disabled background.
            if (InputLocked && ((m.Msg >= 0x100 && m.Msg <= 0x109) || (m.Msg >= 0x201 && m.Msg <= 0x209))) return;
            base.WndProc(ref m);
        }
    }
    internal sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuItemSelected { get { return DarkListView.Selection; } }
        public override Color MenuItemBorder { get { return DarkListView.Selection; } }
        public override Color MenuBorder { get { return Theme.Field; } }
        public override Color SeparatorDark { get { return Theme.Field; } }
        public override Color SeparatorLight { get { return Theme.Field; } }
    }
    internal sealed class LogGroup
    {
        internal int First, Last, SummaryRow;
        internal bool Expanded;
        internal bool IsGroup { get { return Last > First; } }
    }
    internal sealed class LogRow
    {
        internal LogGroup Group;
        internal int Index;
        internal bool Summary;
    }
    internal sealed class MacroLogModel
    {
        internal readonly List<LogGroup> Groups = new List<LogGroup>();
        internal readonly List<LogRow> Rows = new List<LogRow>();
        readonly SavedMacro macro;
        internal MacroLogModel(SavedMacro item)
        {
            macro = item;
            for (int i = 0; i < macro.Actions.Count; i++)
            {
                int first = i;
                if (macro.Actions[i].Kind == ActionKind.Move)
                    while (i + 1 < macro.Actions.Count && macro.Actions[i + 1].Kind == ActionKind.Move) i++;
                Groups.Add(new LogGroup { First = first, Last = i });
            }
            Rebuild();
        }
        void Rebuild()
        {
            Rows.Clear();
            foreach (LogGroup group in Groups)
            {
                group.SummaryRow = Rows.Count;
                Rows.Add(new LogRow { Group = group, Index = group.First, Summary = group.IsGroup });
                if (group.IsGroup && group.Expanded)
                    for (int i = group.First; i <= group.Last; i++) Rows.Add(new LogRow { Group = group, Index = i });
            }
        }
        internal void Toggle(int row)
        { if (row >= 0 && row < Rows.Count && Rows[row].Summary) { Rows[row].Group.Expanded = !Rows[row].Group.Expanded; Rebuild(); } }
        internal string[] Cells(int row)
        {
            LogRow value = Rows[row]; MacroAction a = macro.Actions[value.Index];
            if (value.Summary)
            {
                long duration = macro.Actions[value.Group.Last].At - a.At;
                TimeSpan time = TimeSpan.FromMilliseconds(duration);
                string formatted = ((int)time.TotalHours) + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00") + "." + time.Milliseconds.ToString("000");
                return new string[] { value.Group.Expanded ? "−" : "+", (value.Group.First + 1) + "–" + (value.Group.Last + 1), a.At.ToString(), L.T("마우스 이동: 시간 ") + formatted, (value.Group.Last - value.Group.First + 1) + L.T("개 로그") };
            }
            return new string[] { "", (value.Index + 1).ToString(), a.At.ToString(), a.Describe(), a.Kind == ActionKind.KeyDown || a.Kind == ActionKind.KeyUp ? "—" : a.X + ", " + a.Y };
        }
    }
    internal sealed class MacroLogDialog : Form
    {
        internal readonly MacroLogModel Model;
        internal readonly DarkListView EventList = new DarkListView();
        bool toggleQueued, suppressDoubleClickRelease;
        internal MacroLogDialog(SavedMacro selected)
        {
            Model = new MacroLogModel(selected);
            Text = selected.Name + L.T(" · 녹화 내용"); ClientSize = new Size(840, 500); StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Background; ForeColor = Theme.Ink; Font = new Font("맑은 고딕", 10);
            Label summary = new Label { Dock = DockStyle.Top, Height = 48, Text = "  " + selected.Name + "  ·  " + selected.Created.ToString("yyyy-MM-dd HH:mm:ss"), Padding = new Padding(8, 10, 0, 0) };
            EventList.Dock = DockStyle.Fill; EventList.View = View.Details; EventList.VirtualMode = true; EventList.MultiSelect = false;
            EventList.Columns.Add("", 40); EventList.Columns.Add(L.T("순서"), 96); EventList.Columns.Add(L.T("시간 (ms)"), 112); EventList.Columns.Add(L.T("입력"), 368); EventList.Columns.Add(L.T("화면 위치"), 188);
            EventList.RetrieveVirtualItem += delegate(object sender, RetrieveVirtualItemEventArgs e)
            {
                // Windows can finish an outstanding request while a virtual list is resized.
                e.Item = new ListViewItem(e.ItemIndex >= 0 && e.ItemIndex < Model.Rows.Count ? Model.Cells(e.ItemIndex) : new string[] { "", "", "", "", "" });
            };
            EventList.VirtualListSize = Model.Rows.Count;
            EventList.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (suppressDoubleClickRelease) { suppressDoubleClickRelease = false; return; }
                if (e.Clicks > 1) return;
                ListViewHitTestInfo hit = EventList.HitTest(e.Location);
                if (hit.Item != null && hit.SubItem == hit.Item.SubItems[0]) QueueToggle(hit.Item.Index);
            };
            EventList.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                suppressDoubleClickRelease = true;
                ListViewHitTestInfo hit = EventList.HitTest(e.Location);
                // The button column already toggles on its first click.
                if (hit.Item != null && hit.SubItem != hit.Item.SubItems[0]) QueueToggle(hit.Item.Index);
            };
            EventList.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && EventList.SelectedIndices.Count > 0)
                { QueueToggle(EventList.SelectedIndices[0]); e.Handled = true; e.SuppressKeyPress = true; }
            };
            Controls.Add(EventList); Controls.Add(summary);
        }
        void QueueToggle(int index)
        {
            if (toggleQueued || index < 0 || index >= Model.Rows.Count || !Model.Rows[index].Summary) return;
            LogGroup group = Model.Rows[index].Group; toggleQueued = true;
            // Let the native mouse/keyboard message finish before changing its row indices.
            BeginInvoke((Action)delegate
            {
                toggleQueued = false; if (IsDisposed || EventList.IsDisposed) return;
                int current = Model.Rows.FindIndex(delegate(LogRow row) { return row.Summary && row.Group == group; });
                if (current >= 0) ToggleRow(current);
            });
        }
        internal void ToggleRow(int index)
        {
            if (index < 0 || index >= Model.Rows.Count || !Model.Rows[index].Summary) return;
            EventList.BeginUpdate();
            try
            {
                // Clear native selection/focus while the old model still matches the old size.
                EventList.SelectedIndices.Clear();
                ListViewItem focused = EventList.FocusedItem; if (focused != null) focused.Focused = false;
                EventList.VirtualListSize = 0;
                Model.Toggle(index);
                EventList.VirtualListSize = Model.Rows.Count;
                EventList.Items[index].Selected = true; EventList.Items[index].Focused = true;
                EventList.EnsureVisible(index);
            }
            finally { EventList.EndUpdate(); }
            EventList.Invalidate();
        }
    }
}

