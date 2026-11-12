#Requires -Version 5.1
<#
.SYNOPSIS
  Local test build of the Mac app, on Windows:
    - StreamDeckIconManager{version}Mac-arm64.zip  (Apple silicon)
    - StreamDeckIconManager{version}Mac-x64.zip    (Intel Macs)
  Each zip holds "Stream Deck Icon Manager.app" and "READ ME FIRST.txt".

.EXAMPLE
  .\publish-mac.ps1
  .\publish-mac.ps1 -Version 1.2.1.1 -Runtimes osx-arm64

.NOTES
  RELEASES DON'T USE THIS SCRIPT. They're built on a Mac by GitHub Actions
  (.github/workflows/mac-release.yml), which also makes the .pkg installer and signs the app.
  To release, push a tag:   git tag mac-1.2.1.1; git push origin mac-1.2.1.1
  Mac releases use "mac-" tags and stay pre-releases that are never marked Latest, so Windows
  users are never offered them (and Windows releases never reach Mac users).

  Needs the .NET 8 SDK (or newer). Uses "dotnet" from PATH if it has one, otherwise the per-user
  install in %LOCALAPPDATA%\Microsoft\dotnet (https://dot.net/v1/dotnet-install.ps1 -Channel 8.0).

  These zips aren't signed; READ ME FIRST.txt has the one Terminal line that allows them. The
  zips are written with Unix permissions so the executables stay executable after unzipping.
#>
param(
    [string]$Version = "",
    [string[]]$Runtimes = @("osx-arm64", "osx-x64")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
    $Version = $props.Project.PropertyGroup.Version
    if (-not $Version) { throw "Could not read Version from Directory.Build.props" }
}

function Find-Dotnet {
    $candidates = @()
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $candidates += Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    foreach ($exe in $candidates) {
        if (-not (Test-Path $exe)) { continue }
        $sdks = & $exe --list-sdks 2>$null
        if ($sdks | Where-Object { [int](($_ -split '\.')[0]) -ge 8 }) { return $exe }
    }
    throw "The .NET 8 SDK is needed. Install it with: & ([scriptblock]::Create((iwr https://dot.net/v1/dotnet-install.ps1))) -Channel 8.0"
}

$dotnet = Find-Dotnet
$project = Join-Path $root "Mac\StreamDeckIconManager.Mac.csproj"
$packaging = Join-Path $root "Mac\packaging"
$outDir = Join-Path $root "artifacts\$Version\mac"
$appName = "Stream Deck Icon Manager.app"

Write-Host "Publishing Stream Deck Icon Manager $Version for Mac (beta)" -ForegroundColor Cyan
Write-Host "Using $dotnet"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

# --- App icon (.icns) from the Windows .ico: 256 px PNG (ic08) and 128 px (ic07) ---------------
Add-Type -AssemblyName System.Drawing
function New-Icns([string]$icoPath, [string]$icnsPath) {
    $ico = [IO.File]::ReadAllBytes($icoPath)
    $count = [BitConverter]::ToUInt16($ico, 4)
    $png256 = $null
    for ($i = 0; $i -lt $count; $i++) {
        $o = 6 + 16 * $i
        $size = [BitConverter]::ToUInt32($ico, $o + 8)
        $offset = [BitConverter]::ToUInt32($ico, $o + 12)
        if ($ico[$o] -eq 0 -and $ico[$offset] -eq 0x89) {
            $png256 = New-Object byte[] $size
            [Array]::Copy($ico, $offset, $png256, 0, $size)
        }
    }
    if (-not $png256) { throw "No 256 px PNG image in $icoPath" }

    $big = New-Object Drawing.Bitmap (New-Object IO.MemoryStream (, $png256))
    $small = New-Object Drawing.Bitmap 128, 128
    $g = [Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($big, 0, 0, 128, 128)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $small.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $png128 = $ms.ToArray()
    $small.Dispose(); $big.Dispose()

    $entries = @(@("ic07", $png128), @("ic08", $png256), @("ic13", $png256))
    $out = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $out
    function BE([int]$n) { $b = [BitConverter]::GetBytes($n); [Array]::Reverse($b); return , $b }
    $total = 8
    foreach ($e in $entries) { $total += 8 + $e[1].Length }
    $w.Write([Text.Encoding]::ASCII.GetBytes("icns")); $w.Write((BE $total))
    foreach ($e in $entries) {
        $w.Write([Text.Encoding]::ASCII.GetBytes($e[0])); $w.Write((BE (8 + $e[1].Length))); $w.Write($e[1])
    }
    $w.Flush()
    [IO.File]::WriteAllBytes($icnsPath, $out.ToArray())
}

# --- Zip with Unix permissions -----------------------------------------------------------------
# Windows zip tools don't store Unix permissions, so macOS would unzip the app's executables
# as plain files. This writes each entry's mode and marks the archive as made on Unix.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

public static class UnixZip
{
    public static void Create(string sourceDir, string zipPath, string[] executables)
    {
        var exec = new HashSet<string>(executables, StringComparer.Ordinal);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        var root = sourceDir.TrimEnd('\\', '/') + "\\";
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var name = dir.Substring(root.Length).Replace('\\', '/') + "/";
                var entry = zip.CreateEntry(name);
                entry.ExternalAttributes = (0x41ED << 16) | 0x10;   // drwxr-xr-x
            }
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var name = file.Substring(root.Length).Replace('\\', '/');
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                var mode = exec.Contains(name) ? 0x81ED : 0x81A4;   // -rwxr-xr-x or -rw-r--r--
                entry.ExternalAttributes = mode << 16;
                using (var input = File.OpenRead(file))
                using (var output = entry.Open())
                    input.CopyTo(output);
            }
        }
        MarkMadeOnUnix(zipPath);
    }

    // Sets "version made by" to Unix (3) in every central directory entry, so unzip tools on
    // macOS apply the permissions above.
    private static void MarkMadeOnUnix(string zipPath)
    {
        var bytes = File.ReadAllBytes(zipPath);
        var eocd = -1;
        for (var i = bytes.Length - 22; i >= 0; i--)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06) { eocd = i; break; }
        }
        if (eocd < 0) throw new InvalidDataException("No end of central directory in " + zipPath);
        int count = BitConverter.ToUInt16(bytes, eocd + 10);
        var p = (int)BitConverter.ToUInt32(bytes, eocd + 16);
        for (var n = 0; n < count; n++)
        {
            if (BitConverter.ToUInt32(bytes, p) != 0x02014B50) throw new InvalidDataException("Bad central directory entry");
            bytes[p + 4] = 30;   // spec version 3.0
            bytes[p + 5] = 3;    // made on Unix
            int nameLen = BitConverter.ToUInt16(bytes, p + 28);
            int extraLen = BitConverter.ToUInt16(bytes, p + 30);
            int commentLen = BitConverter.ToUInt16(bytes, p + 32);
            p += 46 + nameLen + extraLen + commentLen;
        }
        File.WriteAllBytes(zipPath, bytes);
    }
}
"@ -ReferencedAssemblies System.IO.Compression, System.IO.Compression.FileSystem

