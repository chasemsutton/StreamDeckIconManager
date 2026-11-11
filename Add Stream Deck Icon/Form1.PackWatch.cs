using Add_Stream_Deck_Icon.UI;

namespace Add_Stream_Deck_Icon;

/// <summary>
/// Notices packs added, updated or removed outside the app (for example a .streamDeckIconPack
/// installed by double-clicking it, or a pack uninstalled in Stream Deck's icon library).
/// Checks every few seconds and whenever the window is activated. Unsaved changes in the app
/// always win: a changed pack isn't reloaded, and a removed one is kept so saving re-creates it.
/// </summary>
public partial class Form1
{
    private const int PackWatchIntervalMs = 4000;

    private readonly System.Windows.Forms.Timer _packWatchTimer = new() { Interval = PackWatchIntervalMs };

    /// <summary>manifest.json write time per pack folder, as last loaded or written by the app.</summary>
    private readonly Dictionary<string, DateTime> _knownStamps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>New folders seen once. Added on a later check if their manifest hasn't changed (finished writing).</summary>
    private readonly Dictionary<string, DateTime> _newFolderStamps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Packs with unsaved changes already reported as removed outside the app, so it's said once.</summary>
    private readonly HashSet<string> _reportedMissing = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Records the current state of every loaded pack, so the app's own writes aren't mistaken for outside changes.</summary>
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
        // Not while a dialog is open or the app is busy (saving, converting, installing live):
        // those change pack folders themselves.
        if (_checkingPacks || !Enabled || Application.OpenForms.Count > 1 || !Directory.Exists(_streamDeckPacksPath))
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
            .ToDictionary(FolderKey, d => d, StringComparer.OrdinalIgnoreCase);

        var messages = new List<string>();
        var listChanged = false;
        var selected = SelectedPack;
        Manifest? reselect = null;

        // Removed (for example uninstalled in Stream Deck's icon library).
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
            pack.DisposeAllImages();
            listChanged = true;
            messages.Add($"{pack.Name} was removed from Stream Deck.");
        }

        // Updated (for example a newer export of the pack installed over it).
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

            if (known == stamp)
                continue;

            // Still being written: wait for the next check.
            if (DateTime.UtcNow - stamp < TimeSpan.FromSeconds(2))
                continue;

            _knownStamps[key] = stamp;
            if (_pendingPacks.Contains(pack))
            {
                messages.Add($"{pack.Name} was changed outside the app. Your unsaved changes will replace it when you save.");
                continue;
            }

            var fresh = new Manifest(pack.IconPackPath);
            _allPacks[i] = fresh;
            pack.DisposeAllImages();
            if (ReferenceEquals(pack, selected))
                reselect = fresh;
            listChanged = true;
            messages.Add($"Reloaded {fresh.Name}; it was changed outside the app.");
        }

        // Added (for example a .streamDeckIconPack installed by double-clicking it).
        var loaded = new HashSet<string>(_allPacks.Select(p => FolderKey(p.IconPackPath)), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, dir) in onDisk)
        {
            if (loaded.Contains(key) || ManifestStamp(dir) is not { } stamp)
                continue;

            // Seen for the first time, or still changing: check again next time.
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

        // Forget folders that disappeared before they were added.
        foreach (var gone in _newFolderStamps.Keys.Where(k => !onDisk.ContainsKey(k)).ToList())
            _newFolderStamps.Remove(gone);

        if (listChanged)
            RefreshPackList(reselect ?? (selected is not null && _allPacks.Contains(selected) ? selected : null));

        if (messages.Count > 0)
            SetStatus(string.Join(" ", messages), Theme.Accent);
    }
}
