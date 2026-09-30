using System.Collections.Generic;
using System.Collections.Immutable;

namespace O2Jam.Formats.Ojn;

public sealed record OjnMetadata
{
    private readonly byte[] cover;
    private readonly byte[] thumbnail;

    public uint SongId { get; }
    public float EncodingVersion { get; }
    public float InitialBpm { get; }
    public string Title { get; }
    public string Artist { get; }
    public string NoteArranger { get; }
    public string OjmFileName { get; }
    public IReadOnlyList<ushort> Levels { get; }
    public IReadOnlyList<uint> Durations { get; }
    public byte[] Cover => (byte[])cover.Clone();
    public byte[] Thumbnail => (byte[])thumbnail.Clone();

    public OjnMetadata(uint songId, float encodingVersion, float initialBpm, string title, string artist,
                       string noteArranger, string ojmFileName, IReadOnlyList<ushort> levels,
                       IReadOnlyList<uint> durations, byte[] cover, byte[] thumbnail)
    {
        SongId = songId;
        EncodingVersion = encodingVersion;
        InitialBpm = initialBpm;
        Title = title;
        Artist = artist;
        NoteArranger = noteArranger;
        OjmFileName = ojmFileName;
        Levels = levels.ToImmutableArray();
        Durations = durations.ToImmutableArray();
        this.cover = (byte[])cover.Clone();
        this.thumbnail = (byte[])thumbnail.Clone();
    }
}

public sealed record OjnDocument
{
    public OjnMetadata Metadata { get; }
    public IReadOnlyList<OjnChart> Charts { get; }

    public OjnDocument(OjnMetadata metadata, IReadOnlyList<OjnChart> charts)
    {
        Metadata = metadata;
        Charts = charts.ToImmutableArray();
    }
}

public sealed record OjnChart
{
    public OjnDifficulty Difficulty { get; }
    public ushort Level { get; }
    public IReadOnlyList<OjnBpmEvent> BpmEvents { get; }
    public IReadOnlyList<OjnNoteEvent> Notes { get; }
    public IReadOnlyList<OjnMeasureFraction> MeasureFractions { get; }
    public uint MeasureCount { get; }

    public OjnChart(OjnDifficulty difficulty, ushort level, IReadOnlyList<OjnBpmEvent> bpmEvents,
                    IReadOnlyList<OjnNoteEvent> notes, IReadOnlyList<OjnMeasureFraction> measureFractions,
                    uint measureCount)
    {
        Difficulty = difficulty;
        Level = level;
        BpmEvents = bpmEvents.ToImmutableArray();
        Notes = notes.ToImmutableArray();
        MeasureFractions = measureFractions.ToImmutableArray();
        MeasureCount = measureCount;
    }
}

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
