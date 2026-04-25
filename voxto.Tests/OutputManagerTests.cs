using Xunit;
using Voxto;

namespace Voxto.Tests;

public class OutputManagerTests
{
    private static TranscriptionResult AnyResult() => new()
    {
        Timestamp = DateTime.Now,
        Segments  = [(TimeSpan.Zero, TimeSpan.FromSeconds(1), "test")]
    };

    // ── Routing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_EnabledOutput_IsInvoked()
    {
        var spy     = new SpyOutput("spy");
        var manager = new OutputManager(spy);
        var settings = new AppSettings { EnabledOutputs = ["spy"] };

        await manager.WriteAsync(AnyResult(), settings);

        Assert.Equal(1, spy.CallCount);
    }

    [Fact]
    public async Task WriteAsync_DisabledOutput_IsNotInvoked()
    {
        var spy      = new SpyOutput("spy");
        var manager  = new OutputManager(spy);
        var settings = new AppSettings { EnabledOutputs = [] };

        await manager.WriteAsync(AnyResult(), settings);

        Assert.Equal(0, spy.CallCount);
    }

    [Fact]
    public async Task WriteAsync_MultipleEnabled_AllInvoked()
    {
        var a = new SpyOutput("a");
        var b = new SpyOutput("b");
        var manager  = new OutputManager(a, b);
        var settings = new AppSettings { EnabledOutputs = ["a", "b"] };

        await manager.WriteAsync(AnyResult(), settings);

        Assert.Equal(1, a.CallCount);
        Assert.Equal(1, b.CallCount);
    }

    [Fact]
    public async Task WriteAsync_OnlyOneEnabled_OnlyThatOneInvoked()
    {
        var a = new SpyOutput("a");
        var b = new SpyOutput("b");
        var manager  = new OutputManager(a, b);
        var settings = new AppSettings { EnabledOutputs = ["a"] };

        await manager.WriteAsync(AnyResult(), settings);

        Assert.Equal(1, a.CallCount);
        Assert.Equal(0, b.CallCount);
    }

    // ── Failure handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_OneOutputThrows_OtherOutputsStillRun()
    {
        var failing = new FailingOutput("fail");
        var spy     = new SpyOutput("spy");
        var manager  = new OutputManager(failing, spy);
        var settings = new AppSettings { EnabledOutputs = ["fail", "spy"] };

        // Should throw, but spy must still have been called
        await Assert.ThrowsAsync<AggregateException>(() =>
            manager.WriteAsync(AnyResult(), settings));

        Assert.Equal(1, spy.CallCount);
    }

    [Fact]
    public async Task WriteAsync_OneOutputThrows_ThrowsAggregateException()
    {
        var failing  = new FailingOutput("fail");
        var manager  = new OutputManager(failing);
        var settings = new AppSettings { EnabledOutputs = ["fail"] };

        await Assert.ThrowsAsync<AggregateException>(() =>
            manager.WriteAsync(AnyResult(), settings));
    }

    [Fact]
    public async Task WriteAsync_TwoOutputsThrow_AggregateExceptionContainsBoth()
    {
        var a = new FailingOutput("a");
        var b = new FailingOutput("b");
        var manager  = new OutputManager(a, b);
        var settings = new AppSettings { EnabledOutputs = ["a", "b"] };

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            manager.WriteAsync(AnyResult(), settings));

        Assert.Equal(2, ex.InnerExceptions.Count);
    }

    // ── All property ──────────────────────────────────────────────────────────

    [Fact]
    public void All_ReturnsAllRegisteredOutputs()
    {
        var a = new SpyOutput("a");
        var b = new SpyOutput("b");
        var manager = new OutputManager(a, b);

        Assert.Equal(2, manager.All.Count);
    }

    [Fact]
    public void DefaultConstructor_RegistersBuiltInOutputs()
    {
        var manager = new OutputManager();

        Assert.Contains(manager.All, output => output.Id == "MarkdownFile");
        Assert.Contains(manager.All, output => output.Id == "TodoAppend");
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class SpyOutput(string id) : ITranscriptionOutput
    {
        public string Id          => id;
        public string DisplayName => id;
        public int    CallCount   { get; private set; }

        public Task WriteAsync(TranscriptionResult result, AppSettings settings)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOutput(string id) : ITranscriptionOutput
    {
        public string Id          => id;
        public string DisplayName => id;

        public Task WriteAsync(TranscriptionResult result, AppSettings settings) =>
            throw new InvalidOperationException($"Output '{id}' intentionally failed");
    }
}
