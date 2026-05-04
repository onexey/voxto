using Voxto;
using Xunit;

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
        var settings = new AppSettings { CursorInsertPressEnter = true };

        await _output.WriteAsync(Result("Run it"), settings);

        Assert.Equal("Run it", _sender.LastText);
        Assert.True(_sender.LastPressEnter);
        Assert.Equal(1, _sender.CallCount);
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

        await _output.WriteAsync(result, new AppSettings { CursorInsertPressEnter = true });

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
    public void Id_IsCursorInsert() => Assert.Equal("CursorInsert", _output.Id);

    [Fact]
    public void DisplayName_IsNotEmpty() => Assert.False(string.IsNullOrWhiteSpace(_output.DisplayName));

    [Fact]
    public void Id_UsesSharedOutputIdConstant() => Assert.Equal(CursorInsertOutput.OutputId, _output.Id);

    [Fact]
    public void BuildFailureMessage_IncludesSentExpectedAndWin32Error()
    {
        var message = SendInputCursorTextSender.BuildFailureMessage(sent: 2, expected: 4, lastError: 5);

        Assert.Contains("sent 2 of 4 inputs", message);
        Assert.Contains("Win32 error 5", message);
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
}
