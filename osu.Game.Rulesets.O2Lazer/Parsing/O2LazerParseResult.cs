using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Parsing;

public sealed record O2LazerParseResult(
    string? Title,
    string? Artist,
    float? PlayLevel,
    int Rank,
    double Total,
    int TickResolution,
    O2LazerTimingMap TimingMap,
    O2LazerLayoutVariant LayoutVariant,
    int TotalColumns,
    IReadOnlyDictionary<ushort, string> SampleDefinitions,
    IReadOnlyList<O2LazerSampleEvent> BackgroundSampleEvents,
    IReadOnlyList<O2LazerParsedHitObject> HitObjects,
    IReadOnlyList<O2LazerBranchDecision> BranchDecisions,
    O2LazerTextEvents TextEvents,
    O2LazerBgaTimeline Bga,
    string? PreviewFile = null,
    string? Genre = null,
    string? Subtitle = null,
    string? SubArtist = null,
    string? Maker = null,
    string? Url = null,
    string? Email = null,
    string? Comment = null,
    O2LazerLongNoteMode LockedLongNoteMode = O2LazerLongNoteMode.Undefined,
    string? StageFile = null,
    string? BackBmp = null,
    string? Banner = null,
    double? DefaultExRank = null)
{
    internal O2LazerParseResult ShiftedBy(double offset)
    {
        if (offset == 0)
            return this;

        return this with
        {
            TimingMap = TimingMap.ShiftedBy(offset),
            BackgroundSampleEvents = BackgroundSampleEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            HitObjects = HitObjects.Select(h => h with { StartTime = h.StartTime + offset }).ToArray(),
            TextEvents = TextEvents with
            {
                TextEvents = TextEvents.TextEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            },
            Bga = Bga with
            {
                Events = Bga.Events.Select(e => e with { Time = e.Time + offset }).ToArray(),
                OpacityEvents = Bga.OpacityEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            },
        };
    }
}

public readonly record struct O2LazerBranchDecision(int MaxValue, int SelectedValue);

public readonly record struct O2LazerParsedHitObject(
    long Tick,
    long EndTick,
    double StartTime,
    double Duration,
    int Column,
    ushort SourceChannel,
    ushort SampleKey,
    bool IsLongNote,
    int SampleVolume = 100,
    double JudgementRate = 0.75);

public sealed record O2LazerSampleEvent(double Time, long Tick, ushort SampleKey, int Volume);

// ReSharper disable once NotAccessedPositionalProperty.Global
public sealed record O2LazerTextEvent(double Time, long Tick, string Text);

public sealed record O2LazerTextEvents(string? MistakeText, O2LazerTextEvent[] TextEvents);

public sealed record O2LazerBgaTimeline(
    IReadOnlyDictionary<ushort, string> BitmapDefinitions,
    IReadOnlyDictionary<ushort, O2LazerBgaDefinition> BgaDefinitions,
    IReadOnlyList<O2LazerBgaEvent> Events,
    IReadOnlyList<O2LazerBgaOpacityEvent> OpacityEvents,
    O2LazerPoorBgaMode PoorMode);

public readonly record struct O2LazerBgaDefinition(
    ushort BitmapKey,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int DestinationX,
    int DestinationY);

public sealed record O2LazerBgaEvent(double Time, long Tick, ushort DefinitionKey, O2LazerBgaLayer Layer, int Sequence);

public sealed record O2LazerBgaOpacityEvent(double Time, long Tick, O2LazerBgaLayer Layer, float Opacity, int Sequence);

public enum O2LazerBgaLayer
{
    Base,
    Poor,
    Layer1,
    Layer2,
}

public enum O2LazerPoorBgaMode
{
    Replace = 0,
    Add = 1,
    Off = 2,
}

public readonly record struct O2LazerScrollEvent(long Tick, double Factor, int Sequence);

public readonly record struct O2LazerSpeedEvent(long Tick, double Factor, int Sequence);
