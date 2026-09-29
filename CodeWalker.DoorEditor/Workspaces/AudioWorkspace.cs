using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Dialogs;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Workspaces
{
    public sealed class AudioWorkspace : UserControl
    {
        private readonly RpfManager _rpfMan;
        private readonly Action<string> _setStatus;

        private readonly DarkListBox _projectList = new() { Dock = DockStyle.Fill };
        private readonly DarkListBox _gameList = new() { Dock = DockStyle.Fill };
        private readonly ThemedTextBox _gameSearch = new();
        private readonly ThemedComboBox _presetCombo = new() { Width = 260, Height = 34 };
        private readonly ThemedTextBox _nameBox = new();
        private readonly ThemedTextBox _soundsBox = new();
        private readonly ThemedTextBox _tuningBox = new();
        private readonly ThemedTextBox _occlusionBox = new();
        private readonly ThemedTextBox _linkBox = new();
        private readonly Label _hashInfo = new() { AutoSize = false, Height = 48, Dock = DockStyle.Bottom };

        private DoorAudioDocument _document = new();
        private List<DoorAudioGameEntry> _gameDoors = new();
        private List<DoorAudioPreset> _presets = new();
        private List<DoorAudioPreset> _curatedPresets = new();
        private bool _loadingUi;

        public AudioWorkspace(RpfManager rpfMan, Action<string> setStatus)
        {
            _rpfMan = rpfMan;
            _setStatus = setStatus;
            BackColor = AppTheme.Background;
            Dock = DockStyle.Fill;

            _hashInfo.ForeColor = AppTheme.Faint;
            _hashInfo.Font = AppTheme.MonoFont;
            _hashInfo.Padding = new Padding(4);

            BuildLayout();
            WireEvents();
        }

        public async Task InitializeAsync()
        {
            _curatedPresets = LoadCuratedPresets();
            await Task.Run(() =>
            {
                _gameDoors = DoorAudioDocument.LoadAllFromGame(_rpfMan, _setStatus);
                _presets = DoorAudioDocument.MergePresets(_curatedPresets, _gameDoors);
            });
            FillPresetCombo();
            RefreshProjectList();
            RefreshGameList();
            _setStatus($"Audio ready — {_gameDoors.Count} game door audios, {_curatedPresets.Count} sound presets");
        }

        private void BuildLayout()
        {
            var header = BuildHeader();
            var split = SplitLayout.Create(Orientation.Vertical, preferredDistance: 260, panel1Min: 180, panel2Min: 400);
            split.BackColor = AppTheme.Background;
            split.Panel1.BackColor = AppTheme.Panel;
            split.Panel2.BackColor = AppTheme.Background;

            var right = SplitLayout.Create(Orientation.Vertical, preferredDistance: 540, panel1Min: 280, panel2Min: 200);
            right.BackColor = AppTheme.Background;
            right.Panel1.BackColor = AppTheme.Background;
            right.Panel2.BackColor = AppTheme.Panel;

            split.Panel1.Controls.Add(BuildProjectPane());
            right.Panel1.Controls.Add(BuildEditorPane());
            right.Panel2.Controls.Add(BuildGamePane());
            split.Panel2.Controls.Add(right);

            Controls.Add(split);
            Controls.Add(header);
        }

        private WorkspaceHeader BuildHeader()
        {
            var header = new WorkspaceHeader("Door Audio");
            header.AddAction("Import…").Click += (_, _) => ImportFile();
            header.AddAction("Generate REL…", primary: true).Click += (_, _) => ExportRel();
            header.AddAction("Export XML…").Click += (_, _) => ExportXmlOnly();
            header.AddAction("Rescan game").Click += async (_, _) => await RescanGameAsync();
            return header;
        }

        private Panel BuildProjectPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var header = new Label { Text = "Project doors", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(header);
            header.Font = AppTheme.UiFontBold;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };
            var add = new Button { Text = "Add…", AutoSize = true };
            var edit = new Button { Text = "Edit sound…", AutoSize = true };
            var del = new Button { Text = "Delete", AutoSize = true };
            AppTheme.StyleButton(add, primary: true);
            AppTheme.StyleButton(edit);
            AppTheme.StyleButton(del);
            add.Click += (_, _) => ShowAddDoorDialog();
            edit.Click += (_, _) => ShowEditPresetDialog();
            del.Click += (_, _) => DeleteDoor();
            buttons.Controls.Add(add);
            buttons.Controls.Add(edit);
            buttons.Controls.Add(del);

            pane.Controls.Add(_projectList);
            pane.Controls.Add(buttons);
            pane.Controls.Add(header);
            return pane;
        }

        private Panel BuildEditorPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = AppTheme.Background };
            var header = new Label { Text = "Door audio settings", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(header);
            header.Font = AppTheme.UiFontBold;

            var presetBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };
            var presetLbl = new Label { Text = "Sound preset", AutoSize = true, Padding = new Padding(0, 8, 8, 0) };
            AppTheme.StyleHint(presetLbl);
            _presetCombo.Width = 260;
            _presetCombo.Height = InputChrome.Height;
            _presetCombo.Margin = new Padding(0, 2, 8, 0);
            var apply = new Button { Text = "Apply", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            AppTheme.StyleButton(apply, primary: true);
            apply.Click += (_, _) => ApplySelectedPresetToDoor();
            presetBar.Controls.Add(presetLbl);
            presetBar.Controls.Add(_presetCombo);
            presetBar.Controls.Add(apply);

            _hashInfo.Dock = DockStyle.Bottom;
            _hashInfo.Height = 52;

            var formHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 8), BackColor = Color.Transparent };
            var stack = new Panel { Dock = DockStyle.Top, Height = 5 * 56, BackColor = Color.Transparent };
            void AddField(int index, string label, ThemedTextBox field)
            {
                int y = index * 56;
                var lbl = new Label
                {
                    Text = label,
                    Left = 0,
                    Top = y,
                    Width = 120,
                    Height = InputChrome.Height,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = AppTheme.Faint,
                    Font = AppTheme.SmallFont
                };
                field.Left = 128;
                field.Top = y;
                field.Height = InputChrome.Height;
                field.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                stack.Controls.Add(lbl);
                stack.Controls.Add(field);
            }
            AddField(0, "Name", _nameBox);
            AddField(1, "Sounds", _soundsBox);
            AddField(2, "Tuning params", _tuningBox);
            AddField(3, "Max occlusion", _occlusionBox);
            AddField(4, "Link name", _linkBox);
            void LayoutFields()
            {
                int w = Math.Max(160, formHost.ClientSize.Width - 128);
                foreach (var f in new[] { _nameBox, _soundsBox, _tuningBox, _occlusionBox, _linkBox })
                    f.Width = w;
            }
            formHost.Resize += (_, _) => LayoutFields();
            formHost.Controls.Add(stack);
            LayoutFields();

            pane.Controls.Add(formHost);
            pane.Controls.Add(_hashInfo);
            pane.Controls.Add(presetBar);
            pane.Controls.Add(header);
            return pane;
        }

        private Panel BuildGamePane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var header = new Label { Text = "Game door audios", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(header);
            header.Font = AppTheme.UiFontBold;
            _gameSearch.PlaceholderText = "Search game door audios…";
            _gameSearch.Dock = DockStyle.Top;
            _gameSearch.Height = InputChrome.Height;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };
            var clone = new Button { Text = "Clone to project", AutoSize = true };
            var apply = new Button { Text = "Apply to selected", AutoSize = true };
            AppTheme.StyleButton(clone, primary: true);
            AppTheme.StyleButton(apply);
            clone.Click += (_, _) => CloneGameDoor();
            apply.Click += (_, _) => ApplyGameToSelected();
            buttons.Controls.Add(clone);
            buttons.Controls.Add(apply);

            pane.Controls.Add(_gameList);
            pane.Controls.Add(_gameSearch);
            pane.Controls.Add(buttons);
            pane.Controls.Add(header);
            return pane;
        }

        private void WireEvents()
        {
            _projectList.SelectedIndexChanged += (_, _) => OnProjectSelectionChanged();
            _gameList.DoubleClick += (_, _) => CloneGameDoor();
            _gameSearch.TextChanged += (_, _) => RefreshGameList();

            void OnField(object? s, EventArgs e)
            {
                if (_loadingUi) return;
                PushFieldsToDoor();
            }
            _nameBox.TextChanged += (_, e) =>
            {
                if (_loadingUi) return;
                PushFieldsToDoor();
                RefreshProjectList(keepSelection: true);
            };
            _soundsBox.TextChanged += OnField;
            _tuningBox.TextChanged += OnField;
            _occlusionBox.TextChanged += OnField;
            _linkBox.TextChanged += OnField;
        }

        private static List<DoorAudioPreset> LoadCuratedPresets()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "presets", "door-sound-presets.json");
                if (!File.Exists(path)) return DefaultCuratedPresets();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<DoorAudioPreset>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? DefaultCuratedPresets();
            }
            catch
            {
                return DefaultCuratedPresets();
            }
        }

        private static List<DoorAudioPreset> DefaultCuratedPresets() =>
        [
            new() { Name = "No Sound", Sounds = "null", TuningParams = "null", MaxOcclusion = 0f },
            new() { Name = "Convenience Store Door", Sounds = "hash_f1e8d9fe", TuningParams = "hash_0246d335", MaxOcclusion = 0.7f },
            new() { Name = "Pushed Door", Sounds = "hash_7f11a20c", TuningParams = "hash_0246d335", MaxOcclusion = 0.7f },
            new() { Name = "Elevator Door", Sounds = "hash_37d314bf", TuningParams = "hash_1ec7576e", MaxOcclusion = 0.7f },
            new() { Name = "Jail Door", Sounds = "hash_0d2cd7d4", TuningParams = "hash_0246d335", MaxOcclusion = 0.7f },
            new() { Name = "Sliding Door", Sounds = "hash_c9e95a9a", TuningParams = "hash_25b9f39f", MaxOcclusion = 0.7f },
            new() { Name = "Normal Gate", Sounds = "hash_d5377a40", TuningParams = "hash_c85140b1", MaxOcclusion = 0.7f },
            new() { Name = "Slide Gate", Sounds = "hash_f0d1de2a", TuningParams = "hash_8c484a48", MaxOcclusion = 0.7f },
            new() { Name = "Slide Gate 2", Sounds = "hash_a526a46e", TuningParams = "hash_8c484a48", MaxOcclusion = 0.7f },
            new() { Name = "Up-Down Gate", Sounds = "hash_0ca57a12", TuningParams = "hash_943a667a", MaxOcclusion = 0.7f },
        ];

        private void FillPresetCombo()
        {
            // Toolbar combo = curated sound presets only (game doors live in the right list).
            // Avoid dumping thousands of game entries into a dropdown.
            _presetCombo.Items.Clear();
            var source = _curatedPresets.Count > 0 ? _curatedPresets : _presets.Take(40).ToList();
            foreach (var p in source)
                _presetCombo.Items.Add(p.Name);
            if (_presetCombo.Items.Count > 0)
                _presetCombo.SelectedIndex = 0;
        }

        private async Task RescanGameAsync()
        {
            try
            {
                UseWaitCursor = true;
                await Task.Run(() =>
                {
                    _gameDoors = DoorAudioDocument.LoadAllFromGame(_rpfMan, _setStatus);
                    _presets = DoorAudioDocument.MergePresets(_curatedPresets, _gameDoors);
                });
                FillPresetCombo();
                RefreshGameList();
                _setStatus($"Rescanned — {_gameDoors.Count} game door audios");
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void RefreshProjectList(bool keepSelection = false)
        {
            var sel = keepSelection ? GetSelectedProjectDoor()?.Name : null;
            if (keepSelection && _projectList.SelectedItem is string s) sel = s;
            _loadingUi = true;
            _projectList.BeginUpdate();
            _projectList.Items.Clear();
            foreach (var d in _document.Doors)
                _projectList.Items.Add(d.Name);
            _projectList.EndUpdate();
            if (sel != null)
            {
                var idx = _projectList.Items.IndexOf(sel);
                if (idx >= 0) _projectList.SelectedIndex = idx;
            }
            else if (_projectList.Items.Count > 0 && _projectList.SelectedIndex < 0)
                _projectList.SelectedIndex = 0;
            _loadingUi = false;
            OnProjectSelectionChanged();
        }

        private void RefreshGameList()
        {
            var q = (_gameSearch.Text ?? string.Empty).Trim();
            IEnumerable<DoorAudioGameEntry> items = _gameDoors;
            if (!string.IsNullOrEmpty(q))
            {
                items = items.Where(d =>
                    d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    d.SettingsName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    d.Sounds.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    d.TuningParams.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    d.SourceFile.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var selected = GetSelectedGameDoor()?.Name;
            _gameList.BeginUpdate();
            _gameList.Items.Clear();
            foreach (var d in items)
            {
                var label = string.IsNullOrEmpty(d.SettingsName) ? d.Name : d.SettingsName;
                _gameList.Items.Add(new GameDoorListItem(d, label));
            }
            _gameList.EndUpdate();
            if (selected != null)
            {
                for (int i = 0; i < _gameList.Items.Count; i++)
                {
                    if (_gameList.Items[i] is GameDoorListItem gi && gi.Entry.Name == selected)
                    {
                        _gameList.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void OnProjectSelectionChanged()
        {
            if (_loadingUi) return;
            var door = GetSelectedProjectDoor();
            _loadingUi = true;
            if (door == null)
            {
                _nameBox.Text = _soundsBox.Text = _tuningBox.Text = _occlusionBox.Text = _linkBox.Text = "";
                _hashInfo.Text = "Add a door to see settings name (d_*) and link name (dasl_*).";
            }
            else
            {
                _nameBox.Text = door.Name;
                _soundsBox.Text = door.Sounds;
                _tuningBox.Text = door.TuningParams;
                _occlusionBox.Text = door.MaxOcclusion.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _linkBox.Text = door.LinkName;
                UpdateHashInfo(door);
            }
            _loadingUi = false;
        }

        private void PushFieldsToDoor()
        {
            var door = GetSelectedProjectDoor();
            if (door == null) return;
            door.Name = (_nameBox.Text ?? string.Empty).Trim();
            door.Sounds = string.IsNullOrWhiteSpace(_soundsBox.Text) ? "null" : _soundsBox.Text.Trim();
            door.TuningParams = string.IsNullOrWhiteSpace(_tuningBox.Text) ? "null" : _tuningBox.Text.Trim();
            if (float.TryParse(_occlusionBox.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var occ))
                door.MaxOcclusion = occ;
            door.LinkName = _linkBox.Text?.Trim() ?? string.Empty;
            UpdateHashInfo(door);
        }

        private void UpdateHashInfo(DoorAudioEntry door)
        {
            var settings = DoorAudioDocument.NormalizeDoorName(door.Name);
            var link = string.IsNullOrWhiteSpace(door.LinkName)
                ? DoorAudioDocument.GetAutoLinkName(door.Name)
                : door.LinkName;
            var hex = DoorAudioDocument.FormatModelHashHex(door.Name);
            _hashInfo.Text =
                $"Settings: {settings}{Environment.NewLine}" +
                $"Link: {link}  |  Model hash: hash_{hex}";
        }

        private DoorAudioEntry? GetSelectedProjectDoor()
        {
            if (_projectList.SelectedItem is not string name) return null;
            return _document.Doors.FirstOrDefault(d => d.Name == name);
        }

        private DoorAudioGameEntry? GetSelectedGameDoor() =>
            _gameList.SelectedItem is GameDoorListItem item ? item.Entry : null;

        private DoorAudioPreset? GetComboPreset()
        {
            if (_presetCombo.SelectedItem is not string name) return null;
            return _curatedPresets.FirstOrDefault(p => p.Name == name)
                ?? _presets.FirstOrDefault(p => p.Name == name);
        }

        private void ShowAddDoorDialog()
        {
            var presetList = _curatedPresets.Count > 0 ? _curatedPresets : _presets;
            using var dlg = new DoorEditDialog(presetList, "Add a new door", null);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var name = dlg.DoorName;
            if (_document.Doors.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "This door already exists.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var preset = dlg.SelectedPreset;
            _document.Doors.Add(new DoorAudioEntry
            {
                Name = name,
                Sounds = preset?.Sounds ?? "null",
                TuningParams = preset?.TuningParams ?? "null",
                MaxOcclusion = preset?.MaxOcclusion ?? 0.7f
            });
            RefreshProjectList();
            _projectList.SelectedItem = name;
            _setStatus($"Added {name} (hash_{DoorAudioDocument.FormatModelHashHex(name)})");
        }

        private void ShowEditPresetDialog()
        {
            var door = GetSelectedProjectDoor();
            if (door == null)
            {
                MessageBox.Show(this, "Select a project door first.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var presetList = _curatedPresets.Count > 0 ? _curatedPresets : _presets;
            using var dlg = new DoorEditDialog(presetList, "Edit sound of the door", door.Name, allowNameEdit: false);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var preset = dlg.SelectedPreset;
            if (preset == null) return;
            door.Sounds = preset.Sounds;
            door.TuningParams = preset.TuningParams;
            door.MaxOcclusion = preset.MaxOcclusion;
            OnProjectSelectionChanged();
            _setStatus($"Updated sound for {door.Name}");
        }

        private void ApplySelectedPresetToDoor()
        {
            var door = GetSelectedProjectDoor();
            var preset = GetComboPreset();
            if (door == null || preset == null) return;
            door.Sounds = preset.Sounds;
            door.TuningParams = preset.TuningParams;
            door.MaxOcclusion = preset.MaxOcclusion;
            OnProjectSelectionChanged();
            _setStatus($"Applied preset '{preset.Name}' to {door.Name}");
        }

        private void DeleteDoor()
        {
            var door = GetSelectedProjectDoor();
            if (door == null) return;
            if (MessageBox.Show(this, $"Delete \"{door.Name}\"?", "Door Audio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _document.Doors.Remove(door);
            RefreshProjectList();
            _setStatus($"Deleted {door.Name}");
        }

        private void CloneGameDoor()
        {
            var src = GetSelectedGameDoor();
            if (src == null)
            {
                MessageBox.Show(this, "Select a game door audio first.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var name = src.Name;
            if (_document.Doors.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                using var dlg = new DoorEditDialog(_presets, "Clone as…", name + "_copy", presetsOptional: true);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                name = dlg.DoorName;
                if (_document.Doors.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, "This door already exists.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            _document.Doors.Add(new DoorAudioEntry
            {
                Name = name,
                Sounds = src.Sounds,
                TuningParams = src.TuningParams,
                MaxOcclusion = src.MaxOcclusion
            });
            RefreshProjectList();
            _projectList.SelectedItem = name;
            _setStatus($"Cloned {src.SettingsName} → {name}");
        }

        private void ApplyGameToSelected()
        {
            var door = GetSelectedProjectDoor();
            var src = GetSelectedGameDoor();
            if (door == null || src == null)
            {
                MessageBox.Show(this, "Select a project door and a game door audio.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            door.Sounds = src.Sounds;
            door.TuningParams = src.TuningParams;
            door.MaxOcclusion = src.MaxOcclusion;
            OnProjectSelectionChanged();
            _setStatus($"Applied {src.SettingsName} settings to {door.Name}");
        }

        private void ImportFile()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Door audio|*.rel;*.xml|REL files|*.rel|XML files|*.xml|All files|*.*",
                Title = "Import door audio (CodeWalker or legacy Door/DoorModel XML)"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var path = dlg.FileName;
                if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    _document = DoorAudioDocument.FromXml(File.ReadAllText(path));
                else
                {
                    var rel = new RelFile();
                    rel.Load(File.ReadAllBytes(path), null);
                    _document = DoorAudioDocument.FromRel(rel);
                }
                RefreshProjectList();
                _setStatus($"Imported {_document.Doors.Count} doors from {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportRel()
        {
            if (_document.Doors.Count == 0)
            {
                MessageBox.Show(this, "You need to add a door before generating a file.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Title = "Generate door audio file",
                Filter = "Dat151 REL|*.dat151.rel|REL files|*.rel|All files|*.*",
                FileName = "door_audio_game.dat151.rel",
                DefaultExt = "rel",
                AddExtension = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var relPath = dlg.FileName;
                if (!relPath.EndsWith(".rel", StringComparison.OrdinalIgnoreCase))
                    relPath += ".rel";
                _document.Export(relPath);

                var xmlPath = relPath + ".xml";
                var exportXml = MessageBox.Show(this,
                    "Also export CodeWalker XML next to the .rel?\n(Useful for inspection; binary .rel is ready for the game.)",
                    "Door Audio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (exportXml)
                    _document.ExportXml(xmlPath);

                var ntPath = Path.ChangeExtension(relPath, null);
                if (ntPath.EndsWith(".rel", StringComparison.OrdinalIgnoreCase))
                    ntPath = ntPath[..^4];
                ntPath += ".nametable";

                _setStatus($"Generated {relPath}");
                MessageBox.Show(this,
                    $"Generated:\n{relPath}\n{ntPath}" + (exportXml ? $"\n{xmlPath}" : string.Empty),
                    "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Generate failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportXmlOnly()
        {
            if (_document.Doors.Count == 0)
            {
                MessageBox.Show(this, "You need to add a door before exporting.", "Door Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Title = "Export CodeWalker Dat151 XML",
                Filter = "XML|*.xml|All files|*.*",
                FileName = "door_audio_game.dat151.rel.xml"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _document.ExportXml(dlg.FileName);
                _setStatus($"Exported XML {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class GameDoorListItem
        {
            public DoorAudioGameEntry Entry { get; }
            private readonly string _label;
            public GameDoorListItem(DoorAudioGameEntry entry, string label)
            {
                Entry = entry;
                _label = label;
            }
            public override string ToString() => _label;
        }
    }
}
