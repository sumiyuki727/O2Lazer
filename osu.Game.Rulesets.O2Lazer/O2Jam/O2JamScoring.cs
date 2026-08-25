using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.O2Jam;

public static class O2JamScoring
{
    public const double DefaultBpm = 130;

    /// <summary>
    /// Full O2Jam judgment area in beats (64px judgment bar / 385px measure at 1x hi-speed).
    /// </summary>
    public const double JudgmentAreaBeats = 0.664;

    public const double CoolBeatThreshold = 0.2;
    public const double GoodBeatThreshold = 0.5;
    public const double BadBeatThreshold = 0.8;

    public const double MaximumLife = 1000;

    public static double BeatWindowForBpm(double bpm, double beatThreshold)
        => beatThreshold * JudgmentAreaBeats * 60000 / (bpm > 0 ? bpm : DefaultBpm);

    public static OjnDifficulty DifficultyFor(IBeatmapInfo beatmap)
    {
        var name = beatmap.DifficultyName.AsSpan().TrimStart();
        if (name.StartsWith("EX", StringComparison.OrdinalIgnoreCase))
            return OjnDifficulty.EX;
        if (name.StartsWith("NX", StringComparison.OrdinalIgnoreCase))
            return OjnDifficulty.NX;
        if (name.StartsWith("HX", StringComparison.OrdinalIgnoreCase))
            return OjnDifficulty.HX;

        return (OjnDifficulty)Math.Clamp((int)Math.Round(beatmap.Difficulty.OverallDifficulty), 0, 2);
    }

    public static int LifeDelta(OjnDifficulty difficulty, HitResult result) => (difficulty, result) switch
    {
        (OjnDifficulty.EX, HitResult.Perfect) => 3,
        (OjnDifficulty.EX, HitResult.Good) => 2,
        (OjnDifficulty.EX, HitResult.Ok) => -10,
        (OjnDifficulty.EX, _) => -50,

        (OjnDifficulty.NX, HitResult.Perfect) => 2,
        (OjnDifficulty.NX, HitResult.Good) => 1,
        (OjnDifficulty.NX, HitResult.Ok) => -7,
        (OjnDifficulty.NX, _) => -40,

        (OjnDifficulty.HX, HitResult.Perfect) => 1,
        (OjnDifficulty.HX, HitResult.Good) => 0,
        (OjnDifficulty.HX, HitResult.Ok) => -5,
        (OjnDifficulty.HX, _) => -30,
        _ => 0,
    };
}

public sealed class O2JamScoreState
{
    public long Score { get; private set; }
    public int Combo { get; private set; }
    public int MaximumCombo { get; private set; }
    public int JamProgress { get; private set; }
    public int JamCombo { get; private set; }
    public int MaximumJamCombo { get; private set; }
    public int Buffer { get; private set; }

    private int jams;
    private int bufferProgress;

    public void Apply(HitResult result, int count = 1)
    {
        for (var i = 0; i < count; i++)
            applySingle(result);
    }

    public void Reset()
    {
        Score = 0;
        Combo = -1;
        MaximumCombo = 0;
        JamProgress = 0;
        JamCombo = 0;
        MaximumJamCombo = 0;
        Buffer = 0;
        jams = 0;
        bufferProgress = 0;
    }

    /// <summary>
    /// Consumes one pill to rescue an incoming BAD before it reaches scoring/display.
    /// Returns false when no pill is available.
    /// </summary>
    public bool TryConsumePillForBad()
    {
        if (Buffer <= 0)
            return false;

        Buffer--;
        bufferProgress = 0;
        return true;
    }

    private void applySingle(HitResult result)
    {
        if (result == HitResult.Ok && Buffer > 0)
        {
            Buffer--;
            bufferProgress = 0;
            result = HitResult.Perfect;
        }

        switch (result)
        {
            case HitResult.Perfect:
                Score += 200 + 10L * jams;
                Combo++;
                bufferProgress++;
                JamProgress += 4;
                if (bufferProgress >= 15)
                {
                    Buffer = Math.Min(Buffer + 1, 5);
                    bufferProgress = 0;
                }

                advanceJam();
                break;

            case HitResult.Good:
                Score += 100 + 5L * jams;
                Combo++;
                bufferProgress = 0;
                JamProgress += 2;
                advanceJam();
                break;

            case HitResult.Ok:
                Score += 4;
                breakCombo();
                break;

            default:
                Score = Math.Max(0, Score - 10);
                breakCombo();
                break;
        }

        MaximumCombo = Math.Max(MaximumCombo, Combo);
    }

    private void advanceJam()
    {
        if (JamProgress < 100)
            return;

        JamProgress %= 100;
        JamCombo++;
        jams++;
        MaximumJamCombo = Math.Max(MaximumJamCombo, JamCombo);
    }

    private void breakCombo()
    {
        Combo = -1;
        bufferProgress = 0;
        JamProgress = 0;
        JamCombo = 0;
    }
}
