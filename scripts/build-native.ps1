[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$upstream = Join-Path $workspace 'vendor/libwdi'
if (!(Test-Path "$upstream/libwdi/libwdi.c")) { throw 'Run git submodule update --init first.' }
$commit = & git -C $upstream rev-parse HEAD
if ($commit -ne '9b23b82a2dd1cbffc16d46c212f92c6bf8c0c602') { throw 'Unexpected libwdi revision. Initialize the pinned submodule.' }
$build = Join-Path $workspace 'artifacts/native-build'
New-Item -ItemType Directory -Force $build | Out-Null
Get-ChildItem "$upstream/libwdi" -File | Copy-Item -Destination $build -Force
Copy-Item "$upstream/msvc/stdint.h", "$upstream/msvc/inttypes.h" -Destination $build -Force
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ Build Tools are required to build the bundled library.' }
$vc = Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$kit = "${env:ProgramFiles(x86)}/Windows Kits/10"
$sdk = Get-ChildItem "$kit/Include" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$env:INCLUDE = "$($vc.FullName)/include;$kit/Include/$($sdk.Name)/ucrt;$kit/Include/$($sdk.Name)/shared;$kit/Include/$($sdk.Name)/um"
$env:LIB = "$($vc.FullName)/lib/x64;$kit/Lib/$($sdk.Name)/ucrt/x64;$kit/Lib/$($sdk.Name)/um/x64"
$compiler = "$($vc.FullName)/bin/Hostx64/x64/cl.exe"
$env:PATH = "$($vc.FullName)/bin/Hostx64/x64;$env:PATH"
@'
#define OPT_M64
#define DDKBUILD
#define WDF_VER 1011
#define INCLUDE_DEBUG_LOGGING
#define ENABLE_LOGGING 1
'@ | Set-Content "$build/config.h"
"#define BUILD64" | Set-Content "$build/build64.h"
$embedderHeader = Get-Content "$build/embedder.h" -Raw
$embedderHeader = $embedderHeader.Replace('#error at least one of WDK_DIR, LIBUSB0_DIR, LIBUSBK_DIR or USER_DIR must be defined - check your config.h', '/* DanceBridge uses inbox WinUSB, with no third-party driver binaries. */')
Set-Content "$build/embedder.h" $embedderHeader
# WinUSB is included in Windows 10/11; no legacy coinstallers are required.
$source = Get-Content "$build/libwdi.c" -Raw
$source = $source.Replace('#if defined(WDK_DIR)', '#if 1 /* DanceBridge: inbox WinUSB on Windows 10/11 x64 */')
$source = $source.Replace('get_version_info(driver_type, driver_info);', 'if (driver_type == WDI_WINUSB) { driver_version[0].dwSignature = VS_FFI_SIGNATURE; driver_version[0].dwFileVersionMS = 0x000A0000; driver_version[0].dwFileVersionLS = 0; if (driver_info != NULL) *driver_info = driver_version[0]; } else get_version_info(driver_type, driver_info);')
Set-Content "$build/libwdi.c" $source
Copy-Item "$workspace/native/winusb.inf.in" "$build/winusb.inf.in" -Force
"# Only the generated INF is included in the catalog.`n" | Set-Content "$build/winusb.cat.in"
Push-Location $build
try {
 & $compiler /nologo /O2 /MT /D_CRT_SECURE_NO_WARNINGS /I. installer.c /Fe:installer_x64.exe /link setupapi.lib newdev.lib ole32.lib advapi32.lib shell32.lib
 if ($LASTEXITCODE) { throw 'Native installer build failed.' }
 & $compiler /nologo /O2 /MT /D_CRT_SECURE_NO_WARNINGS /I. embedder.c /Fe:embedder.exe
 if ($LASTEXITCODE) { throw 'Resource embedder build failed.' }
 & ./embedder.exe embedded.h
 if ($LASTEXITCODE) { throw 'Resource embedding failed.' }
 & $compiler /nologo /O2 /MT /LD /D_CRT_SECURE_NO_WARNINGS /DLIBWDI_DLL_EXPORT /D_WINDLL /I. libwdi.c logging.c tokenizer.c vid_data.c pki.c libwdi_dlg.c /Fe:libwdi.dll /link setupapi.lib newdev.lib ole32.lib advapi32.lib shell32.lib user32.lib gdi32.lib comdlg32.lib
 if ($LASTEXITCODE) { throw 'Driver library build failed.' }
} finally { Pop-Location }
New-Item -ItemType Directory -Force "$workspace/native/bin" | Out-Null
Copy-Item "$build/libwdi.dll" "$workspace/native/bin/libwdi.dll" -Force
Write-Host 'Built native/bin/libwdi.dll from pinned LGPL source.'
