using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

internal class O2LazerDecodedBeatmap : Beatmap, IO2LazerBeatmap
{
    public int TickResolution { get; set; }

    public int TotalColumns { get; set; }

    public O2LazerLayoutVariant LayoutVariant { get; set; } = O2LazerLayoutVariant.O2Jam7K;

    public int Rank { get; set; } = 2;

    public double? ExRank { get; set; }

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

    public string[] RawLines { get; set; } = [];

    public void CopyFrom(O2LazerParseResult parseResult)
    {
        TickResolution = parseResult.TickResolution;
        TimingMap = parseResult.TimingMap;
        TotalColumns = parseResult.TotalColumns;
        LayoutVariant = parseResult.LayoutVariant;
        Rank = parseResult.Rank;
        ExRank = parseResult.DefaultExRank;
        Total = parseResult.Total;
        SampleDefinitions = parseResult.SampleDefinitions;
        BackgroundSampleEvents = parseResult.BackgroundSampleEvents;
        BranchDecisions = parseResult.BranchDecisions;
        TextEvents = parseResult.TextEvents;
        Bga = parseResult.Bga;
        LockedLongNoteMode = parseResult.LockedLongNoteMode;
        PreviewFile = parseResult.PreviewFile;
        StageFile = parseResult.StageFile;
        BackBmp = parseResult.BackBmp;
        Banner = parseResult.Banner;
    }
}
