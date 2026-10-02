using System.Runtime.InteropServices;

namespace BoardADSBridge;

internal static class WindowsInputOutput
{
    private const uint InputKeyboard = 1;
    private const ushort Numpad1ScanCode = 0x4F;
    private const uint KeyScanCode = 0x0008;
    private const uint KeyUp = 0x0002;

    public static (uint InsertedEvents, int LastError) SetNumpad1Held(bool held) =>
        SendKeyboard(Numpad1ScanCode, KeyScanCode | (held ? 0 : KeyUp));

    private static (uint InsertedEvents, int LastError) SendKeyboard(ushort scanCode, uint flags)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { ScanCode = scanCode, Flags = flags }
            }
        };
        var inserted = SendInput(1, [input], Marshal.SizeOf<Input>());
        return (inserted, inserted == 0 ? Marshal.GetLastWin32Error() : 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        // INPUT is sized to its largest native union member even in keyboard-only mode.
        [FieldOffset(0)] public NativeInputPadding Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInputPadding
    {
        public int Value0;
        public int Value1;
        public uint Value2;
        public uint Value3;
        public uint Value4;
        public UIntPtr Value5;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int size);
}
