using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
namespace DanceBridge;

public sealed class Adapter : IDisposable
{
    SafeFileHandle? file; IntPtr usb;
    byte inputPipe, outputPipe;
    public void Open()
    {
        var guids = new HashSet<Guid>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_057E&PID_0337");
        if (root is null) throw new IOException("Connect a compatible GameCube USB adapter. Set Mayflash adapters to Wii U/NS mode.");
        foreach (var instance in root.GetSubKeyNames()) { using var parameters = root.OpenSubKey(instance + @"\Device Parameters"); if (parameters?.GetValue("DeviceInterfaceGUIDs") is string[] values) foreach (var value in values) if (Guid.TryParse(value, out var g)) guids.Add(g); }
        foreach (var guid in guids)
        {
            var g = guid; var set = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 0x12); if (set == new IntPtr(-1)) continue;
            try
            {
                for (uint n = 0; ; n++)
                {
                    var info = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() }; if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, n, ref info)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref info, IntPtr.Zero, 0, out var size, IntPtr.Zero); var detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, IntPtr.Zero)) continue; var path = Marshal.PtrToStringUni(detail + 4)!; if (!path.Contains("vid_057e&pid_0337", StringComparison.OrdinalIgnoreCase)) continue;
                        file = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); if (file.IsInvalid) { file.Dispose(); continue; }
                        if (!WinUsb_Initialize(file, out usb)) { file.Dispose(); continue; }
                        if (!WinUsb_QueryInterfaceSettings(usb, 0, out var descriptor)) throw Error();
                        inputPipe = 0; outputPipe = 0;
                        for (byte endpoint = 0; endpoint < descriptor.Endpoints; endpoint++)
                        {
                            if (!WinUsb_QueryPipe(usb, 0, endpoint, out var pipe)) throw Error();
                            if (pipe.Type != 3) continue; // Native adapter reports use interrupt transfers.
                            if ((pipe.Id & 0x80) != 0) inputPipe = pipe.Id; else outputPipe = pipe.Id;
                        }
                        if (inputPipe == 0 || outputPipe == 0) throw new IOException("Adapter does not expose the expected native interrupt endpoints.");
                        // Some Nyko-compatible firmware requires HID SET_PROTOCOL(1).
                        // Others (including Mayflash) stall this request; a stall is harmless here.
                        WinUsb_ControlTransfer(usb, new SetupPacket { RequestType = 0x21, Request = 0x0B, Value = 1 }, IntPtr.Zero, 0, out _, IntPtr.Zero);
                        uint timeout = 250;
                        if (!WinUsb_SetPipePolicy(usb, inputPipe, 3, 4, ref timeout)) throw Error();
                        byte[] init = [0x13]; if (!WinUsb_WritePipe(usb, outputPipe, init, 1, out _, IntPtr.Zero)) throw Error(); return;
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
        throw new IOException("Cannot open the GameCube adapter. Close other adapter apps, check Wii U/NS mode, or use Driver setup.");
    }
    public byte[] Read() { byte[] report = new byte[37]; if (!WinUsb_ReadPipe(usb, inputPipe, report, 37, out var count, IntPtr.Zero)) throw Error(); if (count != 37) throw new IOException("Incomplete adapter report."); return report; }
    static Win32Exception Error() => new(Marshal.GetLastWin32Error());
    public void Dispose() { if (usb != IntPtr.Zero) { WinUsb_Free(usb); usb = IntPtr.Zero; } file?.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct InterfaceDescriptor { public byte Length, Type, Number, Alternate, Endpoints, Class, Subclass, Protocol, String; }
    [StructLayout(LayoutKind.Sequential)] struct PipeInfo { public int Type; public byte Id; public ushort MaxPacket; public byte Interval; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct SetupPacket { public byte RequestType, Request; public ushort Value, Index, Length; }
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_QueryInterfaceSettings(IntPtr handle, byte alternate, out InterfaceDescriptor descriptor);
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_QueryPipe(IntPtr handle, byte alternate, byte index, out PipeInfo pipe);
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_ControlTransfer(IntPtr handle, SetupPacket setup, IntPtr data, uint length, out uint transferred, IntPtr overlapped);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr handle);
    [DllImport("winusb.dll")] static extern bool WinUsb_Free(IntPtr handle);
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_SetPipePolicy(IntPtr handle, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_ReadPipe(IntPtr handle, byte pipe, byte[] data, uint length, out uint transferred, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)] static extern bool WinUsb_WritePipe(IntPtr handle, byte pipe, byte[] data, uint length, out uint transferred, IntPtr overlapped);
}
public static class Keyboard
{
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public KeyInput Key; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    public static void Emit(int key, bool down) { bool extended = key is >= 0x21 and <= 0x2E or 0x6F or 0x90 or 0xA3 or 0xA5; var input = new Input { Type = 1, Data = new Union { Key = new KeyInput { Scan = (ushort)MapVirtualKey((uint)key, 0), Flags = 8u | (down ? 0u : 2u) | (extended ? 1u : 0u) } } }; if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new IOException("Windows blocked keyboard output. Run the game and DanceBridge at the same privilege level."); }
}
