using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Dialogs
{
    internal sealed class DoorEditDialog : Form
    {
        private readonly ThemedTextBox _name = new() { Width = 320, Height = 32 };
        private readonly ThemedComboBox _preset = new() { Width = 320, Height = 34 };
        private readonly Label _hash = new() { AutoSize = true };
        private readonly List<DoorAudioPreset> _presets;
        private readonly bool _presetsOptional;

        public string DoorName => (_name.Text ?? string.Empty).Trim();
        public DoorAudioPreset? SelectedPreset
        {
            get
            {
                if (_preset.SelectedItem is not string n) return null;
                return _presets.FirstOrDefault(p => p.Name == n);
            }
        }

        public DoorEditDialog(List<DoorAudioPreset> presets, string title, string? initialName, bool allowNameEdit = true, bool presetsOptional = false)
        {
            _presets = presets;
            _presetsOptional = presetsOptional;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 190);
            ShowInTaskbar = false;
            AppTheme.StyleForm(this);

            _hash.ForeColor = AppTheme.Faint;
            _hash.Font = AppTheme.MonoFont;

            var nameLbl = new Label { Text = "Door name", Left = 12, Top = 12, AutoSize = true, ForeColor = AppTheme.Faint };
            _name.Left = 12;
            _name.Top = 32;
            _name.Text = initialName ?? string.Empty;
            _name.ReadOnly = !allowNameEdit;
            _name.TextChanged += (_, _) => UpdateHash();

            var presetLbl = new Label { Text = "Door sound", Left = 12, Top = 64, AutoSize = true, ForeColor = AppTheme.Faint };
            _preset.Left = 12;
            _preset.Top = 84;
            foreach (var p in presets)
                _preset.Items.Add(p.Name);
            if (_preset.Items.Count > 0) _preset.SelectedIndex = 0;

            _hash.Left = 12;
            _hash.Top = 116;

            var ok = new Button { Text = "Confirm", DialogResult = DialogResult.OK, Left = 176, Top = 150, Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 264, Top = 150, Width = 80 };
            AppTheme.StyleButton(ok, primary: true);
            AppTheme.StyleButton(cancel);
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(DoorName))
                {
                    MessageBox.Show(this, "Enter the door name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    return;
                }
                if (!_presetsOptional && SelectedPreset == null)
                {
                    MessageBox.Show(this, "Select a door sound.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };

            Controls.Add(nameLbl);
            Controls.Add(_name);
            Controls.Add(presetLbl);
            Controls.Add(_preset);
            Controls.Add(_hash);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            UpdateHash();
        }

        private void UpdateHash()
        {
            var n = DoorName;
            if (string.IsNullOrEmpty(n))
            {
                _hash.Text = "Converted hash: —";
                return;
            }
            _hash.Text =
                $"d_{n}  |  dasl_{DoorAudioDocument.FormatModelHashHex(n)}  |  hash_{DoorAudioDocument.FormatModelHashHex(n)}";
        }
    }
}
