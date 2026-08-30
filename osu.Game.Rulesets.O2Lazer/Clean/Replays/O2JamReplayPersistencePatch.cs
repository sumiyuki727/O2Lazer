using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.Replays;

/// <summary>
/// Bridges the two replay persistence seams which osu! currently exposes only to its legacy rulesets.
/// </summary>
internal static class O2JamReplayPersistencePatch
{
    private const string player_harmony_id = "osu.Game.Rulesets.O2Lazer.Replay.Player";
    private const string importer_harmony_id = "osu.Game.Rulesets.O2Lazer.Replay.Importer";

    private static readonly object installLock = new();
    private static PropertyInfo? playerScoreManagerProperty;
    private static FieldInfo? scoreImporterFilesField;
    private static FieldInfo? scoreImporterBeatmapsField;
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
                scoreImporterFilesField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Files");
                scoreImporterBeatmapsField = AccessTools.Field(typeof(ScoreImporter), "beatmaps");
                scoreImporterRulesetsField = AccessTools.Field(typeof(ScoreImporter), "rulesets");
                drawableScheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]);

                if (importScoreTarget == null || importScorePrefix == null || importScorePostfix == null
                    || getScoreTarget == null || getScorePrefix == null || createModelTarget == null || createModelPrefix == null
                    || playerScoreManagerProperty == null || scoreImporterFilesField == null
                    || scoreImporterBeatmapsField == null || scoreImporterRulesetsField == null || drawableScheduleMethod == null)
                    return false;

                if (!O2JamBeatmapBoundaryPatches.TryPatchWithBmsHarmony(
                        importScoreTarget,
                        importScorePrefix,
                        importScorePostfix,
                        player_harmony_id))
                {
                    new Harmony(player_harmony_id).Patch(
                        importScoreTarget,
                        prefix: new HarmonyMethod(importScorePrefix),
                        postfix: new HarmonyMethod(importScorePostfix));
                }

                var importerHarmony = new Harmony(importer_harmony_id);
                if (!O2JamBeatmapBoundaryPatches.TryPatchWithBmsHarmony(getScoreTarget, getScorePrefix, importer_harmony_id))
                    importerHarmony.Patch(getScoreTarget, prefix: new HarmonyMethod(getScorePrefix));

                if (!O2JamBeatmapBoundaryPatches.TryPatchWithBmsHarmony(createModelTarget, createModelPrefix, importer_harmony_id))
                    importerHarmony.Patch(createModelTarget, prefix: new HarmonyMethod(createModelPrefix));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install replay persistence support.");
                return false;
            }
        }
    }

    // Harmony state transports the exact bytes hashed before ScoreInfo.DeepClone() to the postfix which attaches the file.
    private static void prepareReplayArchive(Score score, out byte[]? __state)
    {
        __state = null;
        if (!isO2JamScore(score.ScoreInfo) || score.Replay.Frames.Count == 0)
            return;

        __state = O2JamReplayArchive.Create(score);
        using var replayStream = new MemoryStream(__state, writable: false);
        score.ScoreInfo.Hash = replayStream.ComputeSHA2Hash();
    }

    private static void attachReplayArchive(Player __instance, Score score, byte[]? __state, ref Task __result)
    {
        if (__state == null)
            return;

        __result = attachAfterImport(__instance, score, __state, __result);
    }

    private static async Task attachAfterImport(Player player, Score score, byte[] replayData, Task originalImport)
    {
        await originalImport.ConfigureAwait(false);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        drawableScheduleMethod!.Invoke(player,
        [
            () =>
            {
                try
                {
                    if (playerScoreManagerProperty!.GetValue(player) is ScoreManager scoreManager)
                    {
                        using var replayStream = new MemoryStream(replayData, writable: false);
                        scoreManager.AddFile(score.ScoreInfo, replayStream, nativeReplayFilename(score.ScoreInfo));
                    }

                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
        ]);

        await completion.Task.ConfigureAwait(false);
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

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            if (O2JamReplayArchive.TryReadScore(score, bytes, out var restored))
                __result = restored;
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

        var bytes = archive.Get(replayFilename);
        // Pre-rewrite JSON/gzip envelopes are also used by BMSRuleset. Only claim a validated
        // current O2Jam payload; osu!'s importer rejects unknown headers before creating a model.
        if (!O2JamReplayArchive.TryReadMetadata(bytes, out var metadata))
            return true;

        if (scoreImporterBeatmapsField!.GetValue(__instance) is not Func<BeatmapManager> beatmapsFactory
            || scoreImporterRulesetsField!.GetValue(__instance) is not RulesetStore rulesets)
            return true;

        var beatmaps = beatmapsFactory();
        var beatmap = !string.IsNullOrEmpty(metadata.BeatmapHash)
            ? beatmaps.QueryBeatmap(candidate => candidate.Hash == metadata.BeatmapHash)
            : null;
        beatmap ??= !string.IsNullOrEmpty(metadata.BeatmapMd5)
            ? beatmaps.QueryBeatmap(candidate => candidate.MD5Hash == metadata.BeatmapMd5)
            : null;

        if (beatmap != null && beatmap.Ruleset.ShortName != O2LazerIdentity.ShortName)
            return true;

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
        };
        scoreInfo.Pauses.AddRange(metadata.Pauses);
        __result = scoreInfo;
        return false;
    }

    private static string nativeReplayFilename(ScoreInfo scoreInfo) =>
        $"{scoreInfo.GetDisplayString()} ({scoreInfo.Date.LocalDateTime:yyyy-MM-dd_HH-mm}).osr".GetValidFilename();

    private static bool isO2JamScore(ScoreInfo score) =>
        string.Equals(score.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal);
}
