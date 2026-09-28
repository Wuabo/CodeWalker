using System;
using System.Globalization;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using SharpDX;

namespace CodeWalker.Project.Panels
{
    public partial class EditYtypArchetypePanel : ProjectPanel
    {
        public ProjectForm ProjectForm;

        private bool populatingui;

        public EditYtypArchetypePanel(ProjectForm owner)
        {
            InitializeComponent();

            ProjectForm = owner;
        }

        public Archetype? CurrentArchetype { get; set; }

        private void EditYtypArchetypePanel_Load(object sender, EventArgs e)
        {
            AssetTypeComboBox.Items.AddRange(Enum.GetNames(typeof(rage__fwArchetypeDef__eAssetType)));
            ExtensionTypeComboBox.Items.Clear();
            ExtensionTypeComboBox.Items.AddRange(ExtensionTransforms.ArchetypeExtensionTypeNames);
            if (ExtensionTypeComboBox.Items.Count > 0)
                ExtensionTypeComboBox.SelectedIndex = 0;
        }

        public void SetArchetype(Archetype archetype)
        {
            CurrentArchetype = archetype;
            Tag = archetype;
            UpdateFormTitle();
            UpdateControls();
        }

        private void UpdateFormTitle()
        {
            Text = CurrentArchetype?.Name ?? "Edit Archetype";
        }

        private void UpdateControls()
        {
            if (CurrentArchetype != null)
            {
                ArchetypeDeleteButton.Enabled = ProjectForm.YtypExistsInProject(CurrentArchetype.Ytyp);
                ArchetypeNameTextBox.Text = CurrentArchetype.Name;
                AssetNameTextBox.Text = CurrentArchetype.AssetName;
                LodDistNumericUpDown.Value = (decimal)CurrentArchetype._BaseArchetypeDef.lodDist;
                HDTextureDistNumericUpDown.Value = (decimal)CurrentArchetype._BaseArchetypeDef.hdTextureDist;
                SpecialAttributeNumericUpDown.Value = CurrentArchetype._BaseArchetypeDef.specialAttribute;
                ArchetypeFlagsTextBox.Text = CurrentArchetype._BaseArchetypeDef.flags.ToString();
                TextureDictTextBox.Text = CurrentArchetype._BaseArchetypeDef.textureDictionary.ToCleanString();
                ClipDictionaryTextBox.Text = CurrentArchetype._BaseArchetypeDef.clipDictionary.ToCleanString();
                DrawableDictionaryTextBox.Text = CurrentArchetype._BaseArchetypeDef.drawableDictionary.ToCleanString();
                PhysicsDictionaryTextBox.Text = CurrentArchetype._BaseArchetypeDef.physicsDictionary.ToCleanString();
                AssetTypeComboBox.Text = CurrentArchetype._BaseArchetypeDef.assetType.ToString();
                BBMinTextBox.Text = FloatUtil.GetVector3String(CurrentArchetype._BaseArchetypeDef.bbMin);
                BBMaxTextBox.Text = FloatUtil.GetVector3String(CurrentArchetype._BaseArchetypeDef.bbMax);
                BSCenterTextBox.Text = FloatUtil.GetVector3String(CurrentArchetype._BaseArchetypeDef.bsCentre);
                BSRadiusTextBox.Text = FloatUtil.ToString(CurrentArchetype._BaseArchetypeDef.bsRadius);

                if (CurrentArchetype is MloArchetype MloArchetype)
                {
                    if (!TabControl.TabPages.Contains(MloArchetypeTabPage))
                    {
                        TabControl.TabPages.Add(MloArchetypeTabPage);
                    }

                    //MloInstanceData mloinstance = ProjectForm.TryGetMloInstance(MloArchetype);
                    //nothing to see here right now
                }
                else TabControl.TabPages.Remove(MloArchetypeTabPage);



                if (CurrentArchetype is TimeArchetype TimeArchetype)
                {
                    if (!TabControl.TabPages.Contains(TimeArchetypeTabPage))
                    {
                        TabControl.TabPages.Add(TimeArchetypeTabPage);
                    }

                    TimeFlagsTextBox.Text = TimeArchetype.TimeFlags.ToString();

                }
                else TabControl.TabPages.Remove(TimeArchetypeTabPage);

                UpdateExtensionsUI();
            }
            else
            {
                UpdateExtensionsUI();
            }
        }

