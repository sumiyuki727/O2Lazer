using System.IO;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal static class O2JamManiaStarRating
{
    private static readonly RulesetInfo maniaRuleset = new ManiaRuleset().RulesetInfo;

    public static int Version { get; } = new ManiaDifficultyCalculator(maniaRuleset, new FlatWorkingBeatmap(new ManiaBeatmap(new StageDefinition(O2JamBeatmap.ColumnCount)))).Version;

    // The final digit versions our projection independently of the native mania algorithm.
    public static int CacheVersion => checked(Version * 10 + 1);

    public static double Calculate(O2JamBeatmap beatmap, CancellationToken cancellationToken = default)
    {
        // Preserve the OJN's seven columns and absolute note/hold times, but let mania apply
        // its own object defaults. O2Jam judgement and keysound data must not enter this pipeline.
        var mania = new ManiaBeatmap(new StageDefinition(O2JamBeatmap.ColumnCount))
        {
            BeatmapInfo = new BeatmapInfo(maniaRuleset),
            HitObjects = beatmap.HitObjects.Select<ManiaHitObject, ManiaHitObject>(hitObject => hitObject is HoldNote hold
                ? new HoldNote { StartTime = hold.StartTime, Duration = hold.Duration, Column = hold.Column }
                : new Note { StartTime = hitObject.StartTime, Column = hitObject.Column }).ToList(),
        };
        mania.Difficulty.CircleSize = O2JamBeatmap.ColumnCount;

        var stars = new ManiaDifficultyCalculator(maniaRuleset, new FlatWorkingBeatmap(mania)).Calculate(cancellationToken).StarRating;
        if (!double.IsFinite(stars) || stars < 0)
            throw new InvalidDataException("The mania difficulty calculator returned an invalid star rating.");

        return stars;
    }
}
