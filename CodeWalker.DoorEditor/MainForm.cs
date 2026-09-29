using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Controls;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.DoorEditor.Workspaces;
using CodeWalker.GameFiles;
using CodeWalker.Properties;

namespace CodeWalker.DoorEditor
{
    public sealed class MainForm : Form
    {
        private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 220 };
        private readonly Panel _contentHost = new() { Dock = DockStyle.Fill };
        private readonly Panel _titleBar = new() { Dock = DockStyle.Top, Height = 40 };
        private readonly StatusStrip _statusStrip = new();
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label _brand = new();
        private readonly Label _workspaceTitle = new();

        private readonly NavButton _navTuning = new() { Text = "Door Tuning", Subtitle = "YMT named tunings", Dock = DockStyle.Top };
        private readonly NavButton _navType = new() { Text = "Door Type", Subtitle = "YTYP specialAttribute", Dock = DockStyle.Top };
        private readonly NavButton _navAudio = new() { Text = "Door Audio", Subtitle = "DAT151 REL", Dock = DockStyle.Top };
        private readonly NavButton _navNames = new() { Text = "Nametables", Subtitle = ".dat151.nametable", Dock = DockStyle.Top };
        private readonly NavButton _navMerge = new() { Text = "Merger", Subtitle = "Union-merge YMT", Dock = DockStyle.Top };

        private TuningWorkspace? _tuning;
        private TypeWorkspace? _type;
        private AudioWorkspace? _audio;
        private NamesWorkspace? _names;
        private MergeWorkspace? _merge;
        private Control? _active;
        private RpfManager? _rpfMan;
        private bool _booted;

        public MainForm()
        {
            Text = "CodeWalker Door Editor";
            Width = 1480;
            Height = 920;
            MinimumSize = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterScreen;
            AppTheme.StyleForm(this);

            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) Icon = icon;
            }
            catch { /* ignore */ }

            BuildChrome();
            BuildSidebar();

            Controls.Add(_contentHost);
            Controls.Add(_sidebar);
            Controls.Add(_statusStrip);
            Controls.Add(_titleBar);

            _statusStrip.Items.Add(_status);
            AppTheme.StyleStatus(_statusStrip);
            _status.ForeColor = AppTheme.Faint;
            _status.Text = "Starting…";

            Shown += async (_, _) => await BootAsync();
        }

        private void BuildChrome()
        {
            AppTheme.StylePanel(_titleBar, AppTheme.Activity);
            _titleBar.Height = 44;
            _titleBar.Padding = new Padding(16, 0, 16, 0);
            _titleBar.Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.LineSoft);
                e.Graphics.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
            };

            _brand.Text = "Door Editor";
            _brand.AutoSize = true;
            _brand.Font = AppTheme.TitleFont;
            _brand.ForeColor = AppTheme.Bright;
            _brand.Location = new Point(18, 12);

            _workspaceTitle.AutoSize = true;
            _workspaceTitle.Font = AppTheme.UiFont;
            _workspaceTitle.ForeColor = AppTheme.Faint;
            _workspaceTitle.Location = new Point(150, 14);
            _workspaceTitle.Text = "";

            _titleBar.Controls.Add(_brand);
            _titleBar.Controls.Add(_workspaceTitle);
        }

        private void BuildSidebar()
        {
            AppTheme.StylePanel(_sidebar, AppTheme.Activity);
            _sidebar.Padding = new Padding(10, 14, 10, 10);
            _sidebar.Paint += (_, e) =>
            {
                using var pen = new Pen(AppTheme.LineSoft);
                e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
            };

            var navLabel = new Label
            {
                Text = "WORKSPACES",
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(12, 6, 0, 0),
                ForeColor = AppTheme.Faint,
                Font = AppTheme.SmallFont,
                BackColor = Color.Transparent
            };

            _navTuning.Click += (_, _) => ShowWorkspace(WorkspaceId.Tuning);
            _navType.Click += (_, _) => ShowWorkspace(WorkspaceId.Type);
            _navAudio.Click += (_, _) => ShowWorkspace(WorkspaceId.Audio);
            _navNames.Click += (_, _) => ShowWorkspace(WorkspaceId.Names);
            _navMerge.Click += (_, _) => ShowWorkspace(WorkspaceId.Merge);

            _sidebar.Controls.Add(_navMerge);
            _sidebar.Controls.Add(_navNames);
            _sidebar.Controls.Add(_navAudio);
            _sidebar.Controls.Add(_navType);
            _sidebar.Controls.Add(_navTuning);
            _sidebar.Controls.Add(navLabel);
        }

        private async Task BootAsync()
        {
            if (_booted) return;
            _booted = true;
            try
            {
                SetStatus("Checking GTA folder…");
                if (!GTAFolder.UpdateGTAFolder(true))
                {
                    Close();
                    return;
                }

                SetStatus("Loading encryption keys…");
                await Task.Run(() =>
                    GTA5Keys.LoadFromPath(GTAFolder.CurrentGTAFolder, GTAFolder.IsGen9, Settings.Default.Key));

                SetStatus("Scanning game archives…");
                await Task.Run(() =>
                {
                    var rpf = new RpfManager { EnableMods = true };
                    rpf.Init(GTAFolder.CurrentGTAFolder, GTAFolder.IsGen9,
                        SetStatus, msg => SetStatus("ERR: " + msg),
                        rootOnly: false, buildIndex: true);
                    _rpfMan = rpf;
                });

                _tuning = new TuningWorkspace(_rpfMan!, SetStatus);
                _type = new TypeWorkspace(SetStatus);
                _audio = new AudioWorkspace(_rpfMan!, SetStatus);
                _names = new NamesWorkspace(SetStatus);
                _merge = new MergeWorkspace(SetStatus);

                await _tuning.InitializeAsync();
                await _audio.InitializeAsync();

                ShowWorkspace(WorkspaceId.Tuning);
                SetStatus("Ready");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowWorkspace(WorkspaceId id)
        {
            Control? next = id switch
            {
                WorkspaceId.Tuning => _tuning,
                WorkspaceId.Type => _type,
                WorkspaceId.Audio => _audio,
                WorkspaceId.Names => _names,
                WorkspaceId.Merge => _merge,
                _ => null
            };
            if (next == null) return;

            _navTuning.Selected = id == WorkspaceId.Tuning;
            _navType.Selected = id == WorkspaceId.Type;
            _navAudio.Selected = id == WorkspaceId.Audio;
            _navNames.Selected = id == WorkspaceId.Names;
            _navMerge.Selected = id == WorkspaceId.Merge;

            _workspaceTitle.Text = id switch
            {
                WorkspaceId.Tuning => "Door Tuning Editor  ·  NamedTuningArray + model maps",
                WorkspaceId.Type => "Door Type Editor  ·  YTYP specialAttribute",
                WorkspaceId.Audio => "Door Audio Editor  ·  Dat151 REL door audio",
                WorkspaceId.Names => "Nametables Generator  ·  .dat151.nametable",
                WorkspaceId.Merge => "Doortuning Merger  ·  union-merge missing entries",
                _ => ""
            };

            if (_active == next) return;
            _contentHost.SuspendLayout();
            _contentHost.Controls.Clear();
            next.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(next);
            _contentHost.ResumeLayout();
            _active = next;
        }

        public void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(() => _status.Text = text); } catch { /* closing */ }
                return;
            }
            _status.Text = text;
        }

        private enum WorkspaceId { Tuning, Type, Audio, Names, Merge }
    }
}
