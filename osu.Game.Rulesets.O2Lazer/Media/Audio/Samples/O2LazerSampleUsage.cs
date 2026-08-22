namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

public readonly record struct O2LazerSampleUsage(
    ushort SampleKey,
    double Time,
    double? CandidateStartTime = null,
    double? CandidateEndTime = null,
    bool ResumeAfterSeek = false)
{
    public double EarliestTriggerTime => CandidateStartTime ?? Time;

    public double LatestTriggerTime => CandidateEndTime ?? Time;
}
