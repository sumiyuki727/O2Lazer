using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

public sealed class O2JamDifficultyCalculator : DifficultyCalculator
{
    private readonly ushort level;
    private readonly int maximumCombo;

    public override int Version => 260829;

    public O2JamDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
        : base(ruleset, createMetadataWorkingBeatmap(ruleset, beatmap.BeatmapInfo))
    {
        var info = beatmap.BeatmapInfo;
        level = O2JamDifficultyRating.ResolveLevel(info.DifficultyName, info.StarRating);
        maximumCombo = info.TotalObjectCount < 0 || info.EndTimeObjectCount < 0
            ? 0
            : Math.Max(0, info.TotalObjectCount + info.EndTimeObjectCount - 1);
    }

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills) =>
        new(mods, O2JamDifficultyRating.FromLevel(level))
        {
            MaxCombo = maximumCombo,
        };

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => [];

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => [];

    private static IWorkingBeatmap createMetadataWorkingBeatmap(IRulesetInfo ruleset, IBeatmapInfo info)
    {
        var level = O2JamDifficultyRating.ResolveLevel(info.DifficultyName, info.StarRating);
        var difficulty = difficultyFromName(info.DifficultyName);
        var metadataBeatmap = new O2JamBeatmap(difficulty, new O2JamTimingMap(info.BPM > 0 ? info.BPM : 120))
        {
            Level = level,
            BeatmapInfo = new BeatmapInfo(
                ruleset as RulesetInfo ?? new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID),
                metadata: new BeatmapMetadata())
            {
                DifficultyName = info.DifficultyName,
                StarRating = O2JamDifficultyRating.FromLevel(level),
                TotalObjectCount = info.TotalObjectCount,
                EndTimeObjectCount = info.EndTimeObjectCount,
            },
        };

        // Difficulty is the OJN header level divided by ten. Passing a zero-object metadata
        // beatmap keeps osu!'s non-virtual calculator pipeline without decoding the external OJN.
        return new FlatWorkingBeatmap(metadataBeatmap);
    }

    private static O2JamDifficulty difficultyFromName(string difficultyName)
    {
        if (difficultyName.StartsWith(nameof(O2JamDifficulty.NX), StringComparison.OrdinalIgnoreCase))
            return O2JamDifficulty.NX;
        if (difficultyName.StartsWith(nameof(O2JamDifficulty.HX), StringComparison.OrdinalIgnoreCase))
            return O2JamDifficulty.HX;

        return O2JamDifficulty.EX;
    }
}
