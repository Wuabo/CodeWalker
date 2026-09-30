using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodeWalker.DoorEditor.Theme
{
    /// <summary>Dark CodeWalker-inspired palette for Door Tuning.</summary>
    public static class AppTheme
    {
        public static readonly Color Window = Color.FromArgb(18, 20, 26);
        public static readonly Color Background = Color.FromArgb(22, 25, 33);
        public static readonly Color Panel = Color.FromArgb(28, 32, 42);
        public static readonly Color PanelElevated = Color.FromArgb(34, 39, 52);
        public static readonly Color Input = Color.FromArgb(24, 28, 38);
        public static readonly Color InputHover = Color.FromArgb(30, 35, 48);
        public static readonly Color Border = Color.FromArgb(52, 60, 78);
        public static readonly Color BorderFocus = Color.FromArgb(90, 140, 220);
        public static readonly Color Accent = Color.FromArgb(70, 130, 220);
        public static readonly Color AccentHover = Color.FromArgb(90, 150, 235);
        public static readonly Color AccentPressed = Color.FromArgb(50, 105, 185);
        public static readonly Color Bright = Color.FromArgb(246, 248, 252);
        public static readonly Color Foreground = Color.FromArgb(214, 220, 232);
        public static readonly Color Faint = Color.FromArgb(120, 132, 154);
        public static readonly Color Muted = Color.FromArgb(158, 170, 190);
        public static readonly Color BlueBright = Color.FromArgb(130, 175, 255);
        public static readonly Color Warning = Color.FromArgb(230, 190, 90);
        public static readonly Color Danger = Color.FromArgb(210, 90, 90);
        public static readonly Color Success = Color.FromArgb(90, 180, 120);
        public static readonly Color Line = Color.FromArgb(64, 74, 94);
        public static readonly Color OffsetGizmo = Color.FromArgb(80, 210, 230);
        public static readonly Color TriggerGizmo = Color.FromArgb(255, 160, 70);
        public static readonly Color PedGizmo = Color.FromArgb(120, 220, 140);
        public static readonly Color DoorFill = Color.FromArgb(70, 82, 110);
        public static readonly Color DoorEdge = Color.FromArgb(150, 165, 200);
        public static readonly Color ListSelected = Color.FromArgb(48, 78, 128);
        public static readonly Color ListHover = Color.FromArgb(40, 48, 64);
        public static readonly Color CheckMark = Color.FromArgb(120, 190, 255);

        public static readonly Font UiFont = new("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font UiFontBold = new("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font HeaderFont = new("Segoe UI Semibold", 10f, FontStyle.Bold);
        public static readonly Font MonoFont = new("Consolas", 9f, FontStyle.Regular);
        public static readonly Font SmallFont = new("Segoe UI", 8f, FontStyle.Regular);

        public static void FillRoundRect(Graphics g, Brush brush, Rectangle r, int radius)
        {
            using var path = RoundRect(r, radius);
            g.FillPath(brush, path);
        }

        public static void DrawRoundRect(Graphics g, Pen pen, Rectangle r, int radius)
        {
            using var path = RoundRect(r, radius);
            g.DrawPath(pen, path);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => AppTheme.Panel;
        public override Color MenuStripGradientEnd => AppTheme.Panel;
        public override Color ToolStripGradientBegin => AppTheme.PanelElevated;
        public override Color ToolStripGradientMiddle => AppTheme.PanelElevated;
        public override Color ToolStripGradientEnd => AppTheme.PanelElevated;
        public override Color ToolStripBorder => AppTheme.Border;
        public override Color MenuBorder => AppTheme.Border;
        public override Color MenuItemBorder => AppTheme.Accent;
        public override Color MenuItemSelected => AppTheme.ListSelected;
        public override Color MenuItemSelectedGradientBegin => AppTheme.ListSelected;
        public override Color MenuItemSelectedGradientEnd => AppTheme.ListSelected;
        public override Color MenuItemPressedGradientBegin => AppTheme.AccentPressed;
        public override Color MenuItemPressedGradientEnd => AppTheme.AccentPressed;
        public override Color ImageMarginGradientBegin => AppTheme.Panel;
        public override Color ImageMarginGradientMiddle => AppTheme.Panel;
        public override Color ImageMarginGradientEnd => AppTheme.Panel;
        public override Color SeparatorDark => AppTheme.Border;
        public override Color SeparatorLight => AppTheme.Border;
        public override Color StatusStripGradientBegin => AppTheme.Panel;
        public override Color StatusStripGradientEnd => AppTheme.Panel;
        public override Color ButtonSelectedHighlight => AppTheme.ListSelected;
        public override Color ButtonSelectedBorder => AppTheme.Accent;
        public override Color ButtonPressedBorder => AppTheme.Accent;
        public override Color ButtonCheckedHighlight => AppTheme.ListSelected;
    }
}
