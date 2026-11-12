# Releasing

How Windows and Mac releases are published, and how they're kept apart so a release for one
platform is never offered as an update on the other. For contributors; the user-facing install
notes are in `readme.txt` and in each GitHub release.

## The short version

| You want to ship | Do this | GitHub release |
|---|---|---|
| Windows beta | `.\publish-release.ps1 -Beta`, then `gh release create <version> ... --prerelease` | tag `1.2.1.1`, pre-release |
| Windows full release | `.\publish-release.ps1`, then `gh release create <version> ...` | tag `1.2.1.1`, **Latest** |
| Mac beta | `git tag mac-1.2.1.1-beta` and `git push origin mac-1.2.1.1-beta` | tag `mac-1.2.1.1-beta`, pre-release |
| Mac full release | `git tag mac-1.2.1.1` and `git push origin mac-1.2.1.1` | tag `mac-1.2.1.1`, full release, **not** Latest |

Two rules hold everything together:

1. **Mac release tags start with `mac-`. Windows release tags are plain version numbers.**
2. **The repository's "Latest" release is always a full Windows release.** Mac releases are never
   marked Latest.

Keep each platform in its own release: don't attach Mac files to a Windows release or the other
way round.

## Why these rules work

Each piece of software that looks for updates picks releases differently. The rules above are
what makes all of them land on the right platform, including Windows versions already installed
on people's PCs, which can't be changed anymore.

| Who looks | How it picks a release | What keeps the other platform out |
|---|---|---|
| Windows app 1.1.0.1 – 1.2.1.0, **Stable** channel | GitHub's Latest release | Rule 2: Latest is always Windows |
| Windows app 1.1.0.1 – 1.2.1.0, **Beta** channel | Highest version tag it can parse, pre-releases included | Rule 1: it can't parse `mac-…` tags, so it skips them |
| Windows app after 1.2.1.0 (both channels) | Newest release that has a `StreamDeckIconManager…Setup.exe` and no `mac-` tag | No Setup.exe in Mac releases |
| Windows `Setup.exe` (first install) | The release tagged with its own version, else Latest | Its own tag is a plain version; Latest is Windows |
| Mac app (both channels) | Newest release with a `…Mac-arm64.zip` / `…Mac-x64.zip` for its processor; reads `mac-1.2.1.1`, `mac-1.2.1.1-beta` and plain tags | No Mac zip in Windows releases |

"Stable" channels skip pre-releases; "Beta" channels include them. The code is in
`Add Stream Deck Icon/AppUpdateService.cs` (Windows), `Setup/SetupForm.cs` (Windows Setup) and
`Mac/Core/AppUpdateService.cs` (Mac). The shared naming lives in `shared/ReleaseNaming.cs`.

### The safety net

`.github/workflows/keep-windows-latest.yml` runs whenever a release is published or edited. If
Latest has become a `mac-` release (for example someone published one from the GitHub web page
with "Set as the latest release" ticked), it marks the newest full Windows release as Latest
again and leaves a warning on the run. It only works from the default branch, so it's active once
it's merged to `master`. You can also run it by hand from the Actions tab.

## Version numbers

- Windows takes its version from `Directory.Build.props` (`publish-release.ps1 -Version` overrides it).
- Mac takes its version from the tag: `mac-1.2.1.1-beta` builds version `1.2.1.1`.
- Versions only need to go up **within each platform**. The two can differ, e.g. Windows on
  1.2.1.0 while Mac is on 1.2.1.3. Using the same number for the same features is a nice
  convention, not a requirement.
- Don't reuse a tag. To redo a release, use the next version number.

## Windows releases

