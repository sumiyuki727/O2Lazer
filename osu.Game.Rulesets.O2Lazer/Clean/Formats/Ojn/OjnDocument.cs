using System.Collections.Generic;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojn;

public sealed record OjnMetadata(
    uint SongId,
    float EncodingVersion,
    float InitialBpm,
    string Title,
    string Artist,
    string NoteArranger,
    string OjmFileName,
    IReadOnlyList<ushort> Levels,
    IReadOnlyList<uint> Durations,
    byte[] Cover,
    byte[] Thumbnail);

public sealed record OjnDocument(OjnMetadata Metadata, IReadOnlyList<OjnChart> Charts);

public sealed record OjnChart(
    O2JamDifficulty Difficulty,
    ushort Level,
    IReadOnlyList<O2JamBpmEvent> BpmEvents,
    IReadOnlyList<OjnNoteEvent> Notes,
    IReadOnlyList<OjnMeasureFraction> MeasureFractions,
    uint MeasureCount);

public readonly record struct OjnMeasureFraction(int Measure, double Fraction);

public readonly record struct OjnNoteEvent(
    double Position,
    ushort Channel,
    int SampleId,
    int Volume,
    float Pan,
    OjnNoteType Type,
    OjnSampleKind SampleKind,
    double? EndPosition = null,
    int? TailSampleId = null,
    int TailVolume = 100,
    float TailPan = 0)
{
    public bool IsPlayable => Channel is >= 2 and <= 8;
}

public enum OjnNoteType : byte
{
    Tap,
    Hold,
    Release,
}

public enum OjnSampleKind : byte
{
    KeySound,
    Background,
}
