using System;
using System.Collections.Generic;
using Xunit;
using Voxto;

namespace Voxto.Tests;

public class MarkdownFormatterTests
{
    private static readonly DateTime FixedTime = new DateTime(2026, 4, 25, 14, 32, 10);

    // ── Header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Format_ContainsH1Title()
    {
        var result = MarkdownFormatter.Format([], FixedTime);
        Assert.Contains("# Transcription", result);
    }

    [Fact]
    public void Format_ContainsFormattedDate()
    {
        var result = MarkdownFormatter.Format([], FixedTime);
        Assert.Contains("**Date:** Saturday, April 25, 2026", result);
    }

    [Fact]
    public void Format_ContainsFormattedTime()
    {
        var result = MarkdownFormatter.Format([], FixedTime);
        Assert.Contains("**Time:** 14:32:10", result);
    }

    [Fact]
    public void Format_ContainsHorizontalRule()
    {
        var result = MarkdownFormatter.Format([], FixedTime);
        Assert.Contains("---", result);
    }

    // ── Segments ─────────────────────────────────────────────────────────────

    [Fact]
    public void Format_SingleSegment_ContainsTimestampAndText()
    {
        var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>
        {
            (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Hello world")
        };

        var result = MarkdownFormatter.Format(segments, FixedTime);

        Assert.Contains("`00:00:01`", result);
        Assert.Contains("`00:00:04`", result);
        Assert.Contains("Hello world", result);
    }

    [Fact]
    public void Format_MultipleSegments_AllAppear()
    {
        var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>
        {
            (TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3), "First segment"),
            (TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), "Second segment"),
        };

        var result = MarkdownFormatter.Format(segments, FixedTime);

        Assert.Contains("First segment",  result);
        Assert.Contains("Second segment", result);
    }

    [Fact]
    public void Format_SegmentsAreSeparatedByBlankLine()
    {
        var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>
        {
            (TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "Alpha"),
            (TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), "Beta"),
        };

        var result = MarkdownFormatter.Format(segments, FixedTime);

        // Each segment block ends with an extra blank line.
        var alphaIdx = result.IndexOf("Alpha", StringComparison.Ordinal);
        var betaIdx  = result.IndexOf("Beta",  StringComparison.Ordinal);
        var between  = result[alphaIdx..betaIdx];
        Assert.Contains(Environment.NewLine + Environment.NewLine, between);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Format_EmptySegments_ProducesNoTimestamps()
    {
        var result = MarkdownFormatter.Format([], FixedTime);
        Assert.DoesNotContain("→", result);
    }

    [Fact]
    public void Format_WhitespaceOnlyText_IsSkipped()
    {
        var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>
        {
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "   "),
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "\t"),
        };

        var result = MarkdownFormatter.Format(segments, FixedTime);
        Assert.DoesNotContain("→", result);
    }

    [Fact]
    public void Format_MixedWhitespaceAndRealText_OnlyRealTextAppears()
    {
        var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>
        {
            (TimeSpan.Zero,                TimeSpan.FromSeconds(1), "  "),
            (TimeSpan.FromSeconds(1),      TimeSpan.FromSeconds(3), "Real text"),
            (TimeSpan.FromSeconds(3),      TimeSpan.FromSeconds(4), ""),
        };

        var result = MarkdownFormatter.Format(segments, FixedTime);

        Assert.Contains("Real text", result);
        // Only one timestamp arrow (the real-text segment).
        Assert.Equal(1, CountOccurrences(result, "→"));
    }

    // ── Timestamp default ─────────────────────────────────────────────────────

    [Fact]
    public void Format_NullTimestamp_UsesCurrentDate()
    {
        // Just verify it doesn't throw and produces some date output.
        var before = DateTime.Now;
        var result = MarkdownFormatter.Format([], timestamp: null);
        var after  = DateTime.Now;

        // The header date should be today's date (check year at minimum).
        Assert.Contains(before.Year.ToString(), result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountOccurrences(string source, string value)
    {
        int count = 0, index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
