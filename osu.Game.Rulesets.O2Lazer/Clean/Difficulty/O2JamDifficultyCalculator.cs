using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

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

    private sealed class MetadataWorkingBeatmap(IWorkingBeatmap source) : FlatWorkingBeatmap(new Beatmap())
    {
        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var metadata = Beatmap;
            if (metadata.BeatmapInfo.StarRating < 0)
            {
                // Normal lookups need no chart/audio I/O. A native version reset or an old
                // library entry can still be reprocessed without depending on the import UI.
                metadata.BeatmapInfo.StarRating = O2JamStarRatingMetadata.ReadMania(source.BeatmapInfo)
                    ?? O2JamManiaStarRating.Calculate((O2JamBeatmap)source.GetPlayableBeatmap(ruleset, [], token), token);
            }

            return metadata;
        }
    }
}
