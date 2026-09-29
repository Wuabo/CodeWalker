using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Workspaces
{
    public sealed class TypeWorkspace : UserControl
    {
        private readonly Action<string> _setStatus;
        private readonly DarkListBox _list = new() { Dock = DockStyle.Fill };
        private readonly ThemedTextBox _search = new();
        private readonly ThemedComboBox _typeCombo = new();
        private readonly DarkCheck _useFlags = new("Apply door flags preset");
        private readonly Label _flagsInfo = new() { AutoSize = true };
        private readonly DoorPreviewPanel _preview = new() { Dock = DockStyle.Fill };

        private string _xml = "";
        private string? _path;
        private System.Collections.Generic.List<YtypArchetypeItem> _items = new();
        private bool _loading;

        public TypeWorkspace(Action<string> setStatus)
        {
            _setStatus = setStatus;
            BackColor = AppTheme.Background;
            Dock = DockStyle.Fill;

            _useFlags.Width = 280;
            _flagsInfo.ForeColor = AppTheme.Faint;
            _flagsInfo.Font = AppTheme.MonoFont;

            foreach (var (id, label) in DoorConstants.DoorTypes)
                _typeCombo.Items.Add($"{id} — {label}");

            BuildLayout();
            WireEvents();
        }

        private void BuildLayout()
        {
            var header = new WorkspaceHeader("Door Type");
            header.AddAction("Open YTYP…").Click += (_, _) => OpenFile();
            header.AddAction("Save XML…", primary: true).Click += (_, _) => SaveXml();

            var split = SplitLayout.Create(Orientation.Vertical, preferredDistance: 320, panel1Min: 180, panel2Min: 280);
            split.BackColor = AppTheme.Background;
            split.Panel1.BackColor = AppTheme.Panel;
            split.Panel2.BackColor = AppTheme.Background;

            var searchWrap = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 0, 0, 6) };
            _search.PlaceholderText = "Search…";
            _search.Dock = DockStyle.Fill;
            searchWrap.Controls.Add(_search);
            var left = new SideListPane("Archetypes", _list, searchWrap);

            var rightSplit = SplitLayout.Create(Orientation.Vertical, preferredDistance: 360, panel1Min: 200, panel2Min: 240);
            rightSplit.BackColor = AppTheme.Background;
            rightSplit.Panel1.BackColor = AppTheme.Background;
            rightSplit.Panel2.BackColor = AppTheme.Panel;

            var editor = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var eHeader = new Label { Text = "specialAttribute", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(eHeader);
            eHeader.Font = AppTheme.UiFontBold;
            _typeCombo.Dock = DockStyle.Top;
            _typeCombo.Height = 34;
            _typeCombo.Margin = new Padding(0, 0, 0, 8);
            var opts = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 70,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(0, 10, 0, 0)
            };
            opts.Controls.Add(_useFlags);
            opts.Controls.Add(_flagsInfo);
            var hint = new Label
            {
                Text = "Load a matching .ydr in the preview to see door motion for the selected type.",
                Dock = DockStyle.Top,
                Height = 40,
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont
            };
            editor.Controls.Add(hint);
            editor.Controls.Add(opts);
            editor.Controls.Add(_typeCombo);
            editor.Controls.Add(eHeader);

            var previewPane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var pHeader = new Label { Text = "Door preview", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(pHeader);
            pHeader.Font = AppTheme.UiFontBold;
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            void AddView(string label, PreviewViewMode mode)
            {
                var b = new Button { Text = label, AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
                AppTheme.StyleButton(b);
                b.Click += (_, _) => _preview.ViewMode = mode;
                toolbar.Controls.Add(b);
            }
            AddView("Orbit", PreviewViewMode.Iso);
            AddView("Top", PreviewViewMode.TopXY);
            var loadYdr = new Button { Text = "Load YDR…", AutoSize = true };
            AppTheme.StyleButton(loadYdr, primary: true);
            loadYdr.Click += (_, _) => PromptLoadYdr();
            toolbar.Controls.Add(loadYdr);
            previewPane.Controls.Add(_preview);
            previewPane.Controls.Add(toolbar);
            previewPane.Controls.Add(pHeader);

            rightSplit.Panel1.Controls.Add(editor);
            rightSplit.Panel2.Controls.Add(previewPane);
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(rightSplit);

            Controls.Add(split);
            Controls.Add(header);
        }

        private void WireEvents()
        {
            _search.TextChanged += (_, _) => RefreshList();
            _list.SelectedIndexChanged += (_, _) => OnSelect();
            _typeCombo.SelectedIndexChanged += (_, _) => ApplyType();
            _useFlags.CheckedChanged += (_, _) => ApplyFlagsToggle();
            _preview.LoadYdrRequested += (_, _) => PromptLoadYdr();
        }

        private void PromptLoadYdr()
        {
            using var dlg = new OpenFileDialog { Filter = "YDR drawable|*.ydr|All files|*.*", Title = "Load door YDR" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _preview.TryLoadYdr(dlg.FileName);
        }

        private void OpenFile()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "YTYP|*.ytyp;*.xml;*.ytyp.xml|All files|*.*",
                Title = "Open YTYP"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var path = dlg.FileName;
                if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    _xml = File.ReadAllText(path);
                }
                else
                {
                    var bytes = File.ReadAllBytes(path);
                    var ytyp = new YtypFile { Name = Path.GetFileName(path) };
                    ytyp.Load(bytes);
                    _xml = MetaXml.GetXml(ytyp, out _);
                    if (string.IsNullOrWhiteSpace(_xml))
                        throw new InvalidOperationException("Could not convert YTYP to XML.");
                }
                _path = path;
                _items = YtypXmlEditor.Parse(_xml);
                RefreshList();
                _setStatus($"Loaded {_items.Count} archetypes from {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Open YTYP failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshList()
        {
            var q = (_search.Text ?? "").Trim();
            var selected = GetSelected()?.Name;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(q) &&
                    item.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.SpecialAttribute.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            if (selected != null)
            {
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    if (_list.Items[i] is YtypArchetypeItem it && it.Name == selected)
                    {
                        _list.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
        }

        private YtypArchetypeItem? GetSelected() => _list.SelectedItem as YtypArchetypeItem;

        private void OnSelect()
        {
            var item = GetSelected();
            _loading = true;
            if (item == null)
            {
                _loading = false;
                return;
            }
            var idx = Array.FindIndex(DoorConstants.DoorTypes, t => t.Id == item.SpecialAttribute);
            if (idx < 0)
            {
                // custom value — add temporarily
                var label = $"{item.SpecialAttribute} — Custom";
                if (!_typeCombo.Items.Cast<object>().Any(o => o.ToString()!.StartsWith(item.SpecialAttribute + " ")))
                    _typeCombo.Items.Add(label);
                _typeCombo.SelectedItem = _typeCombo.Items.Cast<object>().First(o => o.ToString()!.StartsWith(item.SpecialAttribute));
            }
            else
                _typeCombo.SelectedIndex = idx;

            _useFlags.Checked = item.UseFlags;
            UpdateFlagsLabel(item);
            _preview.SetSpecialAttribute(item.SpecialAttribute);
            _loading = false;
        }

        private void UpdateFlagsLabel(YtypArchetypeItem item)
        {
            var flagsNum = int.TryParse(item.Flags, out var f) ? f : 0;
            var preset = DoorConstants.FlagsPresetLabel(flagsNum);
            _flagsInfo.Text = preset != null
                ? $"flags = {item.Flags} ({preset})"
                : $"flags = {item.Flags}";
        }

        private void ApplyType()
        {
            if (_loading) return;
            var item = GetSelected();
            if (item == null || _typeCombo.SelectedItem is not string sel) return;
            var id = sel.Split('—')[0].Trim();
            item.SpecialAttribute = id;
            _xml = YtypXmlEditor.UpdateSpecialAttribute(_xml, item.Name, id);
            if (item.UseFlags)
            {
                var flags = DoorConstants.FlagsForType(id).ToString();
                item.Flags = flags;
                _xml = YtypXmlEditor.UpdateFlags(_xml, item.Name, flags);
            }
            UpdateFlagsLabel(item);
            _preview.SetSpecialAttribute(id);
            _list.Invalidate();
            _setStatus($"Set {item.Name} specialAttribute = {id}");
        }

        private void ApplyFlagsToggle()
        {
            if (_loading) return;
            var item = GetSelected();
            if (item == null) return;
            item.UseFlags = _useFlags.Checked;
            if (item.UseFlags)
            {
                var flags = DoorConstants.FlagsForType(item.SpecialAttribute).ToString();
                item.Flags = flags;
                _xml = YtypXmlEditor.UpdateFlags(_xml, item.Name, flags);
                UpdateFlagsLabel(item);
            }
        }

        private void SaveXml()
        {
            if (string.IsNullOrEmpty(_xml))
            {
                MessageBox.Show(this, "Open a YTYP first.", "Door Type", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Title = "Save YTYP XML",
                Filter = "XML|*.xml|All files|*.*",
                FileName = Path.GetFileName(_path ?? "types.ytyp") + (_path?.EndsWith(".xml") == true ? "" : ".xml")
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, _xml);
            _setStatus($"Saved {dlg.FileName}");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // ListBox display
            _list.Format += (_, args) =>
            {
                if (args.ListItem is YtypArchetypeItem it)
                    args.Value = $"{it.Name}  [{it.SpecialAttribute}]";
            };
            _list.FormattingEnabled = true;
        }
    }
}
