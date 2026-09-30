using System;
using System.Drawing;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor
{
    internal sealed class NameInputDialog : Form
    {
        private readonly ThemedTextBox _box = new();
        public string Value => _box.Text.Trim();

        public NameInputDialog(string title, string prompt, string? initial = null)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 150);
            Font = AppTheme.UiFont;
            BackColor = AppTheme.Panel;
            ForeColor = AppTheme.Foreground;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { /* ignore */ }

            var lbl = new Label
            {
                Text = prompt,
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(408, 24),
                ForeColor = AppTheme.Muted,
                BackColor = AppTheme.Panel
            };
            _box.Location = new Point(16, 48);
            _box.Size = new Size(408, 30);
            _box.Text = initial ?? string.Empty;

            var ok = new ThemedButton
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(240, 100),
                Size = new Size(88, 30),
                Primary = true
            };
            var cancel = new ThemedButton
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(336, 100),
                Size = new Size(88, 30)
            };

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange([lbl, _box, ok, cancel]);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _box.SelectAll();
            _box.Focus();
        }
    }
}
