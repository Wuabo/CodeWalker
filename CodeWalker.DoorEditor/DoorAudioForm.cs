using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Door audio UI aligned with GTA5-Door-Editor Audio tool:
    /// doors list + preset catalog dropdown + Rel XML import/export.
    /// Catalog labels stay as friendly names; Sounds/TuningParams are resolved via JenkIndex.
    /// </summary>
    public sealed class DoorAudioForm : Form
    {
        private readonly DoorAudioDocument _doc = new();
        private readonly Func<IEnumerable<string>> _getMappingModels;
        private readonly Func<RpfManager?> _getRpf;

        private readonly ThemedListBox _doorList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly ThemedComboBox _preset = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ThemedTextBox _doorName = new() { Width = 360 };
        private readonly ThemedTextBox _sounds = new() { Width = 360 };
        private readonly ThemedTextBox _tuning = new() { Width = 360 };
        private readonly ThemedNumeric _occlusion = new()
        {
            Minimum = 0, Maximum = 1, DecimalPlaces = 3, Increment = 0.05m, Value = 0.7m, Width = 120
        };
        private readonly ThemedTextBox _customPresetName = new() { Width = 220 };
        private readonly ThemedButton _saveCustomBtn = new() { Text = "Save to presets", AutoSize = true, Height = 28 };
        private readonly Label _relPreview = new()
        {
            AutoSize = true, ForeColor = AppTheme.Faint, Font = AppTheme.MonoFont, Margin = new Padding(0, 4, 0, 0)
        };
        private readonly Label _status = new()
        {
            Dock = DockStyle.Bottom, Height = 24, ForeColor = AppTheme.Muted, Font = AppTheme.SmallFont,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0)
        };
        private readonly FlowLayoutPanel _customRow = new()
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Visible = false, BackColor = AppTheme.Window
        };

        private bool _loading;
        private string? _importPath;

        public DoorAudioForm(Func<IEnumerable<string>> getMappingModels, Func<RpfManager?> getRpf)
        {
            _getMappingModels = getMappingModels;
            _getRpf = getRpf;

            Text = "Door Audio";
            Width = 980;
            Height = 640;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = AppTheme.UiFont;
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Foreground;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { /* ignore */ }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10),
                BackColor = AppTheme.Window
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var toolbar = ToolBar(
                ("Import REL…", false, ImportRel),
                ("Export REL…", true, ExportRel),
                ("Edit presets…", false, EditPresets),
                ("Sync mappings", false, SyncFromMappings),
                ("Reset", false, ResetSession));
            root.Controls.Add(toolbar, 0, 0);
            root.SetColumnSpan(toolbar, 2);

            root.Controls.Add(BuildDoorPane(), 0, 1);
            root.Controls.Add(BuildDetailPane(), 1, 1);

            Controls.Add(root);
            Controls.Add(_status);

            _doorList.SelectedIndexChanged += (_, _) =>
            {
                if (_loading) return;
                PullDoorToFields();
            };
            _preset.SelectedIndexChanged += (_, _) =>
            {
                if (_loading) return;
                ApplyPresetSelection();
            };
            _doorName.TextChanged += (_, _) =>
            {
                if (_loading) return;
                UpdateRelPreview();
                PushFieldsToDoor(updateName: true);
            };
            _sounds.TextChanged += (_, _) => { if (!_loading) PushFieldsToDoor(); };
            _tuning.TextChanged += (_, _) => { if (!_loading) PushFieldsToDoor(); };
            _occlusion.ValueChanged += (_, _) => { if (!_loading) PushFieldsToDoor(); };
            _saveCustomBtn.Click += (_, _) => SaveCustomToCatalog();

            // Resolve hash_ / decimal fields to known Jenk strings (labels stay friendly).
            _doc.ResolveCatalogNames();
            EnsureBlankDoor();
            RefreshPresetCombo();
            RefreshDoorList();
            PullDoorToFields();
            SetStatus($"{_doc.Catalog.Count} presets · pick a preset per door, then Export REL");

            Shown += (_, _) =>
            {
                // After game strings load, resolve Sounds/TuningParams again.
                _doc.ResolveCatalogNames();
                var rpf = _getRpf();
                if (rpf != null)
                {
                    // Soft nudge: JenkIndex often fills once RPF/strings are ready.
                    _doc.ResolveCatalogNames();
                    foreach (var a in _doc.Assignments)
                    {
                        a.Sounds = DoorAudioDocument.ResolveAudioName(a.Sounds);
                        a.TuningParams = DoorAudioDocument.ResolveAudioName(a.TuningParams);
                    }
                    RefreshPresetCombo();
                    PullDoorToFields();
                }
            };
        }

        private Control BuildDoorPane()
        {
            var pane = Pane();
            pane.Controls.Add(ListHost(_doorList));
            pane.Controls.Add(ToolBar(
                ("Add door", true, AddDoor),
                ("Remove", false, RemoveDoor)));
            pane.Controls.Add(Header("Doors"));
            return pane;
        }

        private Control BuildDetailPane()
        {
            var pane = Pane();
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = AppTheme.Window,
                Padding = new Padding(8, 4, 8, 8)
            };

            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = AppTheme.Window
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            void Section(string title, string? hint = null)
            {
                int r = form.RowCount++;
                form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var box = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Margin = new Padding(0, 12, 0, 4), BackColor = AppTheme.Window
                };
                box.Controls.Add(new Label
                {
                    Text = title, AutoSize = true, Font = AppTheme.HeaderFont, ForeColor = AppTheme.BlueBright
                });
                if (!string.IsNullOrEmpty(hint))
                {
                    box.Controls.Add(new Label
                    {
                        Text = hint, AutoSize = true, MaximumSize = new Size(520, 0),
                        ForeColor = AppTheme.Faint, Font = AppTheme.SmallFont
                    });
                }
                form.Controls.Add(box, 0, r);
                form.SetColumnSpan(box, 2);
            }

            void Row(string label, Control c)
            {
                int r = form.RowCount++;
                form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                form.Controls.Add(new Label
                {
                    Text = label, AutoSize = true, ForeColor = AppTheme.Muted,
                    Margin = new Padding(0, 8, 8, 4)
                }, 0, r);
                c.Margin = new Padding(0, 4, 0, 4);
                form.Controls.Add(c, 1, r);
            }

            Section("Identity", "Exports as your_name · link dasl_<jenkins-hash> is generated automatically.");
            Row("Door name", _doorName);
            int prevRow = form.RowCount++;
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Controls.Add(_relPreview, 1, prevRow);

            Section("Preset", "Pick a catalog preset, or Custom to edit Sounds / TuningParams.");
            Row("Audio preset", _preset);

            _customRow.Controls.Add(new Label
            {
                Text = "Custom name", AutoSize = true, ForeColor = AppTheme.Muted,
                Margin = new Padding(0, 6, 8, 0)
            });
            _customPresetName.Margin = new Padding(0, 0, 8, 0);
            _customRow.Controls.Add(_customPresetName);
            _customRow.Controls.Add(_saveCustomBtn);
            int customR = form.RowCount++;
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Controls.Add(_customRow, 0, customR);
            form.SetColumnSpan(_customRow, 2);

            Section("Parameters", "Locked by preset — switch to Custom to edit.");
            Row("Sounds", _sounds);
            Row("TuningParams", _tuning);
            Row("MaxOcclusion", _occlusion);

            scroll.Controls.Add(form);
            pane.Controls.Add(scroll);
            pane.Controls.Add(Header("Assignment"));
            return pane;
        }

        private static Panel Pane() => new() { Dock = DockStyle.Fill, BackColor = AppTheme.Window, Padding = new Padding(4) };

        private static Label Header(string text) => new()
        {
            Text = text, Dock = DockStyle.Top, Height = 26,
            Font = AppTheme.HeaderFont, ForeColor = AppTheme.BlueBright,
            TextAlign = ContentAlignment.MiddleLeft
        };

        private static Panel ListHost(Control list)
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 4, 0) };
            host.Controls.Add(list);
            return host;
        }

        private FlowLayoutPanel ToolBar(params (string text, bool primary, Action click)[] buttons)
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = AppTheme.Window
            };
            foreach (var (text, primary, click) in buttons)
            {
                var b = new ThemedButton
                {
                    Text = text, Primary = primary, AutoSize = true,
                    Margin = new Padding(0, 4, 6, 0), Height = 28
                };
                b.Click += (_, _) => click();
                bar.Controls.Add(b);
            }
            return bar;
        }

        private DoorAudioAssignment? SelectedDoor()
        {
            if (_doorList.SelectedIndex < 0 || _doorList.SelectedIndex >= _doc.Assignments.Count) return null;
            return _doc.Assignments[_doorList.SelectedIndex];
        }

        private void EnsureBlankDoor()
        {
            if (_doc.Assignments.Count > 0) return;
            _doc.Assignments.Add(new DoorAudioAssignment());
        }

        private void RefreshPresetCombo(string? selectPresetLabel = null)
        {
            _loading = true;
            _preset.Items.Clear();
            _preset.Items.Add("Choose preset");
            foreach (var p in _doc.Catalog)
                _preset.Items.Add(p.Label); // friendly catalog name only
            _preset.Items.Add("Custom");

            if (!string.IsNullOrEmpty(selectPresetLabel))
            {
                var idx = _doc.Catalog.FindIndex(p =>
                    string.Equals(p.Label, selectPresetLabel, StringComparison.OrdinalIgnoreCase));
                _preset.SelectedIndex = idx >= 0 ? idx + 1 : _preset.Items.Count - 1;
            }
            else if (_preset.SelectedIndex < 0)
                _preset.SelectedIndex = 0;

            _loading = false;
            UpdateParamLock();
        }

        private void RefreshDoorList(string? selectModel = null)
        {
            _loading = true;
            _doorList.BeginUpdate();
            _doorList.Items.Clear();
            foreach (var a in _doc.Assignments)
            {
                var name = string.IsNullOrWhiteSpace(a.NormalizedModel) ? "(new door)" : a.NormalizedModel;
                var tag = string.IsNullOrWhiteSpace(a.PresetLabel)
                    ? (string.IsNullOrWhiteSpace(a.Sounds) ? "unassigned" : "custom")
                    : a.PresetLabel;
                _doorList.Items.Add($"{name}  ·  {tag}");
            }
            _doorList.EndUpdate();
            _loading = false;

            if (!string.IsNullOrEmpty(selectModel))
            {
                var idx = _doc.Assignments.FindIndex(a =>
                    string.Equals(a.NormalizedModel, selectModel, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _doorList.SelectedIndex = idx;
            }
            else if (_doorList.Items.Count > 0 && _doorList.SelectedIndex < 0)
                _doorList.SelectedIndex = 0;

            SetStatus($"{_doc.Catalog.Count} presets · {_doc.Assignments.Count} door(s)");
        }

        private void PullDoorToFields()
        {
            var a = SelectedDoor();
            _loading = true;
            if (a == null)
            {
                _doorName.Text = _sounds.Text = _tuning.Text = "";
                _occlusion.Value = 0.7m;
                _relPreview.Text = "";
                _preset.SelectedIndex = 0;
                _loading = false;
                UpdateParamLock();
                return;
            }

            _doorName.Text = a.NormalizedModel;
            _sounds.Text = DoorAudioDocument.ResolveAudioName(a.Sounds);
            _tuning.Text = DoorAudioDocument.ResolveAudioName(a.TuningParams);
            _occlusion.Value = (decimal)Math.Clamp(a.MaxOcclusion, 0f, 1f);
            UpdateRelPreview();

            var match = _doc.Catalog.FindIndex(p =>
                string.Equals(p.Sounds, a.Sounds, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.TuningParams, a.TuningParams, StringComparison.OrdinalIgnoreCase));
            if (match >= 0)
            {
                _preset.SelectedIndex = match + 1;
                a.PresetLabel = _doc.Catalog[match].Label;
            }
            else if (!string.IsNullOrWhiteSpace(a.Sounds) || !string.IsNullOrWhiteSpace(a.TuningParams))
            {
                _preset.SelectedIndex = _preset.Items.Count - 1; // Custom
                a.PresetLabel = "";
            }
            else
            {
                _preset.SelectedIndex = 0;
                a.PresetLabel = "";
            }

            _loading = false;
            UpdateParamLock();
        }

        private void PushFieldsToDoor(bool updateName = false)
        {
            var a = SelectedDoor();
            if (a == null) return;
            if (updateName)
            {
                // Keep what the user typed in the box; only normalize the stored model.
                var m = _doorName.Text.Trim().ToLowerInvariant();
                if (m.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)) m = m[..^4];
                if (m.StartsWith("d_", StringComparison.OrdinalIgnoreCase) && m.Length > 2)
                    m = m[2..];
                a.ModelName = m;
            }
            a.Sounds = _sounds.Text.Trim();
            a.TuningParams = _tuning.Text.Trim();
            a.MaxOcclusion = (float)_occlusion.Value;

            var match = DoorAudioDocument.MatchPresetLabel(_doc.Catalog, a.Sounds, a.TuningParams);
            a.PresetLabel = match;
            UpdateRelPreview();

            var idx = _doorList.SelectedIndex;
            if (idx >= 0 && idx < _doorList.Items.Count)
            {
                var name = string.IsNullOrWhiteSpace(a.NormalizedModel) ? "(new door)" : a.NormalizedModel;
                var tag = string.IsNullOrWhiteSpace(a.PresetLabel)
                    ? (string.IsNullOrWhiteSpace(a.Sounds) ? "unassigned" : "custom")
                    : a.PresetLabel;
                // ListBox item replace can briefly clear selection — suppress reload so
                // the name field is not overwritten / lowercased mid-typing.
                _loading = true;
                _doorList.Items[idx] = $"{name}  ·  {tag}";
                if (_doorList.SelectedIndex != idx)
                    _doorList.SelectedIndex = idx;
                _loading = false;
            }
        }

        private void UpdateRelPreview()
        {
            var model = _doorName.Text.Trim().ToLowerInvariant();
            if (model.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)) model = model[..^4];
            if (model.StartsWith("d_", StringComparison.OrdinalIgnoreCase) && model.Length > 2)
                model = model[2..];
            if (string.IsNullOrEmpty(model))
            {
                _relPreview.Text = "REL name: …   ·   link dasl_…";
                return;
            }
            var tmp = new DoorAudioAssignment { ModelName = model };
            _relPreview.Text = $"REL name: {tmp.SettingsName}   ·   link {tmp.LinkName}";
        }

        private void UpdateParamLock()
        {
            bool custom = IsCustomSelected();
            bool none = _preset.SelectedIndex <= 0;
            bool locked = !custom && !none;
            _sounds.ReadOnly = locked;
            _tuning.ReadOnly = locked;
            _occlusion.ReadOnly = locked;
            _customRow.Visible = custom;
        }

        private bool IsCustomSelected() =>
            _preset.Items.Count > 0 && _preset.SelectedIndex == _preset.Items.Count - 1;

        private void ApplyPresetSelection()
        {
            UpdateParamLock();
            if (_preset.SelectedIndex <= 0)
            {
                // Choose preset — leave fields as-is
                PushFieldsToDoor();
                return;
            }
            if (IsCustomSelected())
            {
                PushFieldsToDoor();
                return;
            }

            var preset = _doc.Catalog[_preset.SelectedIndex - 1];
            var door = SelectedDoor();
            if (door != null)
            {
                DoorAudioDocument.ApplyPreset(door, preset);
                door.Sounds = DoorAudioDocument.ResolveAudioName(door.Sounds);
                door.TuningParams = DoorAudioDocument.ResolveAudioName(door.TuningParams);
            }

            _loading = true;
            _sounds.Text = DoorAudioDocument.ResolveAudioName(preset.Sounds);
            _tuning.Text = DoorAudioDocument.ResolveAudioName(preset.TuningParams);
            _occlusion.Value = (decimal)Math.Clamp(preset.MaxOcclusion, 0f, 1f);
            _loading = false;
            PushFieldsToDoor();
            SetStatus($"Preset “{preset.Label}” applied");
        }

        private void SaveCustomToCatalog()
        {
            var name = _customPresetName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Name this custom preset first.", "Door Audio",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var sounds = _sounds.Text.Trim();
            var tuning = _tuning.Text.Trim();
            if (string.IsNullOrEmpty(sounds) || string.IsNullOrEmpty(tuning))
            {
                MessageBox.Show(this, "Sounds and TuningParams are required.", "Door Audio",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var existing = _doc.Catalog.FindIndex(p =>
                string.Equals(p.Label, name, StringComparison.OrdinalIgnoreCase));
            var preset = new DoorAudioPreset
            {
                Id = existing >= 0 ? _doc.Catalog[existing].Id : "preset:" + _doc.Catalog.Count,
                Label = name,
                Sounds = sounds,
                TuningParams = tuning,
                MaxOcclusion = (float)_occlusion.Value,
            };
            if (existing >= 0) _doc.Catalog[existing] = preset;
            else _doc.Catalog.Add(preset);

            JenkIndex.Ensure(sounds);
            JenkIndex.Ensure(tuning);
            RefreshPresetCombo(name);
            PushFieldsToDoor();
            SetStatus(existing >= 0 ? $"Updated preset “{name}”" : $"Added preset “{name}”");
        }

        private void SyncFromMappings()
        {
            var models = _getMappingModels()?.ToList() ?? new List<string>();
            if (models.Count == 0)
            {
                MessageBox.Show(this, "No door mappings in the Tuning document yet.", "Door Audio",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DoorAudioPreset? preset = null;
            if (_preset.SelectedIndex > 0 && !IsCustomSelected())
                preset = _doc.Catalog[_preset.SelectedIndex - 1];
            _doc.SyncModelsFromMappings(models, preset);
            RefreshDoorList();
            PullDoorToFields();
            SetStatus($"Synced {models.Count} mapping model(s)");
        }

        private void AddDoor()
        {
            using var dlg = new NameInputDialog("Add door", "Door name (archetype):", "");
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var model = dlg.Value.Trim().ToLowerInvariant();
            if (model.StartsWith("d_", StringComparison.OrdinalIgnoreCase) && model.Length > 2)
                model = model[2..];
            if (string.IsNullOrEmpty(model)) return;
            if (_doc.Assignments.Any(a => a.NormalizedModel == model))
            {
                MessageBox.Show(this, "Door already in the list.", "Door Audio",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var a = new DoorAudioAssignment { ModelName = model };
            if (_preset.SelectedIndex > 0 && !IsCustomSelected())
                DoorAudioDocument.ApplyPreset(a, _doc.Catalog[_preset.SelectedIndex - 1]);
            _doc.Assignments.Add(a);
            RefreshDoorList(model);
            PullDoorToFields();
        }

        private void RemoveDoor()
        {
            var idx = _doorList.SelectedIndex;
            if (idx < 0 || idx >= _doc.Assignments.Count) return;
            _doc.Assignments.RemoveAt(idx);
            EnsureBlankDoor();
            RefreshDoorList();
            PullDoorToFields();
        }

        private void ResetSession()
        {
            if (MessageBox.Show(this,
                    "Clear doors and assignments?\nPreset catalog is left unchanged.",
                    "Reset audio", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _doc.Assignments.Clear();
            _importPath = null;
            EnsureBlankDoor();
            RefreshDoorList();
            PullDoorToFields();
            SetStatus("Session reset");
        }

        private void EditPresets()
        {
            using var dlg = new DoorAudioPresetsForm(_doc.Catalog);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _doc.ReplaceCatalog(dlg.ResultCatalog);
            // Rematch doors to new catalog indices / labels
            foreach (var a in _doc.Assignments)
            {
                a.Sounds = DoorAudioDocument.ResolveAudioName(a.Sounds);
                a.TuningParams = DoorAudioDocument.ResolveAudioName(a.TuningParams);
                a.PresetLabel = DoorAudioDocument.MatchPresetLabel(_doc.Catalog, a.Sounds, a.TuningParams);
            }
            RefreshPresetCombo();
            RefreshDoorList(SelectedDoor()?.NormalizedModel);
            PullDoorToFields();
            SetStatus($"{_doc.Catalog.Count} presets applied");
        }

        private void ImportRel()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Import DAT151 REL",
                Filter = "Rel XML|*.xml;*.rel.xml|All|*.*"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var loaded = DoorAudioDocument.LoadRelXml(dlg.FileName);
                foreach (var a in loaded.Assignments)
                {
                    a.Sounds = DoorAudioDocument.ResolveAudioName(a.Sounds);
                    a.TuningParams = DoorAudioDocument.ResolveAudioName(a.TuningParams);
                    a.PresetLabel = DoorAudioDocument.MatchPresetLabel(_doc.Catalog, a.Sounds, a.TuningParams);
                }
                _doc.Assignments.Clear();
                _doc.Assignments.AddRange(loaded.Assignments);
                if (_doc.Assignments.Count == 0) EnsureBlankDoor();
                _importPath = dlg.FileName;
                RefreshDoorList();
                PullDoorToFields();
                SetStatus($"Imported {_doc.Assignments.Count} door(s) from {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Import REL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportRel()
        {
            PushFieldsToDoor(updateName: true);
            var rows = _doc.Assignments
                .Where(a => !string.IsNullOrWhiteSpace(a.NormalizedModel))
                .ToList();
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "Add at least one door with a name before exporting.", "Door Audio",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var a in rows)
            {
                if (string.IsNullOrWhiteSpace(a.Sounds) || string.IsNullOrWhiteSpace(a.TuningParams))
                {
                    MessageBox.Show(this,
                        $"Door '{a.NormalizedModel}' needs Sounds and TuningParams — pick a preset.",
                        "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            using var dlg = new SaveFileDialog
            {
                Title = "Export DAT151 REL",
                Filter = "Rel XML|*.xml|All|*.*",
                FileName = string.IsNullOrEmpty(_importPath)
                    ? "game.dat151.rel.xml"
                    : Path.GetFileName(_importPath)
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var exportDoc = new DoorAudioDocument();
                exportDoc.Catalog.Clear();
                exportDoc.Assignments.AddRange(rows);
                exportDoc.SaveRelXml(dlg.FileName);

                // CodeWalker needs the sidecar nametable to resolve model / dasl_* hashes.
                var nametablePath = DoorAudioDocument.SuggestNameTablePath(dlg.FileName);
                exportDoc.SaveNameTable(nametablePath);

                _importPath = dlg.FileName;
                SetStatus("Exported REL + nametable → " + Path.GetDirectoryName(dlg.FileName));
                MessageBox.Show(this,
                    "Exported:\n" + dlg.FileName + "\n" + nametablePath +
                    "\n\nPut the .nametable in CodeWalker’s nametables folder (or next to the Rel) so hashes resolve.",
                    "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export REL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetStatus(string text) => _status.Text = text;
    }
}
