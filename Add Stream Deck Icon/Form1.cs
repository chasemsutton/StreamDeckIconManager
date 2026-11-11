using Add_Stream_Deck_Icon.UI;

namespace Add_Stream_Deck_Icon;

/// <summary>
/// Main window. Layout: app bar (save) on top, pack list on the left, the open pack on the right
/// (header, icon grid, drop tray), status line at the bottom.
/// </summary>
public class Form1 : Form
{
    private readonly List<Manifest> _allPacks = new();
    private readonly List<PackIcon> _queue = new();
    private readonly string _streamDeckPacksPath = PackPaths.IconPacksRoot;
    private readonly AppUpdateService _updater = new();
    private readonly AppSettings _settings = AppSettings.Load();

    private bool _changesMade;
    private bool _updateCheckRunning;

    // App bar
    private ThemedButton _saveButton = null!;
    private Label _unsavedLabel = null!;

    // Sidebar
    private PackList _packList = null!;
    private Label _packCountLabel = null!;

    // Pack view
    private Panel _packView = null!;
    private Panel _noPacksView = null!;
    private KeyImage _emblem = null!;
    private Label _packName = null!;
    private Label _packMeta = null!;
    private Label _packDescription = null!;
    private Label _iconsHeading = null!;
    private ThemedButton _selectAllButton = null!;
    private ThemedButton _editIconsButton = null!;
    private ThemedButton _deleteIconsButton = null!;
    private IconGrid _grid = null!;
    private DropZone _dropZone = null!;

    // Status bar
    private Label _statusLabel = null!;
    private ThemedButton _updateButton = null!;
    private ThemedButton _channelButton = null!;

    public Form1()
    {
        BuildUi();

        if (!Directory.Exists(_streamDeckPacksPath))
        {
            MessageBox.Show(
                $"Couldn't find the Stream Deck icon pack folder:\n{_streamDeckPacksPath}\n\nInstall the Elgato Stream Deck app and open it once, then try again.",
                "Stream Deck not found", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(0);
            return;
        }

        foreach (var dir in Directory.GetDirectories(_streamDeckPacksPath))
        {
            if (PackPaths.IsCustomPackDirectory(dir))
                _allPacks.Add(new Manifest(dir));
        }

        RefreshPackList();
        SetStatus(_allPacks.Count switch
        {
            0 => "No custom icon packs yet.",
            1 => "Loaded 1 icon pack from Stream Deck.",
            var n => $"Loaded {n} icon packs from Stream Deck."
        });
        Shown += async (_, _) => await CheckForUpdatesAsync(silentWhenCurrent: true);
    }

    #region Layout

    private void BuildUi()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Stream Deck Icon Manager";
        Icon = AppIcon.Window;
        Font = Theme.Body;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(940, 620);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        _packView = BuildPackView();
        _noPacksView = BuildNoPacksView();
        content.Controls.Add(_packView);
        content.Controls.Add(_noPacksView);

        // Dock order: last added docks first, so fill goes in first.
        Controls.Add(content);
        Controls.Add(BuildSidebar());
        Controls.Add(BuildStatusBar());

        WireDragAndDrop(this, content, _packView, _noPacksView, _grid, _dropZone);
        FormClosing += OnFormClosing;

        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>Save button and its state line, pinned to the bottom of the sidebar.</summary>
    private Control BuildSaveSection()
    {
        var section = new Surface(Theme.Sidebar, Edges.Top)
        {
            Dock = DockStyle.Bottom,
            Height = 92,
            Padding = new Padding(16, 12, 16, 16)
        };

        _unsavedLabel = new Label
        {
            Text = "All changes saved",
            ForeColor = Theme.TextFaint,
            Font = Theme.Small,
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.TopLeft
        };

        _saveButton = new ThemedButton("Save to Stream Deck", ButtonKind.Secondary, Theme.Glyphs.Save)
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 38
        };
        _saveButton.Click += (_, _) => SaveToStreamDeck();
        new ToolTip().SetToolTip(_saveButton, "Save all packs to Stream Deck (Ctrl+S)");

        section.Controls.Add(_saveButton);
        section.Controls.Add(_unsavedLabel);
        return section;
    }