1. Set the version in `Directory.Build.props` (or pass `-Version`).
2. Run `.\publish-release.ps1` (add `-Beta` for a beta). It builds into `artifacts\<version>\`:
   `StreamDeckIconManager<version>Setup.exe` (what users download) and
   `StreamDeckIconManager<version>App.zip` (what Setup downloads). Upload both.
3. Create the release. The script prints the exact command. Beta:
   `gh release create <version> <setup> <zip> --prerelease --title "<version> (beta)"`.
   Full release: the same without `--prerelease`. A full Windows release **must** end up as
   Latest; that's GitHub's default for the newest full release.

## Mac releases

Mac releases are built by GitHub Actions on a real Mac (`.github/workflows/mac-release.yml`).
Pushing a `mac-` tag is the whole process:

```
git tag mac-1.2.1.1-beta        # or mac-1.2.1.1 for a full release
git push origin mac-1.2.1.1-beta
```

Tag the commit you want to ship; it doesn't need to be on `master`. The workflow:

1. Reads the version (and whether it's a beta) from the tag; any other tag shape fails the run.
2. Runs `Mac/packaging/build-mac.sh <version> [beta]`, which builds the app for Apple silicon and
   Intel, signs it ad hoc, and makes:
   - `StreamDeckIconManager<version>MacSetup.pkg`: the installer new users download. One
     installer for both processors; its `postinstall` script puts the right app in
     /Applications, gives it to the logged-in user (so the in-app updater can replace it), and
     clears the quarantine flag.
   - `StreamDeckIconManager<version>Mac-arm64.zip` and `…Mac-x64.zip`: just the app, which the
     in-app updater downloads.
3. Creates the release: a pre-release for `-beta` tags, a full release otherwise, **always with
   `--latest=false`**.

It takes about two minutes. Watch it with `gh run list --workflow mac-release.yml`.

### Beta builds vs full builds

The `-beta` suffix also changes the app itself (MSBuild property `SdimBeta`, read through
`AppUpdateService.IsBetaBuild`):

- The status bar says "Version 1.2.1.1 beta" instead of "Version 1.2.1.1".
- Someone who **first installs** a beta build starts on the Beta update channel; someone who first
  installs a full build starts on Stable, like Windows. The channel is saved on first run, so a
  beta tester stays on betas after updating to a full release until they switch.

So a beta can't simply be "promoted" to a full release: the build itself says beta. When a beta
has tested well, tag the same commit with the next version number and no suffix, e.g. after
`mac-1.2.1.1-beta` passes, push `mac-1.2.1.2`.

(`mac-1.2.1.0` and `mac-1.2.1.1` predate the `-beta` suffix; they're beta builds released as
pre-releases.)

### How the in-app updater installs (Mac)

`Mac/Core/AppUpdateService.cs`: download the zip, unpack it with `ditto`, check it's this app
(bundle ID, version, processor), clear quarantine and sign it ad hoc with `codesign`, then start a
small script and quit. The script waits for the app to exit, swaps the app bundles (putting the
old one back if that fails), and reopens the app. If macOS blocks the swap (System Settings >
Privacy & Security > App Management), the app shows why on its next start. The updater only runs
when the app sits somewhere the user can write to (normally /Applications as installed by the
.pkg); otherwise it opens the release page instead.

### Signing

The Mac app is signed ad hoc, not with an Apple Developer ID, so macOS warns once when the .pkg
is opened (users click Open Anyway in System Settings; the release notes explain this). Once
there's a Developer ID, signing and notarizing belong in `build-mac.sh`, with the certificate and
an app-specific password stored as repository secrets. The ad hoc signing steps then go away.

## Fixing mistakes

- **A Mac release became Latest:** the safety net fixes it automatically. By hand:
  `gh release edit <newest Windows tag> --latest`.
- **A Mac build failed:** fix it, then delete the tag and push it again (no release exists yet):
  `git push origin :refs/tags/mac-1.2.1.1-beta`, `git tag -f mac-1.2.1.1-beta <commit>`, push.
- **A bad release went out:** don't delete it, because people may already have updated. Ship a
  fixed release with a higher version; updaters only move forward.
- **Need to pull a release:** marking it as a draft hides it from every updater. Deleting it works too.

## Building locally

- Windows app: open `Add Stream Deck Icon.sln`, or `dotnet build "Add Stream Deck Icon"` (.NET 6).
- Mac app: needs the .NET 8 SDK. `dotnet build Mac` builds it, and it also runs on Windows for
  quick UI checks. Set `SDIM_ICONPACKS_ROOT` to a test folder so it doesn't touch real packs; on
  Windows it keeps its settings separately from the Windows app.
- `publish-mac.ps1` makes unsigned Mac test zips on Windows (beta builds). Testers allow them once
  with the Terminal line in `READ ME FIRST.txt`. Never attach these to a release; releases come
  from the workflow.
- `build-mac.sh` also runs on any Mac with the .NET 8 SDK.
