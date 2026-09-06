$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /checked+ /target:winexe "/out:$PSScriptRoot\AstralScanner.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$PSScriptRoot\AutomaticUpdater.cs" "$PSScriptRoot\UpdaterTests.cs" "$PSScriptRoot\Scanner.cs" "$PSScriptRoot\LiveReader.cs" "$PSScriptRoot\CacheOverlay.cs" "$PSScriptRoot\OverlayTests.cs" "$PSScriptRoot\ClientSettings.cs" "$PSScriptRoot\SetupTests.cs" "$PSScriptRoot\CacheMemory.cs" "$PSScriptRoot\MemoryTests.cs" "$PSScriptRoot\LocationDatabase.cs" "$PSScriptRoot\DatabaseTests.cs" "$PSScriptRoot\RewardTracking.cs" "$PSScriptRoot\RewardTests.cs" "$PSScriptRoot\DesktopFeatures.cs" "$PSScriptRoot\DesktopTests.cs" "$PSScriptRoot\VersionInfo.cs" "$PSScriptRoot\ScannerUi.cs" "$PSScriptRoot\UiAcceptanceTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen' }


