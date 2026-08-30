using System.IO.Compression;

namespace Add_Stream_Deck_Icon;

public partial class Form1 : Form
{
    private readonly List<Manifest> _allPacks = new();
    private readonly List<PackIcon> _imagesToAdd = new();
    private readonly List<int> _existingIcons = new();
    private readonly string _streamDeckPacksPath = PackPaths.IconPacksRoot;

    private bool _changesMade;
    private bool _existingPack;
    private Manifest? _importWaiting;
    private string? _importWaitingTempPath;

    private readonly AppUpdateService _updater = new();
    private bool _updateCheckRunning;

    public Form1()
    {
        InitializeComponent();

        Text = $"Stream Deck Icon Manager v{AppUpdateService.CurrentVersion}";
        labelVersion.Text = $"v{AppUpdateService.CurrentVersion}";

        if (!Directory.Exists(_streamDeckPacksPath))
        {
            MessageBox.Show("Could not locate Stream Deck icon pack directory");
            Environment.Exit(0);
            return;
        }

        foreach (var dir in Directory.GetDirectories(_streamDeckPacksPath))
        {
            if (PackPaths.IsCustomPackDirectory(dir))
                _allPacks.Add(new Manifest(dir));
        }

        UpdateListBoxText();
        Shown += async (_, _) => await CheckForUpdatesAsync(silentWhenCurrent: true);
    }

    private Manifest? SelectedPack =>
        listBox1.SelectedIndex >= 0 && listBox1.SelectedIndex < _allPacks.Count
            ? _allPacks[listBox1.SelectedIndex]
            : null;

    private bool EnsurePackSelected()
    {
        if (SelectedPack is not null)
            return true;

        MessageBox.Show("Create a pack first!");
        return false;
    }

    private void AddIconWithDefaultMetadata(string fileName) =>
        _imagesToAdd.Add(PackIcon.FromFile(fileName));

    private void buttonBrowse_Click(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Image files (*.jpg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        foreach (var fileName in dialog.FileNames)
            AddIconWithDefaultMetadata(fileName);

        ListNewIcons();
        label5.Visible = false;
    }

    private void textBox1_DragOver(object sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Link
            : DragDropEffects.None;

    private void textBox1_DragDrop(object sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] droppedFiles)
            return;

        var addedFiles = new List<string>();
        foreach (var file in droppedFiles)
        {
            if (PackPaths.IsSupportedImageExtension(file))
                addedFiles.Add(file);
            else
                MessageBox.Show("Must Be PNG or JPG File Format");
        }

        if (addedFiles.Count == 0)
            return;

        foreach (var fileName in addedFiles)
            AddIconWithDefaultMetadata(fileName);

        ListNewIcons();
        label5.Visible = false;
    }

    private void buttonAddIcons_Click(object sender, EventArgs e)
    {
        if (_imagesToAdd.Count == 0)
        {
            MessageBox.Show("You don't have any files selected.", "Error");
            return;
        }

        if (!EnsurePackSelected())
            return;

        AddIconsVisible(true);
        AskForIconInfo(_imagesToAdd[0]);
    }

    private void buttonOK_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        if (_existingIcons.Count > 0)
        {
            ApplyIconInfo(pack.Icons[_existingIcons[0]]);
            _existingIcons.RemoveAt(0);

            if (_existingIcons.Count > 0)
            {
                var next = pack.Icons[_existingIcons[0]];
                pictureBox1.Image = next.Picture;
                textBox3.Text = next.Name;
                textBox4.Text = string.Join(',', next.Tags);
                textBox3.Focus();
            }
            else
            {
                AddIconsVisible(false);
                _existingIcons.Clear();
                StartOver(false);
                ListExistingIcons();
            }

            return;
        }

        ApplyIconInfo(_imagesToAdd[0]);
        _imagesToAdd.RemoveAt(0);

