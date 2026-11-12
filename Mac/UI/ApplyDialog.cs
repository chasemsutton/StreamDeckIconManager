using Avalonia;
using Avalonia.Controls;

namespace StreamDeckIconManager.Mac.UI;

internal enum ApplyOutcome
{
    /// <summary>Nothing was saved.</summary>
    Cancelled,

    /// <summary>Saved, and Stream Deck was restarted or started, so it has everything.</summary>
    Restarted,

    /// <summary>Saved; every changed pack was installed live.</summary>
    AppliedLive,

    /// <summary>Saved, but some changes only show after Stream Deck restarts.</summary>
    SavedNeedsRestart
}

/// <summary>
/// "Save to Stream Deck": choose between applying changed packs live (Stream Deck installs each
/// one and asks to confirm it) or saving everything and restarting Stream Deck (no prompts).
/// Then runs the choice and shows progress.
/// </summary>
internal sealed class ApplyDialog : ThemedDialog
{
    private readonly IReadOnlyList<Manifest> _packs;
    private readonly int _removals;
    private readonly bool _running;
    private readonly Action _saveAll;
    private readonly Action<Manifest> _savePack;

    private readonly OptionCard _liveCard;
    private readonly OptionCard _restartCard;
    private readonly StackPanel _choices;
    private readonly StackPanel _progress;
    private readonly TextBlock _status;
    private readonly TextBlock _detail;
    private readonly ProgressBar _bar;
    private readonly ThemedButton _cancel;
    private readonly ThemedButton _skip;
    private readonly ThemedButton _primary;

    private CancellationTokenSource? _skipToken;
    private bool _finishedWithLeftovers;

