# KyeForge build script: builds the app, publishes it, and bundles it into an installer.
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$app  = Join-Path $root "KyeForge.App\KyeForge.App.csproj"
$inst = Join-Path $root "KyeForge.Installer\KyeForge.Installer.csproj"
$publishDir = Join-Path $root "publish\win-x64"
$dist = Join-Path $root "dist"

Write-Host "==> Building app (Release)..." -ForegroundColor Cyan
dotnet publish $app -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $publishDir | Out-Null

$appExe = Join-Path $publishDir "KyeForge.exe"
if (-not (Test-Path $appExe)) { throw "App publish failed" }
Write-Host "    App: $appExe" -ForegroundColor Green

$resourcesDir = Join-Path $root "KyeForge.Installer\Resources"
$resExe = Join-Path $resourcesDir "KyeForge.exe"
New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
for ($attempt = 1; $attempt -le 8; $attempt++) {
  try {
    Copy-Item $appExe $resExe -Force
    break
  }
  catch {
    if ($attempt -eq 8) { throw }
    Start-Sleep -Milliseconds (250 * $attempt)
  }
}

Write-Host "==> Archiving source code..." -ForegroundColor Cyan
$sourceZip = Join-Path $resourcesDir "source.zip"
if (Test-Path $sourceZip) { Remove-Item $sourceZip -Force }

$tempSrc = Join-Path $root "_src_tmp"
if (Test-Path $tempSrc) { Remove-Item $tempSrc -Recurse -Force }
New-Item -ItemType Directory -Path $tempSrc -Force | Out-Null

$itemsToZip = @("KyeForge.App", "KyeForge.Installer", "KyeForge.sln", "build.ps1", "README.md")
foreach ($item in $itemsToZip) {
    $src = Join-Path $root $item
    if (Test-Path $src) {
        $dst = Join-Path $tempSrc $item
        if ((Get-Item $src).PSIsContainer) {
            Copy-Item $src $dst -Recurse -Exclude "bin","obj"
        } else {
            Copy-Item $src $dst
        }
    }
}

# Remove bin/obj from copied directories
Get-ChildItem $tempSrc -Directory -Recurse | Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Compress-Archive -Path (Join-Path $tempSrc "*") -DestinationPath $sourceZip -Force
Remove-Item $tempSrc -Recurse -Force
Write-Host "    Source archive: $sourceZip" -ForegroundColor Green

Write-Host "==> Building installer (Release)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$setupDir = Join-Path $dist "setup"
dotnet publish $inst -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $setupDir | Out-Null

$setupExe = Join-Path $setupDir "KyeForgeSetup.exe"
if (-not (Test-Path $setupExe)) { throw "Installer publish failed" }

$finalSetup = Join-Path $dist "KyeForge-Setup-1.0.0.exe"
Copy-Item $setupExe $finalSetup -Force
Remove-Item $setupDir -Recurse -Force

Write-Host ""
Write-Host "==============================" -ForegroundColor Cyan
Write-Host " DONE" -ForegroundColor Green
Write-Host "   App      : $appExe" -ForegroundColor Green
Write-Host "   Installer: $finalSetup" -ForegroundColor Green
Write-Host "==============================" -ForegroundColor Cyan
