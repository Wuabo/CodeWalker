using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Controls
{
    internal static class ThemedCheckPaint
    {
        public static void DrawBox(Graphics g, Rectangle boxRect, bool isChecked, bool hover)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = AppTheme.RoundRect(boxRect, 4);
            if (isChecked)
            {
                using var fill = new SolidBrush(AppTheme.Blue);
                g.FillPath(fill, path);
                using var pen = new Pen(AppTheme.BlueBright, 1f);
                g.DrawPath(pen, path);
                using var tick = new Pen(AppTheme.Bright, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(tick, boxRect.Left + 4, boxRect.Top + 9, boxRect.Left + 7, boxRect.Bottom - 5);
                g.DrawLine(tick, boxRect.Left + 7, boxRect.Bottom - 5, boxRect.Right - 3, boxRect.Top + 4);
            }
            else
            {
                var border = hover ? AppTheme.Muted : AppTheme.InputBorder;
                using var fill = new SolidBrush(AppTheme.InputFill);
                g.FillPath(fill, path);
                using var pen = new Pen(border, 1.2f);
                g.DrawPath(pen, path);
            }
        }
    }

    public sealed class NavButton : Button
    {
        private bool _selected;

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        public string Subtitle { get; set; } = string.Empty;

        public NavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = AppTheme.Hover;
            FlatAppearance.MouseDownBackColor = AppTheme.Active;
            BackColor = Color.Transparent;
            ForeColor = AppTheme.Foreground;
            TextAlign = ContentAlignment.MiddleLeft;
            Cursor = Cursors.Hand;
            Height = 56;
            Padding = new Padding(12, 6, 12, 6);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.Activity);

            var bounds = ClientRectangle;
            bounds.Inflate(-6, -3);

            if (_selected || ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                using var brush = new SolidBrush(_selected ? AppTheme.Active : AppTheme.Hover);
                using var path = AppTheme.RoundRect(bounds, 10);
                g.FillPath(brush, path);
            }

            if (_selected)
            {
                using var accent = new SolidBrush(AppTheme.Blue);
                g.FillRectangle(accent, 6, bounds.Top + 10, 3, bounds.Height - 20);
            }

            using var titleBrush = new SolidBrush(_selected ? AppTheme.Bright : AppTheme.Foreground);
            using var subBrush = new SolidBrush(AppTheme.Faint);
            g.DrawString(Text, AppTheme.UiFontBold, titleBrush, bounds.Left + 16, bounds.Top + 10);
            if (!string.IsNullOrEmpty(Subtitle))
                g.DrawString(Subtitle, AppTheme.SmallFont, subBrush, bounds.Left + 16, bounds.Top + 30);
        }
    }

    /// <summary>Card with title + auto-sized body (rounded, no Dock Fill fights).</summary>
    public sealed class SectionCard : Panel
    {
        private readonly Label _title;
        private readonly Panel _body;

        public Panel Body => _body;

        public SectionCard(string title)
        {
            BackColor = AppTheme.CardFill;
            Margin = new Padding(0, 0, 0, 14);
            Padding = new Padding(0);
            AutoSize = false;

            _title = new Label
            {
                Text = title.ToUpperInvariant(),
                Left = 16,
                Top = 12,
                Width = 420,
                Height = 18,
                ForeColor = AppTheme.Muted,
                Font = AppTheme.SmallFont,
                BackColor = Color.Transparent
            };

            _body = new Panel
            {
                Left = 14,
                Top = 34,
                Width = 432,
                Height = 40,
                BackColor = Color.Transparent
            };

            Controls.Add(_body);
            Controls.Add(_title);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = 460;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            AppTheme.PaintCardBorder(e, this, 12);
        }

        /// <summary>Call after filling Body so the card grows to fit.</summary>
        public void FitToBody(int bodyHeight)
        {
            _body.Height = Math.Max(24, bodyHeight);
            Height = _body.Top + _body.Height + 16;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_title == null || _body == null) return;
            _title.Width = Math.Max(40, Width - 32);
            _body.Width = Math.Max(40, Width - 28);
        }
    }

    public sealed class FloatField : Panel
    {
        private readonly Label _label;
        private readonly ThemedTextBox _box;
        private bool _suppress;

        public event EventHandler? ValueChanged;

        public string LabelText
        {
            get => _label.Text;
            set => _label.Text = value;
        }

        public float Value
        {
            get => float.TryParse(_box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
            set
            {
                _suppress = true;
                _box.Text = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                _suppress = false;
            }
        }

        public FloatField(string label, int width = 130)
        {
            Width = width;
            Height = 54;
            Margin = new Padding(0, 0, 10, 8);
            BackColor = Color.Transparent;

            _label = new Label
            {
                Text = label,
                Left = 0,
                Top = 0,
                Width = width - 2,
                Height = 16,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont,
                BackColor = Color.Transparent
            };

            _box = new ThemedTextBox
            {
                Left = 0,
                Top = 18,
                Width = width - 4,
                Height = InputChrome.Height,
                UseMonoFont = true
            };
            _box.TextChanged += (_, _) =>
            {
                if (!_suppress) ValueChanged?.Invoke(this, EventArgs.Empty);
            };

            Controls.Add(_label);
            Controls.Add(_box);
        }
    }

    public sealed class Vec3Field : Panel
    {
        public FloatField X { get; }
        public FloatField Y { get; }
        public FloatField Z { get; }

        public event EventHandler? ValueChanged;

        public Vec3Field(string prefix = "")
        {
            Height = 58;
            Width = 430;
            BackColor = Color.Transparent;
            Margin = new Padding(0, 0, 0, 4);

            int w = 136;
            X = new FloatField(string.IsNullOrEmpty(prefix) ? "X" : $"{prefix} X", w) { Left = 0, Top = 0, Margin = new Padding(0) };
            Y = new FloatField(string.IsNullOrEmpty(prefix) ? "Y" : $"{prefix} Y", w) { Left = w + 8, Top = 0, Margin = new Padding(0) };
            Z = new FloatField(string.IsNullOrEmpty(prefix) ? "Z" : $"{prefix} Z", w) { Left = (w + 8) * 2, Top = 0, Margin = new Padding(0) };

            foreach (var f in new[] { X, Y, Z })
            {
                f.ValueChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
                Controls.Add(f);
            }
        }

        public void Set(float x, float y, float z)
        {
            X.Value = x;
            Y.Value = y;
            Z.Value = z;
        }

        public (float x, float y, float z) Get() => (X.Value, Y.Value, Z.Value);

        public void SetLayoutWidth(int totalWidth)
        {
            Width = totalWidth;
            int gap = 8;
            int w = Math.Max(72, (totalWidth - gap * 2) / 3);
            X.Width = w;
            Y.Width = w;
            Z.Width = w;
            X.Left = 0;
            Y.Left = w + gap;
            Z.Left = (w + gap) * 2;
            foreach (var f in new[] { X, Y, Z })
            {
                if (f.Controls.Count > 1 && f.Controls[1] is ThemedTextBox tb)
                    tb.Width = w - 4;
                if (f.Controls.Count > 0 && f.Controls[0] is Label lbl)
                    lbl.Width = w - 2;
            }
        }
    }

    public sealed class DarkCheck : CheckBox
    {
        private bool _hover;

        public DarkCheck(string text)
        {
            Text = text;
            AutoSize = false;
            Height = 28;
            ForeColor = AppTheme.Foreground;
            BackColor = Color.Transparent;
            Font = AppTheme.UiFont;
            Margin = new Padding(0, 2, 0, 2);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Width = Math.Max(200, TextRenderer.MeasureText(text, Font).Width + 36);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            CheckedChanged += (_, _) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Never Clear(Transparent) — that paints black bars on transparent parents.
            using (var bg = new SolidBrush(InputChrome.OpaqueParentBack(Parent)))
                g.FillRectangle(bg, ClientRectangle);

            const int box = 18;
            var boxRect = new Rectangle(0, (Height - box) / 2, box, box);
            ThemedCheckPaint.DrawBox(g, boxRect, Checked, _hover);

            var textRect = new Rectangle(box + 10, 0, Width - box - 10, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, AppTheme.Foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Horizontal row of float fields with consistent spacing.</summary>
    public static class FieldRow
    {
        public static Panel Create(params FloatField[] fields)
        {
            var row = new Panel
            {
                Height = 58,
                Width = 430,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            int x = 0;
            foreach (var f in fields)
            {
                f.Left = x;
                f.Top = 0;
                f.Margin = new Padding(0);
                row.Controls.Add(f);
                x += f.Width + 8;
            }
            return row;
        }

        public static void SetLayoutWidth(Panel row, int totalWidth)
        {
            row.Width = totalWidth;
            var fields = row.Controls.OfType<FloatField>().ToArray();
            if (fields.Length == 0) return;
            int gap = 8;
            int w = Math.Max(72, (totalWidth - gap * (fields.Length - 1)) / fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                var ff = fields[i];
                ff.Width = w;
                ff.Left = i * (w + gap);
                ff.Top = 0;
                if (ff.Controls.Count > 1 && ff.Controls[1] is ThemedTextBox tb)
                    tb.Width = w - 4;
                if (ff.Controls.Count > 0 && ff.Controls[0] is Label lbl)
                    lbl.Width = w - 2;
            }
        }
    }
}
