using System.Collections.Generic;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public sealed class O2JamBeatmap : ManiaBeatmap
{
    public const int ColumnCount = 7;

    public O2JamDifficulty O2JamDifficulty { get; }

    public O2JamTimingMap TimingMap { get; }

    public ushort Level { get; set; }

    public List<O2JamAudioEvent> AutomaticAudioEvents { get; } = [];

    /// <summary>
    /// OJN measure boundaries retained independently from BPM timing points.
    /// </summary>
    public List<double> MeasureLineTimes { get; } = [];

    public O2JamBeatmap(O2JamDifficulty difficulty, O2JamTimingMap timingMap)
        : base(new StageDefinition(ColumnCount))
    {
        O2JamDifficulty = difficulty;
        TimingMap = timingMap;
        Difficulty.CircleSize = ColumnCount;
        Difficulty.SliderMultiplier = 1;
    }
}

public readonly record struct O2JamAudioEvent(
    double Time,
    int SampleId,
    int Volume,
    float Pan,
    O2JamAudioEventKind Kind = O2JamAudioEventKind.Background);

public enum O2JamAudioEventKind
{
    Background,
    KeySound,
}
