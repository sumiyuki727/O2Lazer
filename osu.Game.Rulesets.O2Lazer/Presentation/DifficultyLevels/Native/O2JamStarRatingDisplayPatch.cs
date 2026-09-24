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
                titleStarRatingDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "starRatingDisplay");
                if (expandedResultsLoad == null || contractedResultsLoad == null
                    || titleAttributes == null || titleStarRatingDisplayField == null)
                    throw new MissingMemberException("The native star display API has changed.");

                harmony.Patch(expandedResultsLoad,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useScoreDisplay))));
                harmony.Patch(contractedResultsLoad,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useContractedScoreIcon))));
                harmony.Patch(titleAttributes,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useSongSelectStars))));
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
        if (score.BeatmapInfo != null)
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
        var statisticsSetter = AccessTools.PropertySetter(typeof(BeatmapTitleWedge.DifficultyStatisticsDisplay), "Statistics");
        var lookupCalls = 0;
        var setterCalls = 0;

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
                lookupCalls++;
            }
            else if (instruction.Calls(statisticsSetter))
            {
                // The native lambda may have inlined the property setter before the ruleset loads.
                // Its call site must carry the title's previous star value into the pending SR row.
                var loadDisplay = new CodeInstruction(OpCodes.Ldarg_0);
                loadDisplay.labels.AddRange(instruction.labels);
                loadDisplay.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return loadDisplay;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(WriteSongSelectStatistics));
                setterCalls++;
            }

            yield return instruction;
        }

        if (lookupCalls != 1 || setterCalls != 2)
            throw new MissingMemberException("The native song-select attribute update has changed.");
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

        // The native title briefly pairs the new ruleset with the previous mode's beatmap.
        // Its star value is not an O2Jam result; the write adapter resolves the pending state.
        var isO2JamBeatmap = beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName;
        var baselineStars = isO2JamBeatmap ? O2JamDisplayedDifficulty.GetStars(beatmap) : -1;
        var adjustedStars = isO2JamBeatmap && double.IsFinite(nativeStars) && nativeStars >= 0
            ? nativeStars
            : baselineStars;
        if (baselineStars < 0 && adjustedStars >= 0)
            baselineStars = adjustedStars;

        if (adjustedStars < 0)
            Logger.Log($"O2Lazer title SR pending: native={nativeStars:F2}, baseline={baselineStars:F2}, chart={beatmap.DifficultyName}.",
                level: LogLevel.Verbose);

        return attributes.Select(attribute => attribute.Label == O2LazerStrings.StarRating
            ? withAdjustedStars(attribute, adjustedStars, baselineStars)
            : attribute);
    }

    private static RulesetBeatmapAttribute withAdjustedStars(RulesetBeatmapAttribute attribute, double stars,
                                                               double originalStars) =>
        new(attribute.Label, attribute.Acronym, (float)originalStars, (float)stars, attribute.MaxValue)
        {
            Description = stars < 0
                ? O2LazerStrings.MissingManiaStarRatingDescription
                : O2LazerStrings.ManiaStarRatingDescription,
            AdditionalMetrics = attribute.AdditionalMetrics,
            ValueFormat = attribute.ValueFormat,
        };

    internal static void WriteSongSelectStatistics(BeatmapTitleWedge.DifficultyStatisticsDisplay display,
                                                   IReadOnlyList<BeatmapTitleWedge.StatisticDifficulty.Data> statistics,
                                                   BeatmapTitleWedge.DifficultyDisplay? title = null)
    {
        var prepared = PrepareSongSelectStatistics(statistics, display.Statistics,
            title == null ? null : O2JamStarRatingPresentationPatch.GetPendingTitleStars(title));
        if (!ReferenceEquals(prepared, statistics))
            Logger.Log($"O2Lazer title SR pending: presenting O2MA/LV with SR held; rows={statistics.Count}.",
                level: LogLevel.Verbose);
        display.Statistics = prepared;
    }

    internal static IReadOnlyList<BeatmapTitleWedge.StatisticDifficulty.Data> PrepareSongSelectStatistics(
        IReadOnlyList<BeatmapTitleWedge.StatisticDifficulty.Data> statistics,
        IReadOnlyList<BeatmapTitleWedge.StatisticDifficulty.Data>? previous = null,
        double? pendingPreviousStars = null)
    {
        var attributes = statistics.Select(data => data.BeatmapAttribute).OfType<RulesetBeatmapAttribute>().ToArray();
        if (!attributes.Any(attribute => attribute.Label == O2LazerStrings.O2Ma)
            || !attributes.Any(attribute => attribute.Label == O2LazerStrings.O2JamLevel))
            return statistics;

        var pending = statistics.FirstOrDefault(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating
                                                         && (!double.IsFinite(data.AdjustedValue) || data.AdjustedValue < 0));
        if (pending == null)
            return statistics;

        var priorStar = previous?.FirstOrDefault(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating
                                                         && double.IsFinite(data.AdjustedValue) && data.AdjustedValue >= 0
                                                         && data.Content != string.Empty);
        var crossModeStars = pendingPreviousStars is double stars && double.IsFinite(stars) && stars >= 0
            ? pendingPreviousStars
            : null;
        var retainedValue = (float)(crossModeStars ?? priorStar?.AdjustedValue ?? 0);
        var source = pending.BeatmapAttribute!;
        var placeholderAttribute = new RulesetBeatmapAttribute(source.Label, source.Acronym,
            retainedValue, retainedValue, source.MaxValue)
        {
            Description = O2LazerStrings.MissingManiaStarRatingDescription,
            AdditionalMetrics = source.AdditionalMetrics,
            ValueFormat = source.ValueFormat,
        };
        var placeholder = pending with
        {
            Value = retainedValue,
            AdjustedValue = retainedValue,
            Content = crossModeStars != null ? null : priorStar == null ? string.Empty : priorStar.Content,
            BeatmapAttribute = placeholderAttribute,
        };

        // Native layout and entry fades start with O2MA/LV immediately. Only SR's numeric value
        // waits for the real Mania difficulty; no -1 value or tooltip reaches the drawable.
        return statistics.Select(data => ReferenceEquals(data, pending) ? placeholder : data).ToArray();
    }
}
