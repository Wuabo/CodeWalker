using CodeWalker.GameFiles;
using CodeWalker.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodeWalker.Forms
{
    public class GfxForm : Form
    {
        private readonly ExploreForm? exploreForm;
        private readonly TreeView infoTree;
        private readonly ListView ytdList;
        private readonly ListView textureList;
        private readonly PictureBox previewBox;
        private readonly ToolStripStatusLabel statusLabel;
        private readonly OpenFileDialog openYtdDialog;

        private string fileName = "";
        private string filePath = "";
        private RpfFileEntry? rpfFileEntry;
        private GfxMovieInfo? info;
        private readonly List<LoadedYtd> loadedYtds = new();
        private Texture? currentTexture;

        private sealed class LoadedYtd
        {
            public string Name { get; init; } = "";
            public string Source { get; init; } = "";
            public YtdFile File { get; init; } = null!;
        }

        public GfxForm(ExploreForm? exploreForm = null)
        {
            this.exploreForm = exploreForm;
            Text = "Scaleform GFX - CodeWalker";
            Width = 1000;
            Height = 700;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 500);

            var toolStrip = new ToolStrip { Dock = DockStyle.Top };
            var addYtdButton = new ToolStripButton("Add YTD...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var removeYtdButton = new ToolStripButton("Remove YTD") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var reloadCompanionButton = new ToolStripButton("Reload companion YTD") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            addYtdButton.Click += (_, _) => AddYtdDialog();
            removeYtdButton.Click += (_, _) => RemoveSelectedYtd();
            reloadCompanionButton.Click += (_, _) => TryLoadCompanionYtd(force: true);
            toolStrip.Items.Add(addYtdButton);
            toolStrip.Items.Add(removeYtdButton);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(reloadCompanionButton);

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 340
            };

            infoTree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            };

            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 220
            };

            var ytdPanel = new Panel { Dock = DockStyle.Fill };
            var ytdLabel = new Label
            {
                Text = "Loaded texture dictionaries (.ytd)",
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft
            };
            ytdList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            ytdList.Columns.Add("YTD", 220);
            ytdList.Columns.Add("Textures", 70);
            ytdList.Columns.Add("Source", 280);
            ytdList.SelectedIndexChanged += (_, _) => RefreshTextureList();
            ytdPanel.Controls.Add(ytdList);
            ytdPanel.Controls.Add(ytdLabel);

            var texSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 280
            };

            textureList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            textureList.Columns.Add("Texture", 180);
            textureList.Columns.Add("Size", 90);
            textureList.SelectedIndexChanged += (_, _) => ShowSelectedTexture();

            var previewPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(32, 32, 32) };
            previewBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Color.Transparent
            };
            previewPanel.Controls.Add(previewBox);

            texSplit.Panel1.Controls.Add(textureList);
            texSplit.Panel2.Controls.Add(previewPanel);

            rightSplit.Panel1.Controls.Add(ytdPanel);
            rightSplit.Panel2.Controls.Add(texSplit);

            mainSplit.Panel1.Controls.Add(infoTree);
            mainSplit.Panel2.Controls.Add(rightSplit);

            var status = new StatusStrip { Dock = DockStyle.Bottom };
            statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(statusLabel);

            Controls.Add(mainSplit);
            Controls.Add(status);
            Controls.Add(toolStrip);

            openYtdDialog = new OpenFileDialog
            {
                Filter = "YTD Texture Dictionary|*.ytd|All Files|*.*",
                Multiselect = true,
                Title = "Add external YTD for Scaleform preview"
            };
        }

        public void LoadGfx(string name, string path, byte[] data, RpfFileEntry? entry)
        {
            fileName = name;
            filePath = path;
            rpfFileEntry = entry;
            Text = name + " - Scaleform GFX - CodeWalker by dexyfex";

            try
            {
                info = GfxFile.Inspect(data);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Unable to inspect GFX");
                info = null;
            }

            BuildInfoTree();
            loadedYtds.Clear();
            RefreshYtdList();
            TryLoadCompanionYtd(force: false);
            UpdateStatus();
        }

        private void BuildInfoTree()
        {
            infoTree.BeginUpdate();
            infoTree.Nodes.Clear();
            if (info == null)
            {
                infoTree.Nodes.Add("Failed to inspect movie");
                infoTree.EndUpdate();
                return;
            }

            var root = infoTree.Nodes.Add($"{fileName} ({info.Signature} v{info.Version})");
            root.Nodes.Add($"Size: {info.ActualLength} bytes (declared {info.FileLength})");
            root.Nodes.Add($"Frames: {info.FrameCount} @ {info.FrameRate:0.##} fps");
            root.Nodes.Add($"Shapes: {info.ShapeCount}, Sprites: {info.SpriteCount}, EditText: {info.EditTextCount}");
            root.Nodes.Add($"PlaceObject: {info.PlaceObjectCount}, ExternalImage tags: {info.ExternalImageTagCount}");
            root.Nodes.Add($"Companion YTD suggestion: {GfxFile.GetCompanionYtdFileName(fileName)}");

            if (info.Warnings.Count > 0)
            {
                var w = root.Nodes.Add($"Notes ({info.Warnings.Count})");
                foreach (var warning in info.Warnings)
                    w.Nodes.Add(warning);
            }

            if (info.Exports.Count > 0)
            {
                var exp = root.Nodes.Add($"Exported symbols ({info.Exports.Count})");
                foreach (var e in info.Exports.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    exp.Nodes.Add(e.ToString());
            }

            if (info.Imports.Count > 0)
            {
                var imp = root.Nodes.Add($"Imports ({info.Imports.Count})");
                foreach (var i in info.Imports.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                    imp.Nodes.Add(i);
            }

            if (info.ImageRefs.Count > 0)
            {
                var imgs = root.Nodes.Add($"img:// refs ({info.ImageRefs.Count})");
                foreach (var r in info.ImageRefs)
                    imgs.Nodes.Add(r);
            }

            if (info.TagCounts.Count > 0)
            {
                var tags = root.Nodes.Add("Tag counts");
                foreach (var kv in info.TagCounts.OrderBy(k => k.Key))
                    tags.Nodes.Add($"{TagName(kv.Key)} ({kv.Key}): {kv.Value}");
            }

            root.Expand();
            infoTree.EndUpdate();
        }

        private static string TagName(int code) => code switch
        {
            0 => "End",
            1 => "ShowFrame",
            2 => "DefineShape",
            9 => "SetBackgroundColor",
            12 => "DoAction",
            26 => "PlaceObject2",
            32 => "DefineShape3",
            37 => "DefineEditText",
            39 => "DefineSprite",
            56 => "ExportAssets",
            57 => "ImportAssets",
            59 => "DoInitAction",
            69 => "FileAttributes",
            70 => "PlaceObject3",
            71 => "ImportAssets2",
            74 => "CSMTextSettings",
            77 => "Metadata",
            83 => "DefineShape4",
            1000 => "ScaleformExt1000",
            1009 => "ScaleformExt1009",
            _ => $"Tag{code}"
        };

        private void TryLoadCompanionYtd(bool force)
        {
            var companionName = GfxFile.GetCompanionYtdFileName(fileName);
            if (!force && loadedYtds.Any(y => y.Name.Equals(companionName, StringComparison.OrdinalIgnoreCase)))
                return;

            // Filesystem next to the gfx
            if (!string.IsNullOrEmpty(filePath))
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    foreach (var candidate in GetCompanionCandidates(dir, fileName))
                    {
                        if (File.Exists(candidate) && TryAddYtdFromPath(candidate))
                            return;
                    }
                }
            }

            // Same RPF folder
            var parent = rpfFileEntry?.Parent;
            if (parent?.Files != null)
            {
                foreach (var name in GetCompanionYtdNames(fileName))
                {
                    var entry = parent.Files.FirstOrDefault(f =>
                        f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (entry != null && TryAddYtdFromRpf(entry))
                        return;
                }
            }

            if (force)
                SetStatus($"No companion YTD found for {companionName}. Use Add YTD...");
        }

        private static IEnumerable<string> GetCompanionYtdNames(string gfxName)
        {
            var baseName = Path.GetFileNameWithoutExtension(gfxName);
            yield return baseName + ".ytd";
            yield return baseName + "_pc.ytd";
            yield return baseName + "_gen9.ytd";
        }

        private static IEnumerable<string> GetCompanionCandidates(string dir, string gfxName)
        {
            foreach (var name in GetCompanionYtdNames(gfxName))
                yield return Path.Combine(dir, name);
        }

        private void AddYtdDialog()
        {
            if (openYtdDialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (var path in openYtdDialog.FileNames)
                TryAddYtdFromPath(path);
        }

        private void RemoveSelectedYtd()
        {
            if (ytdList.SelectedItems.Count == 0) return;
            if (ytdList.SelectedItems[0].Tag is not LoadedYtd item) return;
            loadedYtds.Remove(item);
            RefreshYtdList();
            UpdateStatus();
        }

        private bool TryAddYtdFromPath(string path)
        {
            try
            {
                var data = File.ReadAllBytes(path);
                var name = Path.GetFileName(path);
                return AddYtdData(name, path, data);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, path + ":\n" + ex.Message, "Unable to load YTD");
                return false;
            }
        }

        private bool TryAddYtdFromRpf(RpfFileEntry entry)
        {
            try
            {
                var data = entry.File?.ExtractFile(entry);
                if (data == null)
                {
                    MessageBox.Show(this, "Could not extract " + entry.Name, "Unable to load YTD");
                    return false;
                }
                return AddYtdData(entry.Name, entry.Path, data);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, entry.Name + ":\n" + ex.Message, "Unable to load YTD");
                return false;
            }
        }

        private bool AddYtdData(string name, string source, byte[] data)
        {
            if (loadedYtds.Any(y => y.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                    y.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
            {
                SetStatus(name + " already loaded.");
                return true;
            }

            // Resource YTDs need RSC header handling like ExploreForm.CreateFileEntry
            RpfFileEntry entry;
            uint rsc7 = (data.Length > 4) ? BitConverter.ToUInt32(data, 0) : 0;
            if (rsc7 == 0x37435352)
            {
                entry = RpfFile.CreateResourceFileEntry(ref data, 0);
                data = ResourceBuilder.Decompress(data);
            }
            else
            {
                var be = new RpfBinaryFileEntry
                {
                    FileSize = (uint)data.Length,
                    FileUncompressedSize = (uint)data.Length
                };
                entry = be;
            }
            entry.Name = name;
            entry.NameLower = name.ToLowerInvariant();
            entry.Path = source;

            var ytd = RpfFile.GetFile<YtdFile>(entry, data);
            if (ytd?.TextureDict == null)
            {
                MessageBox.Show(this, name + " did not contain a texture dictionary.", "Unable to load YTD");
                return false;
            }

            loadedYtds.Add(new LoadedYtd { Name = name, Source = source, File = ytd });
            RefreshYtdList();
            // select the newly added one
            foreach (ListViewItem lvi in ytdList.Items)
            {
                if (lvi.Tag is LoadedYtd ly && ly.Name == name && ly.Source == source)
                {
                    lvi.Selected = true;
                    break;
                }
            }
            UpdateStatus();
            return true;
        }

        private void RefreshYtdList()
        {
            ytdList.BeginUpdate();
            ytdList.Items.Clear();
            foreach (var y in loadedYtds)
            {
                int count = y.File.TextureDict?.Textures?.data_items?.Length ?? 0;
                var lvi = ytdList.Items.Add(y.Name);
                lvi.SubItems.Add(count.ToString());
                lvi.SubItems.Add(y.Source);
                lvi.Tag = y;
            }
            ytdList.EndUpdate();

            if (ytdList.Items.Count > 0 && ytdList.SelectedItems.Count == 0)
                ytdList.Items[0].Selected = true;
            else
                RefreshTextureList();
        }

        private void RefreshTextureList()
        {
            textureList.BeginUpdate();
            textureList.Items.Clear();
            previewBox.Image = null;
            currentTexture = null;

            LoadedYtd? selected = null;
            if (ytdList.SelectedItems.Count > 0)
                selected = ytdList.SelectedItems[0].Tag as LoadedYtd;

            var texs = selected?.File.TextureDict?.Textures?.data_items;
            if (texs != null)
            {
                foreach (var tex in texs.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var lvi = textureList.Items.Add(tex.Name ?? "(unnamed)");
                    lvi.SubItems.Add($"{tex.Width} x {tex.Height}");
                    lvi.Tag = tex;
                }
            }
            textureList.EndUpdate();

            if (textureList.Items.Count > 0)
                textureList.Items[0].Selected = true;
        }

        private void ShowSelectedTexture()
        {
            if (textureList.SelectedItems.Count == 0)
            {
                previewBox.Image = null;
                currentTexture = null;
                return;
            }

            if (textureList.SelectedItems[0].Tag is not Texture tex)
                return;

            currentTexture = tex;
            try
            {
                var pixels = DDSIO.GetPixels(tex, 0);
                int w = tex.Width;
                int h = tex.Height;
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                if (pixels != null)
                {
                    var rect = new Rectangle(0, 0, w, h);
                    var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
                    Marshal.Copy(pixels, 0, bmpData.Scan0, Math.Min(pixels.Length, bmpData.Stride * bmp.Height));
                    bmp.UnlockBits(bmpData);
                }
                previewBox.Image?.Dispose();
                previewBox.Image = bmp;
                SetStatus($"{tex.Name} — {w}x{h}");
            }
            catch (Exception ex)
            {
                previewBox.Image = null;
                SetStatus("Error decoding texture: " + ex.Message);
            }
        }

        private void UpdateStatus()
        {
            int texCount = loadedYtds.Sum(y => y.File.TextureDict?.Textures?.data_items?.Length ?? 0);
            SetStatus($"{fileName}: {loadedYtds.Count} YTD(s), {texCount} texture(s). Layout is AS/runtime — textures shown from loaded dictionaries.");
        }

        private void SetStatus(string text) => statusLabel.Text = text;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            previewBox.Image?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
