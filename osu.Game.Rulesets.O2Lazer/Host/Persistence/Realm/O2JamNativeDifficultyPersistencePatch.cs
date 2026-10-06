using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Host.Library;

namespace osu.Game.Rulesets.O2Lazer.Import;

/// <summary>
/// Completes the O2Lazer cache in the native transaction that already saves baseline stars.
/// </summary>
internal static class O2JamNativeDifficultyPersistencePatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.NativeDifficultyPersistence";
    private static readonly object installLock = new();
    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;
            try
            {
                var setter = AccessTools.PropertySetter(typeof(BeatmapInfo), nameof(BeatmapInfo.StarRating));
                if (setter == null)
                    throw new MissingMemberException("The native star rating persistence API has changed.");
                new Harmony(harmony_id).Patch(setter,
                    postfix: new HarmonyMethod(typeof(O2JamNativeDifficultyPersistencePatch), nameof(saveBaselineAttributes)));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its native difficulty persistence adapter.");
                return false;
            }
        }
    }

    private static void saveBaselineAttributes(BeatmapInfo __instance, double __0)
    {
        if (!__instance.IsManaged || !__instance.IsValid || __instance.Realm?.IsInTransaction != true
            || __instance.Ruleset.ShortName != O2LazerIdentity.ShortName)
            return;
        var result = O2JamNativeDifficultyHandoff.Take(__instance.ID);
        if (result == null)
            return;
        try
        {
            var set = __instance.BeatmapSet;
            if (set is not { DeletePending: false } || set.ID != result.SetId
                || set.Beatmaps.Any(beatmap => beatmap.Ruleset.ShortName != O2LazerIdentity.ShortName)
                || __0 != result.StarRating || !double.IsFinite(__0) || __0 < 0 || result.MaxCombo < 0
                || !string.Equals(__instance.Hash, result.BeatmapHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(__instance.MD5Hash, result.Md5Hash, StringComparison.OrdinalIgnoreCase)
                || O2JamImportMetadata.Read(__instance.Metadata.Tags, out var projection) != O2JamImportMetadataStatus.Valid
                || projection!.ProjectionVersion != O2JamImportMetadata.ProjectionVersion || projection.Difficulty != result.Difficulty
                || !string.Equals(set.GetFile(__instance.Metadata.AudioFile)?.File.Hash, result.SourceHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(__instance.Hash, O2JamBeatmapIdentity.FromSource(result.SourceHash, result.Difficulty), StringComparison.OrdinalIgnoreCase))
                return;
            __instance.Metadata.Tags = O2JamStarRatingMetadata.WithManiaCache(__instance.Metadata.Tags, result.MaxCombo);
        }
        catch (Exception exception)
        {
            // The normal post-import processor can recover an incomplete cache. An optional
            // optimisation must not prevent native processing from saving its star result.
            Logger.Error(exception, "O2Lazer could not save the remaining native difficulty attributes.");
        }
    }
}