    public ApplyDialog(IReadOnlyList<Manifest> changedPacks, int removedPacks, ApplyMode? lastMode,
        Action saveAll, Action<Manifest> savePack)
        : base("Save to Stream Deck", Summary(changedPacks, removedPacks), 460)
    {
        _packs = changedPacks;
        _removals = removedPacks;
        _running = StreamDeckApp.IsRunning;
        _saveAll = saveAll;
        _savePack = savePack;

        _liveCard = new OptionCard("Apply live", Glyphs.Lightning)
        {
            Description = "Stream Deck installs each changed pack while it keeps running. It asks you to confirm each one."
        };

        _restartCard = new OptionCard(_running ? "Restart Stream Deck" : "Start Stream Deck", Glyphs.Sync)
        {
            Description = _running
                ? "Saves everything and restarts Stream Deck to load it all at once. No confirmations; your keys go blank for a few seconds."
                : "Saves everything and starts Stream Deck, which loads all your packs as it opens.",
            Margin = new Thickness(0)
        };

        _choices = new StackPanel { Children = { _liveCard, _restartCard } };
        ConfigureLiveCard();

        var recommendLive = _liveCard.IsEnabled && _packs.Count <= 3 && _removals == 0;
        _liveCard.Recommended = recommendLive;
        _restartCard.Recommended = !recommendLive;

        var startWithLive = _liveCard.IsEnabled && (lastMode is null ? recommendLive : lastMode == ApplyMode.Live);
        (startWithLive ? _liveCard : _restartCard).Checked = true;
        _liveCard.CheckedChanged += (_, _) => UpdatePrimary();
        _restartCard.CheckedChanged += (_, _) => UpdatePrimary();

        _status = Palette.Label("", Palette.Body, Palette.Text, strong: true, wrap: true);
        _status.Margin = new Thickness(0, 0, 0, 6);
        _detail = Palette.Label("", Palette.Body, Palette.TextMuted, wrap: true);
        _detail.Margin = new Thickness(0, 0, 0, 14);
        _bar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 8) };
        _progress = new StackPanel { IsVisible = false, Children = { _status, _detail, _bar } };

        AddBody(_choices, _progress);

        _cancel = new ThemedButton("Cancel", ButtonKind.Ghost) { IsCancel = true };
        _cancel.Click += (_, _) => CloseWith(_finishedWithLeftovers ? ApplyOutcome.SavedNeedsRestart : ApplyOutcome.Cancelled);
        _skip = new ThemedButton("Skip this pack", ButtonKind.Ghost) { IsVisible = false };
        _skip.Click += (_, _) => _skipToken?.Cancel();
        _primary = new ThemedButton("", ButtonKind.Primary) { IsDefault = true };
        _primary.Click += async (_, _) => await RunAsync();

        FooterRight.Children.Add(_cancel);
        FooterRight.Children.Add(_skip);
        FooterRight.Children.Add(_primary);

        UpdatePrimary();
        Opened += (_, _) => (_liveCard.Checked ? _liveCard : _restartCard).Focus();
    }

    public ApplyOutcome Outcome { get; private set; } = ApplyOutcome.Cancelled;

    /// <summary>The option the user went with, to offer first next time.</summary>
    public ApplyMode ChosenMode => _liveCard.Checked ? ApplyMode.Live : ApplyMode.Restart;

    public List<string> NotApplied { get; } = new();

    private static string Summary(IReadOnlyList<Manifest> packs, int removals)
    {
        if (packs.Count == 0 && removals == 0)
            return "There are no unsaved changes. You can still restart Stream Deck to make sure it has everything.";

        var parts = new List<string>();
        if (packs.Count > 0)
        {
            var names = packs.Select(p => p.Name).ToList();
            var list = names.Count <= 3
                ? string.Join(", ", names.Take(names.Count - 1)) + (names.Count > 1 ? " and " : "") + names[^1]
                : $"{names[0]}, {names[1]} and {names.Count - 2} more";
            parts.Add($"{(packs.Count == 1 ? "1 pack has" : $"{packs.Count} packs have")} changes: {list}.");
        }

        if (removals > 0)
            parts.Add($"{(removals == 1 ? "1 pack was" : $"{removals} packs were")} deleted or renamed.");

        return string.Join(" ", parts);
    }

    private void ConfigureLiveCard()
    {
        if (!_running)
        {
            _liveCard.IsEnabled = false;
            _liveCard.Note = "Needs Stream Deck to be running.";
            return;
        }

        if (_packs.Count == 0)
        {
            _liveCard.IsEnabled = false;
            _liveCard.Note = _removals > 0 ? "Deleted or renamed packs only update after a restart." : "No changed packs to apply.";
            return;
        }

        if (_removals > 0)
        {
            _liveCard.Note = "Deleted or renamed packs only update after a restart.";
            _liveCard.NoteColor = Palette.Warning;
        }
        else
        {
            _liveCard.Note = _packs.Count == 1 ? "1 confirmation in Stream Deck" : $"{_packs.Count} confirmations in Stream Deck";
        }
    }

    private void UpdatePrimary()
    {
        _primary.Text = _liveCard.Checked
            ? "Save and apply live"
            : _running ? "Save and restart" : "Save and start Stream Deck";
        _primary.Glyph = _liveCard.Checked ? Glyphs.Lightning : Glyphs.Sync;
    }

    private void CloseWith(ApplyOutcome outcome)
    {
        if (Busy)
            return;
        Outcome = outcome;
        Finish(outcome != ApplyOutcome.Cancelled);
    }

    private void ShowProgress(string status, string detail, double percent)
    {
        _choices.IsVisible = false;
        _progress.IsVisible = true;
        _status.Text = status;
        _status.Foreground = Palette.Text;
        _detail.Text = detail;
        _bar.IsVisible = true;
        _bar.Value = percent;
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        _primary.IsVisible = !busy;
        _cancel.IsEnabled = !busy;
    }

    private async Task RunAsync()
    {
        if (Busy)
            return;

        if (_liveCard.Checked && !_finishedWithLeftovers)
            await ApplyLiveAsync();
        else
            await RestartAsync();
    }

    private async Task RestartAsync()
    {
        SetBusy(true);
        try
        {
            ShowProgress("Saving...", "Writing your packs to Stream Deck's icon pack folder.", 20);
            _saveAll();

            ShowProgress(_running ? "Restarting Stream Deck..." : "Starting Stream Deck...",
                OperatingSystem.IsMacOS() && _running
                    ? "Stream Deck loads all your packs as it starts. If macOS asks whether Stream Deck Icon Manager may control Stream Deck, click OK."
                    : "Stream Deck loads all your packs as it starts.", 60);
            await StreamDeckApp.RestartAsync();

            _bar.Value = 100;
            Busy = false;
            CloseWith(ApplyOutcome.Restarted);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            _finishedWithLeftovers = true;
            _bar.IsVisible = false;
            _status.Text = "Your packs are saved, but Stream Deck didn't restart.";
            _status.Foreground = Palette.Danger;
            _detail.Text = ex.Message;
            _primary.Text = "Try restarting again";
            _primary.Glyph = Glyphs.Sync;
            _cancel.Text = "Close";
        }
    }

    private async Task ApplyLiveAsync()
    {
        SetBusy(true);
        var applied = 0;
        var work = Path.Combine(Path.GetTempPath(), "sdim-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            for (var i = 0; i < _packs.Count; i++)
            {
                var pack = _packs[i];
                ShowProgress(
                    $"Confirm \"{pack.Name}\" in Stream Deck",
                    $"Stream Deck is asking whether to install this pack. Click Install there. If you chose Cancel there instead, click Skip this pack. ({i + 1} of {_packs.Count})",
                    100.0 * i / _packs.Count + 3);

                _skip.IsVisible = true;
                _skipToken = new CancellationTokenSource();

                LiveInstallResult result;
                try
                {
                    var package = Path.Combine(work, $"pack{i}{IconPackFile.Extension}");
                    var version = IconPackFile.Export(pack, package);
                    // Stream Deck installs into its own folder, under the pack's folder name (its ID).
                    var manifest = Path.Combine(PackPaths.StreamDeckIconPacksFolder,
                        Path.GetFileName(pack.IconPackPath.TrimEnd(Path.DirectorySeparatorChar)), "manifest.json");
                    result = await StreamDeckApp.InstallPackageAsync(package, manifest, version, _skipToken.Token);
                }
                catch
                {
                    result = LiveInstallResult.Failed;
                }

                if (result == LiveInstallResult.Installed)
                {
                    applied++;
                }
                else
                {
                    // Not installed live: save it the usual way so nothing is lost. Stream Deck
                    // picks it up at its next start.
                    _savePack(pack);
                    NotApplied.Add(pack.Name);
                }
            }
        }
        finally
        {
            _skip.IsVisible = false;
            _skipToken = null;
            try { Directory.Delete(work, true); } catch { /* temp cleanup is best-effort */ }
        }

        _bar.Value = 100;
        if (NotApplied.Count == 0 && _removals == 0)
        {
            Busy = false;
            CloseWith(ApplyOutcome.AppliedLive);
            return;
        }

        // Some changes need a restart: say which, and offer it.
        SetBusy(false);
        _finishedWithLeftovers = true;
        _bar.IsVisible = false;
        _status.Text = applied == 0
            ? "Saved, but no packs were applied live."
            : $"{applied} of {_packs.Count} packs applied live.";
        var waiting = new List<string>();
        if (NotApplied.Count > 0)
            waiting.Add($"{JoinNames(NotApplied)} {(NotApplied.Count == 1 ? "was" : "were")} saved and will appear after Stream Deck restarts.");
        if (_removals > 0)
            waiting.Add("Deleted or renamed packs leave Stream Deck after a restart.");
        _detail.Text = string.Join(" ", waiting);
        _primary.Text = "Restart Stream Deck now";
        _primary.Glyph = Glyphs.Sync;
        _cancel.Text = "Restart later";
    }

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count == 1 ? $"\"{names[0]}\"" : string.Join(", ", names.Take(names.Count - 1).Select(n => $"\"{n}\"")) + $" and \"{names[^1]}\"";
}
