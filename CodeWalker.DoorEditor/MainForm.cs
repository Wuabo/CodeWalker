using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Preview;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;
using CodeWalker.Properties;

namespace CodeWalker.DoorEditor
{
    public sealed class MainForm : Form
    {
        private static readonly string[] KnownFlags =
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

        private static readonly string[] RotDirs =
        [
            "StdDoorOpenBothDir",
            "StdDoorOpenNegDir",
            "StdDoorOpenPosDir",
        ];

        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ThemedListBox _tuningList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly ThemedListBox _mappingList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly ThemedScrollPanel _editorHost = new() { Dock = DockStyle.Fill, Padding = new Padding(10) };
        private readonly DoorPreviewPanel _preview = new() { Dock = DockStyle.Fill };
        private ThemedComboBox? _motionCombo;

        private readonly ThemedNumeric _offX = Num(), _offY = Num(), _offZ = Num();
        private readonly ThemedNumeric _radius = Num(1), _rate = Num(1), _cosine = Num();
        private readonly ThemedNumeric _tMinX = Num(), _tMinY = Num(), _tMinZ = Num();
        private readonly ThemedNumeric _tMaxX = Num(), _tMaxY = Num(), _tMaxZ = Num();
        private readonly ThemedNumeric _breakImpulse = Num(), _mass = Num(1), _weapon = Num(1);
        private readonly ThemedNumeric _rotLimit = Num(), _torque = Num();
        private readonly ThemedCheckBox _closeTaper = new() { Text = "AutoOpenCloseRateTaper" };
        private readonly ThemedCheckBox _useTrigger = new() { Text = "UseAutoOpenTriggerBox" };
        private readonly ThemedCheckBox _customTrigger = new() { Text = "CustomTriggerBox" };
        private readonly ThemedCheckBox _breakable = new() { Text = "BreakableByVehicle" };
        private readonly ThemedCheckBox _latch = new() { Text = "ShouldLatchShut" };
        private readonly ThemedComboBox _rotDir = new() { Width = 240 };
        private readonly List<ThemedCheckBox> _flagChecks = new();

        private RpfManager? _rpfMan;
        private Dictionary<string, RpfFileEntry>? _ydrByName;
        private Dictionary<string, string>? _doorAttrByName;
        private DoorTuningDocument _document = new();
        private string? _sourcePath;
        private bool _loadingUi;
        private bool _suppressMappingLoad;

        public MainForm()
        {
            Text = "CodeWalker Door Tuning";
            Width = 1480;
            Height = 860;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 680);
            Font = AppTheme.UiFont;
            BackColor = AppTheme.Window;
            ForeColor = AppTheme.Foreground;
            try
            {
                var ico = Path.Combine(AppContext.BaseDirectory, "CW.ico");
                Icon = File.Exists(ico)
                    ? new Icon(ico)
                    : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { /* keep default */ }

            foreach (var f in KnownFlags)
                _flagChecks.Add(new ThemedCheckBox { Text = f });
            _rotDir.Items.AddRange(RotDirs);

            BuildUi();
            WireEvents();
            Shown += async (_, _) => await InitGameAsync();
        }

