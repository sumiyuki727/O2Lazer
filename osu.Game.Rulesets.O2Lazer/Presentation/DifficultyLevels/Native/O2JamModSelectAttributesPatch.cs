using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamModSelectAttributesPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ModSelectAttributes";
    private static readonly object installLock = new();
    private static readonly ManiaRuleset maniaPresentation = new();

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            var harmony = new Harmony(harmony_id);
            try
            {
                // updateValues only schedules this compiler-generated callback; the callback
                // owns the ruleset attribute lookup that must remain scoped to mod select.
                var target = AccessTools.GetDeclaredMethods(typeof(BeatmapAttributesDisplay))
                                        .SingleOrDefault(method => method.Name.StartsWith("<updateValues>b__", StringComparison.Ordinal));
                var transpiler = AccessTools.Method(typeof(O2JamModSelectAttributesPatch), nameof(hideAttributes));
                if (target == null || transpiler == null)
                    throw new MissingMemberException("The native mod-select attribute display has changed.");

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not adapt its mod-select difficulty attributes.");
                return false;
            }
        }
    }

    private static IEnumerable<CodeInstruction> hideAttributes(IEnumerable<CodeInstruction> instructions)
    {
        var nativeLookup = AccessTools.Method(typeof(Ruleset), nameof(Ruleset.GetBeatmapAttributesForDisplay));
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeLookup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(O2JamModSelectAttributesPatch), nameof(getModSelectAttributes));
                calls++;
            }

            yield return instruction;
        }

        if (calls != 1)
            throw new MissingMemberException("The native mod-select attribute lookup has changed.");
    }

    private static IEnumerable<RulesetBeatmapAttribute> getModSelectAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                               IReadOnlyCollection<Mod> mods) =>
        GetAttributes(ruleset, beatmap, mods);

    internal static IEnumerable<RulesetBeatmapAttribute> GetAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                         IReadOnlyCollection<Mod> mods)
    {
        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmap, mods);
        if (ruleset.ShortName != O2LazerIdentity.ShortName)
            return attributes;

        var flattened = ModUtils.FlattenMods(mods).ToArray();
        var maniaScore = flattened.OfType<O2JamModManiaScore>().SingleOrDefault();
        if (maniaScore == null)
            return [];

        // Song select continues to use O2LazerRuleset's o2ma/SR/LV attributes. This method is
        // called only by the mod-select callback, where mania's OD window details and HP value
        // should be visible without adding the unrelated key-count row.
        var baseline = new BeatmapDifficulty(beatmap.Difficulty)
        {
            OverallDifficulty = O2JamModManiaScore.DefaultDifficulty,
            DrainRate = O2JamModManiaScore.DefaultDifficulty,
        };
        var presentationBeatmap = new BeatmapInfo(ruleset.RulesetInfo, baseline);
        var difficultyAdjust = new ManiaModDifficultyAdjust();
        difficultyAdjust.OverallDifficulty.Value = maniaScore.OverallDifficulty.Value;
        difficultyAdjust.DrainRate.Value = maniaScore.DrainRate.Value;
        var presentationMods = new Mod[] { difficultyAdjust }
                               .Concat(flattened.Where(mod => mod is not O2JamModManiaScore))
                               .ToArray();

        var nativeAttributes = maniaPresentation.GetBeatmapAttributesForDisplay(presentationBeatmap, presentationMods).ToArray();
        var overallDifficulty = nativeAttributes.Single(attribute => attribute.Acronym == O2LazerStrings.ManiaScoreOverallDifficultyAcronym.ToString());
        var healthDrain = nativeAttributes.Single(attribute => attribute.Acronym == O2LazerStrings.ManiaScoreHealthDrainAcronym.ToString());
        return
        [
            localise(overallDifficulty, O2LazerStrings.ManiaScoreOverallDifficulty,
                O2LazerStrings.ManiaScoreOverallDifficultyAcronym.ToString(), O2LazerStrings.ManiaScoreOverallDifficultyDescription),
            localise(healthDrain, O2LazerStrings.ManiaScoreHealthDrain,
                O2LazerStrings.ManiaScoreHealthDrainAcronym.ToString(), O2LazerStrings.ManiaScoreHealthDrainDescription),
        ];
    }

    private static RulesetBeatmapAttribute localise(RulesetBeatmapAttribute source, LocalisableString label, string acronym,
                                                     LocalisableString description) =>
        new(label, acronym, source.OriginalValue, source.AdjustedValue, source.MaxValue)
        {
            Description = description,
            AdditionalMetrics = source.AdditionalMetrics,
            ValueFormat = source.ValueFormat,
        };
}
