using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Controls
{
    /// <summary>Hides Windows native scrollbars while keeping wheel / programmatic scroll.</summary>
    public static class ScrollChrome
    {
        private const int SbBoth = 3;
        private const int WmNccalcsize = 0x83;
        private const int WmMouseWheel = 0x20A;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        public static void HideBars(Control control)
        {
            if (control == null || !control.IsHandleCreated) return;
            try { ShowScrollBar(control.Handle, SbBoth, false); } catch { /* ignore */ }
        }

        public static void Attach(Control control)
        {
            void KeepHidden(object? s, EventArgs e) => HideBars(control);
            control.HandleCreated += KeepHidden;
            control.SizeChanged += KeepHidden;
            control.VisibleChanged += KeepHidden;
            if (control.IsHandleCreated) HideBars(control);

            // Re-hide after WinForms recalculates non-client area (shows bars again).
            control.HandleCreated += (_, _) =>
            {
                // subclass via WndProc filter on a light helper
            };
        }

        public static void AttachWndProc(Control control, ref Message m, Action baseWndProc)
        {
            baseWndProc();
            if (m.Msg is WmNccalcsize or 0x85 /*WM_NCPAINT*/ or 0x14 /*WM_ERASEBKGND*/)
                HideBars(control);
        }
    }

    /// <summary>Auto-scroll panel with thin custom scrollbar (no native bars).</summary>
    public sealed class DarkScrollPanel : Panel
    {
        private const int TrackW = 6;
        private bool _dragging;
        private int _dragOffset;

        public DarkScrollPanel()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = AppTheme.Background;
            // Do NOT use UserPaint here — it breaks AutoScroll for child controls.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ScrollChrome.Attach(this);
            MouseDown += OnMouseDownThumb;
            MouseMove += OnMouseMoveThumb;
            MouseUp += (_, _) => _dragging = false;
            MouseLeave += (_, _) => { _dragging = false; Invalidate(); };
            Paint += (_, e) => DrawThumb(e.Graphics);
        }

        /// <summary>Route mouse wheel from nested inputs/combos to this scroll host.</summary>
        public void WireMouseWheelBubble(Control root)
        {
            void Hook(Control c)
            {
                c.MouseWheel += OnBubbleWheel;
                foreach (Control child in c.Controls)
                    Hook(child);
            }
            Hook(root);
            MouseWheel += OnBubbleWheel;
        }

        private void OnBubbleWheel(object? sender, MouseEventArgs e)
        {
            if (AutoScrollMinSize.Height <= ClientSize.Height) return;
            ScrollByDelta(-e.Delta / 120 * 36);
        }

        public void ScrollByDelta(int deltaY)
        {
            int max = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height + 1);
            int y = Math.Clamp(Math.Abs(AutoScrollPosition.Y) + deltaY, 0, max);
            AutoScrollPosition = new Point(0, y);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (IsHandleCreated)
                ScrollChrome.HideBars(this);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        private Rectangle ThumbRect()
        {
            if (!VerticalScroll.Visible && AutoScrollMinSize.Height <= ClientSize.Height)
                return Rectangle.Empty;

            int view = ClientSize.Height;
            int content = Math.Max(AutoScrollMinSize.Height, view);
            if (content <= view) return Rectangle.Empty;

            int track = view - 4;
            int thumbH = Math.Max(28, (int)(track * (view / (float)content)));
            int range = Math.Max(1, content - view);
            int scrollable = Math.Max(1, track - thumbH);
            int y = 2 + (int)((-VerticalScroll.Value / (float)range) * scrollable);
            return new Rectangle(ClientSize.Width - TrackW - 3, y, TrackW, thumbH);
        }

        private void DrawThumb(Graphics g)
        {
            var thumb = ThumbRect();
            if (thumb.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool hot = thumb.Contains(PointToClient(Cursor.Position)) || _dragging;
            using var brush = new SolidBrush(hot ? AppTheme.ScrollThumbHot : AppTheme.ScrollThumb);
            using var path = RoundRect(thumb, 3);
            g.FillPath(brush, path);
        }

        private void OnMouseDownThumb(object? sender, MouseEventArgs e)
        {
            var thumb = ThumbRect();
            if (thumb.IsEmpty || e.Button != MouseButtons.Left) return;
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragOffset = e.Y - thumb.Y;
            }
            else if (e.X >= ClientSize.Width - TrackW - 6)
            {
                // page jump
                int view = ClientSize.Height;
                int content = Math.Max(AutoScrollMinSize.Height, view);
                int range = Math.Max(1, content - view);
                float ratio = Math.Clamp((e.Y - 2) / (float)Math.Max(1, view - 4), 0f, 1f);
                SetScrollY((int)(ratio * range));
            }
        }

        private void OnMouseMoveThumb(object? sender, MouseEventArgs e)
        {
            if (!_dragging) { Invalidate(); return; }
            int view = ClientSize.Height;
            int content = Math.Max(AutoScrollMinSize.Height, view);
            if (content <= view) return;
            int track = view - 4;
            int thumbH = Math.Max(28, (int)(track * (view / (float)content)));
            int scrollable = Math.Max(1, track - thumbH);
            int range = Math.Max(1, content - view);
            int y = Math.Clamp(e.Y - _dragOffset, 2, 2 + scrollable);
            float ratio = (y - 2) / (float)scrollable;
            SetScrollY((int)(ratio * range));
        }

        private void SetScrollY(int value)
        {
            int max = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height + 1);
            value = Math.Clamp(value, 0, max);
            AutoScrollPosition = new Point(0, value);
            Invalidate();
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>ListBox with hidden native scrollbar; wheel scroll via TopIndex.</summary>
    public sealed class DarkListBox : ListBox
    {
        private Form? _wheelForm;

        public DarkListBox()
        {
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            SelectionMode = SelectionMode.One;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 28;
            BackColor = AppTheme.Elevated;
            ForeColor = AppTheme.Foreground;
            Font = AppTheme.UiFont;
            ScrollChrome.Attach(this);
            SelectedIndexChanged += (_, _) => Invalidate();
            HandleCreated += (_, _) => HookFormWheel();
            HandleDestroyed += (_, _) => UnhookFormWheel();
        }

        private void HookFormWheel()
        {
            UnhookFormWheel();
            _wheelForm = FindForm();
            if (_wheelForm != null)
                _wheelForm.MouseWheel += OnFormMouseWheel;
        }

        private void UnhookFormWheel()
        {
            if (_wheelForm != null)
            {
                _wheelForm.MouseWheel -= OnFormMouseWheel;
                _wheelForm = null;
            }
        }

        private void OnFormMouseWheel(object? sender, MouseEventArgs e)
        {
            if (Focused || Items.Count == 0) return;
            var pt = PointToClient(Cursor.Position);
            if (!ClientRectangle.Contains(pt)) return;
            ScrollByWheel(e.Delta);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByWheel(e.Delta);
        }

        private void ScrollByWheel(int delta)
        {
            if (Items.Count == 0) return;
            int row = Math.Max(ItemHeight, 1);
            int visible = Math.Max(1, ClientSize.Height / row);
            int maxTop = Math.Max(0, Items.Count - visible);
            int step = Math.Max(1, visible / 2);
            int dir = delta > 0 ? -step : step;
            TopIndex = Math.Clamp(TopIndex + dir, 0, maxTop);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (IsHandleCreated) ScrollChrome.HideBars(this);
            const int WM_VSCROLL = 0x115;
            const int WM_MOUSEWHEEL = 0x20A;
            if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL)
                Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(bg, e.Bounds);

            bool selected = (e.State & DrawItemState.Selected) != 0;
            var bounds = e.Bounds;
            bounds.Inflate(-4, -1);
            if (selected)
            {
                using var brush = new SolidBrush(AppTheme.ListSelected);
                using var path = RoundRect(bounds, 6);
                e.Graphics.FillPath(brush, path);
            }
            else if ((e.State & DrawItemState.HotLight) != 0)
            {
                using var brush = new SolidBrush(AppTheme.Hover);
                using var path = RoundRect(bounds, 6);
                e.Graphics.FillPath(brush, path);
            }

            string text = GetItemText(Items[e.Index]) ?? "";
            var textRect = new Rectangle(bounds.X + 10, bounds.Y, bounds.Width - 14, bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, textRect, selected ? AppTheme.Bright : AppTheme.Foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            int row = Math.Max(ItemHeight, 1);
            int lastPainted = Math.Min(Items.Count - 1, TopIndex + ClientSize.Height / row);
            if (e.Index == lastPainted)
                DrawListThumb(e.Graphics);
        }

        private void DrawListThumb(Graphics g)
        {
            if (Items.Count == 0) return;
            int row = Math.Max(ItemHeight, 1);
            int content = Items.Count * row;
            int view = ClientSize.Height;
            if (content <= view) return;

            const int trackW = 5;
            int track = view - 4;
            int thumbH = Math.Max(24, (int)(track * (view / (float)content)));
            int range = Math.Max(1, content - view);
            int scrollable = Math.Max(1, track - thumbH);
            float ratio = TopIndex * row / (float)range;
            int y = 2 + (int)(ratio * scrollable);
            var thumb = new Rectangle(ClientSize.Width - trackW - 2, y, trackW, thumbH);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(AppTheme.ScrollThumb);
            using var path = RoundRect(thumb, 2);
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
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

    /// <summary>CheckedListBox without native scrollbar chrome.</summary>
    public sealed class DarkCheckedList : CheckedListBox
    {
        private Form? _wheelForm;

        public DarkCheckedList()
        {
            BorderStyle = BorderStyle.None;
            BackColor = AppTheme.Elevated;
            ForeColor = AppTheme.Foreground;
            Font = AppTheme.UiFont;
            CheckOnClick = true;
            IntegralHeight = false;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 26;
            ScrollChrome.Attach(this);
            DrawItem += OnDrawCheckItem;
            HandleCreated += (_, _) => HookFormWheel();
            HandleDestroyed += (_, _) => UnhookFormWheel();
        }

        private void OnDrawCheckItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(selected ? AppTheme.ListSelected : AppTheme.Elevated))
                g.FillRectangle(bg, e.Bounds);

            bool isChecked = GetItemChecked(e.Index);
            var boxRect = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 4, 18, 18);
            ThemedCheckPaint.DrawBox(g, boxRect, isChecked, selected);

            var text = Items[e.Index]?.ToString() ?? string.Empty;
            var textRect = new Rectangle(boxRect.Right + 8, e.Bounds.Top, e.Bounds.Width - boxRect.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(g, text, Font, textRect, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void HookFormWheel()
        {
            UnhookFormWheel();
            _wheelForm = FindForm();
            if (_wheelForm != null)
                _wheelForm.MouseWheel += OnFormMouseWheel;
        }

        private void UnhookFormWheel()
        {
            if (_wheelForm != null)
            {
                _wheelForm.MouseWheel -= OnFormMouseWheel;
                _wheelForm = null;
            }
        }

        private void OnFormMouseWheel(object? sender, MouseEventArgs e)
        {
            if (Focused || Items.Count == 0) return;
            var pt = PointToClient(Cursor.Position);
            if (!ClientRectangle.Contains(pt)) return;
            ScrollByWheel(e.Delta);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByWheel(e.Delta);
        }

        private void ScrollByWheel(int delta)
        {
            if (Items.Count == 0) return;
            int row = Math.Max(ItemHeight, 1);
            int visible = Math.Max(1, ClientSize.Height / row);
            int maxTop = Math.Max(0, Items.Count - visible);
            int step = Math.Max(1, visible / 2);
            int dir = delta > 0 ? -step : step;
            TopIndex = Math.Clamp(TopIndex + dir, 0, maxTop);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (IsHandleCreated) ScrollChrome.HideBars(this);
        }
    }

    /// <summary>Consistent workspace top bar with title + action buttons.</summary>
    public sealed class WorkspaceHeader : Panel
    {
        private readonly Label _title;
        private readonly FlowLayoutPanel _actions;

        public FlowLayoutPanel Actions => _actions;

        public WorkspaceHeader(string title)
        {
            Dock = DockStyle.Top;
            Height = 48;
            BackColor = AppTheme.Activity;
            Padding = new Padding(16, 0, 12, 0);

            _title = new Label
            {
                Text = title,
                AutoSize = true,
                Font = AppTheme.TitleFont,
                ForeColor = AppTheme.Bright,
                BackColor = Color.Transparent,
                Location = new Point(16, 14)
            };

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 9, 0, 0),
                BackColor = Color.Transparent
            };

            Controls.Add(_actions);
            Controls.Add(_title);
            Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.LineSoft);
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            };
        }

        public Button AddAction(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0),
                MinimumSize = new Size(0, 28)
            };
            AppTheme.StyleButton(b, primary);
            _actions.Controls.Add(b);
            return b;
        }
    }

    /// <summary>Labeled list column with optional footer toolbar.</summary>
    public sealed class SideListPane : Panel
    {
        private readonly Label _title;
        private readonly Panel _footer;

        public Panel Footer => _footer;
        public Control? Body
        {
            get => Controls.Count > 2 ? Controls[0] : null;
            set
            {
                // body is fill, under title/footer
            }
        }

        public SideListPane(string title, Control list, Control? topExtra = null)
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10, 8, 10, 8);
            BackColor = AppTheme.Panel;

            _title = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 26,
                ForeColor = AppTheme.Muted,
                Font = AppTheme.UiFontBold,
                BackColor = Color.Transparent,
                Padding = new Padding(2, 4, 0, 0)
            };

            _footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0),
                BackColor = Color.Transparent
            };

            list.Dock = DockStyle.Fill;

            Controls.Add(list);
            Controls.Add(_footer);
            if (topExtra != null)
            {
                topExtra.Dock = DockStyle.Top;
                Controls.Add(topExtra);
            }
            Controls.Add(_title);
        }

        public Button AddFooterButton(string text, bool primary = false)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0), MinimumSize = new Size(0, 28) };
            AppTheme.StyleButton(b, primary);
            _footer.Controls.Add(b);
            return b;
        }
    }
}
