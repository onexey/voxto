using System.Runtime.InteropServices;

namespace Voxto;

/// <summary>
/// Inserts the transcription into the active application's current cursor location.
/// Optionally presses Enter immediately after insertion.
/// </summary>
internal sealed class CursorInsertOutput : ITranscriptionOutput
{
    private readonly ICursorTextSender _textSender;

    public CursorInsertOutput()
        : this(new SendInputCursorTextSender())
    {
    }

    internal CursorInsertOutput(ICursorTextSender textSender) => _textSender = textSender;

    public string Id          => "CursorInsert";
    public string DisplayName => "Insert at cursor location";

    public Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        var text = result.FullText;
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        _textSender.Send(text, settings.CursorInsertPressEnter);
        return Task.CompletedTask;
    }
}

internal interface ICursorTextSender
{
    void Send(string text, bool pressEnter);
}

internal sealed class SendInputCursorTextSender : ICursorTextSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const ushort VirtualKeyEnter = 0x0D;

    public void Send(string text, bool pressEnter)
    {
        var inputs = BuildInputs(text, pressEnter);
        if (inputs.Length == 0)
            return;

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
            throw new InvalidOperationException("Failed to send text to the active cursor location.");
    }

    internal static INPUT[] BuildInputs(string text, bool pressEnter)
    {
        var inputs = new INPUT[(text.Length * 2) + (pressEnter ? 2 : 0)];
        var index = 0;

        foreach (var character in text)
        {
            inputs[index++] = CreateUnicodeInput(character, keyUp: false);
            inputs[index++] = CreateUnicodeInput(character, keyUp: true);
        }

        if (pressEnter)
        {
            inputs[index++] = CreateVirtualKeyInput(VirtualKeyEnter, keyUp: false);
            inputs[index]   = CreateVirtualKeyInput(VirtualKeyEnter, keyUp: true);
        }

        return inputs;
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp) =>
        new()
        {
            type = InputKeyboard,
            Anonymous = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyUp : 0)
                }
            }
        };

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp) =>
        new()
        {
            type = InputKeyboard,
            Anonymous = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KeyeventfKeyUp : 0
                }
            }
        };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION Anonymous;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}
