using Avalonia.Threading;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// Notices packs added, updated or removed outside the app (for example a .streamDeckIconPack
/// installed by opening it, or a pack uninstalled in Stream Deck's icon library). Checks every few
/// seconds and whenever the window is activated. Unsaved changes in the app always win.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly DispatcherTimer _packWatchTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, DateTime> _knownStamps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _newFolderStamps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);
    private bool _checkingPacks;

    private void StartPackWatch()
    {
        RememberPackStamps();
        _packWatchTimer.Tick += (_, _) => CheckForOutsideChanges();
        _packWatchTimer.Start();
        Activated += (_, _) => CheckForOutsideChanges();
    }

    private static string FolderKey(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static DateTime? ManifestStamp(string packDir)
    {
        try
        {
            var file = Path.Combine(packDir, "manifest.json");
            return File.Exists(file) ? File.GetLastWriteTimeUtc(file) : null;
        }
        catch
        {
            return null;
        }
    }

    private void RememberPackStamps()
    {
        _knownStamps.Clear();
        foreach (var pack in _allPacks)
        {
            if (ManifestStamp(pack.IconPackPath) is { } stamp)
                _knownStamps[FolderKey(pack.IconPackPath)] = stamp;
        }
    }

    private void CheckForOutsideChanges()
    {
        // Not while a dialog is open or the app is busy: those change pack folders themselves.
        if (!_loaded || _checkingPacks || !_root.IsEnabled || _modal > 0 || !Directory.Exists(_streamDeckPacksPath))
            return;

        _checkingPacks = true;
        try
        {
            ApplyOutsideChanges();
        }
        catch
        {
            // A folder mid-write or locked; the next check will try again.
        }
        finally
        {
            _checkingPacks = false;
        }
    }

    private void ApplyOutsideChanges()
    {
        var onDisk = Directory.GetDirectories(_streamDeckPacksPath)
            .Where(PackPaths.IsCustomPackDirectory)
            .ToDictionary(FolderKey, d => d, StringComparer.Ordinal);

        var messages = new List<string>();
        var listChanged = false;
        var selected = SelectedPack;
        Manifest? reselect = null;
        var toDispose = new List<Manifest>();

        foreach (var pack in _allPacks.ToList())
        {
            var key = FolderKey(pack.IconPackPath);
            if (onDisk.ContainsKey(key))
            {
                _reportedMissing.Remove(key);
                continue;
            }

            if (_pendingPacks.Contains(pack))
            {
                if (_reportedMissing.Add(key))
                    messages.Add($"{pack.Name} was removed outside the app. Save to Stream Deck to put it back with your changes.");
                continue;
            }

            _allPacks.Remove(pack);
            _knownStamps.Remove(key);
            toDispose.Add(pack);
            listChanged = true;
            messages.Add($"{pack.Name} was removed from Stream Deck.");
        }

        for (var i = 0; i < _allPacks.Count; i++)
        {
            var pack = _allPacks[i];
            var key = FolderKey(pack.IconPackPath);
            if (ManifestStamp(pack.IconPackPath) is not { } stamp)
                continue;

            if (!_knownStamps.TryGetValue(key, out var known))
            {
                _knownStamps[key] = stamp;
                continue;
            }

            if (known == stamp || DateTime.UtcNow - stamp < TimeSpan.FromSeconds(2))
                continue;

            _knownStamps[key] = stamp;
            if (_pendingPacks.Contains(pack))
            {
                messages.Add($"{pack.Name} was changed outside the app. Your unsaved changes will replace it when you save.");
                continue;
            }

            var fresh = new Manifest(pack.IconPackPath);
            _allPacks[i] = fresh;
            toDispose.Add(pack);
            if (ReferenceEquals(pack, selected))
                reselect = fresh;
            listChanged = true;
            messages.Add($"Reloaded {fresh.Name}; it was changed outside the app.");
        }

        var loaded = new HashSet<string>(_allPacks.Select(p => FolderKey(p.IconPackPath)), StringComparer.Ordinal);
        foreach (var (key, dir) in onDisk)
        {
            if (loaded.Contains(key) || ManifestStamp(dir) is not { } stamp)
                continue;

            if (!_newFolderStamps.TryGetValue(key, out var seen) || seen != stamp)
            {
                _newFolderStamps[key] = stamp;
                continue;
            }

            _newFolderStamps.Remove(key);
            var added = new Manifest(dir);
            _allPacks.Add(added);
            _knownStamps[key] = ManifestStamp(dir) ?? stamp;
            listChanged = true;
            messages.Add($"Found a new pack: {added.Name}.");
        }

        foreach (var gone in _newFolderStamps.Keys.Where(k => !onDisk.ContainsKey(k)).ToList())
            _newFolderStamps.Remove(gone);

        if (listChanged)
            RefreshPackList(reselect ?? (selected is not null && _allPacks.Contains(selected) ? selected : null));

        // Free the replaced packs' images only once nothing shows them.
        foreach (var pack in toDispose)
            pack.DisposeAllImages();

        if (messages.Count > 0)
            SetStatus(string.Join(" ", messages), Palette.Accent);
    }
}
