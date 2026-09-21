using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Contracted;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamStarRatingDisplayPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.StarRatingDisplay";
    private static readonly object installLock = new();
    private static FieldInfo titleStarRatingDisplayField = null!;

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
                var expandedResultsLoad = AccessTools.Method(typeof(ExpandedPanelMiddleContent), "load");
                var contractedResultsLoad = AccessTools.Method(typeof(ContractedPanelMiddleContent), "load");
                var titleAttributes = AccessTools.GetDeclaredMethods(typeof(BeatmapTitleWedge.DifficultyDisplay))
                                                .SingleOrDefault(method => method.Name.StartsWith("<updateDifficultyStatistics>b__", StringComparison.Ordinal));
                var statisticsSetter = AccessTools.PropertySetter(typeof(BeatmapTitleWedge.DifficultyStatisticsDisplay), "Statistics");
                titleStarRatingDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "starRatingDisplay");
                if (expandedResultsLoad == null || contractedResultsLoad == null
                    || titleAttributes == null || statisticsSetter == null || titleStarRatingDisplayField == null)
                    throw new MissingMemberException("The native star display API has changed.");

                harmony.Patch(expandedResultsLoad,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useScoreDisplay))));
                harmony.Patch(contractedResultsLoad,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useContractedScoreIcon))));
                harmony.Patch(titleAttributes,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useSongSelectStars))));
                harmony.Patch(statisticsSetter,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(applySongSelectStatistics))));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its star rating display adapter.");
                return false;
            }
        }
    }

    private static IEnumerable<CodeInstruction> useScoreDisplay(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var starDisplayConstructor = AccessTools.Constructor(typeof(StarRatingDisplay), [typeof(StarDifficulty), typeof(StarRatingDisplaySize), typeof(bool)]);
        var difficultyIconConstructor = AccessTools.Constructor(typeof(DifficultyIcon), [typeof(IBeatmapInfo), typeof(IRulesetInfo), typeof(Mod[])]);
        var scoreField = AccessTools.Field(typeof(ExpandedPanelMiddleContent), "score");
        var difficultyLocal = original.GetMethodBody()?.LocalVariables.SingleOrDefault(local => local.LocalType == typeof(StarDifficulty));
        if (scoreField == null || difficultyLocal == null)
            throw new MissingMemberException("The native results difficulty state has changed.");

        var result = new List<CodeInstruction>();
        var starDisplayCalls = 0;
        var difficultyIconCalls = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, starDisplayConstructor))
            {
                var loadPanel = new CodeInstruction(OpCodes.Ldarg_0);
                loadPanel.labels.AddRange(instruction.labels);
                loadPanel.blocks.AddRange(instruction.blocks);
                result.Add(loadPanel);
                result.Add(new CodeInstruction(OpCodes.Ldfld, scoreField));
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(createScoreDisplay))));
                starDisplayCalls++;
            }
            else if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, difficultyIconConstructor))
            {
                var loadPanel = new CodeInstruction(OpCodes.Ldarg_0);
                loadPanel.labels.AddRange(instruction.labels);
                loadPanel.blocks.AddRange(instruction.blocks);
                result.Add(loadPanel);
                result.Add(new CodeInstruction(OpCodes.Ldfld, scoreField));
                result.Add(CodeInstruction.LoadLocal(difficultyLocal.LocalIndex));
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(createScoreIcon))));
                difficultyIconCalls++;
            }
            else
                result.Add(instruction);
        }

        if (starDisplayCalls != 1 || difficultyIconCalls != 1)
            throw new MissingMemberException("The native results star display has changed.");
        return result;
    }

    private static StarRatingDisplay createScoreDisplay(StarDifficulty difficulty, StarRatingDisplaySize size, bool animated, ScoreInfo score)
    {
        var display = new StarRatingDisplay(difficulty, size, animated);
        O2JamStarRatingPresentationPatch.ConfigureStatic(display, score.BeatmapInfo, score.Mods, score.Ruleset, difficulty);
        return display;
    }

    private static IEnumerable<CodeInstruction> useContractedScoreIcon(IEnumerable<CodeInstruction> instructions)
    {
        var constructor = AccessTools.Constructor(typeof(DifficultyIcon), [typeof(IBeatmapInfo), typeof(IRulesetInfo), typeof(Mod[])]);
        var scoreField = AccessTools.Field(typeof(ContractedPanelMiddleContent), "score");
        var result = new List<CodeInstruction>();
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, constructor))
            {
                var loadPanel = new CodeInstruction(OpCodes.Ldarg_0);
                loadPanel.labels.AddRange(instruction.labels);
                loadPanel.blocks.AddRange(instruction.blocks);
                result.Add(loadPanel);
                result.Add(new CodeInstruction(OpCodes.Ldfld, scoreField));
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(createContractedScoreIcon))));
                calls++;
            }
            else
                result.Add(instruction);
        }

        if (calls != 1 || scoreField == null)
            throw new MissingMemberException("The native contracted results difficulty icon has changed.");
        return result;
    }

    private static DifficultyIcon createScoreIcon(IBeatmapInfo beatmap, IRulesetInfo? ruleset, Mod[]? mods, ScoreInfo score, StarDifficulty difficulty)
    {
        var isO2Lazer = score.Ruleset.ShortName == O2LazerIdentity.ShortName
                        && beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName;
        var icon = new DifficultyIcon(beatmap, ruleset, isO2Lazer ? score.Mods : mods);

        if (isO2Lazer)
            icon.Current.Value = getScoreIconDifficulty(beatmap, score.Mods, difficulty);

        return icon;
    }

    private static DifficultyIcon createContractedScoreIcon(IBeatmapInfo beatmap, IRulesetInfo? ruleset, Mod[]? mods, ScoreInfo score)
    {
        var isO2Lazer = score.Ruleset.ShortName == O2LazerIdentity.ShortName
                        && beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName;
        var icon = new DifficultyIcon(beatmap, ruleset, isO2Lazer ? score.Mods : mods);
        if (isO2Lazer)
            icon.Current.Value = getScoreIconDifficulty(beatmap, score.Mods,
                new StarDifficulty(O2JamDisplayedDifficulty.GetStars(beatmap), 0));

        return icon;
    }

    private static StarDifficulty getScoreIconDifficulty(IBeatmapInfo beatmap, IEnumerable<Mod> mods, StarDifficulty maniaDifficulty)
    {
        // Results icons do not share the adjacent badge's presentation state, so select the
        // colour-driving value from the score's recorded MS state explicitly.
        return O2JamDifficultyColourBinding.GetColourDifficulty(beatmap, beatmap.Ruleset, mods, maniaDifficulty);
    }

    private static IEnumerable<CodeInstruction> useSongSelectStars(IEnumerable<CodeInstruction> instructions)
    {
        var nativeLookup = AccessTools.Method(typeof(Ruleset), nameof(Ruleset.GetBeatmapAttributesForDisplay));
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeLookup))
            {
                var loadDisplay = new CodeInstruction(OpCodes.Ldarg_0);
                loadDisplay.labels.AddRange(instruction.labels);
                loadDisplay.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return loadDisplay;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(getSongSelectAttributes));
                calls++;
            }

            yield return instruction;
        }

        if (calls != 1)
            throw new MissingMemberException("The native song-select attribute lookup has changed.");
    }

    private static IEnumerable<RulesetBeatmapAttribute> getSongSelectAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                                 IReadOnlyCollection<Mod> mods,
                                                                                 BeatmapTitleWedge.DifficultyDisplay display)
    {
        var starDisplay = (StarRatingDisplay)titleStarRatingDisplayField.GetValue(display)!;
        return GetSongSelectAttributes(ruleset, beatmap, mods,
            O2JamStarRatingPresentationPatch.GetNativeStars(starDisplay, beatmap));
    }

    internal static IEnumerable<RulesetBeatmapAttribute> GetSongSelectAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                                  IReadOnlyCollection<Mod> mods, double nativeStars)
    {
        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmap, mods);
        if (ruleset.ShortName != O2LazerIdentity.ShortName)
            return attributes;

        var baselineStars = O2JamDisplayedDifficulty.GetStars(beatmap);
        var adjustedStars = double.IsFinite(nativeStars) && nativeStars >= 0 ? nativeStars : baselineStars;
        if (baselineStars < 0 && adjustedStars >= 0)
            baselineStars = adjustedStars;

        return attributes.Select(attribute => attribute.Label == O2LazerStrings.StarRating
            ? withAdjustedStars(attribute, adjustedStars, baselineStars)
            : attribute);
    }

    private static RulesetBeatmapAttribute withAdjustedStars(RulesetBeatmapAttribute attribute, double stars, double originalStars) =>
        new(attribute.Label, attribute.Acronym, (float)originalStars, (float)stars, attribute.MaxValue)
        {
            Description = stars < 0
                ? O2LazerStrings.MissingManiaStarRatingDescription
                : O2LazerStrings.ManiaStarRatingDescription,
            AdditionalMetrics = attribute.AdditionalMetrics,
            ValueFormat = attribute.ValueFormat,
        };

    private static bool applySongSelectStatistics(IReadOnlyList<BeatmapTitleWedge.StatisticDifficulty.Data> __0) =>
        ShouldApplySongSelectStatistics(__0);

    internal static bool ShouldApplySongSelectStatistics(IReadOnlyList<BeatmapTitleWedge.StatisticDifficulty.Data> statistics)
    {
        var attributes = statistics.Select(data => data.BeatmapAttribute).OfType<RulesetBeatmapAttribute>().ToArray();
        var isO2Lazer = attributes.Any(attribute => attribute.Label == O2LazerStrings.O2Ma)
                        && attributes.Any(attribute => attribute.Label == O2LazerStrings.O2JamLevel);

        // Other native attributes are immediately available. Keep the previous stable list while
        // an O2Jam chart is still calculating so the uncalculated sentinel never becomes UI state.
        return !isO2Lazer || attributes.All(attribute => attribute.Label != O2LazerStrings.StarRating || attribute.AdjustedValue >= 0);
    }

}
