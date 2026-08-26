using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public static class O2LazerReplayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.Replay";
    private static bool disabled;

    private static PropertyInfo? playerScoreManagerProperty;
    private static PropertyInfo? modelManagerRealmProperty;
    private static FieldInfo? scoreImporterFilesField;
    private static FieldInfo? scoreImporterBeatmapsField;
    private static FieldInfo? scoreImporterRulesetsField;
    private static FieldInfo? replayFailIndicatorTrackField;
    private static FieldInfo? replayFailIndicatorFailSampleField;
    private static MethodInfo? drawableScheduleMethod;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        try
        {
            var importScoreTarget = AccessTools.Method(typeof(Player), "ImportScore", [typeof(Score)]);
            var importScorePostfixMethod = AccessTools.Method(typeof(O2LazerReplayPatcher), nameof(importScorePostfix));
            var scoreDeepCloneTarget = AccessTools.Method(typeof(Score), nameof(Score.DeepClone));
            var scoreDeepClonePostfixMethod = AccessTools.Method(typeof(O2LazerReplayPatcher), nameof(scoreDeepClonePostfix));
            var getScoreTarget = AccessTools.Method(typeof(ScoreImporter), nameof(ScoreImporter.GetScore), [typeof(ScoreInfo)]);
            var getScorePrefixMethod = AccessTools.Method(typeof(O2LazerReplayPatcher), nameof(getScorePrefix));
            var createModelTarget = AccessTools.Method(typeof(ScoreImporter), "CreateModel", [typeof(ArchiveReader), typeof(ImportParameters)]);
            var createModelPrefixMethod = AccessTools.Method(typeof(O2LazerReplayPatcher), nameof(createModelPrefix));
            var statisticsPanelPopulateTarget = AccessTools.Method(typeof(StatisticsPanel), "populateStatistics", [typeof(ValueChangedEvent<ScoreInfo?>)]);
            var statisticsPanelPopulatePrefixMethod = AccessTools.Method(typeof(O2LazerReplayPatcher), nameof(statisticsPanelPopulatePrefix));
            var replayFailIndicatorDisposeTarget = AccessTools.Method(typeof(ReplayFailIndicator), "Dispose", [typeof(bool)]);
            var replayFailIndicatorDisposePrefixMethod = AccessTools.Method(typeof(O2LazerReplayPatcher), nameof(replayFailIndicatorDisposePrefix));

            playerScoreManagerProperty = AccessTools.Property(typeof(Player), "scoreManager");
            modelManagerRealmProperty = AccessTools.Property(typeof(ModelManager<ScoreInfo>), "Realm");
            scoreImporterFilesField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Files");
            scoreImporterBeatmapsField = AccessTools.Field(typeof(ScoreImporter), "beatmaps");
            scoreImporterRulesetsField = AccessTools.Field(typeof(ScoreImporter), "rulesets");
            replayFailIndicatorTrackField = AccessTools.Field(typeof(ReplayFailIndicator), "track");
            replayFailIndicatorFailSampleField = AccessTools.Field(typeof(ReplayFailIndicator), "failSample");
            drawableScheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]);

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "Player.ImportScore", member: importScoreTarget),
                (name: "O2LazerReplayPatcher.importScorePostfix", member: importScorePostfixMethod),
                (name: "Score.DeepClone", member: scoreDeepCloneTarget),
                (name: "O2LazerReplayPatcher.scoreDeepClonePostfix", member: scoreDeepClonePostfixMethod),
                (name: "ScoreImporter.GetScore", member: getScoreTarget),
                (name: "O2LazerReplayPatcher.getScorePrefix", member: getScorePrefixMethod),
                (name: "ScoreImporter.CreateModel", member: createModelTarget),
                (name: "O2LazerReplayPatcher.createModelPrefix", member: createModelPrefixMethod),
                (name: "StatisticsPanel.populateStatistics", member: statisticsPanelPopulateTarget),
                (name: "O2LazerReplayPatcher.statisticsPanelPopulatePrefix", member: statisticsPanelPopulatePrefixMethod),
                (name: "ReplayFailIndicator.Dispose", member: replayFailIndicatorDisposeTarget),
                (name: "O2LazerReplayPatcher.replayFailIndicatorDisposePrefix", member: replayFailIndicatorDisposePrefixMethod),
                (name: "Player.scoreManager", member: playerScoreManagerProperty),
                (name: "ModelManager<ScoreInfo>.Realm", member: modelManagerRealmProperty),
                (name: "RealmArchiveModelImporter<ScoreInfo>.Files", member: scoreImporterFilesField),
                (name: "ScoreImporter.beatmaps", member: scoreImporterBeatmapsField),
                (name: "ScoreImporter.rulesets", member: scoreImporterRulesetsField),
                (name: "ReplayFailIndicator.track", member: replayFailIndicatorTrackField),
                (name: "ReplayFailIndicator.failSample", member: replayFailIndicatorFailSampleField),
                (name: "Drawable.Schedule", member: drawableScheduleMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                disable("O2LAZER replay patch cannot be installed. Missing: " + string.Join(", ", missingMembers) + ".");
                return;
            }

            var harmony = new Harmony(harmony_id);
            harmony.Patch(importScoreTarget, postfix: new HarmonyMethod(importScorePostfixMethod));
            harmony.Patch(scoreDeepCloneTarget, postfix: new HarmonyMethod(scoreDeepClonePostfixMethod));
            harmony.Patch(getScoreTarget, prefix: new HarmonyMethod(getScorePrefixMethod));
            harmony.Patch(createModelTarget, prefix: new HarmonyMethod(createModelPrefixMethod));
            harmony.Patch(statisticsPanelPopulateTarget, prefix: new HarmonyMethod(statisticsPanelPopulatePrefixMethod));
            harmony.Patch(replayFailIndicatorDisposeTarget, prefix: new HarmonyMethod(replayFailIndicatorDisposePrefixMethod));
            IsInstalled = true;
        }
        catch (Exception e)
        {
            disable("Failed to install the O2LAZER replay patch.", e);
        }
    }

    private static async Task importScoreWithReplay(Player player, Score score, Task originalImport)
    {
        await originalImport.ConfigureAwait(false);

        if (score.Replay.Frames.Count == 0 || score.ScoreInfo.Files.Any(f => f.Filename == O2LazerReplayArchive.FILENAME))
            return;

        try
        {
            await scheduleOnPlayerUpdateThread(player, () => attachReplayToScore(player, score)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, "O2LAZER replay patch failed to attach replay data to a local score.");
        }
    }

    private static void attachReplayToScore(Player player, Score score)
    {
        if (playerScoreManagerProperty?.GetValue(player) is not ScoreManager scoreManager)
            return;

        using var archive = O2LazerReplayArchive.Create(score);
        using var stream = new MemoryStream(archive.Get(O2LazerReplayArchive.FILENAME));
        var hash = O2LazerReplayArchive.ComputeHash(score);

        scoreManager.AddFile(score.ScoreInfo, stream, O2LazerReplayArchive.GetNativeFilename(score.ScoreInfo));
        applyHash(scoreManager, score.ScoreInfo, hash);
        score.ScoreInfo.Hash = hash;
    }

    private static void applyHash(ScoreManager scoreManager, ScoreInfo scoreInfo, string hash)
    {
        if (modelManagerRealmProperty?.GetValue(scoreManager) is not RealmAccess realmAccess)
            return;

        realmAccess.Write(realm =>
        {
            var managed = realm.Find<ScoreInfo>(scoreInfo.ID);

            managed?.Hash = hash;
        });
    }

    private static Task scheduleOnPlayerUpdateThread(Player player, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        drawableScheduleMethod!.Invoke(player,
        [
            () =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            },
        ]);

        return completion.Task;
    }

    // ReSharper disable InconsistentNaming
    private static void importScorePostfix(Player __instance, Score score, ref Task __result)
    {
        if (!isO2LazerScore(score.ScoreInfo))
            return;

        __result = importScoreWithReplay(__instance, score, __result);
    }

    private static void scoreDeepClonePostfix(Score __instance, Score __result)
    {
        if (!isO2LazerScore(__instance.ScoreInfo))
            return;

        // Player clones a completed score before the replay archive is created. These sidecars
        // carry data that ScoreInfo.DeepClone cannot know about, so keep them with that clone.
        if (O2LazerJudgementEventStore.TryGet(__instance.ScoreInfo, out var judgementEvents))
            O2LazerJudgementEventStore.Set(__result.ScoreInfo, judgementEvents);

    }

    private static bool getScorePrefix(ScoreInfo score, ScoreImporter __instance, ref Score __result)
    {
        var replayFile = score.Files.FirstOrDefault(f => f.Filename == O2LazerReplayArchive.FILENAME)
                         ?? score.Files.FirstOrDefault(f => f.Filename.EndsWith(".osr", StringComparison.OrdinalIgnoreCase));

        if (!isO2LazerScore(score) || replayFile == null)
            return true;

        if (scoreImporterFilesField?.GetValue(__instance) is not RealmFileStore files)
            return false;

        try
        {
            using var stream = files.Store.GetStream(replayFile.File.GetStoragePath());
            if (stream == null)
                return true;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();

            if (!O2LazerReplayArchive.IsPayload(bytes))
                return true;

            __result = O2LazerReplayArchive.TryReadScore(score, bytes, out var result)
                ? result
                : new Score { ScoreInfo = score };
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, "O2LAZER replay patch failed to restore replay data from a local score.");
            __result = new Score { ScoreInfo = score };
        }

        return false;
    }

    private static bool createModelPrefix(ScoreImporter __instance, ArchiveReader archive, ImportParameters parameters, ref ScoreInfo? __result)
    {
        if (scoreImporterBeatmapsField?.GetValue(__instance) is not Func<BeatmapManager> beatmapsFactory
            || scoreImporterRulesetsField?.GetValue(__instance) is not RulesetStore rulesets)
        {
            return true;
        }

        try
        {
            var beatmaps = beatmapsFactory();
            var fileName = archive.Filenames.FirstOrDefault(f => f.EndsWith(".osr", StringComparison.OrdinalIgnoreCase));
            if (fileName == null)
                return true;

            var bytes = archive.Get(fileName);
            if (!O2LazerReplayArchive.IsPayload(bytes))
                return true;

            if (!O2LazerReplayFileNameParser.TryParse(archive.Name, out var metadata))
            {
                O2LazerLogger.Log($"O2LAZER replay import: could not parse metadata from '{archive.Name}'.", LogLevel.Error);
                return false;
            }

            var beatmap = findBeatmap(beatmaps, metadata);
            if (beatmap == null)
            {
                O2LazerLogger.Log($"O2LAZER replay import: no local O2Jam beatmap matches '{archive.Name}'.", LogLevel.Error);
                return false;
            }

            var ruleset = rulesets.GetRuleset(Constant.SHORT_NAME);
            if (ruleset == null)
                return true;

            var scoreInfo = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = metadata.Player })
            {
                Date = metadata.Date,
                User = new APIUser { Username = metadata.Player },
                Mods = [],
                Statistics = [],
                MaximumStatistics = [],
            };

            if (!O2LazerReplayArchive.TryReadScore(scoreInfo, bytes, out var score, out var hasEmbeddedScoreData))
                return true;

            if (!hasEmbeddedScoreData)
                populateStatistics(score);

            __result = score.ScoreInfo;
            return false;
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, "O2LAZER replay import failed while creating score metadata.");
            return true;
        }
    }

    private static BeatmapInfo? findBeatmap(BeatmapManager beatmaps, O2LazerReplayFileMetadata metadata)
        => beatmaps.GetAllUsableBeatmapSets()
                   .SelectMany(set => set.Beatmaps)
                   .FirstOrDefault(beatmap => beatmap.Ruleset.ShortName == Constant.SHORT_NAME
                                              && string.Equals(beatmap.Metadata.Artist, metadata.Artist, StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(beatmap.Metadata.Title, metadata.Title, StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(beatmap.DifficultyName, metadata.Difficulty, StringComparison.OrdinalIgnoreCase));

    private static void populateStatistics(Score score)
    {
        if (!O2LazerJudgementEventStore.TryGet(score.ScoreInfo, out var judgementEvents))
            return;

        var scoringEvents = judgementEvents
                            .Where(e => e.Source.IsScoring)
                            .OrderBy(e => e.TimingObservations.Min(observation => observation.ActualTime))
                            .ToArray();

        var o2JamScore = new O2JamScoreState();
        o2JamScore.Reset();

        var statistics = new Dictionary<HitResult, int>();

        foreach (var judgementEvent in scoringEvents)
        {
            statistics[judgementEvent.Result] = statistics.GetValueOrDefault(judgementEvent.Result) + 1;
            o2JamScore.Apply(judgementEvent.Result);
        }

        score.ScoreInfo.Statistics = statistics;
        score.ScoreInfo.MaximumStatistics = new Dictionary<HitResult, int>(statistics);
        score.ScoreInfo.TotalScore = o2JamScore.Score;
        score.ScoreInfo.MaxCombo = o2JamScore.MaximumCombo;

        var total = statistics.Values.Sum();
        var weighted = statistics.GetValueOrDefault(HitResult.Perfect) * 200d
                       + statistics.GetValueOrDefault(HitResult.Good) * 100d
                       + statistics.GetValueOrDefault(HitResult.Ok) * 4d;
        score.ScoreInfo.Accuracy = total > 0 ? weighted / (total * 200d) : 1;
    }

    private static void statisticsPanelPopulatePrefix(StatisticsPanel __instance, ValueChangedEvent<ScoreInfo?> score)
    {
        var scoreInfo = score.NewValue;

        if (scoreInfo == null || !isO2LazerScore(scoreInfo))
            return;

        if (scoreInfo.HitEvents.Count > 0)
            return;

        // CompositeDrawable.Dependencies is populated during InjectDependencies, which the framework
        // runs before the BackgroundDependencyLoader that first fires this callback. We read it here
        // directly rather than capturing it via a separate InjectDependencies patch — Harmony patches
        // on the base Drawable.InjectDependencies don't reliably fire for CompositeDrawable's sealed
        // override, since the override's `base.InjectDependencies()` call is JIT-inlined early.
        var dependencies = __instance.Dependencies;

        if (dependencies == null || !dependencies.TryGet<ScoreManager>(out var scoreManager))
            return;

        try
        {
            var scoreWithReplay = scoreManager.GetScore(scoreInfo);

            if (scoreWithReplay?.ScoreInfo.HitEvents.Count > 0)
                scoreInfo.HitEvents = scoreWithReplay.ScoreInfo.HitEvents;

            if (scoreWithReplay != null
                && O2LazerJudgementEventStore.TryGet(scoreWithReplay.ScoreInfo, out var restoredJudgementEvents)
                && restoredJudgementEvents.Count > 0)
            {
                O2LazerJudgementEventStore.Set(scoreInfo, restoredJudgementEvents);
            }

            if (scoreWithReplay != null && !O2LazerReplayArchive.HasEmbeddedScoreData(scoreWithReplay.ScoreInfo))
                upgradeReplayToEmbeddedScoreData(scoreWithReplay, scoreManager);

        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, "O2LAZER replay patch failed to restore hit events for the statistics panel.");
        }
    }

    private static void upgradeReplayToEmbeddedScoreData(Score score, ScoreManager scoreManager)
    {
        try
        {
            var replayFile = score.ScoreInfo.Files.FirstOrDefault(f => f.Filename.EndsWith(".osr", StringComparison.OrdinalIgnoreCase));

            if (replayFile == null)
                return;

            populateStatistics(score);

            using var archive = O2LazerReplayArchive.Create(score);
            using var stream = new MemoryStream(archive.Get(O2LazerReplayArchive.FILENAME));
            var nativeFilename = O2LazerReplayArchive.GetNativeFilename(score.ScoreInfo);

            if (replayFile.Filename != nativeFilename)
                scoreManager.DeleteFile(score.ScoreInfo, replayFile);

            scoreManager.AddFile(score.ScoreInfo, stream, nativeFilename);
            O2LazerReplayArchive.MarkEmbeddedScoreData(score.ScoreInfo, true);
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, "O2LAZER replay patch failed to upgrade an old replay with embedded score data.");
        }
    }

    private static void replayFailIndicatorDisposePrefix(ReplayFailIndicator __instance)
    {
        if (__instance.LoadState != LoadState.NotLoaded)
            return;

        replayFailIndicatorFailSampleField?.SetValue(__instance, new SkinnableSound());
        replayFailIndicatorTrackField?.SetValue(__instance, new TrackVirtual(0));
    }
    // ReSharper restore InconsistentNaming

    private static bool isO2LazerScore(ScoreInfo score) => score.Ruleset.ShortName == Constant.SHORT_NAME;

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            O2LazerLogger.Log(message, LogLevel.Important);
        else
            O2LazerLogger.Error(exception, message);
    }
}
