using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;
using NativeSongSelect = osu.Game.Screens.Select.SongSelect;

namespace osu.Game.Rulesets.O2Lazer.TestSupport;

// Keep test-only database operations separate from the bridge's navigation and audio controls.
internal static partial class O2LazerTestBridge
{
    private static Guid jobId;
    private static string? jobOperation;
    private static bool jobRunning;
    private static object? jobResult;
    private static string? jobError;

    private static bool tryDispatchAcceptance(NativeSongSelect screen, string? command, JsonElement request, out object? result)
    {
        result = null;
        switch (command)
        {
            case "inspect-data":
                result = startJob(command, () => Task.FromResult(inspectData(screen.Dependencies.Get<RealmAccess>())));
                return true;
            case "library-state":
            case "refresh-library":
                var config = (O2JamRulesetConfigManager)screen.Dependencies.Get<IRulesetConfigCache>()
                    .GetConfigFor(screen.Dependencies.Get<RulesetStore>().GetRuleset("o2lazer")!.CreateInstance())!;
                var session = O2JamLibrarySettingsSession.Get(config,
                    () => new O2JamRealmLibraryBackend(screen.Dependencies.Get<RealmAccess>(), screen.Dependencies.Get<GameHost>().Storage,
                        screen.Dependencies.Get<IWorkingBeatmapCache>(), screen.Dependencies.Get<BeatmapDifficultyCache>()),
                    screen.Dependencies.Get<INotificationOverlay>());
                if (command == "library-state")
                    result = new { session.Application.IsBusy, session.Application.CanRefresh, path = config.GetBindable<string>(O2JamRulesetSetting.LastImportPath).Value };
                else
                {
                    if (!session.Application.CanRefresh)
                        throw new InvalidOperationException("Library refresh is unavailable or already running.");
                    // Share the settings application's queue and notifications rather than creating a second importer.
                    result = startJob(command, async () =>
                    {
                        await session.RefreshAsync().ConfigureAwait(false);
                        return new { finished = !session.Application.IsBusy };
                    });
                }
                return true;
            case "read-replay":
            case "export-replay":
                var scoreManager = screen.Dependencies.Get<ScoreManager>();
                var scoreId = request.GetProperty("scoreId").GetGuid();
                result = startJob(command, async () =>
                {
                    var info = scoreManager.Query(s => s.ID == scoreId)
                               ?? throw new InvalidOperationException("Score is unavailable.");
                    if (info.Ruleset.ShortName != "o2lazer" || info.DeletePending)
                        throw new InvalidOperationException("Acceptance replay commands require an available O2Lazer score.");
                    if (command == "export-replay")
                    {
                        await scoreManager.Export(info).ConfigureAwait(false);
                        return new { scoreId, exported = true };
                    }
                    var score = scoreManager.GetScore(info) ?? throw new InvalidDataException("Stored replay could not be read.");
                    var frames = score.Replay.Frames.Cast<O2JamReplayFrame>().Select(f => new { f.Time, actions = f.Actions.Select(a => (int)a).ToArray() }).ToArray();
                    return new { scoreId, frames = frames.Length, firstTime = frames.FirstOrDefault()?.Time,
                        lastTime = frames.LastOrDefault()?.Time, frameHash = digest(frames) };
                });
                return true;
            case "import-replay":
                var path = Path.GetFullPath(request.GetProperty("path").GetString() ?? throw new InvalidDataException("Replay path is required."));
                if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".osr", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Replay import requires an existing local .osr file.");
                var manager = screen.Dependencies.Get<ScoreManager>();
                result = startJob(command, async () =>
                {
                    // Native import owns matching, duplicate handling and attachment transactions.
                    var notification = new ProgressNotification();
                    var imported = await manager.Import(notification, [new ImportTask(path)]).ConfigureAwait(false);
                    return new { imported = imported.Count(), state = notification.State.ToString(), sourceRetained = File.Exists(path) };
                });
                return true;
            case "job-status":
                lock (sync)
                {
                    if (request.GetProperty("jobId").GetGuid() != jobId)
                        throw new InvalidOperationException("Acceptance job identity changed.");
                    result = new { jobId, operation = jobOperation, running = jobRunning, result = jobResult, error = jobError };
                }
                return true;
            default:
                return false;
        }
    }

    private static object startJob(string operation, Func<Task<object>> work)
    {
        lock (sync)
        {
            if (jobRunning)
                throw new InvalidOperationException("An acceptance job is already running.");
            jobId = Guid.NewGuid();
            jobOperation = operation;
            jobResult = null;
            jobError = null;
            jobRunning = true;
            // Long native jobs must not occupy the pipe timeout or block song-select animations.
            _ = Task.Run(async () =>
            {
                object? value = null;
                string? error = null;
                try { value = await work().ConfigureAwait(false); }
                catch (Exception exception) { error = exception.ToString(); }
                lock (sync)
                {
                    jobResult = value;
                    jobError = error;
                    jobRunning = false;
                }
            });
            return new { jobId, operation, queued = true };
        }
    }

    private static object inspectData(RealmAccess access) => access.Run(realm =>
    {
        // Snapshot managed values on their owning Realm thread; only plain values cross the pipe.
        var scores = realm.All<ScoreInfo>().AsEnumerable().OrderBy(s => s.ID).Select(s => new
        {
            id = s.ID, s.Hash, s.BeatmapHash, beatmapId = s.BeatmapInfo?.ID, ruleset = s.Ruleset.ShortName,
            s.DeletePending, s.TotalScore, s.TotalScoreWithoutMods, s.TotalScoreVersion, s.LegacyTotalScore,
            s.MaxCombo, s.Combo, s.Accuracy, s.RankInt, s.Date, s.ClientVersion, s.ModsJson, s.StatisticsJson,
            s.MaximumStatisticsJson, pauses = s.Pauses.ToArray(), user = new { s.RealmUser.OnlineID, s.RealmUser.Username },
            files = s.Files.OrderBy(f => f.Filename, StringComparer.Ordinal).Select(f => new { f.Filename, f.File.Hash }).ToArray(),
        }).ToArray();
        var beatmaps = realm.All<BeatmapInfo>().AsEnumerable().OrderBy(b => b.ID).Select(b => new
        {
            id = b.ID, b.Hash, b.MD5Hash, ruleset = b.Ruleset.ShortName, b.Hidden,
            setId = b.BeatmapSet?.ID, deletePending = b.BeatmapSet?.DeletePending,
        }).ToArray();
        var collections = realm.All<BeatmapCollection>().AsEnumerable().OrderBy(c => c.ID).Select(c => new
        {
            id = c.ID, c.Name, hashes = c.BeatmapMD5Hashes.OrderBy(h => h, StringComparer.Ordinal).ToArray(),
        }).ToArray();
        return (object)new
        {
            scoreCount = scores.Length, scoreHash = digest(scores), beatmapCount = beatmaps.Length, beatmapHash = digest(beatmaps),
            o2BeatmapCount = beatmaps.Count(b => b.ruleset == "o2lazer"), collectionCount = collections.Length, collectionHash = digest(collections),
            fileCount = realm.All<RealmFile>().Count(),
            recentO2Scores = scores.Where(s => s.ruleset == "o2lazer" && !s.DeletePending && s.files.Length > 0)
                .OrderByDescending(s => s.Date).Take(32).ToArray(),
        };
    });

    private static string digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions)));
}
