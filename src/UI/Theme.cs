using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoMacro
{
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
}
