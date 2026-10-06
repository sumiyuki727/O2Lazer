namespace osu.Game.Rulesets.O2Lazer.Integration.Beatmaps;

public readonly record struct O2JamSourcePosition(int Measure, int Tick);

public readonly record struct O2JamObjectSourcePosition(O2JamSourcePosition Head, O2JamSourcePosition? Tail = null);