        private void UpdateExtensionsUI()
        {
            populatingui = true;
            ExtensionsListBox.BeginUpdate();
            ExtensionsListBox.Items.Clear();
            ExtensionPropertyGrid.SelectedObject = null;
            ExtensionDeleteButton.Enabled = false;

            var exts = CurrentArchetype?.Extensions;
            int count = (exts != null) ? exts.Length : 0;
            ExtensionsCountLabel.Text = "Extensions: " + count;

            if (exts != null)
            {
                for (int i = 0; i < exts.Length; i++)
                {
                    ExtensionsListBox.Items.Add(GetExtensionDisplayName(exts[i], i));
                }
            }

            ExtensionsListBox.EndUpdate();
            populatingui = false;
        }

        private static string GetExtensionDisplayName(MetaWrapper? ext, int index)
        {
            if (ext == null) return "[" + index + "] (null)";

            string type = ext.GetType().Name;
            if (type.StartsWith("MCExtensionDef", StringComparison.Ordinal))
                type = type.Substring("MCExtensionDef".Length);
            else if (type.StartsWith("Mrage__", StringComparison.Ordinal))
                type = type.Substring("Mrage__".Length);
            else if (type.StartsWith("MC", StringComparison.Ordinal))
                type = type.Substring(2);

            string name = ext.Name;
            if (string.IsNullOrEmpty(name) || string.Equals(name, type, StringComparison.Ordinal))
                return "[" + index + "] " + type;

            return "[" + index + "] " + type + ": " + name;
        }

        private void MarkCurrentYtypChanged()
        {
            if (CurrentArchetype?.Ytyp == null || ProjectForm == null) return;

            var ytyp = CurrentArchetype.Ytyp;
            if (!ProjectForm.YtypExistsInProject(ytyp))
            {
                ProjectForm.AddYtypToProject(ytyp);
            }
            else
            {
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void ExtensionsListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (populatingui) return;

            int index = ExtensionsListBox.SelectedIndex;
            var exts = CurrentArchetype?.Extensions;
            if ((exts == null) || (index < 0) || (index >= exts.Length))
            {
                ExtensionPropertyGrid.SelectedObject = null;
                ExtensionDeleteButton.Enabled = false;
                return;
            }

            ExtensionPropertyGrid.SelectedObject = exts[index];
            ExtensionDeleteButton.Enabled = true;
            SelectExtensionInWorld(exts[index]);
        }

        public void SyncExtensionSelection(MetaWrapper? extension, bool selectInWorld, Archetype? worldArchetype = null)
        {
            if (extension == null || CurrentArchetype == null) return;

            var exts = CurrentArchetype.Extensions;
            int index = (exts != null) ? Array.IndexOf(exts, extension) : -1;

            // Viewport may hold a different archetype instance than the panel; match by index.
            if ((index < 0) && (worldArchetype?.Extensions != null))
            {
                int worldIndex = Array.IndexOf(worldArchetype.Extensions, extension);
                if ((worldIndex >= 0) &&
                    (exts != null) &&
                    (worldIndex < exts.Length) &&
                    (CurrentArchetype._BaseArchetypeDef.name == worldArchetype._BaseArchetypeDef.name))
                {
                    index = worldIndex;
                    extension = exts[index];
                }
            }

            if ((index < 0) || (exts == null) || (index >= exts.Length)) return;

            if (TabControl.TabPages.Contains(ExtensionsTabPage) && (TabControl.SelectedTab != ExtensionsTabPage))
            {
                TabControl.SelectedTab = ExtensionsTabPage;
            }

            if (ExtensionsListBox.Items.Count != exts.Length)
            {
                UpdateExtensionsUI();
            }

            if (ExtensionsListBox.SelectedIndex != index)
            {
                populatingui = true;
                ExtensionsListBox.SelectedIndex = index;
                populatingui = false;
            }

            ExtensionPropertyGrid.SelectedObject = extension;
            ExtensionDeleteButton.Enabled = true;
            ExtensionPropertyGrid.Refresh();

            if (selectInWorld)
                SelectExtensionInWorld(extension);
        }

        private void SelectExtensionInWorld(MetaWrapper? extension)
        {
            if ((CurrentArchetype == null) || (extension == null) || (ProjectForm?.WorldForm == null)) return;
            ProjectForm.WorldForm.SelectArchetypeExtension(CurrentArchetype, extension);
        }

        private void ExtensionAddButton_Click(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ExtensionTypeComboBox.SelectedItem == null) return;

            string typeName = ExtensionTypeComboBox.SelectedItem.ToString() ?? string.Empty;
            var nameHash = CurrentArchetype._BaseArchetypeDef.name;
            var ext = ExtensionTransforms.CreateArchetypeExtension(typeName, nameHash);
            if (ext == null) return;

            lock (ProjectForm.ProjectSyncRoot)
            {
                var list = new System.Collections.Generic.List<MetaWrapper>();
                if (CurrentArchetype.Extensions != null)
                    list.AddRange(CurrentArchetype.Extensions);
                list.Add(ext);
                CurrentArchetype.Extensions = list.ToArray();
            }

            MarkCurrentYtypChanged();
            UpdateExtensionsUI();

            if (ExtensionsListBox.Items.Count > 0)
            {
                ExtensionsListBox.SelectedIndex = ExtensionsListBox.Items.Count - 1;
            }
        }