        private async Task InitGameAsync()
        {
            try
            {
                SetStatus("Initialising GTA folder…");
                if (!GTAFolder.UpdateGTAFolder(true))
                {
                    SetStatus("No GTA folder selected — Open a doortuning file manually.");
                    return;
                }

                await Task.Run(() =>
                {
                    GTA5Keys.LoadFromPath(GTAFolder.CurrentGTAFolder, GTAFolder.IsGen9, Settings.Default.Key);
                    var rpf = new RpfManager();
                    rpf.Init(GTAFolder.CurrentGTAFolder, GTAFolder.IsGen9,
                        s => BeginInvoke(() => SetStatus(s)),
                        e => BeginInvoke(() => SetStatus("RPF: " + e)));
                    _rpfMan = rpf;
                    _ydrByName = BuildYdrNameIndex(rpf);
                });
                SetStatus($"Ready — {GTAFolder.CurrentGTAFolder} ({_ydrByName?.Count ?? 0} YDRs indexed)");

                // Door specialAttribute comes from YTYP archetypes — index in background.
                var rpfMan = _rpfMan;
                if (rpfMan != null)
                {
                    _ = Task.Run(() =>
                    {
                        BeginInvoke(() => SetStatus("Indexing door specialAttributes from YTYP…"));
                        var idx = BuildDoorSpecialAttributeIndex(rpfMan, n =>
                        {
                            if (n % 200 == 0)
                                BeginInvoke(() => SetStatus($"Indexing door specialAttributes… {n} YTYPs"));
                        });
                        BeginInvoke(() =>
                        {
                            _doorAttrByName = idx;
                            SetStatus($"Ready — {GTAFolder.CurrentGTAFolder} ({_ydrByName?.Count ?? 0} YDRs, {idx.Count} door attrs)");
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                SetStatus("Init failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Init", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildUi()
        {
            var toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = AppTheme.PanelElevated,
                ForeColor = AppTheme.Bright,
                Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
                Font = AppTheme.UiFont,
                Padding = new Padding(6, 4, 6, 4),
                ImageScalingSize = new Size(16, 16)
            };
            toolbar.Items.Add(ToolBtn("Load from game", async (_, _) => await LoadFromGameAsync()));
            toolbar.Items.Add(ToolBtn("Open…", (_, _) => OpenFile()));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(ToolBtn("Export YMT", (_, _) => ExportYmt()));
            toolbar.Items.Add(ToolBtn("Export FiveM…", (_, _) => ExportFiveM()));
            toolbar.Items.Add(ToolBtn("Export XML", (_, _) => ExportXml()));

            var status = new StatusStrip
            {
                BackColor = AppTheme.Panel,
                ForeColor = AppTheme.Faint,
                SizingGrip = false,
                Renderer = new ToolStripProfessionalRenderer(new DarkColorTable())
            };
            _status.ForeColor = AppTheme.Muted;
            status.Items.Add(_status);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(8),
                BackColor = AppTheme.Window
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var left = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = AppTheme.Window
            };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            left.Controls.Add(BuildTuningPane(), 0, 0);
            left.Controls.Add(BuildMappingPane(), 0, 1);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(_editorHost, 1, 0);
            root.Controls.Add(BuildPreviewPane(), 2, 0);

            BuildEditorFields();

            Controls.Add(root);
            Controls.Add(toolbar);
            Controls.Add(status);
        }

        private static ToolStripButton ToolBtn(string text, EventHandler onClick)
        {
            var b = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = AppTheme.Bright,
                Font = AppTheme.UiFont,
                Margin = new Padding(2, 1, 2, 1),
                Padding = new Padding(8, 4, 8, 4)
            };
            b.Click += onClick;
            return b;
        }

        private Control BuildPreviewPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 0), BackColor = AppTheme.Window };
            var header = new Label
            {
                Text = "Preview",
                Dock = DockStyle.Top,
                Height = 26,
                Font = AppTheme.HeaderFont,
                ForeColor = AppTheme.BlueBright,
                BackColor = AppTheme.Window,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0)
            };

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 2),
                BackColor = AppTheme.Window
            };

            void AddView(string label, PreviewViewMode mode)
            {
                var b = new ThemedButton { Text = label, AutoSize = true, Margin = new Padding(0, 0, 4, 0), Height = 28 };
                b.Click += (_, _) => _preview.ViewMode = mode;
                toolbar.Controls.Add(b);
            }
            AddView("Orbit", PreviewViewMode.Iso);
            AddView("Top", PreviewViewMode.TopXY);
            AddView("Side", PreviewViewMode.SideXZ);
            AddView("Front", PreviewViewMode.FrontYZ);

            toolbar.Controls.Add(new Label
            {
                Text = "Motion",
                AutoSize = true,
                ForeColor = AppTheme.Muted,
                Padding = new Padding(10, 7, 4, 0),
                BackColor = AppTheme.Window
            });

            var motion = new ThemedComboBox
            {
                Width = 170,
                Margin = new Padding(0, 2, 4, 0)
            };
            motion.Items.AddRange([
                "7 — Normal Door",
                "5 — Garage Door",
                "8 — Sliding Door",
                "10 — Sliding Vertical",
                "9 — Barrier",
                "12 — Rail Crossing"
            ]);
            motion.SelectedIndex = 0;
            motion.SelectedIndexChanged += (_, _) =>
            {
                if (_loadingUi || motion.SelectedItem is not string s) return;
                var id = s.Split('—', '-')[0].Trim();
                _preview.SetSpecialAttribute(id);
            };
            _motionCombo = motion;
            toolbar.Controls.Add(motion);

            var loadYdr = new ThemedButton
            {
                Text = "Load YDR…",
                Primary = true,
                AutoSize = true,
                Margin = new Padding(8, 0, 4, 0),
                Height = 28
            };
            loadYdr.Click += (_, _) => PromptLoadYdr();
            toolbar.Controls.Add(loadYdr);

            var unloadYdr = new ThemedButton
            {
                Text = "Unload",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0),
                Height = 28
            };
            unloadYdr.Click += (_, _) => UnloadPreviewMesh();
            toolbar.Controls.Add(unloadYdr);

            var showPed = new ThemedCheckBox
            {
                Text = "Ped 1.8m",
                Checked = true,
                Margin = new Padding(12, 6, 0, 0)
            };
            showPed.CheckedChanged += (_, _) => _preview.ShowPed = showPed.Checked;
            toolbar.Controls.Add(showPed);

            var hint = new Label
            {
                Text = "Offset cyan · Trigger orange · Ped green · drop .ydr · scroll zoom · drag orbit",
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = AppTheme.Faint,
                BackColor = AppTheme.Window
            };

