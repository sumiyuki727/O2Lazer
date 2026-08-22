using System.Collections.Generic;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public interface IO2LazerBeatmap
{
    int TickResolution { get; set; }

    int TotalColumns { get; set; }

    O2LazerLayoutVariant LayoutVariant { get; set; }

    /// <summary>O2LAZER #RANK value: 0=Very Hard, 1=Hard, 2=Normal (default), 3=Easy, 4=Very Easy.</summary>
    int Rank { get; set; }

    /// <summary>O2LAZER #DEFEXRANK / #EXRANK percentage (100 = NORMAL), or null when only #RANK is set.</summary>
    double? ExRank { get; set; }

    /// <summary>
    ///     O2LAZER #TOTAL value: gauge recovery coefficient.
    ///     Zero means the default formula <c>7.605 × N / (0.01 × N + 6.5)</c> applies, where N is the total note count.
    /// </summary>
    double Total { get; set; }

    O2LazerTimingMap? TimingMap { get; set; }

    IReadOnlyDictionary<ushort, string> SampleDefinitions { get; set; }

    IReadOnlyList<O2LazerSampleEvent> BackgroundSampleEvents { get; set; }

    IReadOnlyList<O2LazerBranchDecision> BranchDecisions { get; set; }

    O2LazerTextEvents TextEvents { get; set; }

    O2LazerBgaTimeline Bga { get; set; }

    O2LazerLongNoteMode LockedLongNoteMode { get; set; }

    string? PreviewFile { get; set; }

    string? StageFile { get; set; }

    string? BackBmp { get; set; }

    string? Banner { get; set; }
}

internal static class O2LazerBeatmapExtensions
{
    public static void CopyO2LazerDataFrom(this IO2LazerBeatmap target, IO2LazerBeatmap source)
    {
        target.TickResolution = source.TickResolution;
        target.TotalColumns = source.TotalColumns;
        target.LayoutVariant = source.LayoutVariant;
        target.Rank = source.Rank;
        target.ExRank = source.ExRank;
        target.Total = source.Total;
        target.TimingMap = source.TimingMap;
        target.SampleDefinitions = source.SampleDefinitions;
        target.BackgroundSampleEvents = source.BackgroundSampleEvents;
        target.BranchDecisions = source.BranchDecisions;
        target.TextEvents = source.TextEvents;
        target.Bga = source.Bga;
        target.LockedLongNoteMode = source.LockedLongNoteMode;
        target.PreviewFile = source.PreviewFile;
        target.StageFile = source.StageFile;
        target.BackBmp = source.BackBmp;
        target.Banner = source.Banner;
    }

    public static IEnumerable<string> GetSongSelectBackgroundCandidates(this IO2LazerBeatmap beatmap)
    {
        if (!string.IsNullOrWhiteSpace(beatmap.StageFile))
            yield return beatmap.StageFile;

        if (!string.IsNullOrWhiteSpace(beatmap.BackBmp))
            yield return beatmap.BackBmp;

        if (!string.IsNullOrWhiteSpace(beatmap.Banner))
            yield return beatmap.Banner;
    }
}
