using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Select;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.UI;

/// <summary>
/// Keeps a song-select grade attached to the exact OJN difficulty rather than its shared source file.
/// </summary>
internal static class O2JamSongSelectRankPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.SongSelectRank";
    private static readonly object installLock = new();
    private static System.Reflection.MethodInfo? setRankMethod;
    private static PropertyInfo? rulesetProperty;
    private static FieldInfo? localUserField;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                var target = AccessTools.Method(typeof(PanelLocalRankDisplay), "localScoresChanged");
                var prefix = AccessTools.Method(typeof(O2JamSongSelectRankPatch), nameof(filterExactDifficulty));
                setRankMethod = AccessTools.Method(typeof(PanelLocalRankDisplay), "setRankFromScore");
                rulesetProperty = AccessTools.Property(typeof(PanelLocalRankDisplay), "ruleset");
                localUserField = AccessTools.Field(typeof(PanelLocalRankDisplay), "localUser");
                if (target == null || prefix == null || setRankMethod == null || rulesetProperty == null || localUserField == null)
                    return false;

                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install its exact-difficulty song-select rank adapter.");
                return false;
            }
        }
    }

    // Harmony field injection uses the patched class's private field names.
    private static bool filterExactDifficulty(
        PanelLocalRankDisplay __instance,
        IRealmCollection<ScoreInfo> sender,
        ChangeSet? changes)
    {
        if (rulesetProperty?.GetValue(__instance) is not IBindable<RulesetInfo> ruleset
            || !string.Equals(ruleset.Value.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal))
            return true;

        try
        {
            if (changes?.HasCollectionChanges() == false || __instance.Beatmap == null)
                return false;

            if (localUserField?.GetValue(__instance) is not IBindable<APIUser> localUser)
                return true;

            var topScore = SelectTopScore(sender, __instance.Beatmap, ruleset.Value, localUser.Value.Id);
            setRankMethod!.Invoke(__instance, [topScore]);
            return false;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "O2Lazer failed to resolve the song-select rank for one difficulty.");
            return true;
        }
    }

    internal static ScoreInfo? SelectTopScore(
        IEnumerable<ScoreInfo> scores,
        BeatmapInfo beatmap,
        RulesetInfo ruleset,
        int localUserId) =>
        scores.Where(score => score.BeatmapInfo?.ID == beatmap.ID)
              .Where(score => score.UserID == localUserId || score.UserID <= 1)
              .Where(score => string.Equals(score.Ruleset.ShortName, ruleset.ShortName, StringComparison.Ordinal) && !score.DeletePending)
              .MaxBy(score => (score.TotalScore, -score.Date.UtcDateTime.Ticks));
}
