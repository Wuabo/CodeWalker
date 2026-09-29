using System;
using System.Drawing;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Dialogs
{
    /// <summary>Simple single-field name prompt (rename / new name).</summary>
    internal sealed class NameInputDialog : Form
    {
        private readonly ThemedTextBox _name = new() { Width = 320, Height = InputChrome.Height };

        public string Value => (_name.Text ?? string.Empty).Trim();

        public NameInputDialog(string title, string prompt, string? initialValue = null)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 128);
            ShowInTaskbar = false;
            AppTheme.StyleForm(this);

            var hint = new Label
            {
                Text = prompt,
                Left = 12,
                Top = 12,
                Width = 336,
                Height = 28,
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont
            };
            _name.Left = 12;
            _name.Top = 44;
            _name.Text = initialValue ?? string.Empty;

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 168, Top = 88, Width = 88 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 260, Top = 88, Width = 88 };
            AppTheme.StyleButton(ok, primary: true);
            AppTheme.StyleButton(cancel);

            Controls.Add(hint);
            Controls.Add(_name);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (_, _) =>
            {
                _name.Focus();
                // Select all so typing replaces the old name.
                try
                {
                    foreach (Control c in _name.Controls)
                    {
                        if (c is TextBox tb)
                        {
                            tb.SelectAll();
                            break;
                        }
                    }
                }
                catch { /* ignore */ }
            };
        }
    }
}
