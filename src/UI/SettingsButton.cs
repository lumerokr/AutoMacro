using System;
using System.Windows.Forms;
namespace AutoMacro
{
    internal sealed class SettingsButton : Button
    {
        bool hover;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Brush fill = new System.Drawing.SolidBrush(hover ? Theme.Field : Theme.Card)) e.Graphics.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
            float side = Math.Min(Width, Height), cx = Width / 2f, cy = Height / 2f;
            System.Drawing.PointF[] teeth = new System.Drawing.PointF[32];
            for (int i = 0; i < teeth.Length; i++)
            {
                double angle = i * Math.PI * 2 / teeth.Length - Math.PI / 2;
                float radius = side * (i % 4 == 1 || i % 4 == 2 ? .32f : .24f);
                teeth[i] = new System.Drawing.PointF(cx + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
            }
            using (System.Drawing.Pen pen = new System.Drawing.Pen(Enabled ? Theme.Accent : Theme.Muted, Math.Max(1.4f, side / 22)))
            {
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                e.Graphics.DrawPolygon(pen, teeth);
                float radius = side * .105f;
                e.Graphics.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }
            if (Focused && ShowFocusCues) using (System.Drawing.Pen pen = new System.Drawing.Pen(Theme.Accent)) e.Graphics.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
        }
    }
}
