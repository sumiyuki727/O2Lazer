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
        => getTable(layout, column, RateForRank(rank), tail);

    public static O2LazerJudgementWindowTable GetTable(O2LazerLayoutVariant layout, int column, double judgementRate, bool tail)
        => getTable(layout, column, judgementRate, tail);

    public static double RateForRank(int rank) => rank_rates[Math.Clamp(rank, 0, 4)];

    public static double RateForRank(O2LazerLayoutVariant layout, int rank) => RateForRank(rank);

    public static double RateForExRank(O2LazerLayoutVariant layout, double exRank) => RateForRank(2) * exRank / 100d;

    private static O2LazerJudgementWindowTable getTable(O2LazerLayoutVariant layout, int column, double judgementRate, bool tail)
    {
        var profile = profiles.GetOrAdd(new ProfileKey(layout, judgementRate), static key => o2Jam());
        return tail ? profile.LongNoteTail : profile.Normal;
    }

    private static O2LazerJudgementProfile o2Jam()
    {
        var table = new O2LazerJudgementWindowTable([
            fixedWindow(HitResult.Perfect, (-O2JamScoring.CoolWindow, O2JamScoring.CoolWindow)),
            fixedWindow(HitResult.Good, (-O2JamScoring.GoodWindow, O2JamScoring.GoodWindow)),
            fixedWindow(HitResult.Ok, (-O2JamScoring.BadWindow, O2JamScoring.BadWindow)),
        ]);

        return new O2LazerJudgementProfile(table, table, table, table);
    }

    private static O2LazerJudgementWindow fixedWindow(HitResult result, (double slow, double fast) row)
        => new(result, row.slow, row.fast);

    private readonly record struct ProfileKey(O2LazerLayoutVariant Layout, double Rate);
}
