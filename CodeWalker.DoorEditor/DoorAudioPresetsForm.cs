using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Edit availableDoorSound catalog JSON (same role as GTA5-Door-Editor PresetJsonEditor).
    /// </summary>
    internal sealed class DoorAudioPresetsForm : Form
    {
        private readonly TextBox _editor = new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = AppTheme.MonoFont,
            AcceptsTab = true,
            WordWrap = false,
            BackColor = AppTheme.Input,
            ForeColor = AppTheme.Bright,
            BorderStyle = BorderStyle.FixedSingle
        };
        private readonly Label _status = new()
        {
            Dock = DockStyle.Bottom, Height = 22, ForeColor = AppTheme.Muted, Font = AppTheme.SmallFont,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0)
        };

        public List<DoorAudioPreset> ResultCatalog { get; private set; }

        public DoorAudioPresetsForm(IReadOnlyList<DoorAudioPreset> catalog)
        {
            ResultCatalog = new List<DoorAudioPreset>(catalog);

            Text = "Preset catalog";
            Width = 720;
            Height = 560;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = AppTheme.UiFont;
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Foreground;

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 6, 8, 0),
                BackColor = AppTheme.Window
            };
            bar.Controls.Add(Btn("Apply catalog", true, Apply));
            bar.Controls.Add(Btn("Load JSON…", false, LoadJson));
            bar.Controls.Add(Btn("Save JSON…", false, SaveJson));
            bar.Controls.Add(Btn("Reset to default", false, ResetDefault));
            bar.Controls.Add(Btn("Cancel", false, () => { DialogResult = DialogResult.Cancel; Close(); }));

            _editor.Text = DoorAudioDocument.CatalogToJson(catalog);
            _status.Text = $"{catalog.Count} presets · Ctrl+Enter apply";

            Controls.Add(_editor);
            Controls.Add(bar);
            Controls.Add(_status);

            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    Apply();
                }
            };
        }

        private static ThemedButton Btn(string text, bool primary, Action click)
        {
            var b = new ThemedButton
            {
                Text = text, Primary = primary, AutoSize = true, Height = 28,
                Margin = new Padding(0, 0, 6, 0)
            };
            b.Click += (_, _) => click();
            return b;
        }

        private void Apply()
        {
            try
            {
                var next = DoorAudioDocument.ParseCatalogJson(_editor.Text);
                foreach (var p in next)
                {
                    // Keep friendly Label; only resolve hash Sounds / TuningParams.
                    p.Sounds = DoorAudioDocument.ResolveAudioName(p.Sounds);
                    p.TuningParams = DoorAudioDocument.ResolveAudioName(p.TuningParams);
                }
                ResultCatalog = next;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Preset catalog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LoadJson()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Load sound preset catalog",
                Filter = "JSON|*.json|All|*.*"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _editor.Text = File.ReadAllText(dlg.FileName);
                _status.Text = "Loaded " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Load JSON", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveJson()
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Save sound preset catalog",
                Filter = "JSON|*.json|All|*.*",
                FileName = "door-audio-presets.json"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dlg.FileName, _editor.Text);
                _status.Text = "Saved → " + dlg.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Save JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetDefault()
        {
            if (MessageBox.Show(this, "Reset editor to built-in defaults?", "Preset catalog",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            var defaults = DoorAudioDocument.LoadDefaultCatalog();
            _editor.Text = DoorAudioDocument.CatalogToJson(defaults);
            _status.Text = $"{defaults.Count} default presets";
        }
    }
}
