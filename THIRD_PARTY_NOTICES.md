# References and dependencies

DanceBridge contains a fresh implementation of WUP-028 report handling and keyboard mapping. No Delfinovin, Dolphin, or JoyToKey source files are bundled.

- Delfinovin: https://github.com/Struggleton/Delfinovin — GPL-3.0; consulted for adapter endpoint behavior and existing setup architecture.
- Dolphin: https://github.com/dolphin-emu/dolphin — GPL-2.0-or-later; consulted for WUP-028 report size and protocol behavior.
- libwdi 1.5.1: https://github.com/pbatard/libwdi — LGPL-3.0-or-later. DanceBridge dynamically links a bundled, replaceable DLL built from commit 9b23b82a2dd1cbffc16d46c212f92c6bf8c0c602. Adaptations support Windows 10/11 x64 inbox WinUSB without legacy coinstallers. Exact build transformations and the INF template are in scripts/build-native.ps1 and native/winusb.inf.in. The package includes the LGPL/GPL license texts and corresponding source plus adaptations in third-party/libwdi-source.zip. The upstream installer is embedded in this library. Zadig itself is not bundled or launched. You may replace or rebuild the LGPL library and debug modifications to it as permitted by the LGPL.
- Microsoft .NET/WPF/Windows Forms: https://github.com/dotnet — self-contained releases include Microsoft runtime components and their included license notices.

DanceBridge source is licensed under MIT; third-party dependencies retain the licenses listed above.
