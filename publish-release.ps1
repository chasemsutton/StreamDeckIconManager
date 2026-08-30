#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Stream Deck Icon Manager release artifacts:
    - StreamDeckIconManager{version}Setup.exe  (what users download)
    - StreamDeckIconManager{version}App.zip    (downloaded automatically by Setup)

.EXAMPLE
  .\publish-release.ps1
  .\publish-release.ps1 -Version 1.1.0.1
#>
param(
    [string]$Version = "",
    [string]$Runtime = "win-x64",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$propsPath = Join-Path $root "Directory.Build.props"
if (-not $Version) {
    [xml]$props = Get-Content $propsPath
    $Version = $props.Project.PropertyGroup.Version
    if (-not $Version) { throw "Could not read Version from Directory.Build.props" }
}

$setupName = "StreamDeckIconManager${Version}Setup.exe"
$appZipName = "StreamDeckIconManager${Version}App.zip"
$outDir = Join-Path $root "artifacts\$Version"
$appPublishDir = Join-Path $outDir "app"
$setupPublishDir = Join-Path $outDir "setup"

Write-Host "Publishing Stream Deck Icon Manager $Version" -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $appPublishDir, $setupPublishDir | Out-Null

Write-Host "-> Publishing application (self-contained $Runtime)..."
dotnet publish ".\Add Stream Deck Icon\Add Stream Deck Icon.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -p:InformationalVersion=$Version `
    -o $appPublishDir
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }

Write-Host "-> Creating $appZipName..."
$appZipPath = Join-Path $outDir $appZipName
if (Test-Path $appZipPath) { Remove-Item $appZipPath -Force }
Compress-Archive -Path (Join-Path $appPublishDir "*") -DestinationPath $appZipPath -Force

Write-Host "-> Publishing setup bootstrapper..."
dotnet publish ".\Setup\Setup.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -p:InformationalVersion=$Version `
    -o $setupPublishDir
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed" }

$publishedSetup = Join-Path $setupPublishDir "StreamDeckIconManagerSetup.exe"
if (-not (Test-Path $publishedSetup)) {
    $publishedSetup = Get-ChildItem $setupPublishDir -Filter "*.exe" |
        Where-Object { $_.Name -notmatch "\.pdb$" } |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $publishedSetup -or -not (Test-Path $publishedSetup)) {
    throw "Could not find published setup executable in $setupPublishDir"
}

$setupOutPath = Join-Path $outDir $setupName
Copy-Item $publishedSetup $setupOutPath -Force

Write-Host ""
Write-Host "Release artifacts ready:" -ForegroundColor Green
Write-Host "  User download : $setupOutPath"
Write-Host "  Setup payload : $appZipPath"
Write-Host ""
Write-Host "Upload BOTH files to a GitHub release tagged '$Version'."
Write-Host "Users only need to download $setupName - it fetches the App.zip automatically."
