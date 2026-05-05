using Voxto;
using Xunit;
using System.Text.Json;

namespace Voxto.Tests;

public class CursorInsertOutputTests
{
    private readonly FakeCursorTextSender _sender = new();
    private readonly CursorInsertOutput _output;

    public CursorInsertOutputTests() => _output = new CursorInsertOutput(_sender);

    [Fact]
    public async Task WriteAsync_FullText_SendsTextToCursor()
    {
        await _output.WriteAsync(Result("Send this"), new AppSettings());

        Assert.Equal("Send this", _sender.LastText);
        Assert.False(_sender.LastPressEnter);
        Assert.Equal(1, _sender.CallCount);
    }

    [Fact]
    public async Task WriteAsync_PressEnterEnabled_SendsEnterAfterText()
    {
        var settings = new AppSettings
        {
            OutputSettings =
            {
                [CursorInsertOutput.OutputId] = JsonSerializer.SerializeToElement(new CursorInsertOutputSettings
                {
                    PressEnterAfterInsert = true
                })
            }
        };

        await _output.WriteAsync(Result("Run it"), settings);

        Assert.Equal("Run it", _sender.LastText);
        Assert.True(_sender.LastPressEnter);
        Assert.Equal(1, _sender.CallCount);
    }

    [Fact]
    public async Task WriteAsync_UsesStoredSettingsOverLegacyProperty()
    {
        var settings = new AppSettings
        {
            OutputSettings =
            {
                [CursorInsertOutput.OutputId] = JsonSerializer.SerializeToElement(new CursorInsertOutputSettings
                {
                    PressEnterAfterInsert = true
                })
            }
        };

        await _output.WriteAsync(Result("Configured"), settings);

        Assert.True(_sender.LastPressEnter);
    }

    [Fact]
    public async Task WriteAsync_WhitespaceOnlyText_DoesNotSendInput()
    {
        var result = new TranscriptionResult
        {
            Segments =
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(1), "  "),
                (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "\t")
            ]
        };

        await _output.WriteAsync(result, new AppSettings
        {
            OutputSettings =
            {
                [CursorInsertOutput.OutputId] = JsonSerializer.SerializeToElement(new CursorInsertOutputSettings
                {
                    PressEnterAfterInsert = true
                })
            }
        });

        Assert.Equal(0, _sender.CallCount);
        Assert.Null(_sender.LastText);
    }

    [Fact]
    public void BuildInputs_PressEnterEnabled_AppendsEnterKeyInputs()
    {
        var inputs = SendInputCursorTextSender.BuildInputs("A", pressEnter: true);

        Assert.Equal(4, inputs.Length);
        Assert.Equal((ushort)'A', inputs[0].Anonymous.ki.wScan);
        Assert.Equal((ushort)'A', inputs[1].Anonymous.ki.wScan);
        Assert.Equal((ushort)0x0D, inputs[2].Anonymous.ki.wVk);
        Assert.Equal((ushort)0x0D, inputs[3].Anonymous.ki.wVk);
    }

    [Fact]
    public void SettingsPage_IdIsCursorInsert()
    {
        var pageId = RunInSta(() => _output.SettingsPage.Id);
        Assert.Equal("CursorInsert", pageId);
    }

    [Fact]
    public void SettingsPage_IdMatchesOutputId()
    {
        var pageId = RunInSta(() => _output.SettingsPage.Id);
        Assert.Equal(CursorInsertOutput.OutputId, pageId);
    }

    [Fact]
    public void SettingsPage_DisplayName_IsNotEmpty()
    {
        var displayName = RunInSta(() => _output.SettingsPage.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(displayName));
    }

    [Fact]
    public void BuildFailureMessage_IncludesSentExpectedAndWin32Error()
    {
        var message = SendInputCursorTextSender.BuildFailureMessage(sent: 2, expected: 4, lastError: 5);

        Assert.Contains("sent 2 of 4 inputs", message);
        Assert.Contains("Win32 error 5", message);
    }

    [Fact]
    public void Send_WhenSendInputReturnsInvalidParameter_FallsBackToClipboardPaste()
    {
        var keyboardInputApi = new FakeKeyboardInputApi(sent: 0, lastError: 87);
        var clipboardPasteSender = new FakeClipboardPasteSender();
        var sender = new SendInputCursorTextSender(keyboardInputApi, clipboardPasteSender);

        sender.Send("Paste this", pressEnter: true);

        Assert.Equal("Paste this", clipboardPasteSender.LastText);
        Assert.True(clipboardPasteSender.LastPressEnter);
        Assert.Equal(1, clipboardPasteSender.CallCount);
    }

    [Fact]
    public void Send_WhenSendInputFailsForAnotherReason_Throws()
    {
        var keyboardInputApi = new FakeKeyboardInputApi(sent: 0, lastError: 5);
        var clipboardPasteSender = new FakeClipboardPasteSender();
        var sender = new SendInputCursorTextSender(keyboardInputApi, clipboardPasteSender);

        var exception = Assert.Throws<InvalidOperationException>(() => sender.Send("Paste this", pressEnter: false));

        Assert.Contains("Win32 error 5", exception.Message);
        Assert.Equal(0, clipboardPasteSender.CallCount);
    }

    [Fact]
    public void Win32KeyboardInputApi_UsesSendInputEntryPoint()
    {
        var method = typeof(Win32KeyboardInputApi).GetMethod(
            "NativeSendInput",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute), inherit: false));
        var dllImport = Assert.IsType<System.Runtime.InteropServices.DllImportAttribute>(attribute);

        Assert.Equal("SendInput", dllImport.EntryPoint);
    }

    private static TranscriptionResult Result(string text) =>
        new()
        {
            Timestamp = new DateTime(2026, 5, 4, 9, 0, 0),
            Segments = [(TimeSpan.Zero, TimeSpan.FromSeconds(1), text)]
        };

    private sealed class FakeCursorTextSender : ICursorTextSender
    {
        public int CallCount { get; private set; }
        public string? LastText { get; private set; }
        public bool LastPressEnter { get; private set; }

        public void Send(string text, bool pressEnter)
        {
            CallCount++;
            LastText = text;
            LastPressEnter = pressEnter;
        }
    }

    private sealed class FakeKeyboardInputApi(uint sent, int lastError) : IKeyboardInputApi
    {
        public uint SendInput(uint nInputs, SendInputCursorTextSender.INPUT[] pInputs, int cbSize) => sent;

        public int GetLastError() => lastError;
    }

    private sealed class FakeClipboardPasteSender : IClipboardPasteSender
    {
        public int CallCount { get; private set; }
        public string? LastText { get; private set; }
        public bool LastPressEnter { get; private set; }

        public void Paste(string text, bool pressEnter)
        {
            CallCount++;
            LastText = text;
            LastPressEnter = pressEnter;
        }
    }

    private static T RunInSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var completed = thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(completed, "The STA test thread did not complete within 30 seconds.");
        if (capturedException is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturedException).Throw();

        return result!;
    }
}
