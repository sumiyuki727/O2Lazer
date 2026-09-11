using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
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
    private static readonly ConditionalWeakTable<StarRatingDisplay, SongSelectDisplayState> songSelectStates = new();
    private static readonly ConditionalWeakTable<RulesetBeatmapAttribute, TransitionAttributeMarker> transitionAttributes = new();
    private static FieldInfo titleStarRatingDisplayField = null!;
    private static MethodInfo titleBeatmapGetter = null!;
    private static MethodInfo titleUpdateDifficultyStatisticsMethod = null!;

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
                var target = AccessTools.Method(typeof(BeatmapDifficultyCache), "updateBindable");
                var transpiler = AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useDisplayLookup));
                var expandedResultsLoad = AccessTools.Method(typeof(ExpandedPanelMiddleContent), "load");
                var contractedResultsLoad = AccessTools.Method(typeof(ContractedPanelMiddleContent), "load");
                var titleAttributes = AccessTools.GetDeclaredMethods(typeof(BeatmapTitleWedge.DifficultyDisplay))
                                                .SingleOrDefault(method => method.Name.StartsWith("<updateDifficultyStatistics>b__", StringComparison.Ordinal));
                var titleLoadComplete = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "LoadComplete");
                var titleUpdateDisplay = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "updateDisplay");
                var statisticUpdateDisplay = AccessTools.Method(typeof(BeatmapTitleWedge.StatisticDifficulty), "updateDisplay");
                var statisticsSetter = AccessTools.PropertySetter(typeof(BeatmapTitleWedge.DifficultyStatisticsDisplay), "Statistics");
                titleStarRatingDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "starRatingDisplay");
                titleBeatmapGetter = AccessTools.PropertyGetter(typeof(BeatmapTitleWedge.DifficultyDisplay), "beatmap");
                titleUpdateDifficultyStatisticsMethod = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "updateDifficultyStatistics");
                if (target == null || transpiler == null || expandedResultsLoad == null || contractedResultsLoad == null
                    || titleAttributes == null || titleLoadComplete == null || titleUpdateDisplay == null
                    || statisticUpdateDisplay == null || statisticsSetter == null
                    || titleStarRatingDisplayField == null || titleBeatmapGetter == null
                    || titleUpdateDifficultyStatisticsMethod == null)
                    throw new MissingMemberException("The native star display API has changed.");

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                harmony.Patch(expandedResultsLoad,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useScoreDisplay))));
                harmony.Patch(contractedResultsLoad,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useContractedScoreIcon))));
                harmony.Patch(titleAttributes,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useSongSelectStars))));
                harmony.Patch(titleLoadComplete,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(refreshSongSelectStars))));
                harmony.Patch(titleUpdateDisplay,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(trackSongSelectBeatmap))));
                harmony.Patch(statisticUpdateDisplay,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(finishSongSelectTransition))));
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
        O2JamStarRatingPresentationPatch.Configure(display, score.BeatmapInfo, score.Mods);
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
        return O2JamGameplayProfile.UsesManiaScore(mods)
            ? maniaDifficulty
            : new StarDifficulty(O2JamStarRatingMetadata.ResolveLevel(beatmap) / 10d, maniaDifficulty.MaxCombo);
    }

    private static IEnumerable<CodeInstruction> useDisplayLookup(IEnumerable<CodeInstruction> instructions)
    {
        var result = instructions.ToList();
        var nativeLookup = AccessTools.Method(typeof(BeatmapDifficultyCache), nameof(BeatmapDifficultyCache.GetDifficultyAsync));
        var calls = result.Where(instruction => instruction.Calls(nativeLookup)).ToArray();
        if (calls.Length != 1)
            throw new InvalidOperationException("The native bindable difficulty lookup has changed.");

        // Only display bindables use this lookup. Keep native scheduling, cancellation and
        // invalidation intact; direct calculations, persistence, filtering and sorting stay mania.
        calls[0].opcode = OpCodes.Call;
        calls[0].operand = AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(GetDisplayDifficultyAsync));
        return result;
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
        var state = songSelectStates.GetOrCreateValue(starDisplay);
        var resetAfterRebind = state.ResetAttributeAnimation;
        var attributes = GetSongSelectAttributes(
            ruleset, beatmap, mods, starDisplay.Current.Value.Stars, state.Beatmap, resetAfterRebind, state.BaselineStars).ToArray();

        if (ruleset.ShortName == O2LazerIdentity.ShortName)
        {
            var starAttribute = attributes.Single(attribute => attribute.Label == O2LazerStrings.StarRating);
            if (!O2JamGameplayProfile.RequiresStarCalculation(mods) && starAttribute.AdjustedValue >= 0)
                state.BaselineStars = starAttribute.AdjustedValue;

            if (starAttribute.AdjustedValue >= 0)
            {
                if (resetAfterRebind)
                    transitionAttributes.Add(starAttribute, new TransitionAttributeMarker());

                state.ResetAttributeAnimation = false;
            }
        }

        return attributes;
    }

    internal static IEnumerable<RulesetBeatmapAttribute> GetSongSelectAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                                  IReadOnlyCollection<Mod> mods, double displayedStars,
                                                                                  IBeatmapInfo? displayedBeatmap,
                                                                                  bool resetAfterRebind = false,
                                                                                  double? knownBaselineStars = null)
    {
        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmap, mods);
        if (ruleset.ShortName != O2LazerIdentity.ShortName)
            return attributes;

        // Native difficulty bindables start with an approximation and update asynchronously.
        // During a ruleset switch, keep the new chart's baseline until the bindable has been
        // rebound so the old ruleset's SR cannot appear as a false mod adjustment.
        var stars = displayedBeatmap?.Equals(beatmap) == true && double.IsFinite(displayedStars) && displayedStars >= 0
            ? displayedStars
            : O2JamDisplayedDifficulty.GetStars(beatmap);

        var isBaseline = !O2JamGameplayProfile.RequiresStarCalculation(mods);

        return attributes.Select(attribute => attribute.Label == O2LazerStrings.StarRating
            ? withAdjustedStars(attribute, stars,
                (resetAfterRebind && stars >= 0) || (isBaseline && attribute.OriginalValue < 0 && stars >= 0)
                    ? stars
                    : attribute.OriginalValue < 0 && knownBaselineStars is >= 0
                        ? knownBaselineStars.Value
                        : attribute.OriginalValue)
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

    private static void refreshSongSelectStars(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var starDisplay = (StarRatingDisplay)titleStarRatingDisplayField.GetValue(__instance)!;
        starDisplay.Current.BindValueChanged(_ => titleUpdateDifficultyStatisticsMethod.Invoke(__instance, null));
    }

    private static void trackSongSelectBeatmap(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var starDisplay = (StarRatingDisplay)titleStarRatingDisplayField.GetValue(__instance)!;
        var beatmap = (IBindable<WorkingBeatmap>)titleBeatmapGetter.Invoke(__instance, null)!;
        var state = songSelectStates.GetOrCreateValue(starDisplay);
        state.Beatmap = beatmap.IsDefault ? null : beatmap.Value.BeatmapInfo;
        state.BaselineStars = null;
        state.ResetAttributeAnimation = true;
    }

    private static void finishSongSelectTransition(BeatmapTitleWedge.StatisticDifficulty __instance)
    {
        var attribute = __instance.Value.BeatmapAttribute;
        if (attribute != null && transitionAttributes.Remove(attribute))
            __instance.FinishTransforms(true);
    }

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

    internal static Task<StarDifficulty?> GetDisplayDifficultyAsync(BeatmapDifficultyCache cache, IBeatmapInfo beatmapInfo,
                                                                   IRulesetInfo? rulesetInfo, IEnumerable<Mod>? mods,
                                                                   CancellationToken cancellationToken, int computationDelay)
    {
        if (beatmapInfo.Ruleset.ShortName != O2LazerIdentity.ShortName
            || rulesetInfo != null && rulesetInfo.ShortName != O2LazerIdentity.ShortName)
            return cache.GetDifficultyAsync(beatmapInfo, rulesetInfo, mods, cancellationToken, computationDelay);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<StarDifficulty?>(cancellationToken);

        var selectedMods = mods?.ToArray() ?? [];
        if (O2JamGameplayProfile.RequiresStarCalculation(selectedMods))
            return cache.GetDifficultyAsync(beatmapInfo, rulesetInfo, selectedMods, cancellationToken, computationDelay);

        if (O2JamStarRatingMetadata.ReadMania(beatmapInfo) == null)
            return cache.GetDifficultyAsync(beatmapInfo, rulesetInfo, selectedMods, cancellationToken, computationDelay);

        var maxCombo = O2JamStarRatingMetadata.ResolveManiaMaxCombo(beatmapInfo);
        return Task.FromResult<StarDifficulty?>(new StarDifficulty(O2JamDisplayedDifficulty.GetStars(beatmapInfo), maxCombo));
    }

    private sealed class SongSelectDisplayState
    {
        public IBeatmapInfo? Beatmap;
        public double? BaselineStars;
        public bool ResetAttributeAnimation;
    }

    private sealed class TransitionAttributeMarker
    {
    }
}
