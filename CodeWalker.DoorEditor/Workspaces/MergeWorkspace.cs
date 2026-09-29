using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Workspaces
{
    public sealed class MergeWorkspace : UserControl
    {
        private readonly Action<string> _setStatus;
        private readonly Label _mainInfo = new() { AutoSize = true };
        private readonly DarkListBox _incomingList = new() { Dock = DockStyle.Fill };
        private readonly DarkListBox _addsList = new() { Dock = DockStyle.Fill };
        private readonly DarkListBox _conflictsList = new() { Dock = DockStyle.Fill };

        private DoorTuningDocument? _main;
        private string? _mainPath;
        private readonly List<(string Name, string Path, DoorTuningDocument Doc)> _incoming = new();
        private MergeResult? _result;

        public MergeWorkspace(Action<string> setStatus)
        {
            _setStatus = setStatus;
            BackColor = AppTheme.Background;
            Dock = DockStyle.Fill;
            _mainInfo.ForeColor = AppTheme.Faint;
            _mainInfo.Font = AppTheme.MonoFont;
            BuildLayout();
        }

        private void BuildLayout()
        {
            var header = new WorkspaceHeader("Merger");
            header.AddAction("Load main…").Click += (_, _) => LoadMain();
            header.AddAction("Add incoming…").Click += (_, _) => AddIncoming();
            header.AddAction("Export merged…", primary: true).Click += (_, _) => Export();
            header.AddAction("Reset").Click += (_, _) =>
            {
                _main = null;
                _mainPath = null;
                _incoming.Clear();
                _result = null;
                _mainInfo.Text = "No main file";
                RefreshUi();
            };

            var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16, 10, 16, 10), BackColor = AppTheme.Panel };
            var mainLbl = new Label { Text = "MAIN DOORTUNING", AutoSize = true, ForeColor = AppTheme.Muted, Font = AppTheme.SmallFont };
            _mainInfo.Text = "No main file";
            _mainInfo.Location = new Point(16, 32);
            mainLbl.Location = new Point(16, 10);
            top.Controls.Add(mainLbl);
            top.Controls.Add(_mainInfo);

            var split = SplitLayout.Create(Orientation.Vertical, preferredDistance: 360, panel1Min: 180, panel2Min: 280);
            split.BackColor = AppTheme.Background;
            split.Panel1.BackColor = AppTheme.Panel;
            split.Panel2.BackColor = AppTheme.Background;

            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var lHeader = new Label { Text = "Incoming files", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(lHeader);
            lHeader.Font = AppTheme.UiFontBold;
            var rem = new Button { Text = "Remove selected", AutoSize = true, Dock = DockStyle.Bottom };
            AppTheme.StyleButton(rem);
            rem.Click += (_, _) =>
            {
                if (_incomingList.SelectedIndex < 0) return;
                _incoming.RemoveAt(_incomingList.SelectedIndex);
                Recompute();
            };
            left.Controls.Add(_incomingList);
            left.Controls.Add(rem);
            left.Controls.Add(lHeader);

            var right = SplitLayout.Create(Orientation.Horizontal, preferredDistance: 280, panel1Min: 120, panel2Min: 120);
            right.BackColor = AppTheme.LineSoft;
            right.Panel1.BackColor = AppTheme.Background;
            right.Panel2.BackColor = AppTheme.Panel;

            var adds = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var aHeader = new Label { Text = "Additions", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(aHeader);
            aHeader.Font = AppTheme.UiFontBold;
            adds.Controls.Add(_addsList);
            adds.Controls.Add(aHeader);

            var conf = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var cHeader = new Label { Text = "Conflicts (kept from main)", Dock = DockStyle.Top, Height = 22 };
            AppTheme.StyleHeader(cHeader);
            cHeader.Font = AppTheme.UiFontBold;
            conf.Controls.Add(_conflictsList);
            conf.Controls.Add(cHeader);

            right.Panel1.Controls.Add(adds);
            right.Panel2.Controls.Add(conf);
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);

            Controls.Add(split);
            Controls.Add(top);
            Controls.Add(header);
        }

        private static DoorTuningDocument LoadDoc(string path) =>
            DoorTuningFileLoader.LoadFromPath(path);

        private void LoadMain()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Door tuning|*.ymt;*.xml;*.ymt.xml|All files|*.*",
                Title = "Load main doortuning"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _main = LoadDoc(dlg.FileName);
                _mainPath = dlg.FileName;
                _mainInfo.Text = $"{Path.GetFileName(dlg.FileName)}  ·  {_main.NamedTunings.Count} tunings, {_main.ModelMappings.Count} maps";
                Recompute();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddIncoming()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Door tuning|*.ymt;*.xml;*.ymt.xml|All files|*.*",
                Title = "Add incoming doortuning",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var path in dlg.FileNames)
            {
                try
                {
                    var doc = LoadDoc(path);
                    _incoming.Add((Path.GetFileName(path), path, doc));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"{path}\n{ex.Message}", "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Recompute();
        }

        private void Recompute()
        {
            if (_main == null)
            {
                _result = null;
                RefreshUi();
                return;
            }
            _result = DoorTuningMerge.MergeMany(_main, _incoming.Select(i => (i.Name, i.Doc)));
            RefreshUi();
            _setStatus($"Merge: +{_result.AddTunings.Count} tunings, +{_result.AddMaps.Count} maps, {_result.Conflicts.Count} conflicts");
        }

        private void RefreshUi()
        {
            _incomingList.BeginUpdate();
            _incomingList.Items.Clear();
            foreach (var i in _incoming)
                _incomingList.Items.Add($"{i.Name}  ({i.Doc.NamedTunings.Count}t / {i.Doc.ModelMappings.Count}m)");
            _incomingList.EndUpdate();

            _addsList.BeginUpdate();
            _addsList.Items.Clear();
            if (_result != null)
            {
                foreach (var t in _result.AddTunings)
                    _addsList.Items.Add($"tuning + {t}");
                foreach (var m in _result.AddMaps)
                    _addsList.Items.Add($"map + {m.Model} → {m.Tuning}");
            }
            _addsList.EndUpdate();

            _conflictsList.BeginUpdate();
            _conflictsList.Items.Clear();
            if (_result != null)
            {
                foreach (var c in _result.Conflicts)
                {
                    var src = string.IsNullOrEmpty(c.Source) ? "" : $" [{c.Source}]";
                    _conflictsList.Items.Add($"{c.Kind} {c.Key}: {c.Existing} vs {c.Incoming}{src}");
                }
            }
            _conflictsList.EndUpdate();
        }

        private void Export()
        {
            if (_result == null)
            {
                MessageBox.Show(this, "Load a main file and at least one incoming file.", "Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var folder = new FolderBrowserDialog
            {
                Description = "Export merged doortuning",
                UseDescriptionForTitle = true
            };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var ymtPath = Path.Combine(folder.SelectedPath, "doortuning.ymt");
                var xmlPath = Path.Combine(folder.SelectedPath, "doortuning.ymt.xml");
                File.WriteAllBytes(ymtPath, _result.Document.Save());
                File.WriteAllText(xmlPath, _result.Document.ToXml());
                _setStatus($"Exported merged {ymtPath}");
                MessageBox.Show(this, $"Exported:\n{ymtPath}\n{xmlPath}", "Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
