using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Rift.Desktop;

static class OverlayKeyboardChecks
{
    public static void Run()
    {
        var state = new OverlayShortcutState();
        int Key(ushort key, bool down = true, ushort scan = 0, bool extended = false)
        {
            int header = 8 + 2 * IntPtr.Size;
            var data = new byte[header + 16];
            BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(header), scan);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(header + 2), (ushort)((down ? 0 : 1) | (extended ? 2 : 0)));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(header + 6), key);
            return OverlayKeyboard.Decode(data, header, state);
        }
        Key(0x11); // Left Ctrl, then X.
        if (Key(0x58) != 1 || Key(0x58) != 0) throw new Exception("Raw Ctrl+X or repeat suppression failed");
        Key(0x58, false); Key(0x10, scan: 0x36);
        if (Key(0x58) != 2) throw new Exception("Raw Ctrl+right Shift+X failed");
        Key(0x58, false); Key(0x12);
        if (Key(0x58) != 0) throw new Exception("Alt chord must not activate overlay");
        state.Reset(); // Leaving LoL clears held modifiers.
        if (Key(0x58) != 0) throw new Exception("Modifier survived focus loss");
        state.Reset(); Key(0x11); Key(0x11, extended: true); Key(0x11, false);
        if (Key(0x58) != 1) throw new Exception("Releasing left Ctrl cleared right Ctrl");
        state.Reset(); state.Seed(0xA2); state.Seed(0x58);
        if (Key(0x58) != 0) throw new Exception("Focus entry with held X must not trigger");
        Key(0x58, false);
        if (Key(0x58) != 1 || OverlayKeyboard.Decode([], 24, state) != 0) throw new Exception("Rearmed chord or truncated packet failed");
        using var source = new HwndSource(new HwndSourceParameters("Rift keyboard check") { ParentWindow = new nint(-3) });
        using var keyboard = new OverlayKeyboard(source.Handle);
        keyboard.SetEnabled(true);
        if (!keyboard.Enabled || keyboard.Error != 0 || !Registered(source.Handle)) throw new Exception($"Raw Input registration failed: {keyboard.Error}");
        keyboard.SetEnabled(false);
        if (keyboard.Enabled || Registered(source.Handle)) throw new Exception("Raw Input registration not released");
        Console.WriteLine("OK clavier overlay : paquets Raw Input, Ctrl gauche/droite, Maj, anti-répétition, perte de focus ; inscription Windows réelle puis retrait.");
    }
    private static bool Registered(nint target)
    {
        var devices = new Device[16]; uint count = (uint)devices.Length;
        uint result = GetRegisteredRawInputDevices(devices, ref count, (uint)Marshal.SizeOf<Device>());
        if (result == uint.MaxValue) throw new Exception("Unable to inspect Raw Input registration");
        return devices.Take((int)result).Any(d => d.Page == 1 && d.Usage == 6 && d.Target == target && d.Flags == 0x100);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll")] private static extern uint GetRegisteredRawInputDevices([Out] Device[] devices, ref uint count, uint size);
}
