using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

public sealed class O2JamDifficultyCalculator : DifficultyCalculator
{
    // Native reprocessing must persist mania stars regardless of the selected display mode.
    public override int Version => O2JamManiaStarRating.CacheVersion;

    public O2JamDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
        : base(ruleset, new MetadataWorkingBeatmap(beatmap))
    {
    }

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
    {
        var maxCombo = beatmap.BeatmapInfo.TotalObjectCount < 0 || beatmap.BeatmapInfo.EndTimeObjectCount < 0
            ? 0
            : Math.Max(0, beatmap.BeatmapInfo.TotalObjectCount + beatmap.BeatmapInfo.EndTimeObjectCount - 1);

        if (O2JamGameplayProfile.UsesManiaScore(mods))
        {
            return new ManiaDifficultyAttributes
            {
                StarRating = beatmap.BeatmapInfo.StarRating,
                Mods = mods,
                MaxCombo = maxCombo,
            };
        }

        return new DifficultyAttributes(mods, beatmap.BeatmapInfo.StarRating)
        {
            MaxCombo = maxCombo,
        };
    }

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => [];

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => [];

    protected override Mod[] DifficultyAdjustmentMods => [];

    private sealed class MetadataWorkingBeatmap(IWorkingBeatmap source) : FlatWorkingBeatmap(new Beatmap())
    {
        private double? baselineStars;
        private int baselineMaxCombo;

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var metadata = Beatmap;

            if (O2JamGameplayProfile.RequiresStarCalculation(mods))
            {
                var playable = source.GetPlayableBeatmap(ruleset, mods, token);
                var attributes = O2JamManiaStarRating.CalculateAttributes(playable, mods, true, token);
                metadata.BeatmapInfo.StarRating = attributes.StarRating;
                metadata.BeatmapInfo.TotalObjectCount = attributes.MaxCombo + 1;
                metadata.BeatmapInfo.EndTimeObjectCount = 0;
                return metadata;
            }

            // Imports persist versioned native mania stars. Old entries take the decode path
            // once per calculator, while MS presentation and gameplay stay metadata-only.
            if (!baselineStars.HasValue)
            {
                var storedStars = O2JamStarRatingMetadata.ReadMania(source.BeatmapInfo);
                if (storedStars.HasValue && source.BeatmapInfo.TotalObjectCount >= 0 && source.BeatmapInfo.EndTimeObjectCount >= 0)
                {
                    baselineStars = storedStars.Value;
                    baselineMaxCombo = O2JamStarRatingMetadata.ResolveManiaMaxCombo(source.BeatmapInfo);
                }
                else
                {
                    var playable = source.GetPlayableBeatmap(ruleset, [], token);
                    var attributes = O2JamManiaStarRating.CalculateAttributes(playable, [], false, token);
                    baselineStars = attributes.StarRating;
                    baselineMaxCombo = attributes.MaxCombo;
                }
            }

            metadata.BeatmapInfo.StarRating = baselineStars.Value;
            metadata.BeatmapInfo.TotalObjectCount = baselineMaxCombo + 1;
            metadata.BeatmapInfo.EndTimeObjectCount = 0;
            return metadata;
        }
    }
}
