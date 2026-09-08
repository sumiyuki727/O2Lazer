using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal static class O2JamManiaStarRating
{
    private static readonly RulesetInfo maniaRuleset = new ManiaRuleset().RulesetInfo;

    public static int Version { get; } = new ManiaDifficultyCalculator(maniaRuleset, new FlatWorkingBeatmap(new ManiaBeatmap(new StageDefinition(O2JamBeatmap.ColumnCount)))).Version;

    // The final digit versions our projection independently of the native mania algorithm.
    public static int CacheVersion => checked(Version * 10 + 1);

    public static double Calculate(O2JamBeatmap beatmap, CancellationToken cancellationToken = default)
        => calculate(beatmap, [], false, cancellationToken);

    public static double CalculatePreprocessed(O2JamBeatmap beatmap, IReadOnlyList<Mod> mods, CancellationToken cancellationToken = default)
        => calculate(beatmap, mods, true, cancellationToken);

    private static double calculate(O2JamBeatmap beatmap, IReadOnlyList<Mod> mods, bool isPreprocessed, CancellationToken cancellationToken)
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

        // Structural mods have already been applied to the O2Jam beatmap. Pass only rate mods
        // through mania's playable-beatmap pipeline so defaults are populated without applying
        // Random, Mirror or Invert to the projected objects for a second time.
        var difficultyMods = isPreprocessed
            ? ModUtils.FlattenMods(mods).Where(mod => mod is IApplicableToRate).ToArray()
            : [];
        var stars = new ManiaDifficultyCalculator(maniaRuleset, new FlatWorkingBeatmap(mania))
                    .Calculate(difficultyMods, cancellationToken).StarRating;
        if (!double.IsFinite(stars) || stars < 0)
            throw new InvalidDataException("The mania difficulty calculator returned an invalid star rating.");

        return stars;
    }
}