        private void ExtensionDeleteButton_Click(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;

            int index = ExtensionsListBox.SelectedIndex;
            var exts = CurrentArchetype.Extensions;
            if ((exts == null) || (index < 0) || (index >= exts.Length)) return;

            var ext = exts[index];
            string label = GetExtensionDisplayName(ext, index);
            if (MessageBox.Show(
                    "Delete this archetype extension?\n" + label + "\n\nThis operation cannot be undone. Continue?",
                    "Confirm delete",
                    MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }

            lock (ProjectForm.ProjectSyncRoot)
            {
                var list = new System.Collections.Generic.List<MetaWrapper>(exts.Length);
                for (int i = 0; i < exts.Length; i++)
                {
                    if (i != index)
                        list.Add(exts[i]);
                }
                CurrentArchetype.Extensions = list.ToArray();
            }

            MarkCurrentYtypChanged();
            UpdateExtensionsUI();
        }

        private void ExtensionPropertyGrid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            if (populatingui || CurrentArchetype == null) return;
            MarkCurrentYtypChanged();

            int index = ExtensionsListBox.SelectedIndex;
            if ((index >= 0) && (index < ExtensionsListBox.Items.Count) && (CurrentArchetype.Extensions != null) && (index < CurrentArchetype.Extensions.Length))
            {
                populatingui = true;
                ExtensionsListBox.Items[index] = GetExtensionDisplayName(CurrentArchetype.Extensions[index], index);
                populatingui = false;

                var ext = CurrentArchetype.Extensions[index];
                var wf = ProjectForm?.WorldForm;
                if (wf != null)
                {
                    var sel = wf.CurrentMapSelection;
                    if ((sel.ArchetypeExtension == ext) || (sel.EntityExtension == ext))
                    {
                        if (ExtensionTransforms.TryGetOffsetPosition(ext, out var local))
                        {
                            var world = ExtensionTransforms.LocalToWorld(sel.EntityDef, local);
                            wf.SetWidgetPosition(world);
                        }
                        if (ExtensionTransforms.TryGetOffsetRotation(ext, out var localRot))
                        {
                            var worldRot = ExtensionTransforms.LocalToWorld(sel.EntityDef, localRot);
                            wf.SetWidgetRotation(worldRot);
                        }
                    }
                }
            }
        }

