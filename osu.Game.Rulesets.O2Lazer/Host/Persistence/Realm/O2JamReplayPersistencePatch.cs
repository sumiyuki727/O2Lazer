using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.Replays;

/// <summary>
/// Bridges the two replay persistence seams which osu! currently exposes only to its legacy rulesets.
/// </summary>
internal static partial class O2JamReplayPersistencePatch
{
    private const string player_harmony_id = "osu.Game.Rulesets.O2Lazer.Replay.Player";
    private const string importer_harmony_id = "osu.Game.Rulesets.O2Lazer.Replay.Importer";

    private static readonly object installLock = new();
    private static PropertyInfo? playerScoreManagerProperty;
    private static PropertyInfo? playerDrawableRulesetProperty;
    private static FieldInfo? scoreImporterFilesField;
    private static FieldInfo? scoreImporterRealmField;
    private static FieldInfo? scoreImporterRulesetsField;
    private static MethodInfo? drawableScheduleMethod;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                var importScoreTarget = AccessTools.Method(typeof(Player), "ImportScore", [typeof(Score)]);
                var importScorePrefix = AccessTools.Method(typeof(O2JamReplayPersistencePatch), nameof(prepareReplayArchive));
                var importScorePostfix = AccessTools.Method(typeof(O2JamReplayPersistencePatch), nameof(attachReplayArchive));
                var getScoreTarget = AccessTools.Method(typeof(ScoreImporter), nameof(ScoreImporter.GetScore), [typeof(ScoreInfo)]);
                var getScorePrefix = AccessTools.Method(typeof(O2JamReplayPersistencePatch), nameof(readO2JamReplay));
                var createModelTarget = AccessTools.Method(typeof(ScoreImporter), "CreateModel", [typeof(ArchiveReader), typeof(ImportParameters)]);
                var createModelPrefix = AccessTools.Method(typeof(O2JamReplayPersistencePatch), nameof(readO2JamReplayMetadata));

