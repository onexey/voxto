using System.Text;

namespace Voxto;

/// <summary>
/// Converts a list of transcript segments into a Markdown string.
/// Kept stateless and dependency-free so it can be unit-tested without file I/O.
/// </summary>
internal static class MarkdownFormatter
{
    /// <summary>
    /// Formats a collection of transcript segments as a Markdown document.
    /// </summary>
    /// <param name="segments">The timestamped text segments produced by Whisper.</param>
    /// <param name="timestamp">The date/time stamp to embed in the header (defaults to <see cref="DateTime.Now"/>).</param>
    /// <returns>The complete Markdown text.</returns>
    public static string Format(
        IEnumerable<(TimeSpan Start, TimeSpan End, string Text)> segments,
        DateTime? timestamp = null)
    {
        var ts = timestamp ?? DateTime.Now;
        var sb = new StringBuilder();

        sb.AppendLine("# Transcription");
        sb.AppendLine();
        sb.AppendLine($"**Date:** {ts:dddd, MMMM d, yyyy}  ");
        sb.AppendLine($"**Time:** {ts:HH:mm:ss}  ");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var (start, end, text) in segments)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            sb.AppendLine($"`{start:hh\\:mm\\:ss}` → `{end:hh\\:mm\\:ss}`  ");
            sb.AppendLine(text.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