    private Control BuildStatusBar()
    {
        var bar = new Surface(Theme.Sidebar, Edges.Top)
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(16, 1, 10, 0)
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextMuted,
            Font = Theme.Small,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        var version = new Label
        {
            Text = $"Version {AppUpdateService.CurrentVersion}",
            ForeColor = Theme.TextFaint,
            Font = Theme.Small,
            AutoSize = true,
            Margin = new Padding(0, 9, 4, 0)
        };

        _updateButton = new ThemedButton("Check for updates", ButtonKind.Ghost, Theme.Glyphs.Sync)
        {
            Font = Theme.Small,
            Margin = new Padding(0, 1, 0, 0)
        };
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync(silentWhenCurrent: false);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Theme.Sidebar
        };
        _channelButton = new ThemedButton("", ButtonKind.Ghost, Theme.Glyphs.Settings)
        {
            Font = Theme.Small,
            Margin = new Padding(0, 1, 2, 0)
        };
        var channelMenu = BuildChannelMenu();
        _channelButton.Click += (_, _) => channelMenu.Show(_channelButton, new Point(0, -channelMenu.PreferredSize.Height));
        new ToolTip().SetToolTip(_channelButton, "Choose which releases Check for updates offers");
        UpdateChannelButton();

        right.Controls.Add(version);
        right.Controls.Add(_channelButton);
        right.Controls.Add(_updateButton);

