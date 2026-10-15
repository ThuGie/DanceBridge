# Hardware compatibility research — 15 October 2026

## Mat layout

The Konami Wii DDR mat has B / Up / A in its first large-panel row, Left / an inactive center / Right in the middle, and Down below. Small Minus and Plus inputs sit above the large panels. The center and bottom corners are not inputs. The old nine-input GameCube diagram was incorrect.

Reference photograph inspected: https://www.ebay.com/itm/196259419604 (original product photograph at https://i.ebayimg.com/images/g/gaUAAOSwpOhl1pPz/s-l1600.jpg). The owner confirmed this matches their mat. This is a hardware photograph, not a protocol specification.

The display now follows this physical layout. Menu defaults use GameCube Start for Plus and Z for Minus; confirm these signals using the live display on each mat revision. Saved keyboard mappings are preserved. Fresh profiles map A and Plus to Enter, B and Minus to Escape, and directions to arrow keys. Other controller signals remain available under Additional controller inputs.

## Adapter families

USB IDs describe the protocol family here, not a trustworthy manufacturer identity. A device displaying WUP-028 may be a Nintendo adapter or a compatible third-party adapter. DanceBridge already accepts the shared `057E:0337` identity; it must not claim to know the brand from this alone.

| Adapter | Current DanceBridge coverage | Evidence / requirements |
| --- | --- | --- |
| Nintendo GameCube adapter for Wii U and Switch | Supported protocol; connected WUP-028 hardware tested | Native 37-byte reports. Four independently tracked port slots. |
| Mayflash W012 four-port | Covered in Wii U/NS mode when it presents the shared identity and protocol; this model not physically tested here | Manufacturer documents PC and Wii U/NS modes; Dolphin documents compatible third-party adapters in Wii U mode. PC HID mode is a separate backend. |
| Hit Box GameCube adapter | Expected in Switch/Wii U mode; not physically tested here | Manufacturer explicitly documents Dolphin operation in this mode. Its PC mode uses `2F24:009B`, which must not be treated as native WUP protocol. |
| Nyko native-compatible adapters | Protocol candidate with optional initialization support; not physically tested here | Dolphin source documents a class SET_PROTOCOL request needed by Nyko. DanceBridge now attempts this optional request, discovers interrupt endpoints, then starts native reporting. |
| Other Wii U/Switch-compatible clones | Accepted only when they expose `057E:0337`, interrupt IN/OUT endpoints and valid native reports | No brand-wide guarantee; firmware and mat behavior require testing. |
| Input Integrity Lossless | Strong candidate; not certified | Maker documents WCID WinUSB and Dolphin/Slippi compatibility, but dance-mat compatibility and modes need verification. No extra USB IDs are enabled on inference alone. |
| Raphnet GameCube/N64 USB V3 | Requires a new HID backend; not supported by this release | Maker documents driver-free HID and an actual successful Konami Wii DDR mat report. Do not replace its HID driver with WinUSB. |
| Mayflash PC mode / generic USB dance mats / PS2 converters | Requires HID/DirectInput backend and per-device calibration; not supported by this release | USB identity, report formats and opposite-direction behavior differ. Native WUP parsing cannot safely be applied. |

## Sources

- Nintendo: https://en-americas-support.nintendo.com/app/answers/detail/a_id/13277 — Nintendo's console compatibility is narrower than PC software support; do not confuse the two.
- Dolphin guide: https://dolphin-emu.org/docs/guides/how-use-official-gc-controller-adapter-wii-u/ — Nintendo Wii U/Switch and compatible third-party adapters; correct mode selection.
- Dolphin native implementation: https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/InputCommon/GCAdapter.cpp — shared identity, report format, endpoint discovery and Nyko initialization note.
- Mayflash W012: https://www.mayflash.com/product/W012.html — four ports and PC versus Wii U/NS switch. Firmware updates are not required or performed by DanceBridge.
- Hit Box: https://www.hitboxarcade.com/blogs/support/hit-box-gamecube-adapter — Switch/Wii U mode and separate PC identity.
- Input Integrity: https://www.input-integrity.com/ — WCID WinUSB and separate XInput mode.
- Raphnet: https://www.raphnet-tech.com/products/gc_usb_adapter_gen3/index.php — HID, no special driver, and Konami DDR Wii compatibility report.

Next useful backend is HID, starting with Raphnet and Mayflash PC mode. It needs physical report capture or a validated descriptor-driven decoder and tests for opposite arrows. Adding names to a list is not sufficient to claim working support.