$icns = Join-Path $outDir "AppIcon.icns"
New-Icns (Join-Path $root "Add Stream Deck Icon\StreamDeckIconManagerLogo.ico") $icns

foreach ($rid in $Runtimes) {
    $arch = $rid -replace '^osx-', ''
    $work = Join-Path $outDir $rid
    $publishDir = Join-Path $work "publish"
    $stage = Join-Path $work "zip"
    $bundle = Join-Path $stage $appName

    Write-Host "-> Publishing $rid (self-contained)..."
    # OneDrive can briefly lock files in obj\ while it syncs them, so try a few times.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        & $dotnet publish $project -c Release -r $rid --self-contained true `
            -p:UseAppHost=true `
            -p:Version=$Version -p:AssemblyVersion=$Version -p:FileVersion=$Version -p:InformationalVersion=$Version `
            -o $publishDir
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq 3) { throw "Publish failed for $rid" }
        Write-Host "   Publish failed (attempt $attempt); retrying in 5 seconds..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
    }

    Write-Host "-> Building $appName ($arch)..."
    $macOS = Join-Path $bundle "Contents\MacOS"
    $resources = Join-Path $bundle "Contents\Resources"
    New-Item -ItemType Directory -Path $macOS, $resources -Force | Out-Null
    Copy-Item (Join-Path $publishDir "*") $macOS -Recurse -Force
    Get-ChildItem $macOS -Filter *.pdb -Recurse | Remove-Item -Force
    Copy-Item $icns (Join-Path $resources "AppIcon.icns")
    $plist = (Get-Content (Join-Path $packaging "Info.plist") -Raw).Replace("{VERSION}", $Version) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $bundle "Contents\Info.plist"), $plist, (New-Object Text.UTF8Encoding $false))
    [IO.File]::WriteAllText((Join-Path $bundle "Contents\PkgInfo"), "APPL????", (New-Object Text.UTF8Encoding $false))
    $readme = (Get-Content (Join-Path $packaging "READ ME FIRST.txt") -Raw) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $stage "READ ME FIRST.txt"), $readme, (New-Object Text.UTF8Encoding $false))

    $prefix = "$appName/Contents/MacOS/"
    $executables = @("StreamDeckIconManager", "createdump", "tools/gif2webp/gif2webp", "tools/gif2webp/img2webp") |
        Where-Object { Test-Path (Join-Path $macOS $_) } |
        ForEach-Object { $prefix + $_ }

    $zipName = "StreamDeckIconManager${Version}Mac-$arch.zip"
    $zipPath = Join-Path $outDir $zipName
    [UnixZip]::Create($stage, $zipPath, [string[]]$executables)
    Write-Host ("   {0} ({1:N1} MB)" -f $zipName, ((Get-Item $zipPath).Length / 1MB))
}

Write-Host ""
Write-Host "Mac test build ready in $outDir" -ForegroundColor Green
Write-Host "Apple silicon: StreamDeckIconManager${Version}Mac-arm64.zip   Intel: StreamDeckIconManager${Version}Mac-x64.zip"
Write-Host "Testers follow READ ME FIRST.txt inside the zip (one Terminal line to allow the unsigned app)."
Write-Host "This is a local test build. To release, push a tag: git tag mac-$Version; git push origin mac-$Version" -ForegroundColor Yellow
