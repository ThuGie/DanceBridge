[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/windows-x64')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))
& "$PSScriptRoot/build-native.ps1"
& dotnet publish "$workspace/src/DanceBridge" -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE) { throw 'Application publish failed.' }
# Both components share the same self-contained runtime in one app directory.
& dotnet publish "$workspace/src/DanceBridge.DriverSetup" -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE) { throw 'Setup helper publish failed.' }
$thirdParty = Join-Path $output 'third-party'
New-Item -ItemType Directory -Force $thirdParty | Out-Null
Copy-Item "$workspace/vendor/libwdi/COPYING", "$workspace/vendor/libwdi/COPYING-LGPL" $thirdParty -Force
Copy-Item "$workspace/THIRD_PARTY_NOTICES.md", "$workspace/README.md", "$workspace/LICENSE" $output -Force
Copy-Item "$workspace/docs/COMPATIBILITY.md" $output -Force
& git -C "$workspace/vendor/libwdi" archive --format=zip --output="$thirdParty/libwdi-source.zip" HEAD
if ($LASTEXITCODE) { throw 'Could not archive dependency source.' }
# Include the exact adaptations used to build the replaceable LGPL DLL.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open("$thirdParty/libwdi-source.zip", [IO.Compression.ZipArchiveMode]::Update)
try {
 foreach ($relative in @('scripts/build-native.ps1','native/winusb.inf.in')) {
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $workspace $relative), "dancebridge-adaptations/$relative") | Out-Null
 }
} finally { $archive.Dispose() }
Write-Host "Standalone package: $output"

New-Item -ItemType Directory -Force (Join-Path $output 'docs') | Out-Null
Copy-Item "$workspace/docs/*" (Join-Path $output 'docs') -Recurse -Force
