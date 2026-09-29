using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;

namespace CodeWalker.DoorEditor.Workspaces
{
    public sealed class NamesWorkspace : UserControl
    {
        private readonly Action<string> _setStatus;
        private readonly DarkListBox _list = new() { Dock = DockStyle.Fill };
        private readonly ThemedTextBox _search = new();
        private readonly ThemedTextBox _draft = new();
        private List<string> _names = new();

        public NamesWorkspace(Action<string> setStatus)
        {
            _setStatus = setStatus;
            BackColor = AppTheme.Background;
            Dock = DockStyle.Fill;
            BuildLayout();
        }

        private void BuildLayout()
        {
            var header = new WorkspaceHeader("Nametables");
            header.AddAction("Import…").Click += (_, _) => Import();
            header.AddAction("Export…", primary: true).Click += (_, _) => Export();
            header.AddAction("Clear").Click += (_, _) =>
            {
                if (MessageBox.Show(this, "Clear all names?", "Nametable", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                _names.Clear();
                RefreshList();
            };

            var searchWrap = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 0, 0, 6) };
            _search.PlaceholderText = "Search…";
            _search.Dock = DockStyle.Fill;
            _search.TextChanged += (_, _) => RefreshList();
            searchWrap.Controls.Add(_search);

            var pane = new SideListPane("Names", _list, searchWrap);
            _draft.Width = 280;
            _draft.PlaceholderText = "New name (e.g. d_prop_door_01)";
            _draft.Margin = new Padding(0, 0, 8, 0);
            pane.Footer.Controls.Add(_draft);
            pane.AddFooterButton("Add", primary: true).Click += (_, _) => AddName();
            pane.AddFooterButton("Delete").Click += (_, _) => DeleteSelected();

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4), BackColor = AppTheme.Panel };
            body.Controls.Add(pane);

            Controls.Add(body);
            Controls.Add(header);
        }

        private void RefreshList()
        {
            var q = _search.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var n in _names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (q.Length == 0 || n.Contains(q, StringComparison.OrdinalIgnoreCase))
                    _list.Items.Add(n);
            }
            _list.EndUpdate();
            _setStatus($"{_names.Count} nametable entries");
        }

        private void AddName()
        {
            var n = _draft.Text.Trim();
            if (n.Length == 0) return;
            if (!_names.Contains(n, StringComparer.OrdinalIgnoreCase))
                _names.Add(n);
            _draft.Clear();
            RefreshList();
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItem is not string n) return;
            _names.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            RefreshList();
        }

        private void Import()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Nametable|*.nametable;*.txt;*.dat151.nametable|All files|*.*",
                Title = "Import nametable",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var path in dlg.FileNames)
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#')) continue;
                    if (!_names.Contains(t, StringComparer.OrdinalIgnoreCase))
                        _names.Add(t);
                }
            }
            RefreshList();
        }

        private void Export()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Nametable|*.nametable|Text|*.txt",
                FileName = "doors.dat151.nametable",
                Title = "Export nametable"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllLines(dlg.FileName, _names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            _setStatus("Exported " + Path.GetFileName(dlg.FileName));
        }
    }
}
