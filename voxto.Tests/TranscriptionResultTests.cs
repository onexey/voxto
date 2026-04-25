using Voxto;

namespace Voxto.Tests;

public class TranscriptionResultTests
{
    // ── FullText ──────────────────────────────────────────────────────────────

    [Fact]
    public void FullText_NoSegments_ReturnsEmptyString()
    {
        var result = new TranscriptionResult { Segments = [] };
        Assert.Equal(string.Empty, result.FullText);
    }

    [Fact]
    public void FullText_SingleSegment_ReturnsTrimmedText()
    {
        var result = new TranscriptionResult
        {
            Segments = [(TimeSpan.Zero, TimeSpan.FromSeconds(2), "  Hello world  ")]
        };
        Assert.Equal("Hello world", result.FullText);
    }

    [Fact]
    public void FullText_MultipleSegments_JoinedWithSpace()
    {
        var result = new TranscriptionResult
        {
            Segments =
            [
                (TimeSpan.Zero,               TimeSpan.FromSeconds(2), "Hello"),
                (TimeSpan.FromSeconds(2),     TimeSpan.FromSeconds(4), "world"),
            ]
        };
        Assert.Equal("Hello world", result.FullText);
    }

    [Fact]
    public void FullText_WhitespaceOnlySegments_AreExcluded()
    {
        var result = new TranscriptionResult
        {
            Segments =
            [
                (TimeSpan.Zero,           TimeSpan.FromSeconds(1), "   "),
                (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Real text"),
                (TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "\t"),
            ]
        };
        Assert.Equal("Real text", result.FullText);
    }

    [Fact]
    public void FullText_AllWhitespaceSegments_ReturnsEmptyString()
    {
        var result = new TranscriptionResult
        {
            Segments =
            [
                (TimeSpan.Zero,           TimeSpan.FromSeconds(1), "  "),
                (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), ""),
            ]
        };
        Assert.Equal(string.Empty, result.FullText);
    }

    [Fact]
    public void FullText_LeadingAndTrailingSegmentsAreWhitespace_MiddleOnlyInResult()
    {
        var result = new TranscriptionResult
        {
            Segments =
            [
                (TimeSpan.Zero,           TimeSpan.FromSeconds(1), "  "),
                (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Middle"),
                (TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "  "),
            ]
        };
        Assert.Equal("Middle", result.FullText);
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void Segments_DefaultValue_IsEmptyList()
    {
        var result = new TranscriptionResult();
        Assert.Empty(result.Segments);
    }
}
