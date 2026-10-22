using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Keyboard-only input synthesis via SendInput. Deliberately never touches the clipboard —
/// the build logs record clipboard paste inserting stale host-clipboard contents into NSP
/// fields. The proven pattern instead: focus field, Ctrl+A, Backspace, type literal
/// characters, Tab to commit and move to the next field.
/// </summary>
public static class InputSynthesizer
{
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12; // Alt
    private const ushort VkA = 0x41;
    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT's union must include its largest member even for keyboard-only events.
    // Without MOUSEINPUT, cbSize is 32 rather than 40 on ARM64/x64 and SendInput fails.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeybdInput Keyboard;
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);

    private static void SendKeyDownUp(ushort virtualKey)
    {
        Input[] inputs =
        [
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = virtualKey } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = virtualKey, dwFlags = KeyEventFKeyUp } } },
        ];
        SendChecked(inputs);
    }

    private static void SendChordDownUp(ushort modifierVirtualKey, ushort virtualKey)
    {
        Input[] inputs =
        [
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = modifierVirtualKey } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = virtualKey } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = virtualKey, dwFlags = KeyEventFKeyUp } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = modifierVirtualKey, dwFlags = KeyEventFKeyUp } } },
        ];
        SendChecked(inputs);
    }

    private static void SendUnicodeChar(char character)
    {
        Input[] inputs =
        [
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wScan = character, dwFlags = KeyEventFUnicode } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wScan = character, dwFlags = KeyEventFUnicode | KeyEventFKeyUp } } },
        ];
        SendChecked(inputs);
    }

    /// <summary>Release modifiers before sending a new sequence. This does not dismiss
    /// menu mode: Alt navigation may leave the menu bar active even after Alt is up.</summary>
    public static void ReleaseAllModifierKeys()
    {
        Input[] inputs =
        [
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = VkMenu, dwFlags = KeyEventFKeyUp } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = VkControl, dwFlags = KeyEventFKeyUp } } },
            new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeybdInput { wVk = VkShift, dwFlags = KeyEventFKeyUp } } },
        ];
        SendChecked(inputs);
    }

    private static void SendChecked(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SendInput inserted {sent}/{inputs.Length} events (INPUT size {Marshal.SizeOf<Input>()}).");
        }
    }

    public static void SearchShortcut() => SendChordDownUp(VkControl, 0x46);

    /// <summary>NSP opens the selected page immediately on Alt+Arrow. Escape dismisses
    /// menu focus left by Alt release; Enter here can execute File > New instead.</summary>
    public static void NavigateTree(bool forward)
    {
        SendChordDownUp(VkMenu, forward ? (ushort)0x28 : (ushort)0x26);
        Thread.Sleep(100);
        SendKeyDownUp(0x1B); // Escape, never Enter
    }

    public static void SelectAll() => SendChordDownUp(VkControl, VkA);

    public static void Backspace() => SendKeyDownUp(VkBack);

    public static void FunctionKey(int number)
    {
        if (number < 1 || number > 24) throw new ArgumentOutOfRangeException(nameof(number));
        SendKeyDownUp((ushort)(0x6f + number));
    }
    public static void Tab() => SendKeyDownUp(VkTab);
    public static void Enter() => SendKeyDownUp(0x0D);
    public static void Escape() => SendKeyDownUp(0x1B);

    public static void TypeLiteral(string text)
    {
        foreach (var character in text)
        {
            SendUnicodeChar(character);
        }
    }

    /// <summary>The proven field-replace sequence from the build logs: Ctrl+A, Backspace,
    /// type literal text, Tab to commit. Never pastes.</summary>
    public static void ReplaceFieldValueAndCommit(string newValue, int settleDelayMs = 60)
    {
        SelectAll();
        Thread.Sleep(settleDelayMs);
        Backspace();
        Thread.Sleep(settleDelayMs);
        TypeLiteral(newValue);
        Thread.Sleep(settleDelayMs);
        Tab();
    }
}
