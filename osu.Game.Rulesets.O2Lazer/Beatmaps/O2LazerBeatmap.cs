using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public class O2LazerBeatmap : Beatmap<O2LazerHitObject>, IO2LazerBeatmap
{
    public int TickResolution { get; set; } = 192;

    public int TotalColumns { get; set; }

    public O2LazerLayoutVariant LayoutVariant { get; set; } = O2LazerLayoutVariant.O2Jam7K;

    /// <summary>O2LAZER #RANK value: 0=Very Hard, 1=Hard, 2=Normal (default), 3=Easy, 4=Very Easy.</summary>
    public int Rank { get; set; } = 2;

    /// <summary>O2LAZER #DEFEXRANK / #EXRANK percentage (100 = NORMAL), or null when only #RANK is set.</summary>
    public double? ExRank { get; set; }

    /// <summary>
    ///     O2LAZER #TOTAL value: gauge recovery coefficient.
    ///     Zero means the default formula applies.
    /// </summary>
    public double Total { get; set; }

    public O2LazerTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<ushort, string> SampleDefinitions { get; set; } = new Dictionary<ushort, string>();

    public IReadOnlyList<O2LazerSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<O2LazerBranchDecision> BranchDecisions { get; set; } = [];

    public O2LazerTextEvents TextEvents { get; set; } = new(string.Empty, []);

    public O2LazerBgaTimeline Bga { get; set; } = new(new Dictionary<ushort, string>(), new Dictionary<ushort, O2LazerBgaDefinition>(), [], [], O2LazerPoorBgaMode.Replace);

    public O2LazerLongNoteMode LockedLongNoteMode { get; set; }

    public string? PreviewFile { get; set; }

    public string? StageFile { get; set; }

    public string? BackBmp { get; set; }

    public string? Banner { get; set; }

    public override IEnumerable<BeatmapStatistic> GetStatistics()
    {
        var notes = HitObjects.Count(h => h is not O2LazerLongNote);
        var holdNotes = HitObjects.Count(h => h is O2LazerLongNote);
        double total = notes + holdNotes;
        total = Math.Max(total, 1);

        return filterStatistics([
            new BeatmapStatistic
            {
                Name = O2LazerStrings.Notes,
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Circles),
                Content = notes.ToString(),
                BarDisplayLength = perc(notes),
            },
            new BeatmapStatistic
            {
                Name = O2LazerStrings.LongNote,
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Sliders),
                Content = holdNotes.ToString(),
                BarDisplayLength = perc(holdNotes),
            },
        ]);

        float perc(int x) => (float)(x / total);
    }

    private IEnumerable<BeatmapStatistic> filterStatistics(IEnumerable<BeatmapStatistic> statistics)
    {
        return statistics.Where(x => !string.IsNullOrWhiteSpace(x.Content) && x.Content != "0");
    }
}

