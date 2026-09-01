using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamStarRatingDisplayPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.StarRatingDisplay";
    private static readonly object installLock = new();

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
                if (target == null || transpiler == null)
                    return false;

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                harmony.Patch(AccessTools.Method(typeof(ExpandedPanelMiddleContent), "load"),
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(useScoreDisplay))));
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

    private static IEnumerable<CodeInstruction> useScoreDisplay(IEnumerable<CodeInstruction> instructions)
    {
        var constructor = AccessTools.Constructor(typeof(StarRatingDisplay), [typeof(StarDifficulty), typeof(StarRatingDisplaySize), typeof(bool)]);
        var scoreField = AccessTools.Field(typeof(ExpandedPanelMiddleContent), "score");
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
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(O2JamStarRatingDisplayPatch), nameof(createScoreDisplay))));
                calls++;
            }
            else
                result.Add(instruction);
        }

        if (calls != 1 || scoreField == null)
            throw new MissingMemberException("The native results star display has changed.");
        return result;
    }

    private static StarRatingDisplay createScoreDisplay(StarDifficulty difficulty, StarRatingDisplaySize size, bool animated, ScoreInfo score)
    {
        // Results belong to the recorded mods, not the current song-select selection. Adapt
        // only this badge, including the fallback for scores no longer in the local library.
        if (score.Ruleset.ShortName == O2LazerIdentity.ShortName && score.BeatmapInfo?.Ruleset.ShortName == O2LazerIdentity.ShortName)
            difficulty = new StarDifficulty(O2JamDisplayedDifficulty.GetStars(score.BeatmapInfo, score.Mods), difficulty.MaxCombo);

        return new StarRatingDisplay(difficulty, size, animated);
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

    internal static Task<StarDifficulty?> GetDisplayDifficultyAsync(BeatmapDifficultyCache cache, IBeatmapInfo beatmapInfo,
                                                                   IRulesetInfo? rulesetInfo, IEnumerable<Mod>? mods,
                                                                   CancellationToken cancellationToken, int computationDelay)
    {
        if (beatmapInfo.Ruleset.ShortName != O2LazerIdentity.ShortName
            || rulesetInfo != null && rulesetInfo.ShortName != O2LazerIdentity.ShortName)
            return cache.GetDifficultyAsync(beatmapInfo, rulesetInfo, mods, cancellationToken, computationDelay);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<StarDifficulty?>(cancellationToken);

        var maxCombo = beatmapInfo.TotalObjectCount < 0 || beatmapInfo.EndTimeObjectCount < 0
            ? 0 : Math.Max(0, beatmapInfo.TotalObjectCount + beatmapInfo.EndTimeObjectCount - 1);
        return Task.FromResult<StarDifficulty?>(new StarDifficulty(O2JamDisplayedDifficulty.GetStars(beatmapInfo, mods), maxCombo));
    }
}
