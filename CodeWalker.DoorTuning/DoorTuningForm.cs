using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using CodeWalker.Properties;

namespace CodeWalker.DoorTuning
{
    public sealed class DoorTuningForm : Form
    {
        private static readonly string[] FlagOptions =
        [
            "DontCloseWhenTouched",
            "AutoOpensForSPVehicleWithPedsOnly",
            "AutoOpensForSPPlayerPedsOnly",
            "AutoOpensForMPVehicleWithPedsOnly",
            "AutoOpensForMPPlayerPedsOnly",
            "DelayDoorClosingForPlayer",
            "AutoOpensForAllVehicles",
            "IgnoreOpenDoorTaskEdgeLerp",
            "AutoOpensForLawEnforcement",
        ];

        private readonly MenuStrip _menu = new();
        private readonly StatusStrip _statusStrip = new();
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly SplitContainer _splitMain = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 280 };
        private readonly SplitContainer _splitRight = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 420 };

        private readonly ListBox _tuningList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly ListBox _mappingList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Categorized };
        private readonly CheckedListBox _flags = new() { Dock = DockStyle.Bottom, Height = 140, CheckOnClick = true };
        private readonly TextBox _newTuningName = new() { Dock = DockStyle.Top, PlaceholderText = "New named tuning" };
        private readonly TextBox _modelName = new() { Dock = DockStyle.Top, PlaceholderText = "Model name" };
        private readonly TextBox _modelTuning = new() { Dock = DockStyle.Top, PlaceholderText = "Tuning name" };

        private RpfManager? _rpfMan;
        private DoorTuningDocument _document = new();
        private bool _loadingUi;
        private string? _sourcePath;

        public DoorTuningForm()
        {
            Text = "CodeWalker Door Tuning";
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

            foreach (var f in FlagOptions)
                _flags.Items.Add(f);

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
            file.DropDownItems.Add(new ToolStripMenuItem("&Load from game", null, async (_, _) => await LoadFromGameAsync()) { ShortcutKeys = Keys.Control | Keys.L });
            file.DropDownItems.Add(new ToolStripMenuItem("&Export YMT…", null, (_, _) => ExportYmt()) { ShortcutKeys = Keys.Control | Keys.S });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

            var edit = new ToolStripMenuItem("&Edit");
            edit.DropDownItems.Add(new ToolStripMenuItem("Add named tuning", null, (_, _) => AddTuning()));
            edit.DropDownItems.Add(new ToolStripMenuItem("Delete named tuning", null, (_, _) => DeleteTuning()) { ShortcutKeys = Keys.Delete });
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(new ToolStripMenuItem("Add model mapping", null, (_, _) => AddMapping()));
            edit.DropDownItems.Add(new ToolStripMenuItem("Delete model mapping", null, (_, _) => DeleteMapping()));

            _menu.Items.Add(file);
            _menu.Items.Add(edit);
        }

        private void BuildLayout()
        {
            var left = new Panel { Dock = DockStyle.Fill };
            var leftHeader = new Label
            {
                Text = "Named tunings",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                Padding = new Padding(4),
                FlowDirection = FlowDirection.LeftToRight
            };
            var addT = new Button { Text = "Add", AutoSize = true };
            var delT = new Button { Text = "Delete", AutoSize = true };
            addT.Click += (_, _) => AddTuning();
            delT.Click += (_, _) => DeleteTuning();
            leftButtons.Controls.Add(addT);
            leftButtons.Controls.Add(delT);
            left.Controls.Add(_tuningList);
            left.Controls.Add(_newTuningName);
            left.Controls.Add(leftButtons);
            left.Controls.Add(leftHeader);

            var center = new Panel { Dock = DockStyle.Fill };
            var centerHeader = new Label
            {
                Text = "Tuning parameters",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            var flagsHeader = new Label
            {
                Text = "Flags",
                Dock = DockStyle.Bottom,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            // Dock order: Fill first, then Bottom flags, then Bottom flagsHeader, then Top header
            center.Controls.Add(_grid);
            center.Controls.Add(flagsHeader);
            center.Controls.Add(_flags);
            center.Controls.Add(centerHeader);
            // Fix dock: flags should be bottom, header above flags
            _flags.Dock = DockStyle.Bottom;
            flagsHeader.Dock = DockStyle.Bottom;

            var right = new Panel { Dock = DockStyle.Fill };
            var rightHeader = new Label
            {
                Text = "Model → tuning map",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            var rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                Padding = new Padding(4)
            };
            var addM = new Button { Text = "Add", AutoSize = true };
            var delM = new Button { Text = "Delete", AutoSize = true };
            addM.Click += (_, _) => AddMapping();
            delM.Click += (_, _) => DeleteMapping();
            rightButtons.Controls.Add(addM);
            rightButtons.Controls.Add(delM);
            right.Controls.Add(_mappingList);
            right.Controls.Add(_modelTuning);
            right.Controls.Add(_modelName);
            right.Controls.Add(rightButtons);
            right.Controls.Add(rightHeader);

            _splitMain.Panel1.Controls.Add(left);
            _splitRight.Panel1.Controls.Add(center);
            _splitRight.Panel2.Controls.Add(right);
            _splitMain.Panel2.Controls.Add(_splitRight);

            _tuningList.SelectedIndexChanged += (_, _) => OnTuningSelectionChanged();
            _flags.ItemCheck += Flags_ItemCheck;
            _grid.PropertyValueChanged += (_, _) => { /* live edit on object */ };
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
                });

                await LoadFromGameAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadFromGameAsync()
        {
            if (_rpfMan == null) return;
            try
            {
                UseWaitCursor = true;
                SetStatus("Loading doortuning.ymt…");
                await Task.Run(() =>
                {
                    var ymt = DoorTuningDocument.LoadFromGame(_rpfMan)
                        ?? throw new FileNotFoundException("Could not find " + DoorTuningDocument.GameRelativePath);
                    _document = ymt.DoorTuning ?? DoorTuningDocument.FromYmt(ymt);
                    _sourcePath = DoorTuningDocument.GameRelativePath;
                });
                RefreshTuningList();
                RefreshMappingList();
                SetStatus($"Loaded {_document.NamedTunings.Count} tunings, {_document.ModelMappings.Count} mappings from {_sourcePath}");
            }
            catch (Exception ex)
            {
                SetStatus("Load failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Load from game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void RefreshTuningList()
        {
            var sel = _tuningList.SelectedItem as string;
            _loadingUi = true;
            _tuningList.BeginUpdate();
            _tuningList.Items.Clear();
            foreach (var t in _document.NamedTunings)
                _tuningList.Items.Add(t.Name);
            _tuningList.EndUpdate();
            if (sel != null)
            {
                var idx = _tuningList.Items.IndexOf(sel);
                if (idx >= 0) _tuningList.SelectedIndex = idx;
            }
            else if (_tuningList.Items.Count > 0)
                _tuningList.SelectedIndex = 0;
            _loadingUi = false;
            OnTuningSelectionChanged();
        }

        private void RefreshMappingList()
        {
            _mappingList.BeginUpdate();
            _mappingList.Items.Clear();
            foreach (var m in _document.ModelMappings)
                _mappingList.Items.Add($"{m.ModelName} → {m.TuningName}");
            _mappingList.EndUpdate();
        }

        private DoorNamedTuning? GetSelectedTuning()
        {
            if (_tuningList.SelectedItem is not string name) return null;
            return _document.NamedTunings.FirstOrDefault(t => t.Name == name);
        }

        private void OnTuningSelectionChanged()
        {
            if (_loadingUi) return;
            var nt = GetSelectedTuning();
            _grid.SelectedObject = nt?.Tuning;
            _loadingUi = true;
            for (int i = 0; i < _flags.Items.Count; i++)
            {
                var flag = _flags.Items[i]?.ToString() ?? "";
                _flags.SetItemChecked(i, nt != null && nt.Tuning.Flags.Contains(flag));
            }
            _loadingUi = false;
        }

        private void Flags_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_loadingUi) return;
            var nt = GetSelectedTuning();
            if (nt == null) return;
            BeginInvoke(() =>
            {
                nt.Tuning.Flags = _flags.CheckedItems.Cast<object>().Select(o => o.ToString()!).ToList();
            });
        }

        private void AddTuning()
        {
            var name = (_newTuningName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Enter a tuning name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_document.NamedTunings.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "That named tuning already exists.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _document.NamedTunings.Add(new DoorNamedTuning { Name = name, Tuning = new DoorTuningParams() });
            _newTuningName.Clear();
            RefreshTuningList();
            _tuningList.SelectedItem = name;
            SetStatus($"Added tuning {name}");
        }

        private void DeleteTuning()
        {
            var nt = GetSelectedTuning();
            if (nt == null) return;
            if (MessageBox.Show(this, $"Delete \"{nt.Name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _document.NamedTunings.Remove(nt);
            RefreshTuningList();
            SetStatus($"Deleted {nt.Name}");
        }

        private void AddMapping()
        {
            var model = (_modelName.Text ?? string.Empty).Trim();
            var tuning = (_modelTuning.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(tuning))
            {
                MessageBox.Show(this, "Enter model name and tuning name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _document.ModelMappings.Add(new DoorModelMapping { ModelName = model, TuningName = tuning });
            _modelName.Clear();
            RefreshMappingList();
            SetStatus($"Mapped {model} → {tuning}");
        }

        private void DeleteMapping()
        {
            if (_mappingList.SelectedIndex < 0) return;
            _document.ModelMappings.RemoveAt(_mappingList.SelectedIndex);
            RefreshMappingList();
            SetStatus("Removed mapping");
        }

        private void ExportYmt()
        {
            using var folderDlg = new FolderBrowserDialog
            {
                Description = "Export doortuning.ymt",
                UseDescriptionForTitle = true
            };
            if (folderDlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                // sync flags from UI
                var nt = GetSelectedTuning();
                if (nt != null)
                    nt.Tuning.Flags = _flags.CheckedItems.Cast<object>().Select(o => o.ToString()!).ToList();

                var ymtPath = Path.Combine(folderDlg.SelectedPath, "doortuning.ymt");
                var xmlPath = Path.Combine(folderDlg.SelectedPath, "doortuning.ymt.xml");
                File.WriteAllBytes(ymtPath, _document.Save());
                File.WriteAllText(xmlPath, _document.ToXml());
                SetStatus($"Exported {ymtPath}");
                MessageBox.Show(this, $"Exported:\n{ymtPath}\n{xmlPath}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
    }
}
