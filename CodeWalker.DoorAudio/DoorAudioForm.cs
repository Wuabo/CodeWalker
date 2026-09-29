using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using CodeWalker.Properties;

namespace CodeWalker.DoorAudio
{
    public sealed class DoorAudioForm : Form
    {
        private readonly MenuStrip _menu = new();
        private readonly StatusStrip _statusStrip = new();
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly SplitContainer _splitMain = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 280 };
        private readonly SplitContainer _splitRight = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 420 };

        private readonly ListBox _projectList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly ListBox _gameList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly TextBox _gameSearch = new() { Dock = DockStyle.Top, PlaceholderText = "Search game door audios…" };
        private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Categorized };
        private readonly Label _hashInfo = new()
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(8, 4, 8, 4),
            ForeColor = SystemColors.GrayText
        };
        private readonly ComboBox _presetCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

        private RpfManager? _rpfMan;
        private DoorAudioDocument _document = new();
        private List<DoorAudioGameEntry> _gameDoors = new();
        private List<DoorAudioPreset> _presets = new();
        private List<DoorAudioPreset> _curatedPresets = new();
        private bool _loadingUi;

        public DoorAudioForm()
        {
            Text = "CodeWalker Door Audio Maker";
            Width = 1280;
            Height = 800;
            MinimumSize = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen;
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) Icon = icon;
            }
            catch { /* ignore */ }

            BuildMenu();
            BuildLayout();
            Controls.Add(_splitMain);
            Controls.Add(_statusStrip);
            Controls.Add(_menu);
            MainMenuStrip = _menu;
            _statusStrip.Items.Add(_status);

            Shown += async (_, _) => await BootAsync();
        }

        private void BuildMenu()
        {
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Import XML / .rel…", null, (_, _) => ImportFile()) { ShortcutKeys = Keys.Control | Keys.O });
            file.DropDownItems.Add(new ToolStripMenuItem("&Generate audio file…", null, (_, _) => ExportRel()) { ShortcutKeys = Keys.Control | Keys.S });
            file.DropDownItems.Add(new ToolStripMenuItem("Export XML only…", null, (_, _) => ExportXmlOnly()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

            var edit = new ToolStripMenuItem("&Edit");
            edit.DropDownItems.Add(new ToolStripMenuItem("&Add a new door…", null, (_, _) => ShowAddDoorDialog()) { ShortcutKeys = Keys.Control | Keys.N });
            edit.DropDownItems.Add(new ToolStripMenuItem("&Edit sound preset…", null, (_, _) => ShowEditPresetDialog()) { ShortcutKeys = Keys.Control | Keys.E });
            edit.DropDownItems.Add(new ToolStripMenuItem("&Delete door", null, (_, _) => DeleteDoor()) { ShortcutKeys = Keys.Delete });
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(new ToolStripMenuItem("&Clone selected game door", null, (_, _) => CloneGameDoor()) { ShortcutKeys = Keys.Control | Keys.D });
            edit.DropDownItems.Add(new ToolStripMenuItem("Apply game audio to project door", null, (_, _) => ApplyGameToSelected()));

            var game = new ToolStripMenuItem("&Game");
            game.DropDownItems.Add(new ToolStripMenuItem("&Rescan door audios", null, async (_, _) => await RescanGameAsync()) { ShortcutKeys = Keys.F5 });

            _menu.Items.Add(file);
            _menu.Items.Add(edit);
            _menu.Items.Add(game);
        }

        private void BuildLayout()
        {
            var left = new Panel { Dock = DockStyle.Fill };
            var leftHeader = new Label
            {
                Text = "Project doors",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4)
            };
            var addBtn = new Button { Text = "Add new door…", AutoSize = true };
            var editBtn = new Button { Text = "Edit sound…", AutoSize = true };
            var delBtn = new Button { Text = "Delete", AutoSize = true };
            addBtn.Click += (_, _) => ShowAddDoorDialog();
            editBtn.Click += (_, _) => ShowEditPresetDialog();
            delBtn.Click += (_, _) => DeleteDoor();
            leftButtons.Controls.Add(addBtn);
            leftButtons.Controls.Add(editBtn);
            leftButtons.Controls.Add(delBtn);
            left.Controls.Add(_projectList);
            left.Controls.Add(leftButtons);
            left.Controls.Add(leftHeader);

            var center = new Panel { Dock = DockStyle.Fill };
            var centerHeader = new Label
            {
                Text = "Door audio settings (CodeWalker Dat151)",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            var presetBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(4),
                FlowDirection = FlowDirection.LeftToRight
            };
            presetBar.Controls.Add(new Label { Text = "Sound preset:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            presetBar.Controls.Add(_presetCombo);
            var applyPreset = new Button { Text = "Apply", AutoSize = true };
            applyPreset.Click += (_, _) => ApplySelectedPresetToDoor();
            presetBar.Controls.Add(applyPreset);
            center.Controls.Add(_grid);
            center.Controls.Add(_hashInfo);
            center.Controls.Add(presetBar);
            center.Controls.Add(centerHeader);

            var right = new Panel { Dock = DockStyle.Fill };
            var rightHeader = new Label
            {
                Text = "Game door audios",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            var rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(4)
            };
            var cloneBtn = new Button { Text = "Clone to project", AutoSize = true };
            var applyBtn = new Button { Text = "Apply to selected", AutoSize = true };
            cloneBtn.Click += (_, _) => CloneGameDoor();
            applyBtn.Click += (_, _) => ApplyGameToSelected();
            rightButtons.Controls.Add(cloneBtn);
            rightButtons.Controls.Add(applyBtn);
            right.Controls.Add(_gameList);
            right.Controls.Add(_gameSearch);
            right.Controls.Add(rightButtons);
            right.Controls.Add(rightHeader);

            _splitMain.Panel1.Controls.Add(left);
            _splitRight.Panel1.Controls.Add(center);
            _splitRight.Panel2.Controls.Add(right);
            _splitMain.Panel2.Controls.Add(_splitRight);

            _projectList.SelectedIndexChanged += (_, _) => OnProjectSelectionChanged();
            _gameList.DoubleClick += (_, _) => CloneGameDoor();
            _gameSearch.TextChanged += (_, _) => RefreshGameList();
            _grid.PropertyValueChanged += (_, e) =>
            {
                if (e.ChangedItem?.PropertyDescriptor?.Name == nameof(DoorAudioEntryView.Name))
                    RefreshProjectList(keepSelection: true);
                UpdateHashInfo();
            };
        }

        private async Task BootAsync()
        {
            try
            {
                SetStatus("Checking GTA folder...");
                if (!GTAFolder.UpdateGTAFolder(true))
                {
                    Close();
                    return;
                }

                _curatedPresets = LoadCuratedPresets();

                SetStatus("Loading encryption keys...");
                await Task.Run(() =>
                    GTA5Keys.LoadFromPath(GTAFolder.CurrentGTAFolder, GTAFolder.IsGen9, Settings.Default.Key));

                SetStatus("Scanning game archives...");
                await Task.Run(() =>
                {
                    var rpf = new RpfManager { EnableMods = true };
                    rpf.Init(GTAFolder.CurrentGTAFolder, GTAFolder.IsGen9,
                        SetStatus, msg => SetStatus("ERR: " + msg),
                        rootOnly: false, buildIndex: true);
                    _rpfMan = rpf;
                    _gameDoors = DoorAudioDocument.LoadAllFromGame(rpf, SetStatus);
                    _presets = DoorAudioDocument.MergePresets(_curatedPresets, _gameDoors);
                });

                FillPresetCombo();
                RefreshProjectList();
                RefreshGameList();
                SetStatus($"Ready — {_gameDoors.Count} game door audios, {_curatedPresets.Count} sound presets");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            _presetCombo.Items.Clear();
            foreach (var p in _presets)
                _presetCombo.Items.Add(p.Name);
            if (_presetCombo.Items.Count > 0)
                _presetCombo.SelectedIndex = 0;
        }

        private async Task RescanGameAsync()
        {
            if (_rpfMan == null) return;
            try
            {
                UseWaitCursor = true;
                await Task.Run(() =>
                {
                    _gameDoors = DoorAudioDocument.LoadAllFromGame(_rpfMan, SetStatus);
                    _presets = DoorAudioDocument.MergePresets(_curatedPresets, _gameDoors);
                });
                FillPresetCombo();
                RefreshGameList();
                SetStatus($"Rescanned — {_gameDoors.Count} game door audios");
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void RefreshProjectList(bool keepSelection = false)
        {
            var sel = keepSelection ? _projectList.SelectedItem as string : null;
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
            _grid.SelectedObject = door == null ? null : new DoorAudioEntryView(door);
            UpdateHashInfo();
        }

        private void UpdateHashInfo()
        {
            var door = GetSelectedProjectDoor();
            if (door == null)
            {
                _hashInfo.Text = "Add a door to see settings name (d_*) and link name (dasl_*).";
                return;
            }
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
            return _presets.FirstOrDefault(p => p.Name == name);
        }

        private void ShowAddDoorDialog()
        {
            using var dlg = new DoorEditDialog(_presets, "Add a new door", null);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var name = dlg.DoorName;
            if (_document.Doors.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "This door already exists.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            SetStatus($"Added {name} (hash_{DoorAudioDocument.FormatModelHashHex(name)})");
        }

        private void ShowEditPresetDialog()
        {
            var door = GetSelectedProjectDoor();
            if (door == null)
            {
                MessageBox.Show(this, "Select a project door first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dlg = new DoorEditDialog(_presets, "Edit sound of the door", door.Name, allowNameEdit: false);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var preset = dlg.SelectedPreset;
            if (preset == null) return;
            door.Sounds = preset.Sounds;
            door.TuningParams = preset.TuningParams;
            door.MaxOcclusion = preset.MaxOcclusion;
            OnProjectSelectionChanged();
            SetStatus($"Updated sound for {door.Name}");
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
            SetStatus($"Applied preset '{preset.Name}' to {door.Name}");
        }

        private void DeleteDoor()
        {
            var door = GetSelectedProjectDoor();
            if (door == null) return;
            if (MessageBox.Show(this, $"Delete \"{door.Name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _document.Doors.Remove(door);
            RefreshProjectList();
            SetStatus($"Deleted {door.Name}");
        }

        private void CloneGameDoor()
        {
            var src = GetSelectedGameDoor();
            if (src == null)
            {
                MessageBox.Show(this, "Select a game door audio first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    MessageBox.Show(this, "This door already exists.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            SetStatus($"Cloned {src.SettingsName} → {name}");
        }

        private void ApplyGameToSelected()
        {
            var door = GetSelectedProjectDoor();
            var src = GetSelectedGameDoor();
            if (door == null || src == null)
            {
                MessageBox.Show(this, "Select a project door and a game door audio.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            door.Sounds = src.Sounds;
            door.TuningParams = src.TuningParams;
            door.MaxOcclusion = src.MaxOcclusion;
            OnProjectSelectionChanged();
            SetStatus($"Applied {src.SettingsName} settings to {door.Name}");
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
                {
                    _document = DoorAudioDocument.FromXml(File.ReadAllText(path));
                }
                else
                {
                    var rel = new RelFile();
                    rel.Load(File.ReadAllBytes(path), null);
                    _document = DoorAudioDocument.FromRel(rel);
                }
                RefreshProjectList();
                SetStatus($"Imported {_document.Doors.Count} doors from {Path.GetFileName(path)}");
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
                MessageBox.Show(this, "You need to add a door before generating a file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (exportXml)
                    _document.ExportXml(xmlPath);

                var ntPath = Path.ChangeExtension(relPath, null);
                if (ntPath.EndsWith(".rel", StringComparison.OrdinalIgnoreCase))
                    ntPath = ntPath[..^4];
                ntPath += ".nametable";

                SetStatus($"Generated {relPath}");
                MessageBox.Show(this,
                    $"Generated:\n{relPath}\n{ntPath}" + (exportXml ? $"\n{xmlPath}" : string.Empty),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                MessageBox.Show(this, "You need to add a door before exporting.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                SetStatus($"Exported XML {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(() => _status.Text = text); } catch { /* closing */ }
                return;
            }
            _status.Text = text;
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

        /// <summary>PropertyGrid view with live Jenkins hash fields.</summary>
        private sealed class DoorAudioEntryView
        {
            private readonly DoorAudioEntry _entry;
            public DoorAudioEntryView(DoorAudioEntry entry) => _entry = entry;

            [Category("Door")]
            public string Name
            {
                get => _entry.Name;
                set => _entry.Name = value?.Trim() ?? string.Empty;
            }

            [Category("Audio")]
            public string Sounds
            {
                get => _entry.Sounds;
                set => _entry.Sounds = value ?? "null";
            }

            [Category("Audio")]
            public string TuningParams
            {
                get => _entry.TuningParams;
                set => _entry.TuningParams = value ?? "null";
            }

            [Category("Audio")]
            public float MaxOcclusion
            {
                get => _entry.MaxOcclusion;
                set => _entry.MaxOcclusion = value;
            }

            [Category("Link")]
            [DisplayName("Link name (optional)")]
            public string LinkName
            {
                get => _entry.LinkName;
                set => _entry.LinkName = value ?? string.Empty;
            }

            [Category("Hashes (read-only)")]
            [DisplayName("Settings name")]
            public string SettingsName => DoorAudioDocument.NormalizeDoorName(_entry.Name);

            [Category("Hashes (read-only)")]
            [DisplayName("Auto link name")]
            public string AutoLinkName => DoorAudioDocument.GetAutoLinkName(_entry.Name);

            [Category("Hashes (read-only)")]
            [DisplayName("Model hash")]
            public string ModelHash => "hash_" + DoorAudioDocument.FormatModelHashHex(_entry.Name);
        }
    }

    /// <summary>Add / edit door dialog (name + sound preset + live hash), same workflow as classic door audio tools.</summary>
    internal sealed class DoorEditDialog : Form
    {
        private readonly TextBox _name = new() { Width = 320 };
        private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        private readonly Label _hash = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
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
            ClientSize = new Size(360, 180);
            ShowInTaskbar = false;

            var nameLbl = new Label { Text = "Door name", Left = 12, Top = 12, AutoSize = true };
            _name.Left = 12;
            _name.Top = 32;
            _name.Text = initialName ?? string.Empty;
            _name.ReadOnly = !allowNameEdit;
            _name.TextChanged += (_, _) => UpdateHash();

            var presetLbl = new Label { Text = "Door sound", Left = 12, Top = 64, AutoSize = true };
            _preset.Left = 12;
            _preset.Top = 84;
            foreach (var p in presets)
                _preset.Items.Add(p.Name);
            if (_preset.Items.Count > 0) _preset.SelectedIndex = 0;

            _hash.Left = 12;
            _hash.Top = 116;

            var ok = new Button { Text = "Confirm", DialogResult = DialogResult.OK, Left = 176, Top = 140, Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 264, Top = 140, Width = 80 };
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
