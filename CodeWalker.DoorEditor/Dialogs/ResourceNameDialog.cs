using System;
using System.Drawing;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Dialogs
{
    internal sealed class ResourceNameDialog : Form
    {
        private readonly ThemedTextBox _name = new() { Width = 320, Height = 32 };

        public string ResourceName => (_name.Text ?? string.Empty).Trim();

        public ResourceNameDialog(string? initialName = null)
        {
            Text = "FiveM resource name";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 120);
            ShowInTaskbar = false;
            AppTheme.StyleForm(this);
            var hint = new Label
            {
                Text = "Folder name under resources (e.g. my_doortuning)",
                Left = 12,
                Top = 12,
                Width = 336,
                Height = 32,
                ForeColor = AppTheme.Faint
            };
            _name.Left = 12;
            _name.Top = 48;
            _name.Text = initialName ?? "doortuning";

            var ok = new Button { Text = "Continue", DialogResult = DialogResult.OK, Left = 168, Top = 82, Width = 88 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 260, Top = 82, Width = 88 };
            AppTheme.StyleButton(ok, primary: true);
            AppTheme.StyleButton(cancel);

            Controls.Add(hint);
            Controls.Add(_name);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