        bar.Controls.Add(_statusLabel);
        bar.Controls.Add(right);
        return bar;
    }

    private ContextMenuStrip BuildChannelMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.Body };
        var heading = new ToolStripMenuItem("Update channel") { Enabled = false };
        var stable = new ToolStripMenuItem("Stable (recommended)");
        var beta = new ToolStripMenuItem("Beta (not recommended)") { Tag = "danger" };
        stable.Click += async (_, _) => await SetUpdateChannelAsync(UpdateChannel.Stable);
        beta.Click += async (_, _) => await SetUpdateChannelAsync(UpdateChannel.Beta);
        menu.Items.AddRange(new ToolStripItem[] { heading, new ToolStripSeparator(), stable, beta });
        menu.Opening += (_, _) =>
        {
            stable.Checked = _settings.UpdateChannel == UpdateChannel.Stable;
            beta.Checked = _settings.UpdateChannel == UpdateChannel.Beta;
        };
        return menu;
    }

    private void UpdateChannelButton()
    {
        var beta = _settings.UpdateChannel == UpdateChannel.Beta;
        _channelButton.Text = beta ? "Beta channel" : "Stable channel";
        _channelButton.TextColor = beta ? Theme.Warning : null;
    }

    private async Task SetUpdateChannelAsync(UpdateChannel channel)
    {
        if (channel == _settings.UpdateChannel)
            return;

        if (channel == UpdateChannel.Beta && MessageBox.Show(this,
                "Beta releases get new features first, but they're less tested and may have bugs that affect your icon packs.\n\n" +
                "Export any packs you care about before installing a beta. You can switch back to Stable at any time.\n\n" +
                "Switch to the beta channel?",
                "Switch to beta updates", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _settings.UpdateChannel = channel;
        _settings.Save();
        UpdateChannelButton();

        if (channel == UpdateChannel.Stable)
        {
            SetStatus("Switched to stable updates. If you're on a beta, you'll get the next stable release that's newer than it.");
            await CheckForUpdatesAsync(silentWhenCurrent: true);
        }
        else
        {
            SetStatus("Switched to beta updates.", Theme.Warning);
            await CheckForUpdatesAsync(silentWhenCurrent: false);
        }
    }

    private Control BuildSidebar()
    {
        var sidebar = new Surface(Theme.Sidebar, Edges.Right)
        {
            Dock = DockStyle.Left,
            Width = 272,
            Padding = new Padding(0, 0, 1, 0)
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Sidebar };
        header.Controls.Add(new Label
        {
            Text = "ICON PACKS",
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 22)
        });
        _packCountLabel = new Label
        {
            Font = Theme.Caption,
            ForeColor = Theme.TextFaint,
            AutoSize = true,
            Location = new Point(100, 22)
        };
        header.Controls.Add(_packCountLabel);

        _packList = new PackList { Dock = DockStyle.Fill };
        _packList.RowData = i => (_allPacks[i].Emblem, _allPacks[i].Name, _allPacks[i].Icons.Count);
        _packList.SelectedIndexChanged += (_, _) => ShowSelectedPack();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 104, Padding = new Padding(16, 12, 16, 16), BackColor = Theme.Sidebar };
        var newPack = new ThemedButton("New pack", ButtonKind.Secondary, Theme.Glyphs.Add) { AutoSize = false, Dock = DockStyle.Top, Height = 36 };
        var import = new ThemedButton("Import pack", ButtonKind.Ghost, Theme.Glyphs.Import) { AutoSize = false, Dock = DockStyle.Top, Height = 36 };
        newPack.Click += (_, _) => CreatePack();
        import.Click += (_, _) => ImportPack();
        footer.Controls.Add(import);
        footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Sidebar });
        footer.Controls.Add(newPack);

        // Later additions dock first: header on top, save section at the very bottom, then the
        // pack buttons above it, and the list fills what's left.
        sidebar.Controls.Add(_packList);
        sidebar.Controls.Add(footer);
        sidebar.Controls.Add(BuildSaveSection());
        sidebar.Controls.Add(header);
        return sidebar;
    }

    private Panel BuildPackView()
    {
        var view = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(32, 26, 32, 20) };

        // Header: emblem, name, details, pack actions
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 128,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Window
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _emblem = new KeyImage
        {
            Size = new Size(100, 100),
            CornerRadius = 16,
            HoverGlyph = Theme.Glyphs.Picture,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 0)
        };
        _emblem.Click += (_, _) => ChangeEmblem();
        new ToolTip().SetToolTip(_emblem, "Change the pack emblem shown in the Stream Deck icon library");

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, BackColor = Theme.Window };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _packName = new Label { Font = Theme.Title, ForeColor = Theme.Text, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 40, Margin = Padding.Empty, UseMnemonic = false };
        _packMeta = new Label { Font = Theme.Body, ForeColor = Theme.TextMuted, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 22, Margin = Padding.Empty, UseMnemonic = false };
        _packDescription = new Label { Font = Theme.Body, ForeColor = Theme.TextFaint, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 22, Margin = Padding.Empty, UseMnemonic = false };

        var packActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0), BackColor = Theme.Window };
        var edit = new ThemedButton("Edit details", ButtonKind.Secondary, Theme.Glyphs.Edit);
        var emblem = new ThemedButton("Change emblem", ButtonKind.Secondary, Theme.Glyphs.Picture);
        var export = new ThemedButton("Export", ButtonKind.Secondary, Theme.Glyphs.Export);
        new ToolTip().SetToolTip(export, "Save as a .streamDeckIconPack file. Anyone with Stream Deck can double-click it to install the pack, with or without this app.");
        var delete = new ThemedButton("Delete pack", ButtonKind.Danger, Theme.Glyphs.Delete);
        edit.Click += (_, _) => EditPackDetails();
        emblem.Click += (_, _) => ChangeEmblem();
        export.Click += (_, _) => ExportPack();
        delete.Click += (_, _) => DeletePack();
        packActions.Controls.AddRange(new Control[] { edit, emblem, export, delete });

        details.Controls.Add(_packName, 0, 0);
        details.Controls.Add(_packMeta, 0, 1);
        details.Controls.Add(_packDescription, 0, 2);
        details.Controls.Add(packActions, 0, 3);

        header.Controls.Add(_emblem, 0, 0);
        header.Controls.Add(details, 1, 0);

        // Toolbar above the grid
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Window, Padding = new Padding(0, 10, 0, 8) };
        _iconsHeading = new Label { Dock = DockStyle.Fill, Font = Theme.BodyStrong, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

        _selectAllButton = new ThemedButton("Select all", ButtonKind.Ghost);
        _editIconsButton = new ThemedButton("Name & tags", ButtonKind.Secondary, Theme.Glyphs.Tag);
        _deleteIconsButton = new ThemedButton("Delete", ButtonKind.Danger, Theme.Glyphs.Delete) { Margin = Padding.Empty };
        _selectAllButton.Click += (_, _) => { _grid.SelectAll(); _grid.Focus(); };
        _editIconsButton.Click += (_, _) => EditSelectedIcons();
        _deleteIconsButton.Click += (_, _) => DeleteSelectedIcons();

        var iconActions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Window };
        iconActions.Controls.AddRange(new Control[] { _selectAllButton, _editIconsButton, _deleteIconsButton });
        toolbar.Controls.Add(_iconsHeading);
        toolbar.Controls.Add(iconActions);

        // The grid sits on a plate, like the face of the deck.
        var plate = new Surface(Theme.Sidebar) { Dock = DockStyle.Fill, Radius = 14, Padding = new Padding(6) };
        _grid = new IconGrid
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Sidebar,
            EmptyTitle = "This pack has no icons yet",
            EmptyBody = "Drop images anywhere in this window, or click the tray below to choose files."
        };
        _grid.SelectionChanged += (_, _) => UpdateIconActions();
        _grid.ItemActivated += (_, _) => EditSelectedIcons();
        _grid.DeleteRequested += (_, _) => DeleteSelectedIcons();
        _grid.ContextMenuStrip = BuildGridMenu();
        plate.Controls.Add(_grid);

        var trayHost = new Panel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(0, 16, 0, 0), BackColor = Theme.Window };
        _dropZone = new DropZone { Dock = DockStyle.Fill };
        _dropZone.BrowseRequested += (_, _) => BrowseForImages();
        _dropZone.AddButton.Click += (_, _) => AddQueuedIcons();
        _dropZone.ClearButton.Click += (_, _) => ClearQueue();
        trayHost.Controls.Add(_dropZone);

        view.Controls.Add(plate);
        view.Controls.Add(trayHost);
        view.Controls.Add(toolbar);
        view.Controls.Add(header);
        return view;
    }

    private ContextMenuStrip BuildGridMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
        var edit = new ToolStripMenuItem("Edit name and tags", null, (_, _) => EditSelectedIcons()) { ShortcutKeyDisplayString = "Enter" };
        var selectAll = new ToolStripMenuItem("Select all", null, (_, _) => _grid.SelectAll()) { ShortcutKeyDisplayString = "Ctrl+A" };
        var delete = new ToolStripMenuItem("Delete", null, (_, _) => DeleteSelectedIcons()) { ShortcutKeyDisplayString = "Del", Tag = "danger" };
        menu.Items.AddRange(new ToolStripItem[] { edit, selectAll, new ToolStripSeparator(), delete });
        menu.Opening += (_, e) =>
        {
            var any = _grid.SelectedIndices.Count > 0;
            edit.Enabled = any;
            delete.Enabled = any;
            if (SelectedPack is null || SelectedPack.Icons.Count == 0)
                e.Cancel = true;
        };
        return menu;
    }

    private Panel BuildNoPacksView()
    {
        var view = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Visible = false };

        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            BackColor = Theme.Window
        };

        var keys = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 20), BackColor = Theme.Window };
        for (var i = 0; i < 3; i++)
            keys.Controls.Add(new KeyImage { Size = new Size(64, 64), CornerRadius = 11, Margin = new Padding(6, 0, 6, 0), Image = i == 1 ? AppIcon.Bitmap(64) : null });

        var title = new Label { Text = "Make your first icon pack", Font = Theme.DialogTitle, ForeColor = Theme.Text, AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 6) };
        var body = new Label
        {
            Text = "A pack is a named set of icons that appears in the Stream Deck icon library. Create one, then drop your images into it.",
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 20)
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None, BackColor = Theme.Window };
        var create = new ThemedButton("New pack", ButtonKind.Primary, Theme.Glyphs.Add);
        var import = new ThemedButton("Import a pack", ButtonKind.Secondary, Theme.Glyphs.Import) { Margin = Padding.Empty };
        create.Click += (_, _) => CreatePack();
        import.Click += (_, _) => ImportPack();
        buttons.Controls.Add(create);
        buttons.Controls.Add(import);

        stack.Controls.Add(keys, 0, 0);
        stack.Controls.Add(title, 0, 1);
        stack.Controls.Add(body, 0, 2);
        stack.Controls.Add(buttons, 0, 3);

        view.Controls.Add(stack);
        view.Resize += (_, _) => stack.Location = new Point((view.Width - stack.Width) / 2, (view.Height - stack.Height) / 2 - 20);
        return view;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(this);
        Native.SetCaptionColors(this, Theme.Sidebar, Theme.Text);
    }

    #endregion

    #region State and refresh

    private Manifest? SelectedPack =>
        _packList.SelectedIndex >= 0 && _packList.SelectedIndex < _allPacks.Count
            ? _allPacks[_packList.SelectedIndex]
            : null;

    private bool ChangesMade
    {
        get => _changesMade;
        set
        {
            _changesMade = value;
            _unsavedLabel.Text = value ? "● Unsaved changes" : "All changes saved";
            _unsavedLabel.ForeColor = value ? Theme.Warning : Theme.TextFaint;
            _saveButton.Kind = value ? ButtonKind.Primary : ButtonKind.Secondary;
        }
    }

    private void MarkChanged(string status)
    {
        ChangesMade = true;
        SetStatus(status + " Save to Stream Deck when you're done.");
    }

    private void SetStatus(string text, Color? color = null)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color ?? Theme.TextMuted;
    }

    private void RefreshPackList(Manifest? select = null)
    {
        var target = select ?? SelectedPack;
        _packList.BeginUpdate();
        _packList.Items.Clear();
        foreach (var pack in _allPacks)
            _packList.Items.Add(pack.Name);
        _packList.EndUpdate();

        _packCountLabel.Text = _allPacks.Count.ToString();
        _packCountLabel.Left = _packCountLabel.Parent!.Controls[0].Right + 4;

        var hasPacks = _allPacks.Count > 0;
        _packView.Visible = hasPacks;
        _noPacksView.Visible = !hasPacks;

        if (hasPacks)
        {
            var index = target is null ? 0 : Math.Max(0, _allPacks.IndexOf(target));
            _packList.SelectedIndex = index;
        }

        ShowSelectedPack();
    }

    private void ShowSelectedPack(bool keepSelection = false)
    {
        var pack = SelectedPack;
        if (pack is null)
        {
            _grid.SetItems(Array.Empty<IconTile>());
            UpdateQueueTray();
            return;
        }

        _packName.Text = pack.Name;
        var count = pack.Icons.Count == 1 ? "1 icon" : $"{pack.Icons.Count} icons";
        _packMeta.Text = $"{count}  ·  by {pack.Author}";
        _packDescription.Text = pack.Description;
        _emblem.Image = pack.Emblem;

        _grid.SetItems(pack.Icons.Select(i => new IconTile(
            i.Picture,
            i.Name,
            $"{i.Name}\nTags: {IconDetailsDialog.FormatTags(i.Tags)}")), keepSelection);
        _packList.Invalidate();
        UpdateQueueTray();
        UpdateIconActions();
    }

    private void UpdateIconActions()
    {
        var pack = SelectedPack;
        var total = pack?.Icons.Count ?? 0;
        var selected = _grid.SelectedIndices.Count;

        _iconsHeading.Text = selected > 0
            ? $"{selected} of {total} selected"
            : total == 1 ? "1 icon" : $"{total} icons";
        _iconsHeading.ForeColor = selected > 0 ? Theme.Accent : Theme.Text;

        _selectAllButton.Visible = total > 0 && selected < total;
        _editIconsButton.Enabled = selected > 0;
        _deleteIconsButton.Enabled = selected > 0;
        _editIconsButton.Text = selected > 1 ? $"Name & tags ({selected})" : "Name & tags";
    }

    private void UpdateQueueTray() =>
        _dropZone.SetQueue(_queue.Select(q => (Image)q.Picture).ToList(), SelectedPack?.Name ?? "");

    #endregion

    #region Adding icons

    private void BrowseForImages()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose images for your icons",
            Multiselect = true,
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            QueueImages(dialog.FileNames);
    }

    private void QueueImages(IEnumerable<string> files)
    {
        var added = 0;
        var skipped = 0;
        foreach (var file in files)
        {
            if (!PackPaths.IsSupportedImageExtension(file))
            {
                skipped++;
                continue;
            }

            try
            {
                _queue.Add(PackIcon.FromFile(file));
                added++;
            }
            catch
            {
                skipped++;
            }
        }

        UpdateQueueTray();

        if (skipped > 0)
            SetStatus($"Skipped {skipped} {(skipped == 1 ? "file" : "files")} that {(skipped == 1 ? "isn't" : "aren't")} a readable PNG, JPG or GIF.", Theme.Warning);
        else if (added > 0)
            SetStatus($"{_queue.Count} {(_queue.Count == 1 ? "image" : "images")} ready. Click Add to name them and put them in the pack.");
    }

    private void AddQueuedIcons()
    {
        var pack = SelectedPack;
        if (pack is null || _queue.Count == 0)
            return;

        var drafts = _queue.Select(q => new IconDraft(q.Picture, q.Name, q.Tags)).ToList();
        using var dialog = new IconDetailsDialog(drafts, pack.Name, editing: false);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        for (var i = 0; i < drafts.Count; i++)
            pack.AddIcon(drafts[i].Name, drafts[i].Tags, _queue[i].Picture);

        var count = _queue.Count;
        _queue.Clear();
        ShowSelectedPack();
        MarkChanged($"Added {count} {(count == 1 ? "icon" : "icons")} to {pack.Name}.");
    }

    private void ClearQueue()
    {
        foreach (var icon in _queue)
            icon.Picture.Dispose();
        _queue.Clear();
        UpdateQueueTray();
        SetStatus("Cleared the images waiting to be added.");
    }

    #endregion

    #region Editing icons

    private void EditSelectedIcons()
    {
        var pack = SelectedPack;
        if (pack is null || _grid.SelectedIndices.Count == 0)
            return;

        var indices = _grid.SelectedIndices.OrderBy(i => i).ToList();
        var drafts = indices.Select(i => new IconDraft(pack.Icons[i].Picture, pack.Icons[i].Name, pack.Icons[i].Tags)).ToList();

        using var dialog = new IconDetailsDialog(drafts, pack.Name, editing: true);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        for (var i = 0; i < indices.Count; i++)
        {
            pack.Icons[indices[i]].Name = drafts[i].Name;
            pack.Icons[indices[i]].Tags = drafts[i].Tags;
        }

        ShowSelectedPack(keepSelection: true);
        MarkChanged(indices.Count == 1 ? $"Updated {drafts[0].Name}." : $"Updated {indices.Count} icons.");
    }

    private void DeleteSelectedIcons()
    {
        var pack = SelectedPack;
        if (pack is null || _grid.SelectedIndices.Count == 0)
            return;

        var indices = _grid.SelectedIndices.OrderByDescending(i => i).ToList();
        if (indices.Count == pack.Icons.Count && indices.Count > 1 &&
            MessageBox.Show(this, $"Remove all {indices.Count} icons from {pack.Name}?", "Remove all icons",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        foreach (var index in indices)
            pack.RemoveIcon(index);

        ShowSelectedPack();
        MarkChanged(indices.Count == 1 ? "Removed 1 icon." : $"Removed {indices.Count} icons.");
    }

    #endregion

    #region Packs

    private string? PackNameProblem(string name, Manifest? except = null) =>
        _allPacks.Any(m => !ReferenceEquals(m, except) && PackPaths.NamesConflict(m.Name, name))
            ? $"You already have a pack called \"{name}\"."
            : null;

    private void CreatePack()
    {
        using var dialog = new PackDetailsDialog(name => PackNameProblem(name));
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var created = new Manifest(dialog.PackName, dialog.Description, dialog.Author, _streamDeckPacksPath);
        created.SaveManifest();
        var loaded = new Manifest(created.IconPackPath);
        created.DisposeAllImages();
        _allPacks.Add(loaded);
        RefreshPackList(loaded);
        MarkChanged($"Created {loaded.Name}.");
    }

    private void EditPackDetails()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        using var dialog = new PackDetailsDialog(name => PackNameProblem(name, pack), pack);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var renamed = !PackPaths.NamesConflict(pack.Name, dialog.PackName);
        pack.Name = dialog.PackName;
        pack.Description = dialog.Description;
        pack.Author = dialog.Author;

        if (renamed)
        {
            var oldPath = pack.IconPackPath;
            pack.NewPackPath(_streamDeckPacksPath);
            pack.SaveManifest();
            new DirectoryInfo(oldPath).Delete(true);
        }

        RefreshPackList(pack);
        MarkChanged($"Updated details for {pack.Name}.");
    }

    private void ChangeEmblem()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "Choose an emblem for " + pack.Name,
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        using (var loaded = Image.FromFile(dialog.FileName))
        {
            var old = pack.Emblem;
            pack.Emblem = ImageHelper.CloneBitmap(loaded);
            old.Dispose();
        }

        ShowSelectedPack(keepSelection: true);
        MarkChanged($"Changed the emblem for {pack.Name}.");
    }

    private void DeletePack()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        var icons = pack.Icons.Count == 1 ? "its 1 icon" : $"all {pack.Icons.Count} icons";
        if (MessageBox.Show(this,
                $"Delete \"{pack.Name}\" and {icons}?\n\nThe pack is removed from Stream Deck right away. This can't be undone. Export it first if you want a copy.",
                "Delete pack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var name = pack.Name;
        var path = pack.IconPackPath;
        var index = _allPacks.IndexOf(pack);
        pack.DisposeAllImages();
        new DirectoryInfo(path).Delete(true);
        _allPacks.RemoveAt(index);

        RefreshPackList(_allPacks.Count > 0 ? _allPacks[Math.Min(index, _allPacks.Count - 1)] : null);
        ChangesMade = true;
        SetStatus($"Deleted {name}. Restart Stream Deck to remove it from the icon library.");
    }

    private void ExportPack()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Export " + pack.Name,
            Filter = IconPackFile.SaveFilter,
            FileName = pack.Name + IconPackFile.Extension,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            // Exports what's on screen, unsaved changes included.
            IconPackFile.Export(pack, dialog.FileName);
            SetStatus($"Exported {pack.Name}. Anyone with Stream Deck can double-click the file to install it.", Theme.Success);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't export the pack:\n" + ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportPack(string? packFile = null)
    {
        if (packFile is null)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import an icon pack",
                Filter = IconPackFile.OpenFilter,
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            packFile = dialog.FileName;
        }

        var extractRoot = Path.Combine(Path.GetTempPath(), "sdim-import-" + Guid.NewGuid().ToString("N"));
        Manifest? import = null;

        try
        {
            var packDir = IconPackFile.Extract(packFile, extractRoot);
            if (packDir is null)
            {
                MessageBox.Show(this, "That file doesn't contain a Stream Deck icon pack. Choose a .streamDeckIconPack file, or a .zip exported from this app.",
                    "Nothing to import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            import = Manifest.ReadForImport(packDir, out var skipped);
            if (import.Icons.Count == 0 && skipped > 0)
            {
                MessageBox.Show(this,
                    $"None of the {skipped} icons in this pack can be imported. This app supports PNG, JPG and GIF icons; this pack uses another format, such as SVG.",
                    "Nothing to import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (PackNameProblem(import.Name) is not null)
            {
                using var rename = new RenameImportDialog(import.Name, name => PackNameProblem(name));
                if (rename.ShowDialog(this) != DialogResult.OK)
                {
                    SetStatus("Import cancelled.");
                    return;
                }

                import.ChangePackName(rename.PackName);
            }

            import.NewPackPath(_streamDeckPacksPath);
            import.SaveManifest();
            var loaded = new Manifest(import.IconPackPath);
            _allPacks.Add(loaded);
            RefreshPackList(loaded);
            ChangesMade = true;
            var skippedNote = skipped == 0 ? "" : $" Skipped {skipped} icon{(skipped == 1 ? "" : "s")} in a format this app can't read (such as SVG).";
            SetStatus($"Imported {loaded.Name} with {loaded.Icons.Count} icons.{skippedNote} Save to Stream Deck to see it.", skipped == 0 ? Theme.Success : Theme.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't import the pack:\n" + ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            import?.DisposeAllImages();
            TryDeleteDirectory(extractRoot);
        }
    }

    #endregion

    #region Saving and closing

    private void SaveAllPacks()
    {
        foreach (var manifest in _allPacks)
            manifest.SaveManifest();
        ChangesMade = false;
    }

    private void SaveToStreamDeck()
    {
        try
        {
            SaveAllPacks();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't save your icon packs:\n" + ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetStatus("Saved. Restart Stream Deck to see your changes.", Theme.Success);

        using var dialog = new RestartDialog(_allPacks.Count);
        dialog.ShowDialog(this);
        if (dialog.Restarted)
            SetStatus("Saved and restarted Stream Deck. Your icons are in its icon library.", Theme.Success);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (ChangesMade)
        {
            var answer = MessageBox.Show(this,
                "Save your changes to Stream Deck before closing?",
                "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == DialogResult.Yes)
            {
                try
                {
                    SaveAllPacks();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save your icon packs:\n" + ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    e.Cancel = true;
                    return;
                }

                if (StreamDeckApp.IsRunning && MessageBox.Show(this,
                        "Saved. Restart Stream Deck now so it loads your changes?",
                        "Restart Stream Deck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        StreamDeckApp.RestartAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Couldn't restart Stream Deck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        foreach (var icon in _queue)
            icon.Picture.Dispose();
        CleanupTempIconPacks();
    }

    private static void CleanupTempIconPacks()
    {
        foreach (var dir in Directory.GetDirectories(Path.GetTempPath()))
        {
            var name = Path.GetFileName(dir);
            if (name.Contains(".sdIconPack", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("sdim-import-", StringComparison.OrdinalIgnoreCase))
                TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                new DirectoryInfo(path).Delete(true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }

    #endregion

    #region Drag and drop, shortcuts

    private void WireDragAndDrop(params Control[] targets)
    {
        foreach (var target in targets)
        {
            target.AllowDrop = true;
            target.DragEnter += OnDragEnter;
            target.DragOver += OnDragEnter;
            target.DragLeave += (_, _) => _dropZone.DragActive = false;
            target.DragDrop += OnDragDrop;
        }
    }

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        var images = SelectedPack is not null && files.Any(PackPaths.IsSupportedImageExtension);
        var zip = files.Length == 1 && IconPackFile.IsImportable(files[0]);
        e.Effect = images || zip ? DragDropEffects.Copy : DragDropEffects.None;
        _dropZone.DragActive = images;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        _dropZone.DragActive = false;
        var files = DroppedFiles(e);

        if (files.Length == 1 && IconPackFile.IsImportable(files[0]))
        {
            BeginInvoke(() => ImportPack(files[0]));
            return;
        }

        if (SelectedPack is not null)
            QueueImages(files);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                SaveToStreamDeck();
                return true;
            case Keys.Control | Keys.O when SelectedPack is not null:
                BrowseForImages();
                return true;
            case Keys.Control | Keys.N:
                CreatePack();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    #endregion

    #region Updates

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        if (_updateCheckRunning)
            return;

        _updateCheckRunning = true;
        _updateButton.Enabled = false;
        _updateButton.Text = "Checking…";

        try
        {
            var result = await _updater.CheckForUpdateAsync(_settings.UpdateChannel);
            if (!result.Succeeded)
            {
                if (!silentWhenCurrent)
                    MessageBox.Show(this, result.ErrorMessage ?? "Update check failed.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("Couldn't check for updates.", Theme.TextFaint);
                return;
            }

            if (result.IsUpToDate)
            {
                if (!silentWhenCurrent)
                {
                    var latest = _settings.UpdateChannel == UpdateChannel.Beta ? "latest version, including betas" : "latest stable version";
                    SetStatus($"You're on the {latest} ({AppUpdateService.CurrentVersion}).");
                    MessageBox.Show(this, $"You're on the {latest} ({AppUpdateService.CurrentVersion}).", "Check for updates");
                }
                return;
            }

            var kind = result.IsPrerelease ? "Beta version" : "Version";
            SetStatus($"{kind} {result.LatestVersion} is available.", result.IsPrerelease ? Theme.Warning : Theme.Accent);
            var betaNote = result.IsPrerelease ? "\n\nThis is a beta release and may contain bugs." : "";
            var answer = MessageBox.Show(
                this,
                $"{kind} {result.LatestVersion} is available (you have {AppUpdateService.CurrentVersion}).{betaNote}\n\nDownload and install it now?",
                result.IsPrerelease ? "Beta update available" : "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
                await DownloadAndInstallAsync(result);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't check for updates.", Theme.TextFaint);
            if (!silentWhenCurrent)
                MessageBox.Show(this, "Couldn't check for updates:\n" + ex.Message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateCheckRunning = false;
            _updateButton.Enabled = true;
            _updateButton.Text = "Check for updates";
        }
    }

    private async Task DownloadAndInstallAsync(UpdateCheckResult update)
    {
        if (ChangesMade && MessageBox.Show(this,
                "Save your changes to Stream Deck before updating?",
                "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            SaveAllPacks();

        string setupPath;
        using (var progressForm = new UpdateProgressForm())
        {
            var progress = new Progress<UpdateProgress>(progressForm.Report);
            progressForm.Show(this);
            try
            {
                setupPath = await _updater.DownloadInstallerAsync(update, progress);
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show(this, "Update failed:\n" + ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Update failed.", Theme.Danger);
                return;
            }

            progressForm.Close();
        }

        // Tell the user first, then start Setup and exit at once. Setup replaces this app's
        // files, so the app must not still be open (or showing a dialog) while it runs.
        MessageBox.Show(
            this,
            "The update is downloaded. This app will close and the installer will finish the update.",
            "Update ready",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        try
        {
            AppUpdateService.LaunchInstaller(setupPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't start the installer:\n" + ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Update failed.", Theme.Danger);
            return;
        }

        _changesMade = false;
        Environment.Exit(0);
    }

    #endregion
}
