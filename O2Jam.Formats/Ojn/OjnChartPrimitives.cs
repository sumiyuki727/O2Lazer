namespace osu.Game.Rulesets.O2Lazer.Formats.Ojn;

public enum OjnDifficulty
{
    EX,
    NX,
    HX,
}

/// <summary>
/// A BPM change in normalised measure position, independent of any playback clock.
/// </summary>
public readonly record struct OjnBpmEvent(double Position, double Bpm);
