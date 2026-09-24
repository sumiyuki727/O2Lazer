using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

/// <summary>
/// Keeps native difficulty bindables on the persisted mania-star scale without decoding a chart
/// again when the selected mods cannot change difficulty.
/// </summary>
internal static class O2JamDifficultyCachePatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.DifficultyCache";
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
                var transpiler = AccessTools.Method(typeof(O2JamDifficultyCachePatch), nameof(usePersistedManiaDifficulty));
                if (target == null || transpiler == null)
                    throw new MissingMemberException("The native difficulty cache API has changed.");

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its native difficulty cache adapter.");
                return false;
            }
        }
    }

    private static IEnumerable<CodeInstruction> usePersistedManiaDifficulty(IEnumerable<CodeInstruction> instructions)
    {
        var result = instructions.ToList();
        var nativeLookup = AccessTools.Method(typeof(BeatmapDifficultyCache), nameof(BeatmapDifficultyCache.GetDifficultyAsync));
        var calls = result.Where(instruction => instruction.Calls(nativeLookup)).ToArray();
        if (calls.Length != 1)
            throw new MissingMemberException("The native bindable difficulty lookup has changed.");

        calls[0].opcode = OpCodes.Call;
        calls[0].operand = AccessTools.Method(typeof(O2JamDifficultyCachePatch), nameof(GetNativeDifficultyAsync));
        return result;
    }

    internal static Task<StarDifficulty?> GetNativeDifficultyAsync(BeatmapDifficultyCache cache, IBeatmapInfo beatmap,
                                                                   IRulesetInfo? ruleset, IEnumerable<Mod>? mods,
                                                                   CancellationToken cancellationToken, int computationDelay)
    {
        if (beatmap.Ruleset.ShortName != O2LazerIdentity.ShortName
            || ruleset != null && ruleset.ShortName != O2LazerIdentity.ShortName)
            return cache.GetDifficultyAsync(beatmap, ruleset, mods, cancellationToken, computationDelay);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<StarDifficulty?>(cancellationToken);

        var selectedMods = mods?.ToArray() ?? [];
        if (O2JamGameplayProfile.RequiresStarCalculation(selectedMods)
            || O2JamStarRatingMetadata.ReadMania(beatmap) == null)
            return cache.GetDifficultyAsync(beatmap, ruleset, selectedMods, cancellationToken, computationDelay);

        return Task.FromResult<StarDifficulty?>(new StarDifficulty(
            O2JamDisplayedDifficulty.GetStars(beatmap),
            O2JamStarRatingMetadata.ResolveManiaMaxCombo(beatmap)));
    }
}