        private void ArchetypeFlagsTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (populatingui || CurrentArchetype == null) return;
            if (CurrentArchetype == null) return;
            uint flags = 0;
            uint.TryParse(ArchetypeFlagsTextBox.Text, out flags);
            populatingui = true;
            for (int i = 0; i < EntityFlagsCheckedListBox.Items.Count; i++)
            {
                var c = ((flags & (1u << i)) > 0);
                EntityFlagsCheckedListBox.SetItemCheckState(i, c ? CheckState.Checked : CheckState.Unchecked);
            }
            populatingui = false;
            lock (ProjectForm.ProjectSyncRoot)
            {
                if (CurrentArchetype._BaseArchetypeDef.flags != flags)
                {
                    CurrentArchetype._BaseArchetypeDef.flags = flags;
                    ProjectForm.SetYtypHasChanged(true);
                }
            }
        }

        private void ArchetypeFlagsCheckedListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (populatingui || CurrentArchetype == null) return;
            if (CurrentArchetype == null) return;
            uint flags = 0;
            for (int i = 0; i < EntityFlagsCheckedListBox.Items.Count; i++)
            {
                if (e.Index == i)
                {
                    if (e.NewValue == CheckState.Checked)
                    {
                        flags += (uint)(1 << i);
                    }
                }
                else
                {
                    if (EntityFlagsCheckedListBox.GetItemChecked(i))
                    {
                        flags += (uint)(1 << i);
                    }
                }
            }
            populatingui = true;
            ArchetypeFlagsTextBox.Text = flags.ToString();
            populatingui = false;
            lock (ProjectForm.ProjectSyncRoot)
            {
                if (CurrentArchetype._BaseArchetypeDef.flags != flags)
                {
                    CurrentArchetype._BaseArchetypeDef.flags = flags;
                    ProjectForm.SetYtypHasChanged(true);
                }
            }
        }

        private void ArchetypeNameTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ProjectForm == null) return;

            var hash = 0u;
            if (!uint.TryParse(ArchetypeNameTextBox.Text, out hash))//don't re-hash hashes
            {
                hash = JenkHash.GenHash(ArchetypeNameTextBox.Text);
            }

            if (CurrentArchetype._BaseArchetypeDef.name != hash)
            {
                CurrentArchetype._BaseArchetypeDef.name = hash;
                UpdateFormTitle();

                TreeNode? tn = ProjectForm.ProjectExplorer?.FindArchetypeTreeNode(CurrentArchetype);
                if (tn != null)
                    tn.Text = ArchetypeNameTextBox.Text ?? "0"; // using the text box text because the name may not be in the gfc.

                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void AssetNameTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ProjectForm == null) return;

            var hash = 0u;
            if (!uint.TryParse(AssetNameTextBox.Text, out hash))//don't re-hash hashes
            {
                hash = JenkHash.GenHash(AssetNameTextBox.Text);
            }

            if (CurrentArchetype._BaseArchetypeDef.assetName != hash)
            {
                CurrentArchetype._BaseArchetypeDef.assetName = hash;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void TextureDictTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ProjectForm == null) return;

            lock (ProjectForm.ProjectSyncRoot)
            {
                // Embedded...
                if (TextureDictTextBox.Text == ArchetypeNameTextBox.Text)
                {
                    TextureDictHashLabel.Text = "Embedded";
                    CurrentArchetype._BaseArchetypeDef.textureDictionary = CurrentArchetype._BaseArchetypeDef.name;
                    return;
                }

                var hash = 0u;
                if (!uint.TryParse(TextureDictTextBox.Text, out hash))//don't re-hash hashes
                {
                    hash = JenkHash.GenHash(TextureDictTextBox.Text);
                }

                if (CurrentArchetype._BaseArchetypeDef.textureDictionary != hash)
                {
                    CurrentArchetype._BaseArchetypeDef.textureDictionary = hash;
                    var ytd = ProjectForm.GameFileCache.GetYtd(hash);
                    if (ytd == null)
                    {
                        TextureDictHashLabel.Text = "# " + hash.ToString() + " (invalid)";
                        ProjectForm.SetYtypHasChanged(true);
                        return;
                    }
                    ProjectForm.SetYtypHasChanged(true);
                }
                TextureDictHashLabel.Text = "# " + hash.ToString();
            }
        }

        private void ClipDictionaryTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ProjectForm == null) return;

            var hash = 0u;
            if (!uint.TryParse(ClipDictionaryTextBox.Text, out hash))//don't re-hash hashes
            {
                hash = JenkHash.GenHash(ClipDictionaryTextBox.Text);
            }

            if (CurrentArchetype._BaseArchetypeDef.clipDictionary != hash)
            {
                CurrentArchetype._BaseArchetypeDef.clipDictionary = hash;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void DrawableDictionaryTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ProjectForm == null) return;

            lock (ProjectForm.ProjectSyncRoot)
            {
                var hash = 0u;
                if (!uint.TryParse(DrawableDictionaryTextBox.Text, out hash))//don't re-hash hashes
                {
                    hash = JenkHash.GenHash(DrawableDictionaryTextBox.Text);
                }

                if (CurrentArchetype._BaseArchetypeDef.drawableDictionary != hash)
                {
                    CurrentArchetype._BaseArchetypeDef.drawableDictionary = hash;
                    var ydd = ProjectForm.GameFileCache.GetYdd(hash);
                    if (ydd == null)
                    {
                        DrawableDictHashLabel.Text = "# " + hash.ToString() + " (invalid)";
                        ProjectForm.SetYtypHasChanged(true);
                        return;
                    }
                    ProjectForm.SetYtypHasChanged(true);
                }
                DrawableDictHashLabel.Text = "# " + hash.ToString();
            }
        }

        private void PhysicsDictionaryTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (ProjectForm == null) return;

            lock (ProjectForm.ProjectSyncRoot)
            {
                // Embedded...
                if (PhysicsDictionaryTextBox.Text == ArchetypeNameTextBox.Text)
                {
                    PhysicsDictHashLabel.Text = "Embedded";
                    CurrentArchetype._BaseArchetypeDef.physicsDictionary = CurrentArchetype._BaseArchetypeDef.name;
                    return;
                }

                var hash = 0u;
                if (!uint.TryParse(PhysicsDictionaryTextBox.Text, out hash))//don't re-hash hashes
                {
                    hash = JenkHash.GenHash(PhysicsDictionaryTextBox.Text);
                }

                if (CurrentArchetype._BaseArchetypeDef.physicsDictionary != hash)
                {
                    CurrentArchetype._BaseArchetypeDef.physicsDictionary = hash;
                    var ybn = ProjectForm.GameFileCache.GetYbn(hash);
                    if (ybn == null)
                    {
                        PhysicsDictHashLabel.Text = "# " + hash.ToString() + " (invalid)";
                        ProjectForm.SetYtypHasChanged(true);
                        return;
                    }
                    ProjectForm.SetYtypHasChanged(true);
                }
                PhysicsDictHashLabel.Text = "# " + hash.ToString();
            }
        }

        private void LodDistNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            var loddist = (float)LodDistNumericUpDown.Value;
            if (!MathUtil.NearEqual(loddist, CurrentArchetype._BaseArchetypeDef.lodDist))
            {
                CurrentArchetype._BaseArchetypeDef.lodDist = loddist;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void HDTextureDistNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            var hddist = (float)HDTextureDistNumericUpDown.Value;
            if (!MathUtil.NearEqual(hddist, CurrentArchetype._BaseArchetypeDef.hdTextureDist))
            {
                CurrentArchetype._BaseArchetypeDef.hdTextureDist = hddist;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void SpecialAttributeNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            var att = (uint)SpecialAttributeNumericUpDown.Value;
            if (CurrentArchetype._BaseArchetypeDef.specialAttribute != att)
            {
                CurrentArchetype._BaseArchetypeDef.specialAttribute = att;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void BBMinTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            Vector3 min = FloatUtil.ParseVector3String(BBMinTextBox.Text);
            if (CurrentArchetype._BaseArchetypeDef.bbMin != min)
            {
                CurrentArchetype._BaseArchetypeDef.bbMin = min;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void BBMaxTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            Vector3 max = FloatUtil.ParseVector3String(BBMaxTextBox.Text);

            if (CurrentArchetype._BaseArchetypeDef.bbMax != max)
            {
                CurrentArchetype._BaseArchetypeDef.bbMax = max;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void BSCenterTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            Vector3 c = FloatUtil.ParseVector3String(BSCenterTextBox.Text);

            if (CurrentArchetype._BaseArchetypeDef.bsCentre != c)
            {
                CurrentArchetype._BaseArchetypeDef.bsCentre = c;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void BSRadiusTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (FloatUtil.TryParse(BSRadiusTextBox.Text, out float f))
            {
                if (!MathUtil.NearEqual(CurrentArchetype._BaseArchetypeDef.bsRadius, f))
                {
                    CurrentArchetype._BaseArchetypeDef.bsRadius = f;
                    ProjectForm.SetYtypHasChanged(true);
                }
            }
            else
            {
                CurrentArchetype._BaseArchetypeDef.bsRadius = 0f;
                ProjectForm.SetYtypHasChanged(true);
            }
        }

        private void DeleteArchetypeButton_Click(object sender, EventArgs e)
        {
            ProjectForm.SetProjectItem(CurrentArchetype);
            ProjectForm.DeleteArchetype();
        }

        private void MloUpdatePortalCountsButton_Click(object sender, EventArgs e)
        {
            var mlo = CurrentArchetype as MloArchetype;
            if (mlo == null) return;

            mlo.UpdatePortalCounts();
        }

        private void TimeFlagsTextBox_TextChanged(object sender, EventArgs e)
        {
            if (CurrentArchetype == null) return;
            if (populatingui || CurrentArchetype == null) return;
            if (CurrentArchetype == null) return;
            if (CurrentArchetype is TimeArchetype TimeArchetype)
            {
                uint flags = 0;
                uint.TryParse(TimeFlagsTextBox.Text, out flags);
                populatingui = true;
                for (int i = 0; i < TimeFlagsCheckedListBox.Items.Count; i++)
                {
                    var c = ((flags & (1u << i)) > 0);
                    TimeFlagsCheckedListBox.SetItemCheckState(i, c ? CheckState.Checked : CheckState.Unchecked);
                }
                populatingui = false;
                lock (ProjectForm.ProjectSyncRoot)
                {
                    if (TimeArchetype.TimeFlags != flags)
                    {
                        TimeArchetype.SetTimeFlags(flags);
                        ProjectForm.SetYtypHasChanged(true);
                    }
                }
            }

        }

        private void TimeFlagsCheckedListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (populatingui || CurrentArchetype == null) return;
            if (CurrentArchetype == null) return;
            if (CurrentArchetype is TimeArchetype TimeArchetype)
            {
                uint flags = 0;
                for (int i = 0; i < TimeFlagsCheckedListBox.Items.Count; i++)
                {
                    if (e.Index == i)
                    {
                        if (e.NewValue == CheckState.Checked)
                        {
                            flags += (uint)(1 << i);
                        }
                    }
                    else
                    {
                        if (TimeFlagsCheckedListBox.GetItemChecked(i))
                        {
                            flags += (uint)(1 << i);
                        }
                    }
                }
                populatingui = true;
                TimeFlagsTextBox.Text = flags.ToString();
                populatingui = false;
                lock (ProjectForm.ProjectSyncRoot)
                {
                    if (TimeArchetype.TimeFlags != flags)
                    {
                        TimeArchetype.SetTimeFlags(flags);
                        ProjectForm.SetYtypHasChanged(true);
                    }
                }
            }
        }
    }
}
