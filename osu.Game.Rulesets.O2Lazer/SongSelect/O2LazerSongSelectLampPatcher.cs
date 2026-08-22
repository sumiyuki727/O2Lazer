using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Select;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

/// <summary>
/// Keeps the grade shown on each song-select difficulty tied to that exact O2Jam chart.
/// </summary>
public static class O2LazerSongSelectLampPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.SongSelectRank";

    private static bool disabled;
    private static PropertyInfo? rulesetProperty;
    private static PropertyInfo? beatmapProperty;
    private static PropertyInfo? realmProperty;
    private static FieldInfo? localUserField;
    private static MethodInfo? localScoresChangedTarget;

    // The stock rank lookup filters by beatmap hash, which O2Jam difficulties in one set share.
    // Queries per panel are cheap only once, so cache exact-difficulty top scores and invalidate
    // the whole cache when any score collection actually changes.
    private static readonly ConcurrentDictionary<ScoreCacheKey, CachedScore> score_cache = new();
    private static long scoresVersion;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        try
        {
            var target = AccessTools.Method(typeof(PanelLocalRankDisplay), "setRankFromScore", [typeof(ScoreInfo)]);
            var prefixMethod = AccessTools.Method(typeof(O2LazerSongSelectLampPatcher), nameof(prefix));
            localScoresChangedTarget = AccessTools.Method(
                typeof(PanelLocalRankDisplay),
                "localScoresChanged",
                [typeof(IRealmCollection<ScoreInfo>), typeof(ChangeSet)]);
            var scoresChangedPrefixMethod = AccessTools.Method(typeof(O2LazerSongSelectLampPatcher), nameof(scoresChangedPrefix));

            rulesetProperty = AccessTools.Property(typeof(PanelLocalRankDisplay), "ruleset");
            beatmapProperty = AccessTools.Property(typeof(PanelLocalRankDisplay), nameof(PanelLocalRankDisplay.Beatmap));
            realmProperty = AccessTools.Property(typeof(PanelLocalRankDisplay), "realm");
            localUserField = AccessTools.Field(typeof(PanelLocalRankDisplay), "localUser");

            var missingMembers = new[]
            {
                (name: "PanelLocalRankDisplay.setRankFromScore", member: (MemberInfo?)target),
                (name: "O2LazerSongSelectLampPatcher.prefix", member: prefixMethod),
                (name: "PanelLocalRankDisplay.localScoresChanged", member: localScoresChangedTarget),
                (name: "O2LazerSongSelectLampPatcher.scoresChangedPrefix", member: scoresChangedPrefixMethod),
                (name: "PanelLocalRankDisplay.ruleset", member: rulesetProperty),
                (name: "PanelLocalRankDisplay.Beatmap", member: beatmapProperty),
                (name: "PanelLocalRankDisplay.realm", member: realmProperty),
                (name: "PanelLocalRankDisplay.localUser", member: localUserField),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                disable("O2Jam song-select rank patch cannot be installed. Missing: " + string.Join(", ", missingMembers));
                return;
            }

            var harmony = new Harmony(harmony_id);
            harmony.Patch(target, prefix: new HarmonyMethod(prefixMethod));
            harmony.Patch(localScoresChangedTarget, prefix: new HarmonyMethod(scoresChangedPrefixMethod));
            IsInstalled = true;
        }
        catch (Exception exception)
        {
            disable("Failed to install the O2Jam song-select rank patch.", exception);
        }
    }

    // ReSharper disable once InconsistentNaming
    private static void prefix(PanelLocalRankDisplay __instance, ref ScoreInfo? topScore)
    {
        if (disabled || !isO2JamRuleset(__instance))
            return;

        try
        {
            if (beatmapProperty?.GetValue(__instance) is not BeatmapInfo beatmap
                || localUserField?.GetValue(__instance) is not IBindable<APIUser> localUser)
                return;

            var key = new ScoreCacheKey(beatmap.ID, localUser.Value.Id, Constant.SHORT_NAME);
            var version = Volatile.Read(ref scoresVersion);

            if (score_cache.TryGetValue(key, out var cached) && cached.Version == version)
            {
                topScore = cached.Score;
                return;
            }

            if (realmProperty?.GetValue(__instance) is not RealmAccess realm
                || rulesetProperty?.GetValue(__instance) is not IBindable<RulesetInfo> ruleset)
                return;

            var score = realm.Run(r => SelectTopScoreForDifficulty(r.All<ScoreInfo>(), beatmap, ruleset.Value, localUser.Value.Id)?.DeepClone());

            // Don't cache a result that raced with a score change; the next call will re-query.
            if (Volatile.Read(ref scoresVersion) == version)
                score_cache[key] = new CachedScore(version, score);

            topScore = score;
        }
        catch (Exception exception)
        {
            O2LazerLogger.Error(exception, "O2Jam song-select rank patch failed while resolving the current difficulty.");
        }
    }

    // ReSharper disable once InconsistentNaming
    private static void scoresChangedPrefix(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        // Initial subscription callbacks pass null changes and must not invalidate the cache,
        // otherwise every recycled panel during scrolling would re-query.
        if (changes != null && changes.HasCollectionChanges())
            Interlocked.Increment(ref scoresVersion);
    }

    internal static ScoreInfo? SelectTopScoreForDifficulty(
        IEnumerable<ScoreInfo> scores,
        BeatmapInfo beatmap,
        RulesetInfo ruleset,
        int localUserId) =>
        scores.Where(score => score.BeatmapInfo?.ID == beatmap.ID)
              .Where(score => score.UserID == localUserId || score.UserID <= 1)
              .Where(score => score.Ruleset.ShortName == ruleset.ShortName && !score.DeletePending)
              .MaxBy(score => (score.TotalScore, -score.Date.UtcDateTime.Ticks));

    private static bool isO2JamRuleset(PanelLocalRankDisplay display) =>
        rulesetProperty?.GetValue(display) is IBindable<RulesetInfo> ruleset
        && ruleset.Value.ShortName == Constant.SHORT_NAME;

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            O2LazerLogger.Log(message, LogLevel.Error);
        else
            O2LazerLogger.Error(exception, message);
    }

    private readonly record struct ScoreCacheKey(Guid BeatmapId, int LocalUserId, string RulesetShortName);

    private sealed class CachedScore(long version, ScoreInfo? score)
    {
        public readonly long Version = version;
        public readonly ScoreInfo? Score = score;
    }
}
