using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Dialogs;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Workspaces
{
    public sealed class TuningWorkspace : UserControl
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

        private static readonly string[] RotDirs =
        [
            "StdDoorOpenBothDir",
            "StdDoorOpenNegDir",
            "StdDoorOpenPosDir",
        ];

        private readonly RpfManager _rpfMan;
        private readonly Action<string> _setStatus;

        private readonly DarkListBox _tuningList = new() { Dock = DockStyle.Fill };
        private readonly DarkListBox _mappingList = new() { Dock = DockStyle.Fill };
        private readonly ThemedTextBox _newTuningName = new();
        private readonly ThemedTextBox _modelName = new();
        private readonly ThemedTextBox _modelTuning = new();
        private readonly DoorPreviewPanel _preview = new() { Dock = DockStyle.Fill };
        private ThemedComboBox? _motionCombo;
        private readonly DarkScrollPanel _formHost = new() { Dock = DockStyle.Fill };
        private readonly DarkCheck[] _flagChecks;
        private readonly ThemedComboBox _rotDir = new();

        private readonly Vec3Field _offset = new();
        private readonly Vec3Field _boxMin = new("Min");
        private readonly Vec3Field _boxMax = new("Max");
        private readonly FloatField _radiusMod = new("Radius modifier", 120);
        private readonly FloatField _openRate = new("Open rate", 120);
        private readonly FloatField _cosine = new("Cosine threshold", 120);
        private readonly FloatField _breakImpulse = new("Breaking impulse", 120);
        private readonly FloatField _mass = new("Mass multiplier", 120);
        private readonly FloatField _weapon = new("Weapon impulse", 120);
        private readonly FloatField _rotLimit = new("Rotation limit", 120);
        private readonly FloatField _angVel = new("Angular vel. limit", 120);
        private readonly DarkCheck _closeTaper = new("Close rate taper");
        private readonly DarkCheck _useTrigger = new("Use auto-open trigger box");
        private readonly DarkCheck _customTrigger = new("Custom trigger box");
        private readonly DarkCheck _breakable = new("Breakable by vehicle");
        private readonly DarkCheck _latch = new("Should latch shut");

        private DoorTuningDocument _document = new();
        private bool _loadingUi;
        private string? _sourcePath;

        public TuningWorkspace(RpfManager rpfMan, Action<string> setStatus)
        {
            _rpfMan = rpfMan;
            _setStatus = setStatus;
            BackColor = AppTheme.Background;
            Dock = DockStyle.Fill;

            _flagChecks = FlagOptions.Select(f =>
            {
                var c = new DarkCheck(f);
                c.Width = Math.Max(280, TextRenderer.MeasureText(f, AppTheme.UiFont).Width + 36);
                return c;
            }).ToArray();
            foreach (var d in RotDirs) _rotDir.Items.Add(d);

            BuildLayout();
            WireEvents();
        }

        public async Task InitializeAsync() => await LoadFromGameAsync();

        private void BuildLayout()
        {
            var header = BuildHeader();
            var split = SplitLayout.Create(Orientation.Vertical, preferredDistance: 260, panel1Min: 200, panel2Min: 400);
            split.BackColor = AppTheme.Background;
            split.Panel1.BackColor = AppTheme.Panel;
            split.Panel2.BackColor = AppTheme.Background;

            var left = BuildLeftColumn();
            var rightSplit = SplitLayout.Create(Orientation.Vertical, preferredDistance: 540, panel1Min: 300, panel2Min: 260);
            rightSplit.BackColor = AppTheme.Background;
            rightSplit.Panel1.BackColor = AppTheme.Background;
            rightSplit.Panel2.BackColor = AppTheme.Panel;

            BuildFormPanels();
            var previewPane = BuildPreviewPane();

            rightSplit.Panel1.Controls.Add(_formHost);
            rightSplit.Panel2.Controls.Add(previewPane);
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(rightSplit);

            Controls.Add(split);
            Controls.Add(header);
        }

        private WorkspaceHeader BuildHeader()
        {
            var header = new WorkspaceHeader("Door Tuning");
            var loadBtn = header.AddAction("Load from game");
            var openBtn = header.AddAction("Open…");
            var exportBtn = header.AddAction("Export YMT…", primary: true);
            var fivemBtn = header.AddAction("FiveM resource…");
            loadBtn.Click += async (_, _) => await LoadFromGameAsync();
            openBtn.Click += (_, _) => OpenExisting();
            exportBtn.Click += (_, _) => ExportYmt();
            fivemBtn.Click += (_, _) => ExportFiveMResource();
            return header;
        }

        private Panel BuildLeftColumn()
        {
            var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var split = SplitLayout.Create(Orientation.Horizontal, preferredDistance: 380, panel1Min: 140, panel2Min: 140);
            split.BackColor = AppTheme.Background;
            split.Panel1.BackColor = AppTheme.Panel;
            split.Panel2.BackColor = AppTheme.Panel;

            _newTuningName.PlaceholderText = "New named tuning…";
            _newTuningName.Dock = DockStyle.Top;
            _newTuningName.Height = 32;
            _newTuningName.Margin = new Padding(0, 0, 0, 6);

            var nameWrap = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 0, 0, 6) };
            _newTuningName.Dock = DockStyle.Fill;
            nameWrap.Controls.Add(_newTuningName);

            var tunings = new SideListPane("Named tunings", _tuningList, nameWrap);
            tunings.AddFooterButton("Add", primary: true).Click += (_, _) => AddTuning();
            tunings.AddFooterButton("Delete").Click += (_, _) => DeleteTuning();

            var modelInputs = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(0) };
            _modelName.PlaceholderText = "Model name";
            _modelTuning.PlaceholderText = "Tuning name";
            _modelName.Dock = DockStyle.Top;
            _modelTuning.Dock = DockStyle.Top;
            _modelName.Height = 32;
            _modelTuning.Height = 32;
            var gap = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent };
            modelInputs.Controls.Add(_modelTuning);
            modelInputs.Controls.Add(gap);
            modelInputs.Controls.Add(_modelName);

            var maps = new SideListPane("Model → tuning", _mappingList, modelInputs);
            maps.AddFooterButton("Add", primary: true).Click += (_, _) => AddMapping();
            maps.AddFooterButton("Delete").Click += (_, _) => DeleteMapping();

            split.Panel1.Controls.Add(tunings);
            split.Panel2.Controls.Add(maps);
            root.Controls.Add(split);
            return root;
        }

        private Panel BuildPreviewPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 10), BackColor = AppTheme.Panel };
            var header = new Label
            {
                Text = "PREVIEW",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = AppTheme.Muted,
                Font = AppTheme.SmallFont,
                BackColor = Color.Transparent,
                Padding = new Padding(2, 2, 0, 0)
            };

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            void AddView(string label, PreviewViewMode mode)
            {
                var b = new Button { Text = label, AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
                AppTheme.StyleButton(b);
                b.Click += (_, _) => _preview.ViewMode = mode;
                toolbar.Controls.Add(b);
            }
            AddView("Orbit", PreviewViewMode.Iso);
            AddView("Top", PreviewViewMode.TopXY);
            AddView("Side", PreviewViewMode.SideXZ);
            AddView("Front", PreviewViewMode.FrontYZ);

            var motionLbl = new Label
            {
                Text = "Motion",
                AutoSize = true,
                Padding = new Padding(12, 7, 4, 0),
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont
            };
            var motion = new ThemedComboBox
            {
                Width = 200,
                Height = InputChrome.Height,
                Margin = new Padding(0, 2, 0, 0)
            };
            foreach (var (id, label) in DoorConstants.DoorTypes)
                motion.Items.Add($"{id} — {label}");
            motion.SelectedIndex = 0;
            motion.SelectedIndexChanged += (_, _) =>
            {
                if (motion.SelectedItem is not string sel) return;
                var id = sel.Split('—')[0].Trim();
                _preview.SetSpecialAttribute(id);
            };
            _motionCombo = motion;

            var loadYdr = new Button { Text = "Load YDR…", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            AppTheme.StyleButton(loadYdr, primary: true);
            loadYdr.Click += (_, _) => PromptLoadYdr();
            toolbar.Controls.Add(motionLbl);
            toolbar.Controls.Add(motion);
            toolbar.Controls.Add(loadYdr);
            _preview.LoadYdrRequested += (_, _) => PromptLoadYdr();

            var hint = new Label
            {
                Text = "Offset cyan · Trigger orange · drop .ydr · scroll zoom · drag orbit",
                Dock = DockStyle.Bottom,
                Height = 22
            };
            AppTheme.StyleHint(hint);

            var previewFrame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = AppTheme.LineSoft
            };
            previewFrame.Controls.Add(_preview);

            pane.Controls.Add(previewFrame);
            pane.Controls.Add(toolbar);
            pane.Controls.Add(hint);
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
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (_preview.TryLoadYdr(dlg.FileName))
                {
                    SyncMotionCombo(_preview.SpecialAttribute);
                    _setStatus("Preview mesh: " + Path.GetFileName(dlg.FileName));
                }
            }
        }

        private void SyncMotionCombo(string attr)
        {
            if (_motionCombo == null) return;
            for (int i = 0; i < _motionCombo.Items.Count; i++)
            {
                var s = _motionCombo.Items[i]?.ToString() ?? "";
                if (s.StartsWith(attr + " ", StringComparison.Ordinal) || s.StartsWith(attr + "—", StringComparison.Ordinal) || s.StartsWith(attr + " —", StringComparison.Ordinal))
                {
                    _motionCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void BuildFormPanels()
        {
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(16, 16, 20, 32),
                BackColor = AppTheme.Background,
                Width = 520
            };

            // --- Auto-open volume ---
            var vol = new SectionCard("Auto-open volume");
            var offsetLbl = new Label
            {
                Text = "AutoOpenVolumeOffset (local X/Y/Z)",
                Left = 0,
                Top = 0,
                Width = 420,
                Height = 18,
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont
            };
            _offset.Top = 22;
            _offset.Left = 0;
            var scalars = FieldRow.Create(_radiusMod, _openRate, _cosine);
            scalars.Top = 82;
            scalars.Left = 0;
            vol.Body.Controls.Add(offsetLbl);
            vol.Body.Controls.Add(_offset);
            vol.Body.Controls.Add(scalars);
            vol.FitToBody(156);
            stack.Controls.Add(vol);

            // --- Trigger box ---
            var box = new SectionCard("Trigger box");
            var boxHint = new Label
            {
                Text = "TriggerBoxMinMax (used when Custom trigger box is on)",
                Left = 0,
                Top = 0,
                Width = 420,
                Height = 18,
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont
            };
            var minLbl = new Label
            {
                Text = "MIN",
                Left = 0,
                Top = 22,
                Width = 40,
                Height = 16,
                ForeColor = AppTheme.Muted,
                Font = AppTheme.SmallFont
            };
            _boxMin.Top = 38;
            _boxMin.Left = 0;
            var maxLbl = new Label
            {
                Text = "MAX",
                Left = 0,
                Top = 98,
                Width = 40,
                Height = 16,
                ForeColor = AppTheme.Muted,
                Font = AppTheme.SmallFont
            };
            _boxMax.Top = 114;
            _boxMax.Left = 0;
            box.Body.Controls.Add(boxHint);
            box.Body.Controls.Add(minLbl);
            box.Body.Controls.Add(_boxMin);
            box.Body.Controls.Add(maxLbl);
            box.Body.Controls.Add(_boxMax);
            box.FitToBody(182);
            stack.Controls.Add(box);

            // --- Physics ---
            var phys = new SectionCard("Physics");
            var phys1 = FieldRow.Create(_breakImpulse, _mass, _weapon);
            phys1.Top = 0;
            phys1.Left = 0;
            var phys2 = FieldRow.Create(_rotLimit, _angVel);
            phys2.Top = 62;
            phys2.Left = 0;
            phys.Body.Controls.Add(phys1);
            phys.Body.Controls.Add(phys2);
            phys.FitToBody(128);
            stack.Controls.Add(phys);

            // --- Options ---
            var opts = new SectionCard("Options");
            int oy = 2;
            foreach (var c in new[] { _closeTaper, _useTrigger, _customTrigger, _breakable, _latch })
            {
                c.Left = 0;
                c.Top = oy;
                c.Width = Math.Max(280, TextRenderer.MeasureText(c.Text, c.Font).Width + 36);
                opts.Body.Controls.Add(c);
                oy += 30;
            }
            opts.FitToBody(oy + 6);
            stack.Controls.Add(opts);

            // --- Flags + direction ---
            var flagsCard = new SectionCard("Flags & rotation direction");
            int fy = 2;
            foreach (var c in _flagChecks)
            {
                c.Left = 0;
                c.Top = fy;
                flagsCard.Body.Controls.Add(c);
                fy += 30;
            }
            fy += 8;

            var dirLbl = new Label
            {
                Text = "StdDoorRotDir",
                Left = 0,
                Top = fy,
                Width = 120,
                Height = InputChrome.Height,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont
            };
            _rotDir.Left = 120;
            _rotDir.Top = fy;
            _rotDir.Width = 290;
            _rotDir.Height = InputChrome.Height;

            flagsCard.Body.Controls.Add(dirLbl);
            flagsCard.Body.Controls.Add(_rotDir);
            flagsCard.FitToBody(fy + InputChrome.Height + 8);
            stack.Controls.Add(flagsCard);

            _formHost.Controls.Add(stack);
            void ResizeStack()
            {
                int w = Math.Max(480, _formHost.ClientSize.Width - 24);
                int fieldW = Math.Max(280, w - 48);
                stack.Width = w;
                _offset.SetLayoutWidth(fieldW);
                _boxMin.SetLayoutWidth(fieldW);
                _boxMax.SetLayoutWidth(fieldW);
                FieldRow.SetLayoutWidth(scalars, fieldW);
                FieldRow.SetLayoutWidth(phys1, fieldW);
                FieldRow.SetLayoutWidth(phys2, fieldW);
                foreach (Control c in stack.Controls)
                {
                    if (c is SectionCard card)
                        card.Width = w - 12;
                }
                stack.PerformLayout();
                _formHost.AutoScrollMinSize = new Size(
                    w,
                    stack.Height + stack.Padding.Vertical + 16);
            }
            _formHost.Resize += (_, _) => ResizeStack();
            ResizeStack();
            _formHost.WireMouseWheelBubble(stack);
        }

        private void WireEvents()
        {
            _tuningList.SelectedIndexChanged += (_, _) => OnTuningSelectionChanged();

            void OnAnyChange(object? s, EventArgs e)
            {
                if (_loadingUi) return;
                PushUiToTuning();
            }

            _offset.ValueChanged += OnAnyChange;
            _boxMin.ValueChanged += OnAnyChange;
            _boxMax.ValueChanged += OnAnyChange;
            foreach (var f in new[] { _radiusMod, _openRate, _cosine, _breakImpulse, _mass, _weapon, _rotLimit, _angVel })
                f.ValueChanged += OnAnyChange;
            foreach (var c in new[] { _closeTaper, _useTrigger, _customTrigger, _breakable, _latch })
                c.CheckedChanged += OnAnyChange;
            foreach (var c in _flagChecks)
                c.CheckedChanged += OnAnyChange;
            _rotDir.SelectedIndexChanged += OnAnyChange;
        }

        private async Task LoadFromGameAsync()
        {
            try
            {
                UseWaitCursor = true;
                _setStatus("Loading doortuning.ymt…");
                await Task.Run(() =>
                {
                    var ymt = DoorTuningDocument.LoadFromGame(_rpfMan)
                        ?? throw new FileNotFoundException("Could not find " + DoorTuningDocument.GameRelativePath);
                    _document = ymt.DoorTuning ?? DoorTuningDocument.FromYmt(ymt);
                    _sourcePath = DoorTuningDocument.GameRelativePath;
                });
                RefreshTuningList();
                RefreshMappingList();
                _setStatus($"Loaded {_document.NamedTunings.Count} tunings, {_document.ModelMappings.Count} mappings from {_sourcePath}");
            }
            catch (Exception ex)
            {
                _setStatus("Load failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Load from game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void OpenExisting()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Open existing doortuning",
                Filter = "Door tuning|*.ymt;*.xml;*.ymt.xml|YMT files|*.ymt|XML files|*.xml|All files|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                UseWaitCursor = true;
                var path = dlg.FileName;
                _document = LoadDocumentFromPath(path);
                _sourcePath = path;
                RefreshTuningList();
                RefreshMappingList();
                _setStatus($"Opened {_document.NamedTunings.Count} tunings, {_document.ModelMappings.Count} mappings from {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                _setStatus("Open failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Open doortuning", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private static DoorTuningDocument LoadDocumentFromPath(string path) =>
            DoorTuningFileLoader.LoadFromPath(path);

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
            _loadingUi = true;
            if (nt == null)
            {
                _preview.SetTuning(null);
                _loadingUi = false;
                return;
            }

            var t = nt.Tuning;
            _offset.Set(t.AutoOpenVolumeOffsetX, t.AutoOpenVolumeOffsetY, t.AutoOpenVolumeOffsetZ);
            _boxMin.Set(t.TriggerBoxMinX, t.TriggerBoxMinY, t.TriggerBoxMinZ);
            _boxMax.Set(t.TriggerBoxMaxX, t.TriggerBoxMaxY, t.TriggerBoxMaxZ);
            _radiusMod.Value = t.AutoOpenRadiusModifier;
            _openRate.Value = t.AutoOpenRate;
            _cosine.Value = t.AutoOpenCosineAngleBetweenThreshold;
            _breakImpulse.Value = t.BreakingImpulse;
            _mass.Value = t.MassMultiplier;
            _weapon.Value = t.WeaponImpulseMultiplier;
            _rotLimit.Value = t.RotationLimitAngle;
            _angVel.Value = t.TorqueAngularVelocityLimit;
            _closeTaper.Checked = t.AutoOpenCloseRateTaper;
            _useTrigger.Checked = t.UseAutoOpenTriggerBox;
            _customTrigger.Checked = t.CustomTriggerBox;
            _breakable.Checked = t.BreakableByVehicle;
            _latch.Checked = t.ShouldLatchShut;

            var dirIdx = _rotDir.Items.IndexOf(t.StdDoorRotDir);
            _rotDir.SelectedIndex = dirIdx >= 0 ? dirIdx : 0;

            foreach (var c in _flagChecks)
                c.Checked = t.Flags.Contains(c.Text);

            _preview.SetTuning(t);
            var map = _document.ModelMappings.FirstOrDefault(m =>
                string.Equals(m.TuningName, nt.Name, StringComparison.OrdinalIgnoreCase));
            var guess = DoorPreviewPanel.GuessSpecialAttribute(map?.ModelName ?? nt.Name);
            _preview.SetSpecialAttribute(guess);
            SyncMotionCombo(guess);
            _loadingUi = false;
        }

        private void PushUiToTuning()
        {
            var nt = GetSelectedTuning();
            if (nt == null) return;
            var t = nt.Tuning;
            var (ox, oy, oz) = _offset.Get();
            t.AutoOpenVolumeOffsetX = ox;
            t.AutoOpenVolumeOffsetY = oy;
            t.AutoOpenVolumeOffsetZ = oz;
            var (minX, minY, minZ) = _boxMin.Get();
            var (maxX, maxY, maxZ) = _boxMax.Get();
            t.TriggerBoxMinX = minX;
            t.TriggerBoxMinY = minY;
            t.TriggerBoxMinZ = minZ;
            t.TriggerBoxMaxX = maxX;
            t.TriggerBoxMaxY = maxY;
            t.TriggerBoxMaxZ = maxZ;
            t.AutoOpenRadiusModifier = _radiusMod.Value;
            t.AutoOpenRate = _openRate.Value;
            t.AutoOpenCosineAngleBetweenThreshold = _cosine.Value;
            t.BreakingImpulse = _breakImpulse.Value;
            t.MassMultiplier = _mass.Value;
            t.WeaponImpulseMultiplier = _weapon.Value;
            t.RotationLimitAngle = _rotLimit.Value;
            t.TorqueAngularVelocityLimit = _angVel.Value;
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
            var name = (_newTuningName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Enter a tuning name.", "Door Tuning", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_document.NamedTunings.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "That named tuning already exists.", "Door Tuning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _document.NamedTunings.Add(new DoorNamedTuning { Name = name, Tuning = new DoorTuningParams() });
            _newTuningName.Clear();
            RefreshTuningList();
            _tuningList.SelectedItem = name;
            _setStatus($"Added tuning {name}");
        }

        private void DeleteTuning()
        {
            var nt = GetSelectedTuning();
            if (nt == null) return;
            if (MessageBox.Show(this, $"Delete \"{nt.Name}\"?", "Door Tuning", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _document.NamedTunings.Remove(nt);
            RefreshTuningList();
            _setStatus($"Deleted {nt.Name}");
        }

        private void AddMapping()
        {
            var model = (_modelName.Text ?? string.Empty).Trim();
            var tuning = (_modelTuning.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(tuning))
            {
                MessageBox.Show(this, "Enter model name and tuning name.", "Door Tuning", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _document.ModelMappings.Add(new DoorModelMapping { ModelName = model, TuningName = tuning });
            _modelName.Clear();
            RefreshMappingList();
            _setStatus($"Mapped {model} → {tuning}");
        }

        private void DeleteMapping()
        {
            if (_mappingList.SelectedIndex < 0) return;
            _document.ModelMappings.RemoveAt(_mappingList.SelectedIndex);
            RefreshMappingList();
            _setStatus("Removed mapping");
        }

        private void ExportYmt()
        {
            PushUiToTuning();
            using var folderDlg = new FolderBrowserDialog
            {
                Description = "Export doortuning.ymt",
                UseDescriptionForTitle = true
            };
            if (folderDlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var ymtPath = Path.Combine(folderDlg.SelectedPath, "doortuning.ymt");
                var xmlPath = Path.Combine(folderDlg.SelectedPath, "doortuning.ymt.xml");
                File.WriteAllBytes(ymtPath, _document.Save());
                File.WriteAllText(xmlPath, _document.ToXml());
                _setStatus($"Exported {ymtPath}");
                MessageBox.Show(this, $"Exported:\n{ymtPath}\n{xmlPath}", "Door Tuning", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportFiveMResource()
        {
            PushUiToTuning();
            using var nameDlg = new ResourceNameDialog(_sourcePath != null
                ? FivemResourceExport.SanitizeResourceName(Path.GetFileNameWithoutExtension(_sourcePath))
                : "doortuning");
            if (nameDlg.ShowDialog(this) != DialogResult.OK) return;

            using var folderDlg = new FolderBrowserDialog
            {
                Description = "Pick your FiveM resources folder (e.g. resources\\[main]) — the resource folder will be created inside",
                UseDescriptionForTitle = true
            };
            if (folderDlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var dest = FivemResourceExport.ExportBundle(folderDlg.SelectedPath, nameDlg.ResourceName, _document);
                var resourceName = FivemResourceExport.SanitizeResourceName(nameDlg.ResourceName);
                _setStatus($"FiveM resource exported: {dest}");
                MessageBox.Show(this,
                    $"FiveM resource created:\n{dest}\n\n" +
                    $"• doortuning.ymt (Meta XML)\n" +
                    $"• gta5.meta → resources:/{resourceName}/doortuning\n" +
                    $"• fxmanifest.lua (replace_level_meta)\n\n" +
                    "Add ensure " + resourceName + " to server.cfg.",
                    "FiveM export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "FiveM export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
