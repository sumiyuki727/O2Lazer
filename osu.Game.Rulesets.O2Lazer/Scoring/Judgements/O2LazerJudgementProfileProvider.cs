using System;
using System.Collections.Concurrent;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public static class O2LazerJudgementProfileProvider
{
    private static readonly double[] rank_rates = [0.25, 0.50, 0.75, 1.00, 1.25];
    private static readonly ConcurrentDictionary<ProfileKey, O2LazerJudgementProfile> profiles = new();

    public static O2LazerJudgementWindowTable GetTable(O2LazerLayoutVariant layout, int column, int rank, bool tail)
        => getTable(layout, column, RateForRank(rank), tail, O2JamScoring.DefaultBpm);

    public static O2LazerJudgementWindowTable GetTable(O2LazerLayoutVariant layout, int column, double judgementRate, bool tail)
        => getTable(layout, column, judgementRate, tail, O2JamScoring.DefaultBpm);

    public static O2LazerJudgementWindowTable GetTable(O2LazerLayoutVariant layout, int column, double judgementRate, bool tail, double bpm)
        => getTable(layout, column, judgementRate, tail, bpm);

    public static double RateForRank(int rank) => rank_rates[Math.Clamp(rank, 0, 4)];

    public static double RateForRank(O2LazerLayoutVariant layout, int rank) => RateForRank(rank);

    public static double RateForExRank(O2LazerLayoutVariant layout, double exRank) => RateForRank(2) * exRank / 100d;

    private static O2LazerJudgementWindowTable getTable(O2LazerLayoutVariant layout, int column, double judgementRate, bool tail, double bpm)
    {
        var profile = profiles.GetOrAdd(new ProfileKey(layout, judgementRate, bpm), static key => o2Jam(key.Bpm));
        return tail ? profile.LongNoteTail : profile.Normal;
    }

    private static O2LazerJudgementProfile o2Jam(double bpm)
    {
        var table = new O2LazerJudgementWindowTable([
            fixedWindow(HitResult.Perfect, bpmWindow(O2JamScoring.CoolBeatThreshold, bpm)),
            fixedWindow(HitResult.Good, bpmWindow(O2JamScoring.GoodBeatThreshold, bpm)),
            fixedWindow(HitResult.Ok, bpmWindow(O2JamScoring.BadBeatThreshold, bpm)),
        ]);

        return new O2LazerJudgementProfile(table, table, table, table);
    }

    private static (double slow, double fast) bpmWindow(double beatThreshold, double bpm)
    {
        var window = O2JamScoring.BeatWindowForBpm(bpm, beatThreshold);
        return (-window, window);
    }

    private static O2LazerJudgementWindow fixedWindow(HitResult result, (double slow, double fast) row)
        => new(result, row.slow, row.fast);

    private readonly record struct ProfileKey(O2LazerLayoutVariant Layout, double Rate, double Bpm);
}
