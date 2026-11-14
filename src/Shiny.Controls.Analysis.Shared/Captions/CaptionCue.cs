namespace Shiny.Controls.Analysis;

/// <summary>One caption: a run of recognized speech and when it plays.</summary>
/// <param name="Start">Where the speech starts, in media time.</param>
/// <param name="End">Where it ends, in media time.</param>
/// <param name="Text">The recognized text.</param>
/// <param name="Words">Per-word timings, when the recognizer reports them; otherwise <c>null</c>.</param>
public record CaptionCue(TimeSpan Start, TimeSpan End, string Text, IReadOnlyList<CaptionWord>? Words = null)
{
    /// <summary>Whether <paramref name="position"/> falls inside this cue.</summary>
    public bool Contains(TimeSpan position) => position >= this.Start && position < this.End;
}

/// <summary>A recognized word and when it was spoken, in media time.</summary>
public record CaptionWord(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Something that produces captions — a speech caption analyzer. A caption view reads <see cref="Cues"/> and
/// <see cref="PartialText"/> and redraws on <see cref="CaptionsChanged"/>.
/// </summary>
public interface ICaptionSource
{
    /// <summary>Committed captions, in time order.</summary>
    IReadOnlyList<CaptionCue> Cues { get; }

    /// <summary>The utterance still being recognized, or <c>null</c>. It may change, or be replaced by a cue.</summary>
    string? PartialText { get; }

    /// <summary>
    /// <c>true</c> when <see cref="Cues"/> was built ahead of playback and covers the source, so a view should show the
    /// cue at the playhead; <c>false</c> for live captions, which a view shows as they arrive.
    /// </summary>
    bool IsTimed { get; }

    /// <summary>Raised (on any thread) when <see cref="Cues"/>, <see cref="PartialText"/> or <see cref="IsTimed"/> changes.</summary>
    event EventHandler? CaptionsChanged;
}

/// <summary>Helpers over a list of <see cref="CaptionCue"/>s.</summary>
public static class CaptionCueExtensions
{
    /// <summary>The cue playing at <paramref name="position"/>, or <c>null</c>. Binary search; <paramref name="cues"/> must be in time order.</summary>
    public static CaptionCue? CueAt(this IReadOnlyList<CaptionCue> cues, TimeSpan position)
    {
        int lo = 0, hi = cues.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var cue = cues[mid];
            if (position < cue.Start)
                hi = mid - 1;
            else if (position >= cue.End)
                lo = mid + 1;
            else
                return cue;
        }
        return null;
    }

    /// <summary>The captions as WebVTT, ready to save next to a video or hand to a player.</summary>
    public static string ToWebVtt(this IEnumerable<CaptionCue> cues)
    {
        var sb = new System.Text.StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            sb.Append(Format(cue.Start)).Append(" --> ").Append(Format(cue.End)).Append('\n');
            sb.Append(cue.Text.Replace("\n", " ")).Append("\n\n");
        }
        return sb.ToString();

        static string Format(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
}

/// <summary>How a caption view (<c>CaptionView</c> on either host) picks what to show.</summary>
public enum CaptionDisplayMode
{
    /// <summary>Timed when the source says its cues cover the media (<see cref="ICaptionSource.IsTimed"/>), live otherwise. Default.</summary>
    Auto,

    /// <summary>Show the cue at its <c>Position</c> — captions made ahead of playback.</summary>
    Timed,

    /// <summary>Show the latest text as it is recognized — a live stream, a camera.</summary>
    Live
}