        if (_imagesToAdd.Count > 0)
        {
            AskForIconInfo(_imagesToAdd[0]);
            textBox3.Focus();
        }
        else
        {
            AddIconsVisible(false);
            StartOver(true);
        }
    }

    private void buttonSkip_Click(object sender, EventArgs e)
    {
        textBox3.Clear();
        textBox4.Clear();

        while (_imagesToAdd.Count > 0)
        {
            ApplyIconInfo(_imagesToAdd[0]);
            _imagesToAdd.RemoveAt(0);
        }

        AddIconsVisible(false);
        StartOver(true);
    }

    private void buttonDeleteAll_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        if (MessageBox.Show(
                "Are you sure you want to DELETE ALL ICONS from pack:" + pack.Name,
                "WARNING",
                MessageBoxButtons.YesNoCancel) != DialogResult.Yes)
            return;

        _changesMade = true;
        foreach (var icon in pack.Icons)
            icon.Picture.Dispose();
        pack.Icons.Clear();
        ListExistingIcons();
    }

    private void listBox1_SelectedIndexChanged(object sender, EventArgs e) => ListExistingIcons();

    private void AskForIconInfo(PackIcon picture)
    {
        pictureBox1.Image = picture.Picture;
        textBox3.Clear();
        textBox4.Clear();
        textBox3.Focus();
    }

    private void ApplyIconInfo(PackIcon picture)
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        var name = string.IsNullOrEmpty(textBox3.Text) ? picture.Name : textBox3.Text;
        var tags = string.IsNullOrEmpty(textBox4.Text)
            ? picture.Tags
            : ("custom," + textBox4.Text).Split(',')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();

        if (_existingIcons.Count == 0)
            pack.AddIcon(name, tags, picture.Picture);
        else
        {
            pack.Icons[_existingIcons[0]].Name = name;
            pack.Icons[_existingIcons[0]].Tags = tags;
        }

        _changesMade = true;
        textBox3.Clear();
        textBox4.Clear();
    }

    private void AddIconsVisible(bool enabled)
    {
        listView2.Visible = !enabled;
        textBox3.Visible = enabled;
        textBox4.Visible = enabled;
        buttonBrowse.Visible = !enabled;
        buttonAddIcons.Visible = !enabled;
        buttonOK.Visible = enabled;
        buttonSkip.Visible = enabled;
        label1.Visible = enabled;
        label2.Visible = enabled;
        label3.Visible = enabled;
        pictureBox1.Visible = enabled;
        DefaultControlsEnabled(!enabled);
    }

    public void StartOver(bool newIcons)
    {
        if (newIcons)
            _imagesToAdd.Clear();

        listView2.Items.Clear();
        textBox3.Clear();
        textBox4.Clear();
        label5.Visible = true;
        pictureBox1.Image = null;
        ListExistingIcons();
        MessageBox.Show("Done.");
    }

    private void UpdateListBoxText()
    {
        listBox1.Items.Clear();
        foreach (var manifest in _allPacks)
            listBox1.Items.Add(manifest.Name);

        if (listBox1.Items.Count > 0)
            listBox1.SelectedIndex = 0;
    }

    public void ListExistingIcons()
    {
        listView1.Items.Clear();
        var pack = SelectedPack;
        if (pack is null)
            return;

        listView1.View = View.LargeIcon;
        var imageList = new ImageList { ImageSize = new Size(60, 60) };

        for (var i = 0; i < pack.Icons.Count; i++)
        {
            var icon = pack.Icons[i];
            imageList.Images.Add(icon.Picture);
            listView1.Items.Add(new ListViewItem
            {
                ImageIndex = i,
                Text = icon.Name,
                Tag = icon.Tags
            });
        }

        listView1.LargeImageList = imageList;
        pictureBox2.Image = pack.Emblem;
    }

    public void ListNewIcons()
    {
        listView2.Items.Clear();
        listView2.View = View.LargeIcon;
        var imageList = new ImageList { ImageSize = new Size(60, 60) };

        for (var i = 0; i < _imagesToAdd.Count; i++)
        {
            imageList.Images.Add(_imagesToAdd[i].Picture);
            listView2.Items.Add(new ListViewItem
            {
                ImageIndex = i,
                Text = "Import",
                Tag = "Import"
            });
        }

        listView2.LargeImageList = imageList;
    }

    private void SaveAllPacks()
    {
        foreach (var manifest in _allPacks)
            manifest.SaveManifest();
    }

    private void buttonSave_Click(object sender, EventArgs e)
    {
        if (_allPacks.Count > 0)
            SaveAllPacks();

        MessageBox.Show("Done. Restart Stream Deck for changes to take effect!");
        _changesMade = false;
        Close();
    }

    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        _importWaiting?.DisposeAllImages();
        CleanupTempIconPacks();

        if (!_changesMade)
            return;

        if (MessageBox.Show(
                "Would you like changes saved to stream deck before exiting?",
                "WARNING",
                MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;

        SaveAllPacks();
        MessageBox.Show("Done. Restart Stream Deck for changes to take effect!");
        Environment.Exit(0);
    }

    private static void CleanupTempIconPacks()
    {
        foreach (var dir in Directory.GetDirectories(Path.GetTempPath()))
        {
            if (!dir.Contains(".sdIconPack", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                new DirectoryInfo(dir).Delete(true);
            }
            catch
            {
                // Best-effort cleanup of temp extract folders.
            }
        }
    }

    private void buttonNewPack_Click(object sender, EventArgs e)
    {
        NewPackControlsVisible(true);
        _changesMade = true;
    }

    private void buttonDeletePack_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (!EnsurePackSelected() || pack is null)
            return;

        if (MessageBox.Show(
                "Are you sure you want to delete the entire icon pack named:" + pack.Name + "?",
                "WARNING",
                MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;

        _changesMade = true;
        var path = pack.IconPackPath;
        pack.DisposeAllImages();
        new DirectoryInfo(path).Delete(true);
        _allPacks.RemoveAt(listBox1.SelectedIndex);
        UpdateListBoxText();
    }

    private void buttonImport_Click(object sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "zip archive (*.zip)|*.zip",
            RestoreDirectory = true
        };

        if (ofd.ShowDialog() != DialogResult.OK)
            return;

        _changesMade = true;
        ZipFile.ExtractToDirectory(ofd.FileName, Path.GetTempPath(), overwriteFiles: true);

        foreach (var dir in Directory.GetDirectories(Path.GetTempPath()))
        {
            if (!dir.Contains(".sdIconPack", StringComparison.OrdinalIgnoreCase))
                continue;

            _importWaiting = new Manifest(dir);
            _importWaitingTempPath = dir;

            if (PackNameExists(_importWaiting.Name))
            {
                BadNameControlsVisible(true);
                label7.Text = _importWaiting.Name;
                MessageBox.Show("Pack Name Already Exists");
                textBox5.Clear();
                textBox5.Focus();
                return;
            }

            FinishImport(_importWaiting, _importWaitingTempPath);
            return;
        }
    }

    private void TestNewName()
    {
        if (_importWaiting is null || _importWaitingTempPath is null)
            return;

        _importWaiting.ChangePackName(textBox5.Text);

        if (PackNameExists(_importWaiting.Name))
        {
            BadNameControlsVisible(true);
            label7.Text = _importWaiting.Name;
            MessageBox.Show("Pack Name Already Exists");
            textBox5.Clear();
            textBox5.Focus();
            return;
        }

        textBox5.Clear();
        BadNameControlsVisible(false);
        label7.Text = "";
        FinishImport(_importWaiting, _importWaitingTempPath);
    }

    private void FinishImport(Manifest import, string tempPath)
    {
        import.NewPackPath(_streamDeckPacksPath);
        import.SaveManifest();
        _allPacks.Add(new Manifest(import.IconPackPath));
        import.DisposeAllImages();
        _importWaiting = null;
        _importWaitingTempPath = null;

        try
        {
            new DirectoryInfo(tempPath).Delete(true);
        }
        catch
        {
            // Temp folder cleanup is best-effort.
        }

        UpdateListBoxText();
    }

    private bool PackNameExists(string name, Manifest? except = null) =>
        _allPacks.Any(m => !ReferenceEquals(m, except) && PackPaths.NamesConflict(m.Name, name));

    private void buttonExport_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (!EnsurePackSelected() || pack is null)
            return;

        using var saveDialog = new SaveFileDialog
        {
            Filter = "zip archive (*.zip)|*.zip",
            RestoreDirectory = true
        };

        if (saveDialog.ShowDialog() == DialogResult.OK)
            ZipFile.CreateFromDirectory(pack.IconPackPath, saveDialog.FileName, CompressionLevel.Optimal, includeBaseDirectory: true);
    }

    private void buttonDeleteSelectedIcons_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (!EnsurePackSelected() || pack is null)
            return;

        _changesMade = true;
        foreach (var index in listView1.SelectedIndices.Cast<int>().OrderByDescending(i => i))
            pack.RemoveIcon(index);

        ListExistingIcons();
    }

    private void buttonbadNameOK_Click(object sender, EventArgs e) => TestNewName();

    private void textBox4_KeyPress(object sender, KeyPressEventArgs e) =>
        InputFilters.LettersDigitsSpacesCommas(sender, e);

    private void textBox3_KeyPress(object sender, KeyPressEventArgs e) =>
        InputFilters.LettersDigitsSpaces(sender, e);

    private void textBox5_KeyPress(object sender, KeyPressEventArgs e) =>
        InputFilters.LettersDigitsSpaces(sender, e);

    private void textBox6_KeyPress(object sender, KeyPressEventArgs e) =>
        InputFilters.LettersDigitsSpaces(sender, e);

    private void textBox7_KeyPress(object sender, KeyPressEventArgs e) =>
        InputFilters.LettersDigitsSpaces(sender, e);

    private void textBox8_KeyPress(object sender, KeyPressEventArgs e) =>
        InputFilters.LettersSpaces(sender, e);

    private void DefaultControlsEnabled(bool enabled)
    {
        buttonAddIcons.Enabled = enabled;
        buttonBrowse.Enabled = enabled;
        buttonChangePackEmblem.Enabled = enabled;
        buttonDeleteAll.Enabled = enabled;
        buttonDeletePack.Enabled = enabled;
        buttonDeleteSelectedIcons.Enabled = enabled;
        buttonExport.Enabled = enabled;
        buttonImport.Enabled = enabled;
        buttonNewPack.Enabled = enabled;
        buttonSave.Enabled = enabled;
        listBox1.Enabled = enabled;
        buttonChangeIconName.Enabled = enabled;
        buttonChangePackDetails.Enabled = enabled;
        buttonCheckUpdates.Enabled = enabled;
    }

    private void NewPackControlsVisible(bool enabled)
    {
        listView1.Visible = !enabled;
        label9.Visible = enabled;
        label10.Visible = enabled;
        label11.Visible = enabled;
        label12.Visible = enabled;
        textBox6.Visible = enabled;
        textBox7.Visible = enabled;
        textBox8.Visible = enabled;
        buttonNewPackOK.Visible = enabled;
        buttonCancelNewPack.Visible = enabled;

        if (!enabled)
        {
            textBox6.Clear();
            textBox7.Clear();
            textBox8.Clear();
        }

        DefaultControlsEnabled(!enabled);
    }

    private void BadNameControlsVisible(bool enabled)
    {
        listView1.Visible = !enabled;
        label6.Visible = enabled;
        label7.Visible = enabled;
        label8.Visible = enabled;
        textBox5.Visible = enabled;
        buttonbadNameOK.Visible = enabled;
        DefaultControlsEnabled(!enabled);
    }

    private void buttonCancelNewPack_Click(object sender, EventArgs e)
    {
        NewPackControlsVisible(false);
        _existingPack = false;
    }

    private void buttonNewPackOK_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(textBox6.Text))
        {
            MessageBox.Show("You must enter a pack name");
            return;
        }

        var name = textBox6.Text;
        var description = string.IsNullOrWhiteSpace(textBox7.Text)
            ? PackPaths.DefaultDescription
            : textBox7.Text;
        var author = string.IsNullOrWhiteSpace(textBox8.Text)
            ? PackPaths.DefaultAuthor
            : textBox8.Text;

        if (_existingPack && SelectedPack is { } editing)
        {
            var nameUnchanged = PackPaths.NamesConflict(editing.Name, name);
            if (nameUnchanged || !PackNameExists(name, editing))
            {
                ApplyPackDetails(editing, name, description, author, renameFolder: !nameUnchanged);
                return;
            }

            MessageBox.Show("Pack Name Already Exists");
            textBox6.Clear();
            textBox6.Focus();
            return;
        }

        if (PackNameExists(name))
        {
            MessageBox.Show("Pack Name Already Exists");
            textBox6.Clear();
            textBox6.Focus();
            return;
        }

        var created = new Manifest(name, description, author, _streamDeckPacksPath);
        created.SaveManifest();
        _allPacks.Add(new Manifest(created.IconPackPath));
        FinishPackDetailsEdit();
    }

    private void ApplyPackDetails(Manifest pack, string name, string description, string author, bool renameFolder)
    {
        pack.Name = name;
        pack.Description = description;
        pack.Author = author;

        if (renameFolder)
        {
            var oldPath = pack.IconPackPath;
            pack.NewPackPath(_streamDeckPacksPath);
            pack.SaveManifest();
            new DirectoryInfo(oldPath).Delete(true);
        }

        FinishPackDetailsEdit();
    }

    private void FinishPackDetailsEdit()
    {
        _existingPack = false;
        textBox6.Clear();
        textBox7.Clear();
        textBox8.Clear();
        NewPackControlsVisible(false);
        UpdateListBoxText();
        _changesMade = true;
    }

    private void buttonChangePackEmblem_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (!EnsurePackSelected() || pack is null)
            return;

        using var dialog = new OpenFileDialog
        {
            Multiselect = false,
            Filter = "Image files (*.jpg;*.png)|*.jpg;*.jpeg;*.png"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        using (var loaded = Image.FromFile(dialog.FileName))
            pack.Emblem = ImageHelper.CloneBitmap(loaded);
        _changesMade = true;
        ListExistingIcons();
    }

    private void buttonChangePackDetails_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (!EnsurePackSelected() || pack is null)
            return;

        NewPackControlsVisible(true);
        textBox6.Text = pack.Name;
        textBox7.Text = pack.Description;
        textBox8.Text = pack.Author;
        _existingPack = true;
    }

    private void buttonChangeIconName_Click(object sender, EventArgs e)
    {
        var pack = SelectedPack;
        if (!EnsurePackSelected() || pack is null)
            return;

        _existingIcons.Clear();
        foreach (int index in listView1.SelectedIndices)
            _existingIcons.Add(index);

        if (_existingIcons.Count == 0)
            return;

        AddIconsVisible(true);
        buttonSkip.Visible = false;
        label5.Visible = false;

        var first = pack.Icons[_existingIcons[0]];
        pictureBox1.Image = first.Picture;
        textBox3.Text = first.Name;
        textBox4.Text = string.Join(',', first.Tags);
        textBox3.Focus();
    }

    private async void buttonCheckUpdates_Click(object sender, EventArgs e) =>
        await CheckForUpdatesAsync(silentWhenCurrent: false);

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        if (_updateCheckRunning)
            return;

        _updateCheckRunning = true;
        buttonCheckUpdates.Enabled = false;
        labelUpdateStatus.Text = "Checking for updates...";

        try
        {
            var result = await _updater.CheckForUpdateAsync();
            if (!result.Succeeded)
            {
                if (!silentWhenCurrent)
                    MessageBox.Show(this, result.ErrorMessage ?? "Update check failed.", "Update Check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                labelUpdateStatus.Text = "Update check failed";
                return;
            }

            if (result.IsUpToDate)
            {
                labelUpdateStatus.Text = "Up to date";
                if (!silentWhenCurrent)
                    MessageBox.Show(this, $"You're on the latest version ({AppUpdateService.CurrentVersion}).", "Update Check");
                return;
            }

            labelUpdateStatus.Text = $"Update {result.LatestVersion} available";
            var answer = MessageBox.Show(
                this,
                $"Version {result.LatestVersion} is available (you have {AppUpdateService.CurrentVersion}).\n\nDownload and install it now?",
                "Update Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
                await DownloadAndInstallAsync(result);
        }
        catch (Exception ex)
        {
            labelUpdateStatus.Text = "Update check failed";
            if (!silentWhenCurrent)
                MessageBox.Show(this, "Could not check for updates:\n" + ex.Message, "Update Check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateCheckRunning = false;
            buttonCheckUpdates.Enabled = true;
        }
    }

    private async Task DownloadAndInstallAsync(UpdateCheckResult update)
    {
        using var progressForm = new UpdateProgressForm();
        var progress = new Progress<UpdateProgress>(progressForm.Report);
        progressForm.Show(this);

        try
        {
            await _updater.InstallUpdateAsync(update, progress);
            MessageBox.Show(
                this,
                "The installer will start now. This app will close so the update can finish.",
                "Update Ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _changesMade = false;
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Update failed:\n" + ex.Message, "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            labelUpdateStatus.Text = "Update failed";
        }
        finally
        {
            progressForm.Close();
        }
    }
}
