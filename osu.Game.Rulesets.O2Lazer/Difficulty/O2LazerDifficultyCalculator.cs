using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

public class O2LazerDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap) : DifficultyCalculator(ruleset, beatmap)
{
    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
    {
        var storedDifficulty = O2LazerDifficultyInfo.FromOsuDifficulty(beatmap.Difficulty);
        var level = storedDifficulty.PlayLevel ?? (float)storedDifficulty.Total;
        return new DifficultyAttributes(mods, O2LazerDifficultyInfo.ComputeStarRating(level))
        {
            MaxCombo = beatmap.HitObjects.Sum(hitObject => hitObject is O2LazerLongNote ? 2 : 1),
        };
    }

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods)
    {
        var clockRate = ModUtils.CalculateRateWithMods(mods);
        var objects = beatmap.HitObjects.OrderBy(h => h.StartTime).ToList();
        var difficultyObjects = new List<DifficultyHitObject>();

        for (var i = 1; i < objects.Count; i++)
            difficultyObjects.Add(new DifficultyHitObject(objects[i], objects[i - 1], clockRate, difficultyObjects, difficultyObjects.Count));

        return difficultyObjects;
    }

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => [];
}
