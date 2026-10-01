using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoMacro
{
    // Decorative hover target: no focus, click action, text glyph, or help cursor.
    internal sealed class HelpIcon : Control
    {
        bool hover;
        internal HelpIcon()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            TabStop = false; Cursor = Cursors.Default; AccessibleRole = AccessibleRole.StaticText;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Card); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState saved = e.Graphics.Save();
            float side = Math.Min(Width, Height);
            e.Graphics.TranslateTransform((Width - side) / 2f, (Height - side) / 2f);
            e.Graphics.ScaleTransform(side / 22f, side / 22f);
            using (Brush brush = new SolidBrush(hover ? Color.FromArgb(49, 62, 81) : Theme.Field)) e.Graphics.FillEllipse(brush, 1, 1, 20, 20);
            using (Pen pen = new Pen(hover ? Theme.Accent : Theme.Muted, 1)) e.Graphics.DrawEllipse(pen, 1, 1, 20, 20);
            using (GraphicsPath question = new GraphicsPath())
            using (Pen pen = new Pen(Theme.Accent, 1.7f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                question.AddBezier(7.6f, 7.5f, 7.6f, 4.5f, 14.4f, 4.5f, 14.4f, 7.5f);
                question.AddBezier(14.4f, 7.5f, 14.4f, 10, 11, 9.5f, 11, 12.2f);
                e.Graphics.DrawPath(pen, question);
            }
            using (Brush brush = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(brush, 10, 15, 2, 2);
            e.Graphics.Restore(saved);
        }
    }
    internal sealed class HelpBubble : Form
    {
        internal string Message;
        internal HelpBubble(string message)
        {
            Message = message; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; Font = new Font("맑은 고딕", 10);
            BackColor = Theme.Field; ForeColor = Theme.Ink; DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; }
        }
        internal void ShowAbove(Control anchor)
        {
            if (IsDisposed || Disposing || anchor == null || anchor.IsDisposed || !anchor.Visible) return;
            Form owner = anchor.FindForm();
            if (owner == null || owner.IsDisposed || owner.Disposing || !owner.Visible) return;
            Font = owner.Font;
            float scale = anchor.DeviceDpi / 96f;
            int pad = (int)(16 * scale), width = (int)(380 * scale);
            int textHeight = TextRenderer.MeasureText(Message, Font, new Size(width - pad * 2, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Size = new Size(width, textHeight + pad * 2);
            using (GraphicsPath path = Theme.Round(ClientRectangle, (int)(12 * scale)))
            { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
            Rectangle bounds = anchor.RectangleToScreen(anchor.ClientRectangle), screen = Screen.FromControl(anchor).WorkingArea;
            Location = new Point(Math.Max(screen.Left, Math.Min(screen.Right - Width, bounds.Left + bounds.Width / 2 - Width / 2)),
                Math.Max(screen.Top, bounds.Top - Height - (int)(8 * scale)));
            // Hover and repeated native notifications may arrive while already visible.
            if (!Visible) Show(owner);
            Invalidate();
        }
        internal void Dismiss() { if (!IsDisposed && !Disposing) Hide(); }
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x21) { message.Result = (IntPtr)3; return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = (int)(16 * DeviceDpi / 96f);
            using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)(12 * DeviceDpi / 96f)))
            using (Pen pen = new Pen(Theme.Accent)) e.Graphics.DrawPath(pen, path);
            TextRenderer.DrawText(e.Graphics, Message, Font, new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2), Theme.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