                playerScoreManagerProperty = AccessTools.Property(typeof(Player), "scoreManager");
                playerDrawableRulesetProperty = AccessTools.Property(typeof(Player), "DrawableRuleset");
                scoreImporterFilesField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Files");
                scoreImporterRealmField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Realm");
                scoreImporterRulesetsField = AccessTools.Field(typeof(ScoreImporter), "rulesets");
                drawableScheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]);

                if (importScoreTarget == null || importScorePrefix == null || importScorePostfix == null
                    || getScoreTarget == null || getScorePrefix == null || createModelTarget == null || createModelPrefix == null
                    || playerScoreManagerProperty == null || playerDrawableRulesetProperty == null || scoreImporterFilesField == null
                    || scoreImporterRealmField == null || scoreImporterRulesetsField == null || drawableScheduleMethod == null)
                    return false;

                var playerUsesBmsHarmony = O2JamBmsHarmonyCompatibility.TryPatch(
                        importScoreTarget,
                        importScorePrefix,
                        importScorePostfix,
                        player_harmony_id);
                if (!playerUsesBmsHarmony)
                {
                    new Harmony(player_harmony_id).Patch(
                        importScoreTarget,
                        prefix: new HarmonyMethod(importScorePrefix),
                        postfix: new HarmonyMethod(importScorePostfix));
                }

                var importerHarmony = new Harmony(importer_harmony_id);
                var getScoreUsesBmsHarmony = O2JamBmsHarmonyCompatibility.TryPatch(getScoreTarget, getScorePrefix, importer_harmony_id);
                if (!getScoreUsesBmsHarmony)
                    importerHarmony.Patch(getScoreTarget, prefix: new HarmonyMethod(getScorePrefix));

                var createModelUsesBmsHarmony = O2JamBmsHarmonyCompatibility.TryPatch(createModelTarget, createModelPrefix, importer_harmony_id);
                if (!createModelUsesBmsHarmony)
                    importerHarmony.Patch(createModelTarget, prefix: new HarmonyMethod(createModelPrefix));

                IsInstalled = true;
                if (!playerUsesBmsHarmony)
                    O2JamBmsHarmonyCompatibility.RegisterForLateLoad(importScoreTarget, importScorePrefix, importScorePostfix, player_harmony_id);
                if (!getScoreUsesBmsHarmony)
                    O2JamBmsHarmonyCompatibility.RegisterForLateLoad(getScoreTarget, getScorePrefix, null, importer_harmony_id);
                if (!createModelUsesBmsHarmony)
                    O2JamBmsHarmonyCompatibility.RegisterForLateLoad(createModelTarget, createModelPrefix, null, importer_harmony_id);
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(player_harmony_id, importer_harmony_id);
                Logger.Error(exception, "O2Lazer could not install replay persistence support.");
                return false;
            }
        }
    }

    private static bool readO2JamReplay(ScoreInfo score, ScoreImporter __instance, ref Score __result)
    {
        if (!isO2JamScore(score))
            return true;

        // A default Score contains an empty but non-null Replay, which osu! still offers to play.
        // Unsupported stored data must be explicitly unavailable and never enter the legacy decoder.
        __result = new Score { ScoreInfo = score, Replay = null! };

        var replayFile = score.Files.FirstOrDefault(file => file.Filename.EndsWith(".osr", StringComparison.OrdinalIgnoreCase));
        if (replayFile == null || scoreImporterFilesField!.GetValue(__instance) is not RealmFileStore files)
            return false;

        try
        {
            using var stream = files.Store.GetStream(replayFile.File.GetStoragePath());
            if (stream == null)
                return false;

            if (O2JamReplayArchive.TryReadScore(score, stream, out var restored, out var failure))
                __result = restored;
            else if (failure.ResourceLimitExceeded)
                notifyReplayLimit(__instance);
            return false;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "O2Lazer could not read a stored replay.");
            return false;
        }
    }

    private static bool readO2JamReplayMetadata(
        ScoreImporter __instance,
        ArchiveReader archive,
        ref ScoreInfo? __result)
    {
        var replayFilename = archive.Filenames.FirstOrDefault(file => file.EndsWith(".osr", StringComparison.OrdinalIgnoreCase));
        if (replayFilename == null)
            return true;

        using var stream = archive.GetStream(replayFilename);
        if (stream == null)
            return true;

        // JSON/gzip envelopes are also used by foreign rulesets. Their data must keep
        // using the existing importer until a current O2Jam marker has been identified.
        if (!O2JamReplayArchive.TryReadMetadata(stream, out var metadata, out var failure))
        {
            // Unknown envelopes still belong to the native/foreign importer. Once the
            // v5 O2Jam marker is known, invalid data must not retry legacy decoding.
            if (failure.IdentifiedO2JamEnvelope)
            {
                __result = null;
                if (failure.ResourceLimitExceeded)
                    notifyReplayLimit(__instance);
                return false;
            }
            return true;
        }

        if (scoreImporterRealmField!.GetValue(__instance) is not RealmAccess realm
            || scoreImporterRulesetsField!.GetValue(__instance) is not RulesetStore rulesets)
            return true;

        // Native QueryBeatmap returns the first match. Shared legacy hashes and foreign
        // rulesets require a unique, consistent match before an archive can become a score.
        var beatmap = realm.Run(database => O2JamReplayBeatmapResolver.Resolve(database, metadata.BeatmapHash, metadata.BeatmapMd5)?.Detach());

        var ruleset = rulesets.GetRuleset(O2LazerIdentity.ShortName);
        if (beatmap == null || ruleset == null)
            return false;

        var scoreInfo = new ScoreInfo(
            beatmap,
            ruleset,
            new RealmUser { OnlineID = metadata.UserId, Username = metadata.Player })
        {
            User = new APIUser { Id = metadata.UserId, Username = metadata.Player },
            Date = metadata.Date,
            TotalScore = metadata.TotalScore,
            TotalScoreWithoutMods = metadata.TotalScoreWithoutMods,
            MaxCombo = metadata.MaxCombo,
            Accuracy = metadata.Accuracy,
            Statistics = metadata.Statistics,
            MaximumStatistics = metadata.MaximumStatistics,
            ModsJson = metadata.ModsJson,
            ClientVersion = metadata.ClientVersion,
            Rank = metadata.Rank,
            Passed = metadata.Rank != ScoreRank.F,
        };
        scoreInfo.Pauses.AddRange(metadata.Pauses);
        __result = scoreInfo;
        return false;
    }

    private static string nativeReplayFilename(ScoreInfo scoreInfo) =>
        $"{scoreInfo.GetDisplayString()} ({scoreInfo.Date.LocalDateTime:yyyy-MM-dd_HH-mm}).osr".GetValidFilename();

    private static void notifyReplayLimit(ScoreImporter importer) =>
        importer.PostNotification?.Invoke(new SimpleNotification { Text = O2LazerStrings.ReplayReadLimitExceeded });

    private static bool isO2JamScore(ScoreInfo score) =>
        string.Equals(score.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal);
}
