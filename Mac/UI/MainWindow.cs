using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// Main window. Layout: pack list on the left (with Save at the bottom), the open pack on the
/// right (header, icon grid, drop tray), status line at the bottom. Mirrors the Windows Form1.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly List<Manifest> _allPacks = new();
    private readonly List<PackIcon> _queue = new();
    private readonly string _streamDeckPacksPath = PackPaths.IconPacksRoot;
    private readonly AppUpdateService _updater = new();
    private readonly AppSettings _settings = AppSettings.Load();

    private bool _changesMade;
    private readonly HashSet<Manifest> _pendingPacks = new();
    private int _pendingRemovals;
    private bool _updateCheckRunning;
    private bool _allowClose;
    private bool _loaded;

    /// <summary>Dialogs and file pickers open; the pack watch waits while any are.</summary>
    private int _modal;

    private readonly DockPanel _root;

    // Sidebar
    private ThemedButton _saveButton = null!;
    private TextBlock _unsavedLabel = null!;
    private PackList _packList = null!;
    private TextBlock _packCountLabel = null!;

    // Pack view
    private Control _packView = null!;
    private Control _noPacksView = null!;
    private KeyView _emblem = null!;
    private TextBlock _packName = null!;
    private TextBlock _packMeta = null!;
    private TextBlock _packDescription = null!;
    private TextBlock _iconsHeading = null!;
    private ThemedButton _selectAllButton = null!;
    private ThemedButton _editIconsButton = null!;
    private ThemedButton _deleteIconsButton = null!;
    private IconGrid _grid = null!;
    private Control _gridEmpty = null!;
    private DropZone _dropZone = null!;

    // Status bar
    private TextBlock _statusLabel = null!;
    private ThemedButton _updateButton = null!;
    private ThemedButton _channelButton = null!;

    public MainWindow()
    {
        Title = "Stream Deck Icon Manager";
        Width = 1180;
        Height = 760;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Window;
        TrySetIcon();

        var content = new Panel();
        _packView = BuildPackView();
        _noPacksView = BuildNoPacksView();
        content.Children.Add(_packView);
        content.Children.Add(_noPacksView);

        _root = new DockPanel();
        var status = BuildStatusBar();
        var sidebar = BuildSidebar();
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(sidebar, Dock.Left);
        _root.Children.Add(status);
        _root.Children.Add(sidebar);
        _root.Children.Add(content);
        Content = _root;

        WireDragAndDrop();
        BuildMenuAndShortcuts();
        SetStatus("Loading your icon packs...");
        _packView.IsVisible = false;
        _noPacksView.IsVisible = false;

        Opened += async (_, _) => await LoadAsync();
    }

    /// <summary>True when the window can close (or the app quit) without asking about unsaved changes.</summary>
    public bool CanCloseWithoutAsking => _allowClose || !ChangesMade;

    private void TrySetIcon()
    {
        try
        {
            Icon = new WindowIcon(new MemoryStream(ImageHelper.ReadResource("AppIcon.ico")));
        }
        catch
        {
            // The Dock icon comes from the app bundle on macOS anyway.
        }
    }

    private static Bitmap? AppIconBitmap()
    {
        try
        {
            return new Bitmap(new MemoryStream(ImageHelper.ReadResource("AppIcon.ico")));
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadAsync()
    {
        if (!Directory.Exists(_streamDeckPacksPath))
        {
            // Stream Deck is installed (its folder exists) but no packs yet: make the folder.
            var parent = Path.GetDirectoryName(_streamDeckPacksPath.TrimEnd(Path.DirectorySeparatorChar));
            if (parent is not null && Directory.Exists(parent))
            {
                try { Directory.CreateDirectory(_streamDeckPacksPath); } catch { /* reported below */ }
            }
        }

        if (!Directory.Exists(_streamDeckPacksPath))
        {
            await ShowMessageAsync("Stream Deck not found",
                $"Couldn't find the Stream Deck icon pack folder:\n{_streamDeckPacksPath}\n\nInstall the Elgato Stream Deck app and open it once, then try again.",
                MessageKind.Error);
            _allowClose = true;
            Close();
            return;
        }

        try
        {
            var dirs = Directory.GetDirectories(_streamDeckPacksPath).Where(PackPaths.IsCustomPackDirectory).ToList();
            var packs = await Task.Run(() => dirs.Select(d => new Manifest(d)).ToList());
            _allPacks.AddRange(packs);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Couldn't load packs", "Something went wrong reading your icon packs:\n" + ex.Message, MessageKind.Error);
        }

        _loaded = true;
        RefreshPackList();
        StartPackWatch();
        SetStatus(_allPacks.Count switch
        {
            0 => "No custom icon packs yet.",
            1 => "Loaded 1 icon pack from Stream Deck.",
            var n => $"Loaded {n} icon packs from Stream Deck."
        });

        await ShowLoadProblemsAsync();
        AppUpdateService.CleanupOldDownloads();
        if (AppUpdateService.TakeFailedUpdateNote() is { } updateNote)
            await ShowMessageAsync("Update didn't finish", updateNote +
                "\n\nIf this keeps happening, allow Stream Deck Icon Manager under System Settings > Privacy & Security > App Management, " +
                "or download the new version from the release page.", MessageKind.Warning);
        await CheckExistingGifsAsync();
        await CheckLargeImagesAsync();
        await CheckForUpdatesAsync(silentWhenCurrent: true);
    }

    private async Task ShowLoadProblemsAsync()
    {
        var problems = new List<string>();
        while (Manifest.LoadProblems.TryDequeue(out var problem))
            problems.Add(problem);
        if (problems.Count > 0)
            await ShowMessageAsync("Error", string.Join("\n\n", problems.Take(3)) + (problems.Count > 3 ? $"\n\n...and {problems.Count - 3} more." : ""), MessageKind.Error);
    }

    #region Layout

    private Control BuildSidebar()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(20, 22, 20, 10)
        };
        header.Children.Add(Palette.Label("ICON PACKS", Palette.Caption, Palette.TextMuted, strong: true));
        _packCountLabel = Palette.Label("", Palette.Caption, Palette.TextFaint, strong: true);
        header.Children.Add(_packCountLabel);

        _packList = new PackList
        {
            RowData = i => (_allPacks[i].Emblem, _allPacks[i].Name, _allPacks[i].Icons.Count)
        };
        _packList.SelectedIndexChanged += (_, _) => ShowSelectedPack();
        var listScroll = new ScrollViewer { Content = _packList, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        var newPack = new ThemedButton("New pack", ButtonKind.Secondary, Glyphs.Add) { HorizontalAlignment = HorizontalAlignment.Stretch };
        var import = new ThemedButton("Import pack", ButtonKind.Ghost, Glyphs.Import) { HorizontalAlignment = HorizontalAlignment.Stretch };
        newPack.Click += async (_, _) => await CreatePackAsync();
        import.Click += async (_, _) => await ImportPackAsync();
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(16, 12, 16, 16), Children = { newPack, import } };

        _unsavedLabel = Palette.Label("All changes saved", Palette.Small, Palette.TextFaint);
        _unsavedLabel.Margin = new Thickness(0, 0, 0, 8);
        _saveButton = new ThemedButton("Save to Stream Deck", ButtonKind.Secondary, Glyphs.Save)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38
        };
        _saveButton.Click += async (_, _) => await SaveToStreamDeckAsync();
        ToolTip.SetTip(_saveButton, $"Save all packs to Stream Deck ({ShortcutText("S")})");
        var saveSection = new Border
        {
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12, 16, 16),
            Child = new StackPanel { Children = { _unsavedLabel, _saveButton } }
        };

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(saveSection, Dock.Bottom);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(saveSection);
        dock.Children.Add(footer);
        dock.Children.Add(listScroll);

        return new Border
        {
            Width = 272,
            Background = Palette.Sidebar,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock
        };
    }

    private Control BuildStatusBar()
    {
        _statusLabel = Palette.Label("", Palette.Small, Palette.TextMuted);
        _statusLabel.VerticalAlignment = VerticalAlignment.Center;

        var version = Palette.Label($"Version {AppUpdateService.CurrentVersion} (Mac beta)", Palette.Small, Palette.TextFaint);
        version.VerticalAlignment = VerticalAlignment.Center;
        version.Margin = new Thickness(0, 0, 6, 0);

        _updateButton = new ThemedButton("Check for updates", ButtonKind.Ghost, Glyphs.Sync);
        _updateButton.Classes.Add("small");
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync(silentWhenCurrent: false);

        _channelButton = new ThemedButton("", ButtonKind.Ghost, Glyphs.Settings);
        _channelButton.Classes.Add("small");
        _channelButton.Flyout = BuildChannelMenu();
        ToolTip.SetTip(_channelButton, "Choose which releases Check for updates offers");
        UpdateChannelButton();

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { version, _channelButton, _updateButton }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(_statusLabel);
        grid.Children.Add(right);

        return new Border
        {
            Height = 34,
            Background = Palette.Sidebar,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 0, 10, 0),
            Child = grid
        };
    }

    private MenuFlyout BuildChannelMenu()
    {
        var heading = new MenuItem { Header = "Update channel", IsEnabled = false };
        var stable = new MenuItem();
        var beta = new MenuItem();
        stable.Click += async (_, _) => await SetUpdateChannelAsync(UpdateChannel.Stable);
        beta.Click += async (_, _) => await SetUpdateChannelAsync(UpdateChannel.Beta);

        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        menu.Items.Add(heading);
        menu.Items.Add(new Separator());
        menu.Items.Add(stable);
        menu.Items.Add(beta);
        menu.Opening += (_, _) =>
        {
            stable.Header = (_settings.UpdateChannel == UpdateChannel.Stable ? "✓  " : "     ") + "Stable";
            beta.Header = (_settings.UpdateChannel == UpdateChannel.Beta ? "✓  " : "     ") + "Beta (recommended for the Mac beta)";
        };
        return menu;
    }

    private void UpdateChannelButton()
    {
        var beta = _settings.UpdateChannel == UpdateChannel.Beta;
        _channelButton.Text = beta ? "Beta channel" : "Stable channel";
        _channelButton.Classes.Set("warning", beta);
    }

    private async Task SetUpdateChannelAsync(UpdateChannel channel)
    {
        if (channel == _settings.UpdateChannel)
            return;

        _settings.UpdateChannel = channel;
        _settings.Save();
        UpdateChannelButton();

        if (channel == UpdateChannel.Stable)
        {
            SetStatus("Switched to stable updates. Mac versions are betas for now, so you'll hear about the first stable Mac release.");
            await CheckForUpdatesAsync(silentWhenCurrent: true);
        }
        else
        {
            SetStatus("Switched to beta updates.", Palette.Warning);
            await CheckForUpdatesAsync(silentWhenCurrent: false);
        }
    }

    private Control BuildPackView()
    {
        // Header: emblem, name, details, pack actions
        _emblem = new KeyView
        {
            Width = 100,
            Height = 100,
            CornerRadiusValue = 16,
            Clickable = true,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 20, 0)
        };
        _emblem.Click += async (_, _) => await ChangeEmblemAsync();
        ToolTip.SetTip(_emblem, "Change the pack emblem shown in the Stream Deck icon library");

        _packName = Palette.Label("", Palette.Title, Palette.Text, strong: true);
        _packMeta = Palette.Label("", Palette.Body, Palette.TextMuted);
        _packMeta.Margin = new Thickness(0, 4, 0, 2);
        _packDescription = Palette.Label("", Palette.Body, Palette.TextFaint);

        var edit = new ThemedButton("Edit details", ButtonKind.Secondary, Glyphs.Edit);
        var emblem = new ThemedButton("Change emblem", ButtonKind.Secondary, Glyphs.Picture);
        var export = new ThemedButton("Export", ButtonKind.Secondary, Glyphs.Export);
        ToolTip.SetTip(export, "Save as a .streamDeckIconPack file. Anyone with Stream Deck can open it to install the pack, with or without this app.");
        var delete = new ThemedButton("Delete pack", ButtonKind.Danger, Glyphs.Delete);
        edit.Click += async (_, _) => await EditPackDetailsAsync();
        emblem.Click += async (_, _) => await ChangeEmblemAsync();
        export.Click += async (_, _) => await ExportPackAsync();
        delete.Click += async (_, _) => await DeletePackAsync();
        var packActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { edit, emblem, export, delete }
        };

        var details = new StackPanel { Children = { _packName, _packMeta, _packDescription, packActions } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(details, 1);
        header.Children.Add(_emblem);
        header.Children.Add(details);

        // Toolbar above the grid
        _iconsHeading = Palette.Label("", Palette.Body, Palette.Text, strong: true);
        _iconsHeading.VerticalAlignment = VerticalAlignment.Center;
        _selectAllButton = new ThemedButton("Select all", ButtonKind.Ghost);
        _editIconsButton = new ThemedButton("Name & tags", ButtonKind.Secondary, Glyphs.Tag);
        _deleteIconsButton = new ThemedButton("Delete", ButtonKind.Danger, Glyphs.Delete);
        _selectAllButton.Click += (_, _) =>
        {
            _grid.SelectAll();
            _grid.Focus();
        };
        _editIconsButton.Click += async (_, _) => await EditSelectedIconsAsync();
        _deleteIconsButton.Click += async (_, _) => await DeleteSelectedIconsAsync();
        var iconActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _selectAllButton, _editIconsButton, _deleteIconsButton }
        };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 18, 0, 10) };
        Grid.SetColumn(iconActions, 1);
        toolbar.Children.Add(_iconsHeading);
        toolbar.Children.Add(iconActions);

        // The grid sits on a plate, like the face of the deck.
        _grid = new IconGrid { ContextMenu = BuildGridMenu() };
        _grid.SelectionChanged += (_, _) => UpdateIconActions();
        _grid.ItemActivated += async (_, _) => await EditSelectedIconsAsync();
        _grid.DeleteRequested += async (_, _) => await DeleteSelectedIconsAsync();
        var gridScroll = new ScrollViewer { Content = _grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _gridEmpty = BuildGridEmptyState();
        var plate = new Border
        {
            Background = Palette.Sidebar,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            ClipToBounds = true,
            Child = new Panel { Children = { gridScroll, _gridEmpty } }
        };

        _dropZone = new DropZone { Margin = new Thickness(0, 16, 0, 0) };
        _dropZone.BrowseRequested += async (_, _) => await BrowseForImagesAsync();
        _dropZone.AddButton.Click += async (_, _) => await AddQueuedIconsAsync();
        _dropZone.ClearButton.Click += (_, _) => ClearQueue();

        var view = new DockPanel { Margin = new Thickness(32, 26, 32, 20) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_dropZone, Dock.Bottom);
        view.Children.Add(header);
        view.Children.Add(toolbar);
        view.Children.Add(_dropZone);
        view.Children.Add(plate);
        return view;
    }

    private static Control BuildGridEmptyState()
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 3; i++)
            keys.Children.Add(new KeyView { Width = 56, Height = 56, CornerRadiusValue = 9, Ghost = true, GhostGlyph = i == 1 ? Glyphs.Photo : null });

        var title = Palette.Label("This pack has no icons yet", Palette.Body, Palette.Text, strong: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 18, 0, 2);
        var body = Palette.Label("Drop images anywhere in this window, or click the tray below to choose files.", Palette.Body, Palette.TextMuted, wrap: true);
        body.TextAlignment = TextAlignment.Center;
        body.MaxWidth = 420;

        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24, 0, 24, 30),
            IsHitTestVisible = false,
            Children = { keys, title, body }
        };
    }

    private ContextMenu BuildGridMenu()
    {
        var edit = new MenuItem { Header = "Edit name and tags" };
        var selectAll = new MenuItem { Header = "Select all" };
        var delete = new MenuItem { Header = "Delete" };
        delete.Classes.Add("danger");
        edit.Click += async (_, _) => await EditSelectedIconsAsync();
        selectAll.Click += (_, _) => _grid.SelectAll();
        delete.Click += async (_, _) => await DeleteSelectedIconsAsync();

        var menu = new ContextMenu();
        menu.Items.Add(edit);
        menu.Items.Add(selectAll);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Opening += (_, e) =>
        {
            var any = _grid.SelectedIndices.Count > 0;
            edit.IsEnabled = any;
            delete.IsEnabled = any;
            if (SelectedPack is null || SelectedPack.Icons.Count == 0)
                e.Cancel = true;
        };
        return menu;
    }

    private Control BuildNoPacksView()
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        var logo = AppIconBitmap();
        for (var i = 0; i < 3; i++)
            keys.Children.Add(new KeyView { Width = 64, Height = 64, CornerRadiusValue = 11, Image = i == 1 ? logo : null });

        var title = Palette.Label("Make your first icon pack", Palette.DialogTitle, Palette.Text, strong: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 0, 0, 6);
        var body = Palette.Label("A pack is a named set of icons that appears in the Stream Deck icon library. Create one, then drop your images into it.",
            Palette.Body, Palette.TextMuted, wrap: true);
        body.TextAlignment = TextAlignment.Center;
        body.MaxWidth = 420;
        body.Margin = new Thickness(0, 0, 0, 20);

        var create = new ThemedButton("New pack", ButtonKind.Primary, Glyphs.Add);
        var import = new ThemedButton("Import a pack", ButtonKind.Secondary, Glyphs.Import);
        create.Click += async (_, _) => await CreatePackAsync();
        import.Click += async (_, _) => await ImportPackAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { create, import } };

        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 40),
            Children = { keys, title, body, buttons }
        };
    }

    /// <summary>
    /// Shortcuts: on macOS a native File menu with Cmd shortcuts; elsewhere Ctrl key handling.
    /// </summary>
    private void BuildMenuAndShortcuts()
    {
        if (OperatingSystem.IsMacOS())
        {
            NativeMenuItem Item(string header, Func<Task> action, KeyGesture? gesture = null)
            {
                var item = new NativeMenuItem(header) { Gesture = gesture };
                item.Click += async (_, _) =>
                {
                    if (_root.IsEnabled && _modal == 0 && _loaded)
                        await action();
                };
                return item;
            }

            var file = new NativeMenu
            {
                Item("New Pack…", CreatePackAsync, new KeyGesture(Key.N, KeyModifiers.Meta)),
                Item("Import Pack…", () => ImportPackAsync(), new KeyGesture(Key.I, KeyModifiers.Meta | KeyModifiers.Shift)),
                Item("Add Images…", BrowseForImagesAsync, new KeyGesture(Key.O, KeyModifiers.Meta)),
                new NativeMenuItemSeparator(),
                Item("Export Pack…", ExportPackAsync, new KeyGesture(Key.E, KeyModifiers.Meta)),
                new NativeMenuItemSeparator(),
                Item("Save to Stream Deck", async () => await SaveToStreamDeckAsync(), new KeyGesture(Key.S, KeyModifiers.Meta))
            };
            NativeMenu.SetMenu(this, new NativeMenu { new NativeMenuItem("File") { Menu = file } });
            return;
        }

        KeyDown += async (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !_root.IsEnabled || _modal > 0 || !_loaded)
                return;

            switch (e.Key)
            {
                case Key.S:
                    e.Handled = true;
                    await SaveToStreamDeckAsync();
                    break;
                case Key.O when SelectedPack is not null:
                    e.Handled = true;
                    await BrowseForImagesAsync();
                    break;
                case Key.N:
                    e.Handled = true;
                    await CreatePackAsync();
                    break;
            }
        };
    }

    private static string ShortcutText(string key) => OperatingSystem.IsMacOS() ? "⌘" + key : "Ctrl+" + key;

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
            _unsavedLabel.Foreground = value ? Palette.Warning : Palette.TextFaint;
            _saveButton.Kind = value ? ButtonKind.Primary : ButtonKind.Secondary;
        }
    }

    private void MarkChanged(Manifest pack, string status)
    {
        _pendingPacks.Add(pack);
        ChangesMade = true;
        SetStatus(status + " Save to Stream Deck when you're done.");
    }

    private void SetStatus(string text, IBrush? color = null)
    {
        _statusLabel.Text = text;
        _statusLabel.Foreground = color ?? Palette.TextMuted;
        ToolTip.SetTip(_statusLabel, text);
    }

    private void SetBusy(bool busy)
    {
        _root.IsEnabled = !busy;
        Cursor = busy ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
    }

    private void RefreshPackList(Manifest? select = null)
    {
        var target = select ?? SelectedPack;
        _packList.Count = _allPacks.Count;
        _packCountLabel.Text = _allPacks.Count.ToString();

        var hasPacks = _allPacks.Count > 0;
        _packView.IsVisible = hasPacks;
        _noPacksView.IsVisible = !hasPacks;

        if (hasPacks)
        {
            var index = target is null ? 0 : Math.Max(0, _allPacks.IndexOf(target));
            if (_packList.SelectedIndex == index)
                ShowSelectedPack();
            else
                _packList.SelectedIndex = index; // raises SelectedIndexChanged -> ShowSelectedPack
        }
        else
        {
            _packList.SelectedIndex = -1;
            ShowSelectedPack();
        }

        _packList.Refresh();
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
            $"{i.Name}\nTags: {IconDetailsDialog.FormatTags(i.Tags)}",
            i.Data,
            Path.GetExtension(i.Path))), keepSelection);
        _gridEmpty.IsVisible = pack.Icons.Count == 0;
        _packList.Refresh();
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
        _iconsHeading.Foreground = selected > 0 ? Palette.Accent : Palette.Text;

        _selectAllButton.IsVisible = total > 0 && selected < total;
        _editIconsButton.IsEnabled = selected > 0;
        _deleteIconsButton.IsEnabled = selected > 0;
        _editIconsButton.Text = selected > 1 ? $"Name & tags ({selected})" : "Name & tags";
    }

    private void UpdateQueueTray() =>
        _dropZone.SetQueue(_queue.Select(q => (IImage)q.Picture).ToList(), SelectedPack?.Name ?? "");

    /// <summary>Shows a dialog modally, pausing the pack watch while it's open.</summary>
    private async Task<bool> ShowDialogAsync(Window dialog)
    {
        _modal++;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _modal--;
        }
    }

    private async Task<string?> ShowMessageAsync(string title, string text, MessageKind kind = MessageKind.Info, params string[] buttons)
    {
        _modal++;
        try
        {
            return await MessageDialog.ShowAsync(this, title, text, kind, buttons);
        }
        finally
        {
            _modal--;
        }
    }

    public async Task ShowAboutAsync()
    {
        await ShowMessageAsync("Stream Deck Icon Manager",
            $"Version {AppUpdateService.CurrentVersion} (Mac beta, {AppUpdateService.MacArchitecture})\n\n" +
            $"Icon packs: {_streamDeckPacksPath}\nError log: {CrashLog.FilePath}\n\nBy Sutton Tools.");
    }

    #endregion

    #region File pickers

    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = ImageHelper.Extensions.Select(e => "*" + e).ToArray()
    };

    private static readonly FilePickerFileType PackFiles = new("Icon packs")
    {
        Patterns = new[] { "*" + IconPackFile.Extension, "*.zip" }
    };

    private async Task<IReadOnlyList<string>> PickFilesAsync(string title, FilePickerFileType type, bool multiple)
    {
        _modal++;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = multiple,
                FileTypeFilter = new[] { type }
            });
            return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        }
        finally
        {
            _modal--;
        }
    }

    #endregion

    #region Adding icons

    private async Task BrowseForImagesAsync()
    {
        if (SelectedPack is null)
            return;

        var files = await PickFilesAsync("Choose images for your icons", ImageFiles, multiple: true);
        if (files.Count > 0)
            await QueueImagesAsync(files);
    }

    private async Task QueueImagesAsync(IEnumerable<string> files)
    {
        var list = files.ToList();
        SetBusy(true);
        var (loaded, skipped) = await Task.Run(() =>
        {
            var ok = new List<PackIcon>();
            var bad = 0;
            foreach (var file in list)
            {
                if (!ImageHelper.IsSupported(file))
                {
                    bad++;
                    continue;
                }

                try
                {
                    ok.Add(PackIcon.FromFile(file));
                }
                catch
                {
                    bad++;
                }
            }

            return (ok, bad);
        });
        SetBusy(false);

        _queue.AddRange(loaded);
        UpdateQueueTray();

        if (skipped > 0)
            SetStatus($"Skipped {skipped} {(skipped == 1 ? "file" : "files")} that {(skipped == 1 ? "isn't" : "aren't")} a readable {ImageHelper.FormatList} image.", Palette.Warning);
        else if (loaded.Count > 0)
            SetStatus($"{_queue.Count} {(_queue.Count == 1 ? "image" : "images")} ready. Click Add to name them and put them in the pack.");
    }

    private async Task AddQueuedIconsAsync()
    {
        var pack = SelectedPack;
        if (pack is null || _queue.Count == 0)
            return;

        var drafts = _queue.Select(q => new IconDraft(q.Picture, q.Name, q.Tags, q.Data, Path.GetExtension(q.Path))).ToList();
        var oversized = _queue.Count(q => IconResizer.IsOversized(q.Data, Path.GetExtension(q.Path)));
        var dialog = new IconDetailsDialog(drafts, pack.Name, editing: false, oversized, _settings.KeepFullSizeImages);
        if (!await ShowDialogAsync(dialog))
            return;

        if (oversized > 0 && dialog.KeepFullSize != _settings.KeepFullSizeImages)
        {
            _settings.KeepFullSizeImages = dialog.KeepFullSize;
            _settings.Save();
        }

        // Resize first: an oversized animated GIF comes out as a 144 px animated WEBP, so it
        // doesn't need converting afterwards.
        var resized = await FitImagesAsync(_queue);
        var converted = await ConvertGifsAsync(_queue);

        for (var i = 0; i < drafts.Count; i++)
            pack.AddIcon(drafts[i].Name, drafts[i].Tags, _queue[i]);

        var count = _queue.Count;
        _queue.Clear();
        ShowSelectedPack();
        MarkChanged(pack, $"Added {count} {(count == 1 ? "icon" : "icons")} to {pack.Name}{ProcessingNote(resized, converted)}.");
    }

    private async Task<int> FitImagesAsync(IReadOnlyList<PackIcon> icons)
    {
        if (_settings.KeepFullSizeImages)
            return 0;

        var large = icons.Where(i => IconResizer.IsOversized(i.Data, Path.GetExtension(i.Path))).ToList();
        if (large.Count == 0)
            return 0;

        SetStatus(large.Count == 1
            ? $"Scaling 1 large image down to {IconResizer.TargetSize} px..."
            : $"Scaling {large.Count} large images down to {IconResizer.TargetSize} px...");
        SetBusy(true);
        var oldPictures = new List<Bitmap>();
        var resized = 0;
        try
        {
            foreach (var icon in large)
            {
                if (await IconResizer.FitWithPreviewAsync(icon.Data, Path.GetExtension(icon.Path)) is not { } fit)
                    continue;

                var (data, ext, picture) = fit;
                oldPictures.Add(icon.Picture);
                icon.Data = data;
                icon.Picture = picture;
                icon.Path = UniquePath(icons, icon, Path.ChangeExtension(icon.Path, "." + ext));
                resized++;
            }
        }
        finally
        {
            SetBusy(false);
        }

        UpdateQueueTray();
        foreach (var old in oldPictures)
            old.Dispose();

        return resized;
    }

    private static string UniquePath(IEnumerable<PackIcon> icons, PackIcon self, string path)
    {
        var taken = icons.Where(i => !ReferenceEquals(i, self)).Select(i => Path.GetFileName(i.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(Path.GetFileName(path)))
            return path;

        var folder = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!taken.Contains(candidate))
                return folder.Length == 0 ? candidate : Path.Combine(folder, candidate);
        }
    }

    private Task<(byte[] Data, string Extension, Bitmap Preview)?> FitEmblemAsync(byte[] data, string extension) =>
        _settings.KeepFullSizeImages
            ? Task.FromResult<(byte[] Data, string Extension, Bitmap Preview)?>(null)
            : IconResizer.FitWithPreviewAsync(data, extension);

    private async Task<int> ConvertGifsAsync(IEnumerable<PackIcon> icons)
    {
        var gifs = icons.Where(i => GifConverter.IsGif(i.Path)).ToList();
        if (!_settings.ConvertGifsToWebp || gifs.Count == 0 || !GifConverter.IsAvailable)
            return 0;

        SetStatus(gifs.Count == 1 ? "Converting 1 GIF to animated WEBP..." : $"Converting {gifs.Count} GIFs to animated WEBP...");
        SetBusy(true);
        try
        {
            return await GifConverter.ConvertAllAsync(gifs);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckExistingGifsAsync()
    {
        if (!GifConverter.IsAvailable || _settings.ExistingGifs == ExistingGifsChoice.Keep)
            return;

        var packsWithGifs = _allPacks
            .Select(p => (Pack: p, Gifs: p.Icons.Count(i => GifConverter.IsGif(i.Path))))
            .Where(x => x.Gifs > 0)
            .ToList();
        if (packsWithGifs.Count == 0)
            return;

        List<Manifest> changed;
        int converted, failed;

        if (_settings.ExistingGifs == ExistingGifsChoice.Convert)
        {
            var gifIcons = packsWithGifs
                .Select(p => (p.Pack, Icons: p.Pack.Icons.Where(i => GifConverter.IsGif(i.Path)).ToList()))
                .ToList();
            converted = await ConvertGifsAsync(gifIcons.SelectMany(g => g.Icons));
            failed = gifIcons.Sum(g => g.Icons.Count) - converted;
            changed = gifIcons.Where(g => g.Icons.Any(i => !GifConverter.IsGif(i.Path))).Select(g => g.Pack).ToList();
        }
        else
        {
            var dialog = new ConvertGifsDialog(packsWithGifs);
            await ShowDialogAsync(dialog);
            if (dialog.Choice is null)
                return; // Closed without choosing: ask again next time.

            _settings.ExistingGifs = dialog.Choice;
            _settings.Save();
            if (dialog.Choice == ExistingGifsChoice.Keep)
            {
                SetStatus("Left your existing GIFs as they are. New GIFs you add are still converted to WEBP.");
                return;
            }

            changed = dialog.ChangedPacks;
            converted = dialog.Converted;
            failed = dialog.Failed;
        }

        if (converted == 0)
        {
            if (failed > 0)
                SetStatus($"Couldn't convert {failed} GIF{(failed == 1 ? "" : "s")}; {(failed == 1 ? "it was" : "they were")} left as {(failed == 1 ? "a GIF" : "GIFs")}.", Palette.Warning);
            return;
        }

        foreach (var pack in changed)
            _pendingPacks.Add(pack);
        ChangesMade = true;
        ShowSelectedPack(keepSelection: true);

        var failedNote = failed > 0 ? $" {failed} couldn't be converted and stayed as GIFs." : "";
        SetStatus($"Converted {converted} GIF{(converted == 1 ? "" : "s")} to animated WEBP in {changed.Count} pack{(changed.Count == 1 ? "" : "s")}.{failedNote} Save to Stream Deck to apply.",
            failed > 0 ? Palette.Warning : Palette.Success);
    }

    private async Task CheckLargeImagesAsync()
    {
        if (_settings.ExistingLargeImages is not null || _settings.KeepFullSizeImages)
            return;

        var keepGifs = _settings.ExistingGifs == ExistingGifsChoice.Keep || !_settings.ConvertGifsToWebp;
        var large = _allPacks
            .Select(p => new LargeImages(p,
                p.Icons.Where(i => !(keepGifs && GifConverter.IsGif(i.Path)) && IconResizer.IsOversized(i.Data, Path.GetExtension(i.Path))).ToList(),
                IconResizer.IsOversized(p.EmblemData, p.EmblemExtension)))
            .Where(l => l.Icons.Count > 0 || l.Emblem)
            .ToList();
        if (large.Count == 0)
            return;

        var before = large.Sum(l => l.Bytes);
        var dialog = new ShrinkImagesDialog(large);
        await ShowDialogAsync(dialog);
        if (dialog.Choice is null)
            return;

        _settings.ExistingLargeImages = dialog.Choice;
        _settings.Save();
        if (dialog.Choice == ExistingLargeImagesChoice.Keep)
        {
            SetStatus($"Left your large images as they are. New images you add are still scaled to {IconResizer.TargetSize} px unless you choose to keep them full size.");
            return;
        }

        var results = dialog.Results;
        var failed = dialog.Failed;
        var oldPictures = new List<IDisposable>();
        var changed = new HashSet<Manifest>();
        var saved = 0L;
        foreach (var r in results)
        {
            if (!_allPacks.Contains(r.Pack) || (r.Icon is not null && !r.Pack.Icons.Contains(r.Icon)))
            {
                r.Preview.Dispose();
                continue;
            }

            if (r.Icon is null)
            {
                saved += r.Pack.EmblemData.Length - r.Data.Length;
                var oldEmblem = r.Pack.Emblem;
                r.Pack.SetEmblem(r.Data, r.Extension, r.Preview, disposeOld: false);
                oldPictures.Add(oldEmblem);
            }
            else
            {
                saved += r.Icon.Data.Length - r.Data.Length;
                oldPictures.Add(r.Icon.Picture);
                r.Icon.Data = r.Data;
                r.Icon.Picture = r.Preview;
                r.Icon.Path = Path.ChangeExtension(r.Icon.Path, "." + r.Extension);
            }

            changed.Add(r.Pack);
        }

        if (changed.Count > 0)
        {
            foreach (var pack in changed)
                _pendingPacks.Add(pack);
            ChangesMade = true;
            RefreshPackList(SelectedPack);
        }

        foreach (var old in oldPictures)
            old.Dispose();

        var shrunk = results.Count;
        if (shrunk == 0)
        {
            if (failed > 0)
                SetStatus($"Couldn't shrink {failed} image{(failed == 1 ? "" : "s")}; {(failed == 1 ? "it was" : "they were")} left as {(failed == 1 ? "it is" : "they are")}.", Palette.Warning);
            return;
        }

        var failedNote = failed > 0 ? $" {failed} couldn't be resized and stayed full size." : "";
        SetStatus($"Shrank {shrunk} image{(shrunk == 1 ? "" : "s")} in {changed.Count} pack{(changed.Count == 1 ? "" : "s")}, " +
                  $"from {ShrinkImagesDialog.FormatSize(before)} to {ShrinkImagesDialog.FormatSize(Math.Max(0, before - saved))}.{failedNote} Save to Stream Deck to apply.",
            failed > 0 ? Palette.Warning : Palette.Success);
    }

    private static string ProcessingNote(int resized, int converted)
    {
        var parts = new List<string>();
        if (resized > 0)
            parts.Add(resized == 1
                ? $"1 large image scaled down to {IconResizer.TargetSize} px"
                : $"{resized} large images scaled down to {IconResizer.TargetSize} px");
        if (converted > 0)
            parts.Add(converted == 1
                ? "1 GIF converted to animated WEBP so Stream Deck can preview it"
                : $"{converted} GIFs converted to animated WEBP so Stream Deck can preview them");
        return parts.Count == 0 ? "" : $" ({string.Join("; ", parts)})";
    }

    private void ClearQueue()
    {
        var old = _queue.ToList();
        _queue.Clear();
        UpdateQueueTray();
        foreach (var icon in old)
            icon.Picture.Dispose();
        SetStatus("Cleared the images waiting to be added.");
    }

    #endregion

    #region Editing icons

    private async Task EditSelectedIconsAsync()
    {
        var pack = SelectedPack;
        if (pack is null || _grid.SelectedIndices.Count == 0)
            return;

        var indices = _grid.SelectedIndices.OrderBy(i => i).ToList();
        var drafts = indices.Select(i => new IconDraft(pack.Icons[i].Picture, pack.Icons[i].Name, pack.Icons[i].Tags,
            pack.Icons[i].Data, Path.GetExtension(pack.Icons[i].Path))).ToList();

        if (!await ShowDialogAsync(new IconDetailsDialog(drafts, pack.Name, editing: true)))
            return;

        for (var i = 0; i < indices.Count; i++)
        {
            pack.Icons[indices[i]].Name = drafts[i].Name;
            pack.Icons[indices[i]].Tags = drafts[i].Tags;
        }

        ShowSelectedPack(keepSelection: true);
        MarkChanged(pack, indices.Count == 1 ? $"Updated {drafts[0].Name}." : $"Updated {indices.Count} icons.");
    }

    private async Task DeleteSelectedIconsAsync()
    {
        var pack = SelectedPack;
        if (pack is null || _grid.SelectedIndices.Count == 0)
            return;

        var indices = _grid.SelectedIndices.OrderByDescending(i => i).ToList();
        if (indices.Count == pack.Icons.Count && indices.Count > 1 &&
            await ShowMessageAsync("Remove all icons", $"Remove all {indices.Count} icons from {pack.Name}?", MessageKind.Warning, "Cancel", "Delete all") != "Delete all")
            return;

        // Take them off screen before freeing their previews.
        var removed = indices.Select(i => pack.Icons[i]).ToList();
        foreach (var index in indices)
            pack.Icons.RemoveAt(index);
        ShowSelectedPack();
        foreach (var icon in removed)
            icon.Picture.Dispose();

        MarkChanged(pack, indices.Count == 1 ? "Removed 1 icon." : $"Removed {indices.Count} icons.");
    }

    #endregion

    #region Packs

    private string? PackNameProblem(string name, Manifest? except = null) =>
        _allPacks.Any(m => !ReferenceEquals(m, except) && PackPaths.NamesConflict(m.Name, name))
            ? $"You already have a pack called \"{name}\"."
            : null;

    private async Task CreatePackAsync()
    {
        var dialog = new PackDetailsDialog(name => PackNameProblem(name));
        if (!await ShowDialogAsync(dialog))
            return;

        try
        {
            var created = new Manifest(dialog.PackName, dialog.Description, dialog.Author, _streamDeckPacksPath);
            created.SaveManifest();
            var loaded = new Manifest(created.IconPackPath);
            created.DisposeAllImages();
            _allPacks.Add(loaded);
            RefreshPackList(loaded);
            RememberPackStamps();
            MarkChanged(loaded, $"Created {loaded.Name}.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Couldn't create the pack", ex.Message, MessageKind.Error);
        }
    }

    private async Task EditPackDetailsAsync()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        var dialog = new PackDetailsDialog(name => PackNameProblem(name, pack), pack);
        if (!await ShowDialogAsync(dialog))
            return;

        var renamed = !PackPaths.NamesConflict(pack.Name, dialog.PackName);
        pack.Name = dialog.PackName;
        pack.Description = dialog.Description;
        pack.Author = dialog.Author;

        if (renamed)
        {
            try
            {
                _pendingRemovals++;
                var oldPath = pack.IconPackPath;
                pack.NewPackPath(_streamDeckPacksPath);
                pack.SaveManifest();
                new DirectoryInfo(oldPath).Delete(true);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Couldn't rename the pack folder", ex.Message, MessageKind.Error);
            }
        }

        RefreshPackList(pack);
        RememberPackStamps();
        MarkChanged(pack, $"Updated details for {pack.Name}.");
    }

    private async Task ChangeEmblemAsync()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        var files = await PickFilesAsync("Choose an emblem for " + pack.Name, ImageFiles, multiple: false);
        if (files.Count == 0)
            return;

        byte[] data;
        string ext;
        Bitmap preview;
        try
        {
            (data, preview) = ImageHelper.Load(files[0]);
            ext = ImageHelper.ExtensionOf(files[0]);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Change emblem", "Couldn't read that image:\n" + ex.Message, MessageKind.Warning);
            return;
        }

        var resized = false;
        SetBusy(true);
        try
        {
            if (await FitEmblemAsync(data, ext) is { } fit)
            {
                preview.Dispose();
                (data, ext, preview) = fit;
                resized = true;
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (!_allPacks.Contains(pack))
        {
            preview.Dispose();
            return;
        }

        var old = pack.Emblem;
        pack.SetEmblem(data, ext, preview, disposeOld: false);
        ShowSelectedPack(keepSelection: true);
        old.Dispose();
        MarkChanged(pack, $"Changed the emblem for {pack.Name}{(resized ? $" (scaled down to {IconResizer.TargetSize} px)" : "")}.");
    }

    private async Task DeletePackAsync()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        var icons = pack.Icons.Count == 1 ? "its 1 icon" : $"all {pack.Icons.Count} icons";
        if (await ShowMessageAsync("Delete pack",
                $"Delete \"{pack.Name}\" and {icons}?\n\nThe pack is removed from Stream Deck right away. This can't be undone. Export it first if you want a copy.",
                MessageKind.Warning, "Cancel", "Delete pack") != "Delete pack")
            return;

        var name = pack.Name;
        var index = _allPacks.IndexOf(pack);
        try
        {
            new DirectoryInfo(pack.IconPackPath).Delete(true);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Couldn't delete the pack", ex.Message, MessageKind.Error);
            return;
        }

        _allPacks.RemoveAt(index);
        _pendingPacks.Remove(pack);
        _pendingRemovals++;

        RefreshPackList(_allPacks.Count > 0 ? _allPacks[Math.Min(index, _allPacks.Count - 1)] : null);
        pack.DisposeAllImages();
        ChangesMade = true;
        SetStatus($"Deleted {name}. Restart Stream Deck to remove it from the icon library.");
    }

    private async Task ExportPackAsync()
    {
        var pack = SelectedPack;
        if (pack is null)
            return;

        IStorageFile? file;
        _modal++;
        try
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export " + pack.Name,
                SuggestedFileName = pack.Name + IconPackFile.Extension,
                DefaultExtension = IconPackFile.Extension.TrimStart('.'),
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType("Stream Deck icon pack") { Patterns = new[] { "*" + IconPackFile.Extension } } }
            });
        }
        finally
        {
            _modal--;
        }

        var path = file?.TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            IconPackFile.Export(pack, path);
            SetStatus($"Exported {pack.Name}. Anyone with Stream Deck can open the file to install it.", Palette.Success);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Export failed", "Couldn't export the pack:\n" + ex.Message, MessageKind.Error);
        }
    }

    private async Task ImportPackAsync(string? packFile = null)
    {
        if (packFile is null)
        {
            var files = await PickFilesAsync("Import an icon pack", PackFiles, multiple: false);
            if (files.Count == 0)
                return;
            packFile = files[0];
        }

        var extractRoot = Path.Combine(Path.GetTempPath(), "sdim-import-" + Guid.NewGuid().ToString("N"));
        Manifest? import = null;

        try
        {
            SetBusy(true);
            int skipped;
            try
            {
                var file = packFile;
                (import, skipped) = await Task.Run(() =>
                {
                    var packDir = IconPackFile.Extract(file, extractRoot);
                    if (packDir is null)
                        return ((Manifest?)null, 0);
                    var read = Manifest.ReadForImport(packDir, out var s);
                    return (read, s);
                });
            }
            finally
            {
                SetBusy(false);
            }

            if (import is null)
            {
                await ShowMessageAsync("Nothing to import", "That file doesn't contain a Stream Deck icon pack. Choose a .streamDeckIconPack file, or a .zip exported from this app.");
                return;
            }

            if (import.Icons.Count == 0 && skipped > 0)
            {
                await ShowMessageAsync("Nothing to import",
                    $"None of the {skipped} icons in this pack can be imported. Stream Deck icon packs use {ImageHelper.FormatList} images; this pack's icons are missing or in another format.");
                return;
            }

            if (PackNameProblem(import.Name) is not null)
            {
                var rename = new RenameImportDialog(import.Name, name => PackNameProblem(name));
                if (!await ShowDialogAsync(rename))
                {
                    SetStatus("Import cancelled.");
                    return;
                }

                import.ChangePackName(rename.PackName);
            }

            var resized = await FitImagesAsync(import.Icons);
            var converted = await ConvertGifsAsync(import.Icons);
            if (await FitEmblemAsync(import.EmblemData, import.EmblemExtension) is { } emblem)
                import.SetEmblem(emblem.Data, emblem.Extension, emblem.Preview);
            import.NewPackPath(_streamDeckPacksPath);
            import.SaveManifest();
            var loaded = new Manifest(import.IconPackPath);
            _allPacks.Add(loaded);
            RefreshPackList(loaded);
            _pendingPacks.Add(loaded);
            RememberPackStamps();
            ChangesMade = true;
            var skippedNote = skipped == 0 ? "" : $" Skipped {skipped} icon{(skipped == 1 ? "" : "s")} that {(skipped == 1 ? "was" : "were")} missing or not a {ImageHelper.FormatList} image.";
            SetStatus($"Imported {loaded.Name} with {loaded.Icons.Count} icons{ProcessingNote(resized, converted)}.{skippedNote} Save to Stream Deck to see it.", skipped == 0 ? Palette.Success : Palette.Warning);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Import failed", "Couldn't import the pack:\n" + ex.Message, MessageKind.Error);
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
        RememberPackStamps();
    }

    /// <summary>Opens the Save dialog. Returns false if the user cancelled.</summary>
    private async Task<bool> SaveToStreamDeckAsync()
    {
        if (!_loaded)
            return false;

        var changed = _allPacks.Where(_pendingPacks.Contains).ToList();
        var dialog = new ApplyDialog(changed, _pendingRemovals, _settings.LastApplyMode, SaveAllPacks, p => p.SaveManifest());
        await ShowDialogAsync(dialog);

        if (dialog.Outcome == ApplyOutcome.Cancelled)
            return false;

        _settings.LastApplyMode = dialog.ChosenMode;
        _settings.Save();
        _pendingPacks.Clear();
        _pendingRemovals = 0;
        RememberPackStamps();
        ChangesMade = false;

        switch (dialog.Outcome)
        {
            case ApplyOutcome.Restarted:
                SetStatus("Saved and restarted Stream Deck. Your changes are in its icon library.", Palette.Success);
                break;
            case ApplyOutcome.AppliedLive:
                SetStatus(changed.Count == 1
                    ? $"Applied {changed[0].Name} live. It's in Stream Deck's icon library now."
                    : $"Applied {changed.Count} packs live. They're in Stream Deck's icon library now.", Palette.Success);
                break;
            default:
                SetStatus("Saved. Some changes will show up after Stream Deck restarts.", Palette.Warning);
                break;
        }

        return true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose && ChangesMade)
        {
            e.Cancel = true;
            if (_modal == 0)
                Dispatcher.UIThread.Post(async () => await AskToSaveThenCloseAsync());
            base.OnClosing(e);
            return;
        }

        base.OnClosing(e);
    }

    private async Task AskToSaveThenCloseAsync()
    {
        var answer = await ShowMessageAsync("Unsaved changes", "Save your changes to Stream Deck before closing?",
            MessageKind.Question, "Don't save", "Cancel", "Save");

        if (answer is null or "Cancel")
            return;

        if (answer == "Save" && !await SaveToStreamDeckAsync())
            return;

        _allowClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _packWatchTimer.Stop();
        foreach (var icon in _queue)
            icon.Picture.Dispose();
        CleanupTempIconPacks();
    }

    private static void CleanupTempIconPacks()
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(Path.GetTempPath()))
            {
                var name = Path.GetFileName(dir);
                if (name.Contains(".sdIconPack", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("sdim-", StringComparison.OrdinalIgnoreCase))
                    TryDeleteDirectory(dir);
            }
        }
        catch
        {
            // Best-effort.
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

    #region Drag and drop

    private void WireDragAndDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => _dropZone.DragActive = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

#pragma warning disable CS0618 // IDataObject.GetFiles is the drag-and-drop API in Avalonia 11.
    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToArray() ?? Array.Empty<string>();
#pragma warning restore CS0618

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        var ready = _loaded && _root.IsEnabled;
        var images = ready && SelectedPack is not null && files.Any(ImageHelper.IsSupported);
        var pack = ready && files.Length == 1 && IconPackFile.IsImportable(files[0]);
        e.DragEffects = images || pack ? DragDropEffects.Copy : DragDropEffects.None;
        _dropZone.DragActive = images;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _dropZone.DragActive = false;
        if (!_loaded || !_root.IsEnabled)
            return;

        var files = DroppedFiles(e);
        if (files.Length == 1 && IconPackFile.IsImportable(files[0]))
        {
            await ImportPackAsync(files[0]);
            return;
        }

        if (SelectedPack is not null)
            await QueueImagesAsync(files);
    }

    #endregion

    #region Updates

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        if (_updateCheckRunning)
            return;

        _updateCheckRunning = true;
        _updateButton.IsEnabled = false;
        _updateButton.Text = "Checking…";

        try
        {
            var result = await _updater.CheckForUpdateAsync(_settings.UpdateChannel);
            if (!result.Succeeded)
            {
                if (!silentWhenCurrent)
                    await ShowMessageAsync("Check for updates", result.ErrorMessage ?? "Update check failed.", MessageKind.Warning);
                SetStatus("Couldn't check for updates.", Palette.TextFaint);
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                if (!silentWhenCurrent)
                {
                    var latest = _settings.UpdateChannel == UpdateChannel.Beta ? "latest Mac version, including betas" : "latest stable Mac version";
                    SetStatus($"You're on the {latest} ({AppUpdateService.CurrentVersion}).");
                    await ShowMessageAsync("Check for updates", $"You're on the {latest} ({AppUpdateService.CurrentVersion}).");
                }

                return;
            }

            var kind = result.IsPrerelease ? "Beta version" : "Version";
            SetStatus($"{kind} {result.LatestVersion} is available.", result.IsPrerelease ? Palette.Warning : Palette.Accent);
            var canInstall = AppUpdateService.CanInstallInPlace;
            if (!await ShowDialogAsync(new UpdateAvailableDialog(result.LatestVersion!, AppUpdateService.CurrentVersion, result.IsPrerelease,
                    result.ReleasePageUrl, ChangesMade, canInstall)))
                return;

            if (canInstall)
                await DownloadAndInstallAsync(result);
            else if (AppUpdateService.IsReleasePage(result.ReleasePageUrl))
                UpdateAvailableDialog.OpenInBrowser(result.ReleasePageUrl!);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't check for updates.", Palette.TextFaint);
            if (!silentWhenCurrent)
                await ShowMessageAsync("Check for updates", "Couldn't check for updates:\n" + ex.Message, MessageKind.Warning);
        }
        finally
        {
            _updateCheckRunning = false;
            _updateButton.IsEnabled = true;
            _updateButton.Text = "Check for updates";
        }
    }

    /// <summary>
    /// The user already chose "Download and install" (and was warned about unsaved changes), so
    /// once the download is ready there's no second question: hand over to the installer and quit.
    /// </summary>
    private async Task DownloadAndInstallAsync(MacUpdateCheckResult update)
    {
        var progressDialog = new UpdateProgressDialog(update.LatestVersion!);
        var progress = new Progress<(string Status, int Percent)>(progressDialog.Report);
        _modal++;
        var shown = progressDialog.ShowDialog(this);

        PreparedUpdate prepared;
        try
        {
            prepared = await _updater.DownloadAsync(update, progress, progressDialog.Token);
        }
        catch (OperationCanceledException)
        {
            progressDialog.Close();
            await shown;
            _modal--;
            SetStatus($"Update cancelled. You're still on version {AppUpdateService.CurrentVersion}; check for updates whenever you're ready.");
            return;
        }
        catch (Exception ex)
        {
            progressDialog.Close();
            await shown;
            _modal--;
            CrashLog.Write(ex);
            SetStatus("Update failed.", Palette.Danger);
            if (await ShowMessageAsync("Update failed", "Couldn't install the update:\n" + ex.Message +
                    "\n\nYou can download it from the release page instead.", MessageKind.Error, "Close", "Open download page") == "Open download page"
                && AppUpdateService.IsReleasePage(update.ReleasePageUrl))
                UpdateAvailableDialog.OpenInBrowser(update.ReleasePageUrl!);
            return;
        }

        progressDialog.Close();
        await shown;
        _modal--;

        try
        {
            AppUpdateService.LaunchInstaller(prepared);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Update failed", "Couldn't start the installer:\n" + ex.Message, MessageKind.Error);
            SetStatus("Update failed.", Palette.Danger);
            return;
        }

        // The installer swaps the app once this process has gone, then reopens it.
        _allowClose = true;
        Environment.Exit(0);
    }

    #endregion
}
