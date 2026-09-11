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
    public static int CacheVersion => checked(Version * 10 + 2);

    public static double Calculate(O2JamBeatmap beatmap, CancellationToken cancellationToken = default) =>
        CalculateAttributes(beatmap, [], false, cancellationToken).StarRating;

    public static double CalculatePreprocessed(O2JamBeatmap beatmap, IReadOnlyList<Mod> mods, CancellationToken cancellationToken = default) =>
        CalculateAttributes(beatmap, mods, true, cancellationToken).StarRating;

    public static ManiaDifficultyAttributes CalculateAttributes(IBeatmap beatmap, IReadOnlyList<Mod> mods, bool isPreprocessed,
                                                                CancellationToken cancellationToken = default)
    {
        // Preserve the OJN's seven columns and absolute note/hold times, but let mania apply
        // its own object defaults. O2Jam judgement and keysound data must not enter this pipeline.
        var mania = new ManiaBeatmap(new StageDefinition(O2JamBeatmap.ColumnCount))
        {
            BeatmapInfo = new BeatmapInfo(maniaRuleset, new BeatmapDifficulty(beatmap.Difficulty)),
            HitObjects = beatmap.HitObjects.Select(hitObject => hitObject is HoldNote hold
                ? (ManiaHitObject)new HoldNote { StartTime = hold.StartTime, Duration = hold.Duration, Column = hold.Column }
                : new Note { StartTime = hitObject.StartTime, Column = ((ManiaHitObject)hitObject).Column }).ToList(),
        };
        mania.Difficulty.CircleSize = O2JamBeatmap.ColumnCount;

        // Structural mods have already been applied to the O2Jam beatmap. Pass only rate mods
        // through mania's playable-beatmap pipeline so defaults are populated without applying
        // Random, Mirror or Invert to the projected objects for a second time.
        var difficultyMods = isPreprocessed
            ? ModUtils.FlattenMods(mods).Where(mod => mod is IApplicableToRate).ToArray()
            : [];
        var attributes = (ManiaDifficultyAttributes)new ManiaDifficultyCalculator(maniaRuleset, new FlatWorkingBeatmap(mania))
            .Calculate(difficultyMods, cancellationToken);
        if (!double.IsFinite(attributes.StarRating) || attributes.StarRating < 0)
            throw new InvalidDataException("The mania difficulty calculator returned an invalid star rating.");

        attributes.Mods = mods.ToArray();
        return attributes;
    }
}
