using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Controls
{
    public sealed class ThemedButton : Button
    {
        private bool _hover;
        private bool _pressed;
        public bool Primary { get; set; }

        public ThemedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            BackColor = Color.Transparent;
            ForeColor = AppTheme.Bright;
            Font = AppTheme.UiFont;
            Cursor = Cursors.Hand;
            Height = 28;
            Padding = new Padding(8, 0, 8, 0);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var sz = TextRenderer.MeasureText(Text ?? "", Font);
            int w = Math.Max(MinimumSize.Width > 0 ? MinimumSize.Width : 56, sz.Width + Padding.Horizontal + 12);
            int h = Height > 0 ? Height : 28;
            return new Size(w, h);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.Panel);

            Color fill = Primary
                ? (_pressed ? AppTheme.AccentPressed : _hover ? AppTheme.AccentHover : AppTheme.Accent)
                : (_pressed ? AppTheme.Input : _hover ? AppTheme.InputHover : AppTheme.PanelElevated);
            Color border = Primary ? fill : (_hover || Focused ? AppTheme.BorderFocus : AppTheme.Border);

            var r = ClientRectangle;
            r.Inflate(-1, -1);
            using (var brush = new SolidBrush(fill))
                AppTheme.FillRoundRect(g, brush, r, 6);
            using (var pen = new Pen(border))
                AppTheme.DrawRoundRect(g, pen, r, 6);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle,
                Enabled ? AppTheme.Bright : AppTheme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public sealed class ThemedCheckBox : Control
    {
        private bool _hover;
        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; CheckedChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
        }
        public event EventHandler? CheckedChanged;

        public ThemedCheckBox()
        {
            Height = 24;
            Cursor = Cursors.Hand;
            ForeColor = AppTheme.Foreground;
            Font = AppTheme.UiFont;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); Focus(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.Panel);

            var box = new Rectangle(0, (Height - 16) / 2, 16, 16);
            var fill = _checked ? AppTheme.Accent : (_hover ? AppTheme.InputHover : AppTheme.Input);
            using (var brush = new SolidBrush(fill))
                AppTheme.FillRoundRect(g, brush, box, 4);
            using (var pen = new Pen(_hover || Focused ? AppTheme.BorderFocus : AppTheme.Border))
                AppTheme.DrawRoundRect(g, pen, box, 4);

            if (_checked)
            {
                using var pen = new Pen(AppTheme.Bright, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(pen, new[]
                {
                    new Point(box.X + 3, box.Y + 8),
                    new Point(box.X + 6, box.Y + 11),
                    new Point(box.X + 12, box.Y + 4)
                });
            }

            var textRect = new Rectangle(24, 0, Math.Max(0, Width - 24), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, AppTheme.Foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            var sz = TextRenderer.MeasureText(Text, Font);
            Width = Math.Max(120, sz.Width + 32);
            Invalidate();
        }
    }

    /// <summary>Numeric field without spin buttons — dark rounded input + mouse-wheel nudge.</summary>
    public sealed class ThemedNumeric : Control
    {
        private readonly TextBox _box;
        private bool _hover;
        private decimal _value;
        private bool _suppress;

        public int DecimalPlaces { get; set; } = 6;
        public decimal Minimum { get; set; } = -100000;
        public decimal Maximum { get; set; } = 100000;
        public decimal Increment { get; set; } = 0.01M;

        public decimal Value
        {
            get => _value;
            set
            {
                var v = Math.Clamp(value, Minimum, Maximum);
                if (_value == v) { SyncText(); return; }
                _value = v;
                SyncText();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;

        public ThemedNumeric()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Input,
                ForeColor = AppTheme.Bright,
                Font = AppTheme.MonoFont,
                Location = new Point(10, 6),
                Width = 108,
                Height = 16
            };
            _box.TextChanged += (_, _) =>
            {
                if (_suppress) return;
                if (TryParse(_box.Text, out var v))
                {
                    v = Math.Clamp(v, Minimum, Maximum);
                    if (_value == v) return;
                    _value = v;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            _box.Leave += (_, _) => CommitText();
            _box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; }
            };
            _box.MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            _box.MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            Controls.Add(_box);

            // Size after child exists — OnResize touches _box.
            Width = 128;
            Height = 28;
            SyncText();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null) return;
            _box.SetBounds(10, Math.Max(4, (Height - 16) / 2), Math.Max(20, Width - 20), 16);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_box.Focused && !ClientRectangle.Contains(PointToClient(MousePosition)))
                _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var steps = e.Delta > 0 ? 1 : -1;
            Value = _value + Increment * steps;
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.PanelElevated);

            bool focus = _box.Focused || Focused;
            var fill = focus || _hover ? AppTheme.InputHover : AppTheme.Input;
            var border = focus ? AppTheme.BorderFocus : (_hover ? Color.FromArgb(80, 95, 120) : AppTheme.Border);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(fill))
                AppTheme.FillRoundRect(g, brush, r, 6);
            using (var pen = new Pen(border))
                AppTheme.DrawRoundRect(g, pen, r, 6);

            _box.BackColor = fill;
        }

        private void CommitText()
        {
            if (!TryParse(_box.Text, out var v))
                v = _value;
            Value = v;
            SyncText();
        }

        private void SyncText()
        {
            _suppress = true;
            var fmt = "0." + new string('0', Math.Max(0, DecimalPlaces));
            _box.Text = _value.ToString(fmt, CultureInfo.CurrentCulture);
            _suppress = false;
        }

        private static bool TryParse(string text, out decimal v)
        {
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v)
                || decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }

    /// <summary>Fully custom dropdown — dark closed state + popup list, no system combo chrome.</summary>
    public sealed class ThemedComboBox : Control
    {
        private readonly List<object> _items = new();
        private int _selectedIndex = -1;
        private bool _hover;
        private bool _open;
        private DropHost? _drop;

        public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDownList;
        public ObjectCollection Items { get; }
        public event EventHandler? SelectedIndexChanged;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value < -1 || value >= _items.Count) return;
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public object? SelectedItem
        {
            get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
            set
            {
                if (value == null) { SelectedIndex = -1; return; }
                SelectedIndex = _items.IndexOf(value);
            }
        }

        public ThemedComboBox()
        {
            Height = 28;
            Width = 180;
            Cursor = Cursors.Hand;
            Font = AppTheme.UiFont;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Items = new ObjectCollection(this);
        }

        public void AddRange(object[] items)
        {
            foreach (var i in items) _items.Add(i);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            ToggleDrop();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            // keep open while interacting with popup
            if (_drop == null || !_drop.ContainsFocus)
                CloseDrop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.Window);

            bool focus = Focused || _open;
            var fill = focus || _hover ? AppTheme.InputHover : AppTheme.Input;
            var border = focus ? AppTheme.BorderFocus : (_hover ? Color.FromArgb(80, 95, 120) : AppTheme.Border);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(fill))
                AppTheme.FillRoundRect(g, brush, r, 6);
            using (var pen = new Pen(border))
                AppTheme.DrawRoundRect(g, pen, r, 6);

            string text = SelectedItem?.ToString() ?? "";
            var textRect = new Rectangle(10, 0, Width - 28, Height);
            TextRenderer.DrawText(g, text, Font, textRect, AppTheme.Bright,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            using var chevron = new SolidBrush(AppTheme.Muted);
            int cx = Width - 14, cy = Height / 2;
            if (_open)
                g.FillPolygon(chevron, new[] { new Point(cx - 4, cy + 2), new Point(cx + 4, cy + 2), new Point(cx, cy - 3) });
            else
                g.FillPolygon(chevron, new[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) });
        }

        private void ToggleDrop()
        {
            if (_open) CloseDrop();
            else OpenDrop();
        }

        private void OpenDrop()
        {
            if (_items.Count == 0) return;
            CloseDrop();
            _open = true;
            Invalidate();

            int itemH = 28;
            int maxVisible = Math.Min(_items.Count, 8);
            int h = maxVisible * itemH + 4;
            var screen = PointToScreen(new Point(0, Height + 2));
            _drop = new DropHost(this, _items, _selectedIndex, itemH)
            {
                Location = screen,
                Size = new Size(Width, h)
            };
            _drop.ItemPicked += idx =>
            {
                SelectedIndex = idx;
                CloseDrop();
            };
            _drop.Show();
            _drop.Focus();
        }

        private void CloseDrop()
        {
            _open = false;
            if (_drop != null)
            {
                var d = _drop;
                _drop = null;
                d.Close();
                d.Dispose();
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseDrop();
            base.Dispose(disposing);
        }

        public sealed class ObjectCollection
        {
            private readonly ThemedComboBox _owner;
            public ObjectCollection(ThemedComboBox owner) => _owner = owner;
            public void AddRange(object[] items) => _owner.AddRange(items);
            public int Count => _owner._items.Count;
            public object? this[int index] => _owner._items[index];
        }

        private sealed class DropHost : Form
        {
            private readonly ThemedListBox _list;
            public event Action<int>? ItemPicked;

            public DropHost(ThemedComboBox owner, List<object> items, int selected, int itemH)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = AppTheme.Border;
                Padding = new Padding(1);
                TopMost = true;
                Deactivate += (_, _) => owner.CloseDrop();

                _list = new ThemedListBox
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    IntegralHeight = false,
                    ItemHeight = itemH
                };
                foreach (var i in items) _list.Items.Add(i);
                if (selected >= 0 && selected < items.Count)
                    _list.SelectedIndex = selected;

                _list.Click += (_, _) =>
                {
                    if (_list.SelectedIndex >= 0)
                        ItemPicked?.Invoke(_list.SelectedIndex);
                };
                _list.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter && _list.SelectedIndex >= 0)
                        ItemPicked?.Invoke(_list.SelectedIndex);
                    if (e.KeyCode == Keys.Escape)
                        owner.CloseDrop();
                };

                // Hide native list scrollbar visually — wheel still works via list.
                typeof(Control).GetProperty("DoubleBuffered",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(_list, true, null);

                Controls.Add(_list);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    const int csDropshadow = 0x00020000;
                    cp.ClassStyle |= csDropshadow;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation => true;
        }
    }

    public sealed class ThemedListBox : ListBox
    {
        private int _hotIndex = -1;

        public ThemedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 28;
            BorderStyle = BorderStyle.None;
            BackColor = AppTheme.Input;
            ForeColor = AppTheme.Bright;
            Font = AppTheme.UiFont;
            IntegralHeight = false;
            // No visible scrollbars — mouse wheel still scrolls.
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                const int wsVscroll = 0x00200000;
                const int wsHscroll = 0x00100000;
                cp.Style &= ~wsVscroll;
                cp.Style &= ~wsHscroll;
                return cp;
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool hot = e.Index == _hotIndex && !selected;
            Color bg = selected ? AppTheme.ListSelected : hot ? AppTheme.ListHover : AppTheme.Input;
            using var brush = new SolidBrush(bg);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
            {
                using var accent = new SolidBrush(AppTheme.Accent);
                e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 4, 3, e.Bounds.Height - 8);
            }
            string text = Items[e.Index]?.ToString() ?? "";
            var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, textRect, AppTheme.Bright,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexFromPoint(e.Location);
            if (idx != _hotIndex) { _hotIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hotIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Manual scroll without scrollbar chrome.
            int lines = e.Delta > 0 ? -1 : 1;
            TopIndex = Math.Clamp(TopIndex + lines, 0, Math.Max(0, Items.Count - 1));
            base.OnMouseWheel(e);
        }
    }

    /// <summary>Manual wheel scroll + thin themed thumb (no WinForms AutoScroll chrome).</summary>
    public sealed class ThemedScrollPanel : Panel
    {
        private Control? _content;
        private int _scrollY;

        public ThemedScrollPanel()
        {
            AutoScroll = false;
            DoubleBuffered = true;
            BackColor = AppTheme.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control == null || _content != null) return;
            _content = e.Control;
            _content.Dock = DockStyle.None;
            _scrollY = 0;
            WireWheelRecursive(_content);
            BeginInvokeIfReady(ApplyLayout);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_content == null)
            {
                base.OnMouseWheel(e);
                return;
            }

            int step = Math.Max(24, SystemInformation.MouseWheelScrollLines * 28);
            _scrollY -= Math.Sign(e.Delta) * step;
            ClampAndPlace();
            Invalidate();
            if (e is HandledMouseEventArgs he) he.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawThumb(e.Graphics);
        }

        /// <summary>Re-measure content after external size/content changes.</summary>
        public void Relayout() => ApplyLayout();

        private void ApplyLayout()
        {
            if (_content == null || !IsHandleCreated) return;

            // Reserve 8px for the themed thumb so content never sits under it
            int w = Math.Max(280, ClientSize.Width - Padding.Horizontal - 8);
            _content.MinimumSize = new Size(w, 0);
            _content.MaximumSize = new Size(w, 0);
            _content.Width = w;

            if (_content is FlowLayoutPanel flp)
            {
                int cw = Math.Max(40, w - flp.Padding.Horizontal);
                foreach (Control child in flp.Controls)
                {
                    child.MinimumSize = new Size(cw, 0);
                    child.MaximumSize = new Size(cw, 0);
                    child.Width = cw;
                }
            }

            _content.PerformLayout();
            if (_content.AutoSize)
            {
                var pref = _content.GetPreferredSize(new Size(w, 0));
                int h = Math.Max(pref.Height, _content.PreferredSize.Height);
                if (h > 0) _content.Height = h;
            }

            ClampAndPlace();
            Invalidate();
        }

        private int MaxScroll =>
            _content == null
                ? 0
                : Math.Max(0, _content.Height - Math.Max(1, ClientSize.Height - Padding.Vertical));

        private void ClampAndPlace()
        {
            if (_content == null) return;
            _scrollY = Math.Clamp(_scrollY, 0, MaxScroll);
            _content.Location = new Point(Padding.Left, Padding.Top - _scrollY);
        }

        private void DrawThumb(Graphics g)
        {
            int max = MaxScroll;
            if (_content == null || max <= 0) return;

            int trackTop = 6;
            int trackH = Math.Max(1, ClientSize.Height - 12);
            float viewRatio = Math.Clamp((float)ClientSize.Height / Math.Max(1, _content.Height), 0.08f, 1f);
            int thumbH = Math.Max(28, (int)(trackH * viewRatio));
            int thumbY = trackTop + (int)((trackH - thumbH) * ((float)_scrollY / max));
            int x = ClientSize.Width - 5;

            using (var track = new SolidBrush(Color.FromArgb(40, AppTheme.Border)))
                g.FillRectangle(track, x, trackTop, 3, trackH);
            using (var thumb = new SolidBrush(AppTheme.BorderFocus))
                g.FillRectangle(thumb, x, thumbY, 3, thumbH);
        }

        private void WireWheelRecursive(Control c)
        {
            c.MouseWheel -= Child_MouseWheel;
            c.MouseWheel += Child_MouseWheel;
            c.ControlAdded -= Child_ControlAdded;
            c.ControlAdded += Child_ControlAdded;
            foreach (Control child in c.Controls)
                WireWheelRecursive(child);
        }

        private void Child_ControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control != null) WireWheelRecursive(e.Control);
        }

        private void Child_MouseWheel(object? sender, MouseEventArgs e) => OnMouseWheel(e);

        private void BeginInvokeIfReady(Action action)
        {
            if (!IsHandleCreated)
            {
                void Handler(object? s, EventArgs e)
                {
                    HandleCreated -= Handler;
                    BeginInvoke(action);
                }
                HandleCreated += Handler;
                return;
            }
            BeginInvoke(action);
        }
    }

    public sealed class ThemedSection : Panel
    {
        private readonly Panel _body;
        private readonly Label _title;

        public ThemedSection(string title)
        {
            // Height from content; width forced by parent. Avoid Dock.Fill (collapses AutoSize).
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.None;
            Margin = new Padding(0, 0, 0, 10);
            Padding = new Padding(1);
            BackColor = AppTheme.Border;

            var inner = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = AppTheme.PanelElevated,
                Padding = new Padding(12, 10, 12, 12)
            };
            _title = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 22,
                Font = AppTheme.HeaderFont,
                ForeColor = AppTheme.BlueBright,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = AppTheme.PanelElevated
            };
            _body = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = AppTheme.PanelElevated
            };
            // Dock Top: last added paints on top
            inner.Controls.Add(_body);
            inner.Controls.Add(_title);
            Controls.Add(inner);
        }

        public void SetContent(Control content)
        {
            _body.Controls.Clear();
            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.Margin = Padding.Empty;
            _body.Controls.Add(content);
            PerformLayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ForceFullWidth();
        }

        private void ForceFullWidth()
        {
            int innerW = Math.Max(40, Width - Padding.Horizontal);
            foreach (Control c in Controls)
            {
                // Lock width so AutoSize doesn't shrink and leave empty gray side panels
                c.MinimumSize = new Size(innerW, 0);
                c.MaximumSize = new Size(innerW, 0);
                c.Width = innerW;
            }
            int bodyW = Math.Max(40, innerW - 24);
            foreach (Control c in _body.Controls)
            {
                c.MinimumSize = new Size(bodyW, 0);
                c.MaximumSize = new Size(bodyW, 0);
                c.Width = bodyW;
            }
            _title.Width = bodyW;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 280);
            if (w < 50) w = 280;
            ForceFullWidth();
            int h = Padding.Vertical + 2;
            foreach (Control c in Controls)
            {
                var ps = c.GetPreferredSize(new Size(w - Padding.Horizontal, 0));
                h += Math.Max(ps.Height, c.Height) + c.Margin.Vertical;
            }
            return new Size(w, Math.Max(48, h));
        }
    }

    public sealed class ThemedTextBox : Control
    {
        private readonly TextBox _box;
        private bool _hover;

        public override string Text
        {
            get => _box.Text;
#pragma warning disable CS8765
            set => _box.Text = value ?? string.Empty;
#pragma warning restore CS8765
        }

        public ThemedTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Input,
                ForeColor = AppTheme.Bright,
                Font = AppTheme.UiFont,
                Location = new Point(10, 7),
                Width = 100,
                Height = 16
            };
            _box.GotFocus += (_, _) => Invalidate();
            _box.LostFocus += (_, _) => Invalidate();
            Controls.Add(_box);
            Height = 30;
        }

        public void SelectAll() => _box.SelectAll();
        public new void Focus() => _box.Focus();

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null) return;
            _box.SetBounds(10, Math.Max(4, (Height - 16) / 2), Math.Max(20, Width - 20), 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.Panel);
            bool focus = _box.Focused;
            var fill = focus || _hover ? AppTheme.InputHover : AppTheme.Input;
            var border = focus ? AppTheme.BorderFocus : AppTheme.Border;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(fill))
                AppTheme.FillRoundRect(g, brush, r, 6);
            using (var pen = new Pen(border))
                AppTheme.DrawRoundRect(g, pen, r, 6);
            _box.BackColor = fill;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }
}
