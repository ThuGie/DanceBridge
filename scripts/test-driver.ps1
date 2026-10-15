[CmdletBinding()]
param([string]$Package = 'artifacts/windows-x64')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$helper = Join-Path $workspace "$Package/DanceBridge.DriverSetup.exe"
$device = & $helper --inspect | ConvertFrom-Json
if ($LASTEXITCODE) { throw 'Adapter detection failed.' }
if ($device.DeviceId -notlike 'USB\VID_057E&PID_0337\*') { throw 'Wrong adapter selected.' }
$lines = @(& $helper --stage-preview)
if ($LASTEXITCODE) { throw 'Preview package generation failed.' }
$directory = $lines | Where-Object { Test-Path (Join-Path $_ 'dancebridge-wup028.inf') -ErrorAction SilentlyContinue } | Select-Object -First 1
if (!$directory) { throw 'No generated INF found.' }
$inf = Get-Content (Join-Path $directory 'dancebridge-wup028.inf') -Raw
foreach ($required in @('VID_057E&PID_0337','Needs = WINUSB.NT.Services','NTamd64.10.0','B1541DF6-41F2-453B-A05B-B81FAAEB102E')) {
 if (!$inf.Contains($required)) { throw "Missing driver rule: $required" }
}
if ($inf -match 'CoInstaller|libusbK|libusb0' -or $inf.Contains('#DEVICE_')) { throw 'Unexpected generated driver content.' }
if (Test-Path (Join-Path $directory 'dancebridge-wup028.cat')) { throw 'Preview must not produce a signed installation package.' }
if ($device.Ready) {
 & $helper --install
 if ($LASTEXITCODE) { throw 'Already-ready setup path failed.' }
}
Write-Host 'PASS adapter targeting, inbox-WinUSB INF, interface GUID, no external coinstallers, preview-only generation, already-ready behavior.'
