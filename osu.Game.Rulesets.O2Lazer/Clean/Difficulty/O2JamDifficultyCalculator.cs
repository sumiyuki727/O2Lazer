using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

public sealed class O2JamDifficultyCalculator : DifficultyCalculator
{
    private readonly int maximumCombo;

    // Native reprocessing must persist mania stars regardless of the selected display mode.
    public override int Version => O2JamManiaStarRating.CacheVersion;

    public O2JamDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
        : base(ruleset, new MetadataWorkingBeatmap(beatmap))
    {
        var info = beatmap.BeatmapInfo;
        maximumCombo = info.TotalObjectCount < 0 || info.EndTimeObjectCount < 0
            ? 0
            : Math.Max(0, info.TotalObjectCount + info.EndTimeObjectCount - 1);
    }

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills) =>
        new(mods, beatmap.BeatmapInfo.StarRating)
        {
            MaxCombo = maximumCombo,
        };

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) =>
        [];

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) =>
        [];

    protected override Mod[] DifficultyAdjustmentMods => [];

    internal static bool RequiresModdedCalculation(IEnumerable<Mod> mods)
    {
        foreach (var mod in mods)
        {
            if (mod is MultiMod multiMod && RequiresModdedCalculation(multiMod.Mods))
                return true;

            // Fixed column permutations and judgement-only mods leave mania strain unchanged.
            // Invert is the current non-rate mod which replaces the chart's hit objects.
            if (mod is IApplicableToRate or O2JamModInvert)
                return true;
        }

        return false;
    }

    private sealed class MetadataWorkingBeatmap(IWorkingBeatmap source) : FlatWorkingBeatmap(new Beatmap())
    {
        private double? baselineStars;

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var metadata = Beatmap;

            if (RequiresModdedCalculation(mods))
            {
                var playable = (O2JamBeatmap)source.GetPlayableBeatmap(ruleset, mods, token);
                metadata.BeatmapInfo.StarRating = O2JamManiaStarRating.CalculatePreprocessed(playable, mods, token);
            }
            else
            {
                // Ordinary lookups retain the metadata-only path. Old entries are decoded once
                // per calculator, while switching back from a modded lookup restores the baseline.
                baselineStars ??= O2JamStarRatingMetadata.ReadMania(source.BeatmapInfo)
                                   ?? O2JamManiaStarRating.Calculate(
                                       (O2JamBeatmap)source.GetPlayableBeatmap(ruleset, [], token), token);
                metadata.BeatmapInfo.StarRating = baselineStars.Value;
            }

            return metadata;
        }
    }
}
