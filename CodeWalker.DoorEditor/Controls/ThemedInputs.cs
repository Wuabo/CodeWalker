using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Controls
{
    internal static class NativeTheme
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static void Strip(Control c)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, "", ""); } catch { /* ignore */ }
        }
    }

    internal static class InputChrome
    {
        public const int Radius = 8;
        public const int Height = 32;

        public static void Paint(Graphics g, Rectangle client, bool focused)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = client;
            r.Width -= 1;
            r.Height -= 1;
            using var path = AppTheme.RoundRect(r, Radius);
            using var fill = new SolidBrush(AppTheme.InputFill);
            g.FillPath(fill, path);
            using var pen = new Pen(focused ? AppTheme.InputFocus : AppTheme.InputBorder, focused ? 1.6f : 1f);
            g.DrawPath(pen, path);
        }

        public static void PaintChevron(Graphics g, Rectangle client)
        {
            int cx = client.Right - 16;
            int cy = client.Top + client.Height / 2;
            using var pen = new Pen(AppTheme.Muted, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(pen, new[]
            {
                new Point(cx - 4, cy - 2),
                new Point(cx, cy + 2),
                new Point(cx + 4, cy - 2)
            });
        }

        public static Color OpaqueParentBack(Control? c)
        {
            while (c != null)
            {
                if (c.BackColor.A == 255)
                    return c.BackColor;
                c = c.Parent;
            }
            return AppTheme.CardFill;
        }
    }

    /// <summary>Rounded single-line text field with focus ring.</summary>
    public sealed class ThemedTextBox : UserControl
    {
        private readonly TextBox _inner;
        private bool _focused;
        private bool _ready;

        public override string Text
        {
            get => _inner.Text;
            set => _inner.Text = value ?? string.Empty;
        }

        public void Clear() => _inner.Clear();

        public string PlaceholderText
        {
            get => _inner.PlaceholderText;
            set => _inner.PlaceholderText = value;
        }

        public bool UseMonoFont
        {
            get => ReferenceEquals(_inner.Font, AppTheme.MonoFont);
            set => _inner.Font = value ? AppTheme.MonoFont : AppTheme.UiFont;
        }

        public bool ReadOnly
        {
            get => _inner.ReadOnly;
            set => _inner.ReadOnly = value;
        }

        public new event EventHandler? TextChanged
        {
            add => _inner.TextChanged += value;
            remove => _inner.TextChanged -= value;
        }

        public new event EventHandler? Enter
        {
            add => _inner.Enter += value;
            remove => _inner.Enter -= value;
        }

        public new event EventHandler? Leave
        {
            add => _inner.Leave += value;
            remove => _inner.Leave -= value;
        }

        public ThemedTextBox()
        {
            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.InputFill,
                ForeColor = AppTheme.Bright,
                Font = AppTheme.UiFont,
                Margin = Padding.Empty
            };
            _inner.GotFocus += (_, _) => SetFocused(true);
            _inner.LostFocus += (_, _) => SetFocused(false);
            _inner.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
            _inner.HandleCreated += (_, _) =>
            {
                NativeTheme.Strip(_inner);
                ApplyColors();
            };

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Controls.Add(_inner);
            MinimumSize = new Size(48, InputChrome.Height);
            Size = new Size(120, InputChrome.Height);
            _ready = true;
            LayoutInner();
        }

        private void SetFocused(bool on)
        {
            _focused = on;
            ApplyColors();
            Invalidate();
        }

        private void ApplyColors()
        {
            _inner.BackColor = AppTheme.InputFill;
            _inner.ForeColor = AppTheme.Bright;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private void LayoutInner()
        {
            if (!_ready) return;
            const int padX = 10;
            int h = Math.Max(_inner.PreferredSize.Height, 16);
            int y = Math.Max(0, (Height - h) / 2);
            _inner.SetBounds(padX, y, Math.Max(4, Width - padX * 2), h);
            ApplyColors();
        }

        protected override void OnPaint(PaintEventArgs e) =>
            InputChrome.Paint(e.Graphics, ClientRectangle, _focused);

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _inner.Focus();
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            _inner.Focus();
        }
    }

    /// <summary>Fully custom dark dropdown (no native white chrome).</summary>
    public sealed class ThemedComboBox : UserControl
    {
        private readonly ComboBox.ObjectCollection _items;
        private readonly ComboBox _itemsHost; // owns ObjectCollection only
        private int _selectedIndex = -1;
        private bool _focused;
        private bool _hover;
        private ToolStripDropDown? _drop;

        public ComboBox.ObjectCollection Items => _items;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int next = value;
                if (next < -1) next = -1;
                if (next >= _items.Count) next = _items.Count - 1;
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object? SelectedItem
        {
            get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
            set
            {
                if (value == null) { SelectedIndex = -1; return; }
                int i = _items.IndexOf(value);
                if (i < 0)
                {
                    for (int n = 0; n < _items.Count; n++)
                    {
                        if (Equals(_items[n], value) || string.Equals(_items[n]?.ToString(), value.ToString(), StringComparison.Ordinal))
                        {
                            i = n;
                            break;
                        }
                    }
                }
                SelectedIndex = i;
            }
        }

        /// <summary>Kept for API compatibility; always DropDownList behaviour.</summary>
        public ComboBoxStyle DropDownStyle
        {
            get => ComboBoxStyle.DropDownList;
            set { }
        }

        public event EventHandler? SelectedIndexChanged;

        public ThemedComboBox()
        {
            // Hidden host so we can reuse ComboBox.ObjectCollection without showing chrome.
            _itemsHost = new ComboBox { Visible = false, Size = Size.Empty };
            _items = _itemsHost.Items;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            MinimumSize = new Size(80, InputChrome.Height);
            Size = new Size(160, InputChrome.Height);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            InputChrome.Paint(g, ClientRectangle, _focused || _hover);
            InputChrome.PaintChevron(g, ClientRectangle);

            var text = SelectedItem?.ToString() ?? string.Empty;
            var textRect = new Rectangle(10, 0, Math.Max(4, Width - 32), Height);
            TextRenderer.DrawText(g, text, AppTheme.UiFont, textRect, AppTheme.Bright,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            ToggleDropDown();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _focused = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (_drop is { Visible: true }) return;
            _focused = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Down or Keys.F4)
            {
                ToggleDropDown();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && _selectedIndex > 0)
            {
                SelectedIndex--;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && _selectedIndex < _items.Count - 1)
            {
                SelectedIndex++;
                e.Handled = true;
            }
        }

        private void ToggleDropDown()
        {
            if (_drop is { Visible: true })
            {
                _drop.Close();
                return;
            }
            ShowDropDown();
        }

        private void ShowDropDown()
        {
            // ListBox (not ToolStripMenuItem-per-row) — stays responsive with hundreds of presets.
            var list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Panel,
                ForeColor = AppTheme.Bright,
                Font = AppTheme.UiFont,
                IntegralHeight = false,
                Width = Math.Max(Width, 200),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 26
            };
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using var bg = new SolidBrush(sel ? AppTheme.ListSelected : AppTheme.Panel);
                e.Graphics.FillRectangle(bg, e.Bounds);
                var text = list.Items[e.Index]?.ToString() ?? string.Empty;
                var rect = Rectangle.Inflate(e.Bounds, -8, 0);
                TextRenderer.DrawText(e.Graphics, text, AppTheme.UiFont, rect,
                    AppTheme.Bright,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };

            list.BeginUpdate();
            for (int i = 0; i < _items.Count; i++)
                list.Items.Add(_items[i]?.ToString() ?? string.Empty);
            list.EndUpdate();
            if (_selectedIndex >= 0 && _selectedIndex < list.Items.Count)
                list.SelectedIndex = _selectedIndex;

            int visible = Math.Clamp(_items.Count, 1, 12);
            list.Height = list.ItemHeight * visible + 4;

            var host = new ToolStripControlHost(list)
            {
                AutoSize = false,
                Size = list.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            var drop = new ToolStripDropDown
            {
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoClose = true,
                BackColor = AppTheme.Panel,
                DropShadowEnabled = true
            };
            drop.Items.Add(host);

            void Pick()
            {
                if (list.SelectedIndex >= 0)
                    SelectedIndex = list.SelectedIndex;
                drop.Close();
            }
            list.Click += (_, _) => Pick();
            list.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Space)
                {
                    Pick();
                    e.Handled = true;
                }
            };
            drop.Closed += (_, _) =>
            {
                _focused = Focused;
                Invalidate();
                _drop = null;
            };

            _drop = drop;
            _focused = true;
            Invalidate();
            drop.Show(this, new Point(0, Height));
            list.Focus();
        }
    }
}
