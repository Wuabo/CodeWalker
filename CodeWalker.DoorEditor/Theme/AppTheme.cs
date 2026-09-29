using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;

namespace CodeWalker.DoorEditor.Theme
{
    /// <summary>Dark UI palette for the Door Editor.</summary>
    public static class AppTheme
    {
        public static readonly Color Background = Color.FromArgb(22, 25, 33);
        public static readonly Color Activity = Color.FromArgb(18, 21, 28);
        public static readonly Color Panel = Color.FromArgb(28, 32, 42);
        public static readonly Color Panel2 = Color.FromArgb(36, 41, 54);
        public static readonly Color Elevated = Color.FromArgb(24, 28, 37);
        public static readonly Color Hover = Color.FromArgb(44, 52, 68);
        public static readonly Color Active = Color.FromArgb(48, 62, 98);
        public static readonly Color ListSelected = Color.FromArgb(42, 68, 118);
        public static readonly Color Line = Color.FromArgb(64, 74, 94);
        public static readonly Color LineSoft = Color.FromArgb(42, 48, 62);
        public static readonly Color Bright = Color.FromArgb(246, 248, 252);
        public static readonly Color Foreground = Color.FromArgb(214, 220, 232);
        public static readonly Color Faint = Color.FromArgb(120, 132, 154);
        public static readonly Color Muted = Color.FromArgb(158, 170, 190);
        public static readonly Color Blue = Color.FromArgb(88, 148, 255);
        public static readonly Color BlueBright = Color.FromArgb(130, 175, 255);
        public static readonly Color Mint = Color.FromArgb(90, 210, 190);
        public static readonly Color Warning = Color.FromArgb(230, 190, 90);
        public static readonly Color Danger = Color.FromArgb(230, 110, 100);
        public static readonly Color OffsetGizmo = Color.FromArgb(80, 210, 230);
        public static readonly Color TriggerGizmo = Color.FromArgb(255, 160, 70);
        public static readonly Color DoorFill = Color.FromArgb(70, 82, 110);
        public static readonly Color DoorEdge = Color.FromArgb(150, 165, 200);
        public static readonly Color ScrollThumb = Color.FromArgb(70, 80, 100);
        public static readonly Color ScrollThumbHot = Color.FromArgb(110, 130, 170);
        public static readonly Color CardFill = Color.FromArgb(30, 35, 46);
        public static readonly Color InputFill = Color.FromArgb(38, 44, 58);
        public static readonly Color InputBorder = Color.FromArgb(70, 80, 102);
        public static readonly Color InputFocus = Color.FromArgb(100, 158, 255);

        public static readonly Font UiFont = new("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font UiFontBold = new("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font TitleFont = new("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font MonoFont = new("Consolas", 9f, FontStyle.Regular);
        public static readonly Font SmallFont = new("Segoe UI", 8f, FontStyle.Regular);

        public static void StyleForm(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Foreground;
            form.Font = UiFont;
        }

        public static void StylePanel(Panel panel, Color? back = null)
        {
            panel.BackColor = back ?? Panel;
            panel.ForeColor = Foreground;
        }

        public static void StyleHeader(Label label)
        {
            label.ForeColor = Bright;
            label.Font = TitleFont;
            label.BackColor = Color.Transparent;
        }

        public static void StyleHint(Label label)
        {
            label.ForeColor = Faint;
            label.Font = SmallFont;
            label.BackColor = Color.Transparent;
        }

        public static void StyleButton(Button btn, bool primary = false)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(70, 110, 190) : Hover;
            btn.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(55, 90, 160) : Active;
            btn.BackColor = primary ? Color.FromArgb(55, 95, 175) : Panel2;
            btn.ForeColor = primary ? Bright : Foreground;
            btn.Font = UiFontBold;
            btn.Cursor = Cursors.Hand;
            btn.Padding = new Padding(12, 4, 12, 4);
            btn.Height = Math.Max(btn.Height, 30);
            btn.MinimumSize = new Size(btn.MinimumSize.Width, 30);
        }

        [Obsolete("Use ThemedTextBox instead.")]
        public static void StyleTextBox(TextBox tb)
        {
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = InputFill;
            tb.ForeColor = Bright;
            tb.Font = UiFont;
        }

        [Obsolete("Use ThemedComboBox instead.")]
        public static void StyleCombo(ComboBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = InputFill;
            cb.ForeColor = Bright;
            cb.Font = UiFont;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.ItemHeight = 28;
        }

        public static void StyleList(ListBox lb)
        {
            lb.BorderStyle = BorderStyle.None;
            lb.BackColor = Elevated;
            lb.ForeColor = Foreground;
            lb.Font = UiFont;
            lb.IntegralHeight = false;
            ScrollChrome.Attach(lb);
            lb.HandleCreated += (_, _) => ScrollChrome.HideBars(lb);
            lb.SizeChanged += (_, _) => ScrollChrome.HideBars(lb);
        }

        public static void StyleCheckedList(CheckedListBox clb)
        {
            clb.BorderStyle = BorderStyle.None;
            clb.BackColor = Elevated;
            clb.ForeColor = Foreground;
            clb.Font = UiFont;
            clb.CheckOnClick = true;
            clb.IntegralHeight = false;
            ScrollChrome.Attach(clb);
            clb.HandleCreated += (_, _) => ScrollChrome.HideBars(clb);
            clb.SizeChanged += (_, _) => ScrollChrome.HideBars(clb);
        }

        public static void StyleMenu(MenuStrip menu)
        {
            menu.BackColor = Activity;
            menu.ForeColor = Foreground;
            menu.Renderer = new DarkMenuRenderer();
        }

        public static void StyleStatus(StatusStrip status)
        {
            status.BackColor = Activity;
            status.ForeColor = Faint;
            status.SizingGrip = false;
            status.Renderer = new DarkToolStripRenderer();
        }

        public static void PaintCardBorder(PaintEventArgs e, Control control, int radius = 10)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = control.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            using var fill = new SolidBrush(CardFill);
            using var pen = new Pen(LineSoft);
            using var path = RoundRect(r, radius);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 2)
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

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = AppTheme.Foreground;
            base.OnRenderItemText(e);
        }
    }

    internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => AppTheme.Activity;
        public override Color MenuStripGradientEnd => AppTheme.Activity;
        public override Color MenuItemSelected => AppTheme.Hover;
        public override Color MenuItemSelectedGradientBegin => AppTheme.Hover;
        public override Color MenuItemSelectedGradientEnd => AppTheme.Hover;
        public override Color MenuItemBorder => AppTheme.LineSoft;
        public override Color MenuBorder => AppTheme.LineSoft;
        public override Color ToolStripDropDownBackground => AppTheme.Panel;
        public override Color ImageMarginGradientBegin => AppTheme.Panel;
        public override Color ImageMarginGradientMiddle => AppTheme.Panel;
        public override Color ImageMarginGradientEnd => AppTheme.Panel;
        public override Color SeparatorDark => AppTheme.LineSoft;
        public override Color SeparatorLight => AppTheme.LineSoft;
        public override Color StatusStripGradientBegin => AppTheme.Activity;
        public override Color StatusStripGradientEnd => AppTheme.Activity;
    }
}
