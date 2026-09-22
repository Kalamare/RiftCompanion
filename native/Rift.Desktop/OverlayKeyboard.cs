using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Rift.Desktop;

// Event-driven input sink. No keyboard hook, injection, character decoding or key journal.
internal sealed class OverlayKeyboard(nint target) : IDisposable
{
    private readonly byte[] buffer = new byte[64];
    private readonly OverlayShortcutState state = new();
    public bool Enabled { get; private set; }
    public int Error { get; private set; }
    public void SetEnabled(bool enabled)
    {
        if (enabled == Enabled) return;
        var device = new RawDevice { Page = 1, Usage = 6, Flags = enabled ? 0x100u : 1u, Target = enabled ? target : 0 };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawDevice>()))
        { Error = Marshal.GetLastWin32Error(); return; }
        Enabled = enabled; Error = 0; state.Reset();
        if (enabled)
        {
            foreach (ushort key in new ushort[] { 0xA2, 0xA3, 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C, 0x58 })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) state.Seed(key);
        }
    }
    public int Read(nint input)
    {
        if (!Enabled) return 0;
        uint size = (uint)buffer.Length;
        int header = 8 + 2 * IntPtr.Size;
        uint read = GetRawInputData(input, 0x10000003, buffer, ref size, (uint)header);
        return read == uint.MaxValue || read > buffer.Length ? 0 : Decode(buffer.AsSpan(0, (int)read), header, state);
    }
    internal static int Decode(ReadOnlySpan<byte> data, int header, OverlayShortcutState state)
    {
        if (header < 16 || data.Length < header + 16 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 1) return 0;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(data[(header + 2)..]);
        ushort key = BinaryPrimitives.ReadUInt16LittleEndian(data[(header + 6)..]);
        ushort scan = BinaryPrimitives.ReadUInt16LittleEndian(data[header..]);
        if (key == 0x11) key = (ushort)((flags & 2) == 0 ? 0xA2 : 0xA3);
        if (key == 0x10) key = (ushort)(scan == 0x36 ? 0xA1 : 0xA0);
        if (key == 0x12) key = (ushort)((flags & 2) == 0 ? 0xA4 : 0xA5);
        return state.Process(key, (flags & 1) == 0);
    }
    public void Dispose() => SetEnabled(false);
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input, uint command, [Out] byte[] data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}

internal sealed class OverlayShortcutState
{
    private int modifiers;
    private bool xDown;
    public void Reset() { modifiers = 0; xDown = false; }
    public void Seed(ushort key) { if (key == 0x58) xDown = true; else Process(key, true); }
    public int Process(ushort key, bool down)
    {
        int mask = key switch { 0xA2 => 1, 0xA3 => 2, 0xA0 => 4, 0xA1 => 8, 0xA4 => 16, 0xA5 => 32, 0x5B => 64, 0x5C => 128, _ => 0 };
        if (mask != 0) { modifiers = down ? modifiers | mask : modifiers & ~mask; return 0; }
        if (key != 0x58) return 0;
        bool pressed = down && !xDown; xDown = down;
        return !pressed || (modifiers & 3) == 0 || (modifiers & 240) != 0 ? 0 : (modifiers & 12) != 0 ? 2 : 1;
    }
}
