using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voxto;

/// <summary>
/// Inserts the transcription into the active application's current cursor location.
/// Optionally presses Enter immediately after insertion.
/// </summary>
internal sealed class CursorInsertOutput : ITranscriptionOutput
{
    public const string OutputId = "CursorInsert";

    private readonly ICursorTextSender _textSender;

    public CursorInsertOutput()
        : this(new SendInputCursorTextSender())
    {
    }

    internal CursorInsertOutput(ICursorTextSender textSender) => _textSender = textSender;

    public string Id          => OutputId;
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
    private const int ErrorInvalidParameter = 87;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const ushort VirtualKeyEnter = 0x0D;

    private readonly IKeyboardInputApi _keyboardInputApi;
    private readonly IClipboardPasteSender _clipboardPasteSender;

    public SendInputCursorTextSender()
        : this(new Win32KeyboardInputApi(), new ClipboardPasteSender())
    {
    }

    internal SendInputCursorTextSender(IKeyboardInputApi keyboardInputApi, IClipboardPasteSender clipboardPasteSender)
    {
        _keyboardInputApi    = keyboardInputApi;
        _clipboardPasteSender = clipboardPasteSender;
    }

    public void Send(string text, bool pressEnter)
    {
        var inputs = BuildInputs(text, pressEnter);
        if (inputs.Length == 0)
            return;

        var sent = _keyboardInputApi.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == (uint)inputs.Length)
            return;

        var lastError = _keyboardInputApi.GetLastError();
        if (ShouldUseClipboardFallback(sent, inputs.Length, lastError))
        {
            _clipboardPasteSender.Paste(text, pressEnter);
            return;
        }

        throw new InvalidOperationException(BuildFailureMessage(sent, inputs.Length, lastError));
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

    internal static string BuildFailureMessage(uint sent, int expected, int lastError) =>
        $"Failed to send text to the active cursor location (sent {sent} of {expected} inputs, Win32 error {lastError}).";

    internal static bool ShouldUseClipboardFallback(uint sent, int expected, int lastError) =>
        sent == 0 && expected > 0 && lastError == ErrorInvalidParameter;

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

internal interface IKeyboardInputApi
{
    uint SendInput(uint nInputs, SendInputCursorTextSender.INPUT[] pInputs, int cbSize);
    int GetLastError();
}

internal sealed class Win32KeyboardInputApi : IKeyboardInputApi
{
    public uint SendInput(uint nInputs, SendInputCursorTextSender.INPUT[] pInputs, int cbSize) =>
        NativeSendInput(nInputs, pInputs, cbSize);

    public int GetLastError() => Marshal.GetLastWin32Error();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint NativeSendInput(uint nInputs, SendInputCursorTextSender.INPUT[] pInputs, int cbSize);
}

internal interface IClipboardPasteSender
{
    void Paste(string text, bool pressEnter);
}

internal sealed class ClipboardPasteSender : IClipboardPasteSender
{
    public void Paste(string text, bool pressEnter) =>
        RunInSta(() =>
        {
            var previousData = System.Windows.Clipboard.GetDataObject();

            try
            {
                System.Windows.Clipboard.SetText(text);
                System.Windows.Forms.SendKeys.SendWait("^v");
                if (pressEnter)
                    System.Windows.Forms.SendKeys.SendWait("{ENTER}");
            }
            finally
            {
                if (previousData is not null)
                    System.Windows.Clipboard.SetDataObject(previousData, true);
                else
                    System.Windows.Clipboard.Clear();
            }
        });

    private static void RunInSta(Action action)
    {
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var completed = thread.Join(TimeSpan.FromSeconds(30));
        if (!completed)
            throw new TimeoutException("Clipboard paste did not complete within 30 seconds.");

        if (capturedException is not null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();
    }
}