            var frame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = AppTheme.Border
            };
            frame.Controls.Add(_preview);

            pane.Controls.Add(frame);
            pane.Controls.Add(hint);
            pane.Controls.Add(toolbar);
            pane.Controls.Add(header);
            return pane;
        }

        private void PromptLoadYdr()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "YDR drawable|*.ydr|All files|*.*",
                Title = "Load door YDR for preview"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (_preview.TryLoadYdr(dlg.FileName))
            {
                SyncMotionCombo(_preview.SpecialAttribute);
                SetStatus("Preview mesh: " + Path.GetFileName(dlg.FileName));
            }
            else
            {
                MessageBox.Show(this, "Could not load that YDR.", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UnloadPreviewMesh()
        {
            _suppressMappingLoad = true;
            _mappingList.SelectedIndex = -1;
            _suppressMappingLoad = false;
            _preview.ClearMesh();
            SetStatus("Preview mesh unloaded");
        }

        private void SyncMotionCombo(string attr)
        {
            if (_motionCombo == null) return;
            attr = (attr ?? "7").Trim();
            for (int i = 0; i < _motionCombo.Items.Count; i++)
            {
                var s = _motionCombo.Items[i]?.ToString() ?? "";
                var head = s.Split(' ', '—', '-', '–')[0].Trim();
                if (head.Equals(attr, StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith(attr + " ", StringComparison.Ordinal) ||
                    s.StartsWith(attr + "—", StringComparison.Ordinal) ||
                    s.StartsWith(attr + " —", StringComparison.Ordinal))
                {
                    _loadingUi = true;
                    _motionCombo.SelectedIndex = i;
                    _loadingUi = false;
                    return;
                }
            }
        }

        /// <summary>YTYP specialAttribute if indexed, otherwise filename heuristic.</summary>
        private string ResolveSpecialAttribute(string? modelName)
        {
            var key = (modelName ?? "").Trim();
            if (key.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                key = key[..^4];
            if (key.Length == 0) return "7";

            if (_doorAttrByName != null)
            {
                if (_doorAttrByName.TryGetValue(key, out var attr)) return attr;
                if (_doorAttrByName.TryGetValue(key + ".ydr", out attr)) return attr;
            }
            return DoorPreviewPanel.GuessSpecialAttribute(key);
        }

        private void ApplyMotionForModel(string? modelName)
        {
            var attr = ResolveSpecialAttribute(modelName);
            _preview.SetSpecialAttribute(attr);
            SyncMotionCombo(attr);
        }

        private void UpdatePreviewFromSelection()
        {
            var nt = SelectedTuning();
            if (nt == null)
            {
                _preview.SetTuning(null);
                return;
            }
            var t = nt.Tuning ?? new DoorTuningParams();
            _preview.SetTuning(t);
            var map = _document.ModelMappings.FirstOrDefault(m =>
                string.Equals(m.TuningName, nt.Name, StringComparison.OrdinalIgnoreCase));
            ApplyMotionForModel(map?.ModelName ?? nt.Name);
        }

        private Control BuildTuningPane()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Panel, Padding = new Padding(1), Margin = new Padding(0, 0, 8, 8) };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.PanelElevated };
            var header = new Label
            {
                Text = "Named tunings",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = AppTheme.HeaderFont,
                ForeColor = AppTheme.BlueBright,
                BackColor = AppTheme.PanelElevated
            };
            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(4),
                BackColor = AppTheme.PanelElevated
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            void AddBtn(string text, Action onClick, bool primary, int col)
            {
                var b = MakeButton(text, onClick, primary);
                b.Dock = DockStyle.Fill;
                b.AutoSize = false;
                b.Margin = new Padding(2);
                buttons.Controls.Add(b, col, 0);
            }
            AddBtn("Add", AddTuning, true, 0);
            AddBtn("Rename", RenameTuning, false, 1);
            AddBtn("Delete", DeleteTuning, false, 2);
            _tuningList.Dock = DockStyle.Fill;
            inner.Controls.Add(_tuningList);
            inner.Controls.Add(buttons);
            inner.Controls.Add(header);
            panel.Controls.Add(inner);
            return panel;
        }

        private Control BuildMappingPane()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Panel, Padding = new Padding(1), Margin = new Padding(0, 8, 8, 0) };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.PanelElevated };
            var header = new Label
            {
                Text = "Model → tuning maps",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = AppTheme.HeaderFont,
                ForeColor = AppTheme.BlueBright,
                BackColor = AppTheme.PanelElevated
            };
            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(4),
                BackColor = AppTheme.PanelElevated
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            void AddBtn(string text, Action onClick, bool primary, int col)
            {
                var b = MakeButton(text, onClick, primary);
                b.Dock = DockStyle.Fill;
                b.AutoSize = false;
                b.Margin = new Padding(2);
                buttons.Controls.Add(b, col, 0);
            }
            AddBtn("Add", AddMapping, true, 0);
            AddBtn("Edit", EditMapping, false, 1);
            AddBtn("Delete", DeleteMapping, false, 2);
            _mappingList.Dock = DockStyle.Fill;
            inner.Controls.Add(_mappingList);
            inner.Controls.Add(buttons);
            inner.Controls.Add(header);
            panel.Controls.Add(inner);
            return panel;
        }

        private void BuildEditorFields()
        {
            // FlowLayout stacks AutoSize sections reliably (TableLayout + Dock.Fill was collapsing height to 0).
            var root = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.None,
                Padding = new Padding(2),
                BackColor = AppTheme.Background
            };

            void Add(Control section)
            {
                section.Dock = DockStyle.None;
                section.Width = 420;
                root.Controls.Add(section);
            }

            Add(Section("Auto-open volume offset", Row3("X", _offX, "Y", _offY, "Z", _offZ)));
            Add(Section("Auto-open", Col(
                Row("Radius modifier", _radius),
                Row("Open rate", _rate),
                Row("Cosine angle threshold", _cosine),
                _closeTaper, _useTrigger, _customTrigger)));
            Add(Section("Trigger box min (− side)", Row3("X", _tMinX, "Y", _tMinY, "Z", _tMinZ)));
            Add(Section("Trigger box max (+ side)", Row3("X", _tMaxX, "Y", _tMaxY, "Z", _tMaxZ)));
            Add(Section("Physics", Col(
                _breakable,
                Row("Breaking impulse", _breakImpulse),
                _latch,
                Row("Mass multiplier", _mass),
                Row("Weapon impulse multiplier", _weapon),
                Row("Rotation limit angle", _rotLimit),
                Row("Torque angular velocity limit", _torque))));
            Add(Section("Rotation direction", Row("StdDoorRotDir", _rotDir)));

            var flagsPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = AppTheme.PanelElevated
            };
            foreach (var c in _flagChecks)
                flagsPanel.Controls.Add(c);
            Add(Section("Flags", flagsPanel));

            _editorHost.Controls.Add(root);
        }

        private void WireEvents()
        {
            _tuningList.SelectedIndexChanged += (_, _) => PullTuningToUi();
            _mappingList.SelectedIndexChanged += (_, _) => OnMappingSelected();
            void MarkDirty(object? s, EventArgs e)
            {
                if (!_loadingUi) PushUiToTuning();
            }
            foreach (Control c in new Control[]
                     {
                         _offX, _offY, _offZ, _radius, _rate, _cosine,
                         _tMinX, _tMinY, _tMinZ, _tMaxX, _tMaxY, _tMaxZ,
                         _breakImpulse, _mass, _weapon, _rotLimit, _torque,
                         _closeTaper, _useTrigger, _customTrigger, _breakable, _latch, _rotDir
                     })
            {
                if (c is ThemedNumeric n) n.ValueChanged += MarkDirty;
                else if (c is ThemedCheckBox cb) cb.CheckedChanged += MarkDirty;
                else if (c is ThemedComboBox combo) combo.SelectedIndexChanged += MarkDirty;
            }
            foreach (var f in _flagChecks)
                f.CheckedChanged += MarkDirty;
        }

        private void OnMappingSelected()
        {
            if (_suppressMappingLoad) return;
            if (_mappingList.SelectedIndex < 0 || _mappingList.SelectedIndex >= _document.ModelMappings.Count)
                return;

            var map = _document.ModelMappings[_mappingList.SelectedIndex];

            // Always update Motion from YTYP / name before (and after) mesh load
            ApplyMotionForModel(map.ModelName);

            // Select the matching named tuning so options update
            if (!string.IsNullOrWhiteSpace(map.TuningName))
            {
                for (int i = 0; i < _tuningList.Items.Count; i++)
                {
                    if (string.Equals(_tuningList.Items[i]?.ToString(), map.TuningName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_tuningList.SelectedIndex != i)
                            _tuningList.SelectedIndex = i;
                        break;
                    }
                }
            }

            LoadPreviewFromModelName(map.ModelName);
        }

        private void LoadPreviewFromModelName(string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                SetStatus("No model name on mapping");
                return;
            }

            var attr = ResolveSpecialAttribute(modelName);

            if (_rpfMan == null || _ydrByName == null)
            {
                ApplyMotionForModel(modelName);
                SetStatus("Game files not ready — use Load YDR… or wait for init");
                return;
            }

            var key = modelName.Trim();
            if (key.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                key = key[..^4];
            var fileKey = key + ".ydr";

            if (!_ydrByName.TryGetValue(fileKey, out var entry) &&
                !_ydrByName.TryGetValue(key, out entry))
            {
                ApplyMotionForModel(modelName);
                SetStatus($"YDR not found in game files: {fileKey} (Motion={attr}; use Load YDR… for custom props)");
                return;
            }

            try
            {
                UseWaitCursor = true;
                var ydr = _rpfMan.GetFile<YdrFile>(entry);
                if (ydr == null)
                {
                    ApplyMotionForModel(modelName);
                    SetStatus("Failed to extract " + entry.Name);
                    return;
                }
                ydr.Name = entry.Name;
                if (_preview.TryLoadMesh(YdrMeshExtractor.FromYdr(ydr), attr))
                {
                    SyncMotionCombo(attr);
                    var src = _doorAttrByName != null &&
                              (_doorAttrByName.ContainsKey(key) || _doorAttrByName.ContainsKey(fileKey))
                        ? "YTYP"
                        : "name guess";
                    SetStatus($"Loaded {entry.Name} · Motion {attr} ({src})");
                }
            }
            catch (Exception ex)
            {
                SetStatus("YDR load failed: " + ex.Message);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private static Dictionary<string, RpfFileEntry> BuildYdrNameIndex(RpfManager rpf)
        {
            var dict = new Dictionary<string, RpfFileEntry>(StringComparer.OrdinalIgnoreCase);
            void Consider(RpfEntry e)
            {
                if (e is not RpfFileEntry fe) return;
                var name = fe.NameLower;
                if (string.IsNullOrEmpty(name) || !name.EndsWith(".ydr", StringComparison.Ordinal))
                    return;
                // Mods / later entries override base game
                dict[name] = fe;
                var shortName = Path.GetFileNameWithoutExtension(name);
                if (!string.IsNullOrEmpty(shortName))
                    dict[shortName] = fe;
            }

            foreach (var e in rpf.EntryDict.Values)
                Consider(e);
            if (rpf.EnableMods)
            {
                foreach (var e in rpf.ModEntryDict.Values)
                    Consider(e);
            }
            return dict;
        }

        private static Dictionary<string, string> BuildDoorSpecialAttributeIndex(RpfManager rpf, Action<int>? progress = null)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;

            void Consider(RpfEntry e)
            {
                if (e is not RpfFileEntry fe) return;
                var path = fe.Path ?? fe.NameLower;
                if (string.IsNullOrEmpty(fe.NameLower) || !fe.NameLower.EndsWith(".ytyp", StringComparison.Ordinal))
                    return;
                if (!seen.Add(path)) return;
                n++;
                progress?.Invoke(n);

                try
                {
                    var ytyp = rpf.GetFile<YtypFile>(fe);
                    if (ytyp?.AllArchetypes == null) return;
                    foreach (var arch in ytyp.AllArchetypes)
                    {
                        uint sa = arch._BaseArchetypeDef.specialAttribute;
                        if (sa is not (5 or 7 or 8 or 9 or 10 or 12)) continue;
                        var attr = sa.ToString();

                        void AddKey(string? raw)
                        {
                            if (string.IsNullOrWhiteSpace(raw)) return;
                            var k = raw.Trim();
                            if (k.StartsWith("hash_", StringComparison.OrdinalIgnoreCase)) return;
                            dict[k] = attr;
                            if (k.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                                dict[k[..^4]] = attr;
                            else
                                dict[k + ".ydr"] = attr;
                        }

                        AddKey(arch.Name);
                        AddKey(arch.AssetName);
                    }
                }
                catch
                {
                    // skip unreadable ytyp
                }
            }

            foreach (var e in rpf.EntryDict.Values)
                Consider(e);
            if (rpf.EnableMods)
            {
                foreach (var e in rpf.ModEntryDict.Values)
                    Consider(e);
            }
            return dict;
        }

        private async Task LoadFromGameAsync()
        {
            if (_rpfMan == null)
            {
                MessageBox.Show(this, "Game files are not ready yet.", "Door Tuning", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                UseWaitCursor = true;
                SetStatus("Loading doortuning.ymt…");
                await Task.Run(() =>
                {
                    _document = DoorTuningDocument.LoadFromGame(_rpfMan)
                        ?? throw new FileNotFoundException("Could not find " + DoorTuningDocument.GameRelativePath);
                    _sourcePath = DoorTuningDocument.GameRelativePath;
                });
                RefreshLists();
                SetStatus($"Loaded {_document.NamedTunings.Count} tunings, {_document.ModelMappings.Count} mappings from game");
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

        private void OpenFile()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Open doortuning",
                Filter = "Door tuning|*.ymt;*.xml;*.ymt.xml;*.ymt.pso.xml|YMT|*.ymt|XML|*.xml;*.pso.xml|All|*.*"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _document = DoorTuningFileLoader.LoadFromPath(dlg.FileName);
                _sourcePath = dlg.FileName;
                RefreshLists();
                SetStatus($"Opened {_document.NamedTunings.Count} tunings / {_document.ModelMappings.Count} maps — {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportYmt()
        {
            PushUiToTuning();
            if (!EnsureExportable()) return;
            if (!WarnIfBadTriggerBoxes()) return;
            using var dlg = new SaveFileDialog
            {
                Title = "Export game-safe doortuning.ymt (binary PSO only)",
                Filter = "YMT|*.ymt|All|*.*",
                FileName = "doortuning.ymt"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var bytes = _document.SaveBinary();
                File.WriteAllBytes(dlg.FileName, bytes);
                SetStatus($"Exported PSO {bytes.Length} bytes → {dlg.FileName}");
                MessageBox.Show(this,
                    "Exported binary PSO doortuning.ymt only (no XML).\n\n" +
                    "For FiveM use Export FiveM — binary PSO does not work with replace_level_meta.",
                    "Export YMT", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export YMT", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// FiveM replace_level_meta: Meta XML doortuning.ymt + fxmanifest.lua + gta5.meta (no companion .xml).
        /// </summary>
        private void ExportFiveM()
        {
            PushUiToTuning();
            if (!EnsureExportable()) return;
            if (!WarnIfBadTriggerBoxes()) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = "Choose FiveM resource folder (writes doortuning.ymt + fxmanifest.lua + gta5.meta)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                FiveMResourceExport.WriteResourceFolder(dlg.SelectedPath, _document);
                var resName = Path.GetFileName(dlg.SelectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                SetStatus("Exported FiveM resource → " + dlg.SelectedPath);
                MessageBox.Show(this,
                    "Exported FiveM resource (no companion XML):\n" +
                    $"• doortuning.ymt (Meta XML)\n• fxmanifest.lua\n• gta5.meta\n\n" +
                    $"Folder: {dlg.SelectedPath}\nResource name used in gta5.meta: {resName}\n\n" +
                    "ensure this resource is started; .ytyp door needs specialAttribute (5=garage…).",
                    "Export FiveM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export FiveM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportXml()
        {
            PushUiToTuning();
            if (!EnsureExportable()) return;
            using var dlg = new SaveFileDialog
            {
                Title = "Export CodeWalker PSO XML only",
                Filter = "PSO XML|*.ymt.pso.xml;*.xml|All|*.*",
                FileName = "doortuning.ymt.pso.xml"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dlg.FileName, DoorTuningPsoExport.ToCodeWalkerPsoXml(_document));
                SetStatus("Exported XML → " + dlg.FileName);
                MessageBox.Show(this, "Exported XML only:\n" + dlg.FileName, "Export XML",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export XML", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool EnsureExportable()
        {
            if (_document.NamedTunings.Count == 0 && _document.ModelMappings.Count == 0)
            {
                MessageBox.Show(this, "Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        private bool WarnIfBadTriggerBoxes()
        {
            var bad = new List<string>();
            foreach (var nt in _document.NamedTunings)
            {
                var t = nt.Tuning;
                if (t == null || !t.CustomTriggerBox) continue;
                // Negatives on min are normal (box extends both ways). Bad = min > max on same axis.
                bool inverted =
                    t.TriggerBoxMinX > t.TriggerBoxMaxX ||
                    t.TriggerBoxMinY > t.TriggerBoxMaxY ||
                    t.TriggerBoxMinZ > t.TriggerBoxMaxZ;
                if (inverted)
                    bad.Add($"{nt.Name}: min must be <= max per axis (e.g. min.x=-3 max.x=3, not swapped)");
            }
            if (bad.Count == 0) return true;
            var msg =
                "CustomTriggerBox AABB has min > max on an axis (values swapped).\n" +
                "Negative mins are fine; each axis needs min <= max:\n\n• " +
                string.Join("\n• ", bad) +
                "\n\nExport anyway?";
            return MessageBox.Show(this, msg, "Bad trigger boxes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void RefreshLists()
        {
            var selT = _tuningList.SelectedItem as string;
            var selM = _mappingList.SelectedIndex;
            _loadingUi = true;
            _suppressMappingLoad = true;
            _tuningList.BeginUpdate();
            _tuningList.Items.Clear();
            foreach (var t in _document.NamedTunings)
                _tuningList.Items.Add(t.Name);
            _tuningList.EndUpdate();

            _mappingList.BeginUpdate();
            _mappingList.Items.Clear();
            foreach (var m in _document.ModelMappings)
                _mappingList.Items.Add($"{m.ModelName}  →  {m.TuningName}");
            _mappingList.EndUpdate();
            _loadingUi = false;

            if (selT != null)
            {
                var idx = _tuningList.Items.IndexOf(selT);
                if (idx >= 0) _tuningList.SelectedIndex = idx;
            }
            else if (_tuningList.Items.Count > 0)
                _tuningList.SelectedIndex = 0;

            if (selM >= 0 && selM < _mappingList.Items.Count)
                _mappingList.SelectedIndex = selM;
            _suppressMappingLoad = false;

            PullTuningToUi();
        }

        private DoorNamedTuning? SelectedTuning()
        {
            if (_tuningList.SelectedItem is not string name) return null;
            return _document.NamedTunings.FirstOrDefault(t => t.Name == name);
        }

        private void PullTuningToUi()
        {
            var nt = SelectedTuning();
            _loadingUi = true;
            var enabled = nt != null;
            foreach (Control c in _editorHost.Controls)
                SetEnabledRecursive(c, enabled);

            if (nt == null)
            {
                _loadingUi = false;
                _preview.SetTuning(null);
                return;
            }

            var t = nt.Tuning ?? new DoorTuningParams();
            _offX.Value = Clamp(t.AutoOpenVolumeOffsetX);
            _offY.Value = Clamp(t.AutoOpenVolumeOffsetY);
            _offZ.Value = Clamp(t.AutoOpenVolumeOffsetZ);
            _radius.Value = Clamp(t.AutoOpenRadiusModifier);
            _rate.Value = Clamp(t.AutoOpenRate);
            _cosine.Value = Clamp(t.AutoOpenCosineAngleBetweenThreshold);
            _tMinX.Value = Clamp(t.TriggerBoxMinX);
            _tMinY.Value = Clamp(t.TriggerBoxMinY);
            _tMinZ.Value = Clamp(t.TriggerBoxMinZ);
            _tMaxX.Value = Clamp(t.TriggerBoxMaxX);
            _tMaxY.Value = Clamp(t.TriggerBoxMaxY);
            _tMaxZ.Value = Clamp(t.TriggerBoxMaxZ);
            _breakImpulse.Value = Clamp(t.BreakingImpulse);
            _mass.Value = Clamp(t.MassMultiplier);
            _weapon.Value = Clamp(t.WeaponImpulseMultiplier);
            _rotLimit.Value = Clamp(t.RotationLimitAngle);
            _torque.Value = Clamp(t.TorqueAngularVelocityLimit);
            _closeTaper.Checked = t.AutoOpenCloseRateTaper;
            _useTrigger.Checked = t.UseAutoOpenTriggerBox;
            _customTrigger.Checked = t.CustomTriggerBox;
            _breakable.Checked = t.BreakableByVehicle;
            _latch.Checked = t.ShouldLatchShut;
            var dir = string.IsNullOrWhiteSpace(t.StdDoorRotDir) ? "StdDoorOpenBothDir" : t.StdDoorRotDir;
            _rotDir.SelectedItem = RotDirs.Contains(dir) ? dir : "StdDoorOpenBothDir";
            var flags = t.Flags ?? [];
            foreach (var c in _flagChecks)
                c.Checked = flags.Contains(c.Text);
            _loadingUi = false;
            UpdatePreviewFromSelection();
        }

        private void PushUiToTuning()
        {
            var nt = SelectedTuning();
            if (nt == null) return;
            var t = nt.Tuning ??= new DoorTuningParams();
            t.AutoOpenVolumeOffsetX = (float)_offX.Value;
            t.AutoOpenVolumeOffsetY = (float)_offY.Value;
            t.AutoOpenVolumeOffsetZ = (float)_offZ.Value;
            t.AutoOpenRadiusModifier = (float)_radius.Value;
            t.AutoOpenRate = (float)_rate.Value;
            t.AutoOpenCosineAngleBetweenThreshold = (float)_cosine.Value;
            t.TriggerBoxMinX = (float)_tMinX.Value;
            t.TriggerBoxMinY = (float)_tMinY.Value;
            t.TriggerBoxMinZ = (float)_tMinZ.Value;
            t.TriggerBoxMaxX = (float)_tMaxX.Value;
            t.TriggerBoxMaxY = (float)_tMaxY.Value;
            t.TriggerBoxMaxZ = (float)_tMaxZ.Value;
            t.BreakingImpulse = (float)_breakImpulse.Value;
            t.MassMultiplier = (float)_mass.Value;
            t.WeaponImpulseMultiplier = (float)_weapon.Value;
            t.RotationLimitAngle = (float)_rotLimit.Value;
            t.TorqueAngularVelocityLimit = (float)_torque.Value;
            t.AutoOpenCloseRateTaper = _closeTaper.Checked;
            t.UseAutoOpenTriggerBox = _useTrigger.Checked;
            t.CustomTriggerBox = _customTrigger.Checked;
            t.BreakableByVehicle = _breakable.Checked;
            t.ShouldLatchShut = _latch.Checked;
            t.StdDoorRotDir = _rotDir.SelectedItem as string ?? "StdDoorOpenBothDir";
            t.Flags = _flagChecks.Where(c => c.Checked).Select(c => c.Text).ToList();
            _preview.SetTuning(t);
        }

        private void AddTuning()
        {
            using var dlg = new NameInputDialog("Add tuning", "Named tuning name:");
            if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;
            if (_document.NamedTunings.Any(t => string.Equals(t.Name, dlg.Value, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "A tuning with that name already exists.", "Add", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _document.NamedTunings.Add(new DoorNamedTuning { Name = dlg.Value, Tuning = new DoorTuningParams() });
            RefreshLists();
            _tuningList.SelectedItem = dlg.Value;
        }

        private void RenameTuning()
        {
            var nt = SelectedTuning();
            if (nt == null) return;
            using var dlg = new NameInputDialog("Rename tuning", "New name:", nt.Name);
            if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;
            if (_document.NamedTunings.Any(t =>
                    !ReferenceEquals(t, nt) &&
                    string.Equals(t.Name, dlg.Value, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "A tuning with that name already exists.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var old = nt.Name;
            nt.Name = dlg.Value;
            foreach (var m in _document.ModelMappings)
            {
                if (string.Equals(m.TuningName, old, StringComparison.OrdinalIgnoreCase))
                    m.TuningName = dlg.Value;
            }
            RefreshLists();
            _tuningList.SelectedItem = dlg.Value;
        }

        private void DeleteTuning()
        {
            var nt = SelectedTuning();
            if (nt == null) return;
            if (MessageBox.Show(this, $"Delete tuning \"{nt.Name}\"?", "Delete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _document.NamedTunings.Remove(nt);
            RefreshLists();
        }

        private void AddMapping()
        {
            if (_document.NamedTunings.Count == 0)
            {
                MessageBox.Show(this, "Add a named tuning first.", "Add map", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var modelDlg = new NameInputDialog("Add mapping", "Model name (prop / hash name):");
            if (modelDlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(modelDlg.Value)) return;
            using var tuneDlg = new NameInputDialog("Add mapping", "Tuning name:", _document.NamedTunings[0].Name);
            if (tuneDlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(tuneDlg.Value)) return;
            if (_document.ModelMappings.Any(m => string.Equals(m.ModelName, modelDlg.Value, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "That model is already mapped.", "Add map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _document.ModelMappings.Add(new DoorModelMapping { ModelName = modelDlg.Value, TuningName = tuneDlg.Value });
            RefreshLists();
        }

        private void EditMapping()
        {
            if (_mappingList.SelectedIndex < 0 || _mappingList.SelectedIndex >= _document.ModelMappings.Count)
                return;
            var map = _document.ModelMappings[_mappingList.SelectedIndex];
            using var modelDlg = new NameInputDialog("Edit mapping", "Model name:", map.ModelName);
            if (modelDlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(modelDlg.Value)) return;
            using var tuneDlg = new NameInputDialog("Edit mapping", "Tuning name:", map.TuningName);
            if (tuneDlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(tuneDlg.Value)) return;
            map.ModelName = modelDlg.Value;
            map.TuningName = tuneDlg.Value;
            RefreshLists();
        }

        private void DeleteMapping()
        {
            if (_mappingList.SelectedIndex < 0 || _mappingList.SelectedIndex >= _document.ModelMappings.Count)
                return;
            _document.ModelMappings.RemoveAt(_mappingList.SelectedIndex);
            RefreshLists();
        }

        private void SetStatus(string text) => _status.Text = text;

        private static ThemedButton MakeButton(string text, Action onClick, bool primary = false)
        {
            var b = new ThemedButton
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(2),
                Height = 28,
                Primary = primary,
                MinimumSize = new Size(72, 28)
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        private static ThemedNumeric Num(decimal value = 0)
        {
            return new ThemedNumeric { Value = value };
        }

        private static decimal Clamp(float v)
        {
            var d = (decimal)v;
            if (d < -100000) return -100000;
            if (d > 100000) return 100000;
            return d;
        }

        private static Control Section(string title, Control content)
        {
            var section = new ThemedSection(title);
            section.SetContent(content);
            return section;
        }

        private static Control Row(string label, Control editor)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 3, 0, 3),
                BackColor = AppTheme.PanelElevated
            };
            p.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = AppTheme.Muted,
                Padding = new Padding(0, 6, 8, 0),
                BackColor = AppTheme.PanelElevated,
                MinimumSize = new Size(150, 0)
            });
            p.Controls.Add(editor);
            return p;
        }

        private static Control Row3(string l1, Control c1, string l2, Control c2, string l3, Control c3)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                BackColor = AppTheme.PanelElevated
            };
            void Add(string lab, Control c)
            {
                p.Controls.Add(new Label
                {
                    Text = lab,
                    AutoSize = true,
                    ForeColor = AppTheme.Muted,
                    Padding = new Padding(0, 6, 4, 0),
                    BackColor = AppTheme.PanelElevated
                });
                p.Controls.Add(c);
            }
            Add(l1, c1);
            p.Controls.Add(new Label { Width = 10, BackColor = AppTheme.PanelElevated });
            Add(l2, c2);
            p.Controls.Add(new Label { Width = 10, BackColor = AppTheme.PanelElevated });
            Add(l3, c3);
            return p;
        }

        private static Control Col(params Control[] items)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = AppTheme.PanelElevated
            };
            foreach (var i in items)
            {
                i.Margin = new Padding(0, 2, 0, 2);
                p.Controls.Add(i);
            }
            return p;
        }

        private static void SetEnabledRecursive(Control c, bool enabled)
        {
            c.Enabled = enabled;
            foreach (Control child in c.Controls)
                SetEnabledRecursive(child, enabled);
        }
    }
}
