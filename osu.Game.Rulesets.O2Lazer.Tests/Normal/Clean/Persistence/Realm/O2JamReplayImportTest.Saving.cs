using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Development;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Online.API;
using osu.Game.Overlays.Notifications;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamReplayImportTest
{
    [TestCase("budget")]
    [TestCase("invalid")]
    [TestCase("missing")]
    public void FailedRecordingStillImportsAndReloadsAFindableNativeScore(string kind)
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"score-only-{Guid.NewGuid():N}");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        Guid scoreId;
        string hash;
        using (var realm = new RealmAccess(storage, "client.realm"))
        {
            using var api = new DummyAPIAccess();
            var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
            var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
            if (kind == "invalid")
                ((O2JamReplayFrame)score.Replay.Frames[0]).Actions = [(ManiaAction)7];
            if (kind == "missing")
                score.Replay = null!;
            var prepared = O2JamReplayPersistencePatch.PrepareForImport(score,
                kind == "budget" ? O2JamReplayResourceLimits.Default with { EncodedBytes = 1 } : null);
            Assert.That(prepared.Bytes, Is.Null);
            var notices = new List<Notification>();
            manager.PostNotification = notices.Add;
            importSavingScore(manager, score);
            O2JamReplayPersistencePatch.CompleteImport(score, prepared, Task.CompletedTask, manager, action => action()).GetAwaiter().GetResult();
            scoreId = score.ScoreInfo.ID;
            hash = score.ScoreInfo.Hash;
            Assert.Multiple(() =>
            {
                Assert.That(hash, Is.Not.Empty);
                Assert.That(manager.GetScore(score.ScoreInfo), Is.Not.Null);
                Assert.That(score.ScoreInfo.Files, Is.Empty);
                Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.Zero);
                Assert.That(notices.OfType<SimpleNotification>().Single().Text,
                    Is.EqualTo(kind == "budget" ? O2LazerStrings.ReplaySaveLimitExceeded : O2LazerStrings.ReplaySaveFailed));
            });
        }

        using var reopened = new RealmAccess(storage, "client.realm");
        using var reopenedApi = new DummyAPIAccess();
        var reader = new ScoreManager(rulesets, () => null!, storage, reopened, reopenedApi);
        var restored = reader.GetScore(new ScoreInfo { Hash = hash });
        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.ScoreInfo.ID, Is.EqualTo(scoreId));
            Assert.That(restored.ScoreInfo.TotalScore, Is.EqualTo(12345));
            Assert.That(restored.ScoreInfo.Accuracy, Is.EqualTo(0.9));
            Assert.That(restored.ScoreInfo.MaxCombo, Is.EqualTo(3));
            Assert.That(restored.ScoreInfo.Rank, Is.EqualTo(ScoreRank.F));
            Assert.That(restored.ScoreInfo.Statistics[HitResult.Meh], Is.EqualTo(1));
            Assert.That(restored.Replay, Is.Null);
        });
    }

    [Test]
    public void SeparateFailedRecordingsDoNotReplaceEachOther()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"score-identities-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var beatmap = createBudgetBeatmap(realm, ruleset);
        var first = createSavingScore(beatmap, ruleset);
        var second = createSavingScore(beatmap, ruleset);
        first.ScoreInfo.ID = Guid.Empty;
        foreach (var score in new[] { first, second })
        {
            score.Replay = null!;
            var prepared = O2JamReplayPersistencePatch.PrepareForImport(score);
            importSavingScore(manager, score);
            O2JamReplayPersistencePatch.CompleteImport(score, prepared, Task.CompletedTask, manager, action => action()).GetAwaiter().GetResult();
        }
        Assert.That(first.ScoreInfo.Hash, Is.Not.EqualTo(second.ScoreInfo.Hash));
        Assert.That(first.ScoreInfo.ID, Is.Not.EqualTo(Guid.Empty));
        Assert.That(first.ScoreInfo.ID, Is.Not.EqualTo(second.ScoreInfo.ID));
        Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(2));
    }

    [Test]
    public void SuccessfulReplayIsAttachedAfterSchedulingAndNativeExportCopiesItExactly()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"native-export-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var prepared = O2JamReplayPersistencePatch.PrepareForImport(score);
        Assert.That(prepared.Bytes, Is.Not.Null);
        var notices = new List<Notification>();
        manager.PostNotification = notices.Add;
        importSavingScore(manager, score);
        Action? callback = null;
        var attachment = O2JamReplayPersistencePatch.CompleteImport(score, prepared, Task.CompletedTask, manager, action => callback = action);
        Assert.That(attachment.IsCompleted, Is.False);
        Assert.That(score.ScoreInfo.Files, Is.Empty);
        callback!();
        attachment.GetAwaiter().GetResult();
        var restored = manager.GetScore(score.ScoreInfo)!;
        Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
        Assert.That(restored.ScoreInfo.Files.Single().File.Hash, Is.EqualTo(score.ScoreInfo.Hash));
        Assert.That(notices, Is.Empty);
        using var exported = new MemoryStream();
        new LegacyScoreExporter(storage).ExportToStream(restored.ScoreInfo, exported, null);
        Assert.That(exported.ToArray(), Is.EqualTo(prepared.Bytes));
        Assert.That(O2JamReplayArchive.TryReadMetadata(exported.ToArray(), out var metadata), Is.True);
        Assert.That(metadata.TotalScore, Is.EqualTo(12345));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SchedulingAndNotificationFailuresDoNotFailAnImportedScore(bool failNotification)
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"schedule-failure-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var prepared = O2JamReplayPersistencePatch.PrepareForImport(score);
        importSavingScore(manager, score);
        var notices = new List<Notification>();
        manager.PostNotification = notice =>
        {
            notices.Add(notice);
            if (failNotification)
                throw new IOException("Simulated notification consumer failure.");
        };
        Assert.That(() => O2JamReplayPersistencePatch.CompleteImport(score, prepared, Task.CompletedTask, manager,
            _ => throw new IOException("Simulated scheduler failure.")).GetAwaiter().GetResult(), Throws.Nothing);
        Assert.That(notices.OfType<SimpleNotification>().Single().Text, Is.EqualTo(O2LazerStrings.ReplaySaveFailed));
        Assert.That(manager.GetScore(score.ScoreInfo)!.ScoreInfo.TotalScore, Is.EqualTo(12345));
        Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.Zero);
    }

    [Test]
    public void NativeFileWriteFailureRollsBackOnlyTheAttachment()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"attachment-failure-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var prepared = O2JamReplayPersistencePatch.PrepareForImport(score);
        importSavingScore(manager, score);
        var notices = new List<Notification>();
        manager.PostNotification = notices.Add;
        var filesDirectory = storage.GetFullPath("files");
        // This fixture owns a fresh, empty file store. A file at the directory path
        // reproduces a native I/O failure without patching ModelManager.AddFile itself.
        if (Directory.Exists(filesDirectory))
            Directory.Delete(filesDirectory);
        File.WriteAllText(filesDirectory, "block native file directory");
        try
        {
            Assert.That(() => O2JamReplayPersistencePatch.CompleteImport(score, prepared, Task.CompletedTask, manager, action => action()).GetAwaiter().GetResult(), Throws.Nothing);
            Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
            Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.Zero);
            Assert.That(score.ScoreInfo.Files, Is.Empty);
            Assert.That(manager.GetScore(score.ScoreInfo)!.ScoreInfo.TotalScore, Is.EqualTo(12345));
            Assert.That(notices.OfType<SimpleNotification>().Single().Text, Is.EqualTo(O2LazerStrings.ReplaySaveFailed));
        }
        finally
        {
            File.Delete(filesDirectory);
            Directory.CreateDirectory(filesDirectory);
        }
    }

    [Test]
    public void OriginalScoreImportFailureIsPreservedWithoutASavedNotification()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"original-failure-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var prepared = O2JamReplayPersistencePatch.PrepareForImport(score);
        var originalError = new IOException("Original native score import failed.");
        var notices = new List<Notification>();
        manager.PostNotification = notices.Add;
        var scheduled = false;
        var task = O2JamReplayPersistencePatch.CompleteImport(score, prepared, Task.FromException(originalError), manager, _ => scheduled = true);
        Assert.That(Assert.Throws<IOException>(() => task.GetAwaiter().GetResult()), Is.SameAs(originalError));
        Assert.That(scheduled, Is.False);
        Assert.That(notices, Is.Empty);
        Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.Zero);
    }

    [Test]
    public void InstalledPlayerPatchPreservesScoreWhenEncodingFails() => AssertPlayerEncodingFailureWithInstalledPatches();

    internal static void AssertPlayerEncodingFailureWithInstalledPatches()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"player-save-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        score.Replay.Frames.Clear();
        var notices = new List<Notification>();
        manager.PostNotification = notices.Add;
        using var drawable = new DrawableManiaRuleset(new ManiaRuleset(), new ManiaBeatmap(new StageDefinition(7)));
        using var player = new SavingTestPlayer();
        typeof(Player).GetProperty("scoreManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, manager);
        typeof(Player).GetProperty("DrawableRuleset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, drawable);
        var import = player.Save(score);
        player.Pump();
        import.GetAwaiter().GetResult();
        Assert.That(manager.GetScore(score.ScoreInfo)!.ScoreInfo.TotalScore, Is.EqualTo(12345));
        Assert.That(score.ScoreInfo.Files, Is.Empty);
        Assert.That(notices.OfType<SimpleNotification>().Single().Text, Is.EqualTo(O2LazerStrings.ReplaySaveFailed));
    }

    [Test]
    public void InstalledPlayerPatchDoesNotRewriteOrReimportAReplayScore() => AssertPlayerReplayGuardWithInstalledPatches();

    internal static void AssertPlayerReplayGuardWithInstalledPatches()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var score = createSavingScore(new BeatmapInfo(ruleset), ruleset);
        score.ScoreInfo.Hash = "existing-replay-hash";
        using var drawable = new DrawableManiaRuleset(new ManiaRuleset(), new ManiaBeatmap(new StageDefinition(7)));
        typeof(DrawableRuleset).GetProperty(nameof(DrawableRuleset.ReplayScore))!.SetValue(drawable, score);
        using var player = new SavingTestPlayer();
        typeof(Player).GetProperty("DrawableRuleset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, drawable);
        Assert.That(player.Save(score).IsCompletedSuccessfully, Is.True, "No manager was supplied: replay playback must never reach native import or attachment.");
        Assert.That(score.ScoreInfo.Hash, Is.EqualTo("existing-replay-hash"));
        Assert.That(score.ScoreInfo.Files, Is.Empty);
    }

    [Test]
    public void SavePrefixLeavesForeignScoresUntouched()
    {
        _ = new O2LazerRuleset();
        var score = new Score { ScoreInfo = new ScoreInfo { Ruleset = new ManiaRuleset().RulesetInfo } };
        var prefix = typeof(O2JamReplayPersistencePatch).GetMethod("prepareReplayArchive", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [null, score, null];
        Assert.That(() => prefix.Invoke(null, arguments), Throws.Nothing);
        Assert.That(arguments[2], Is.Null);
        Assert.That(score.ScoreInfo.Hash, Is.Empty);
    }

    [Test]
    public void EncodingFailurePreservesAnExistingHashAndReplayAttachment()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"existing-replay-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var valid = O2JamReplayPersistencePatch.PrepareForImport(score);
        importSavingScore(manager, score);
        O2JamReplayPersistencePatch.CompleteImport(score, valid, Task.CompletedTask, manager, action => action()).GetAwaiter().GetResult();
        var originalHash = score.ScoreInfo.Hash;
        var retry = new Score { ScoreInfo = score.ScoreInfo.DeepClone(), Replay = null! };
        retry.ScoreInfo.Files.Clear();
        var failed = O2JamReplayPersistencePatch.PrepareForImport(retry);
        Assert.That(retry.ScoreInfo.Hash, Is.EqualTo(originalHash));
        O2JamReplayPersistencePatch.CompleteImport(retry, failed, Task.CompletedTask, manager, action => action()).GetAwaiter().GetResult();
        Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
        Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.EqualTo(1));
        var restored = manager.GetScore(retry.ScoreInfo)!;
        Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
        using var exported = new MemoryStream();
        new LegacyScoreExporter(storage).ExportToStream(restored.ScoreInfo, exported, null);
        Assert.That(exported.ToArray(), Is.EqualTo(valid.Bytes));
    }
    [Test]
    public void PendingOriginalImportCannotAttachOrNotifyBeforeItSucceeds()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"pending-native-import-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var prepared = O2JamReplayPersistencePatch.PrepareForImport(score);
        importSavingScore(manager, score);
        var original = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduled = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new List<Notification>();
        manager.PostNotification = notices.Add;
        var attachment = O2JamReplayPersistencePatch.CompleteImport(score, prepared, original.Task, manager, action => scheduled.SetResult(action));
        Assert.That(scheduled.Task.IsCompleted, Is.False);
        Assert.That(notices, Is.Empty);
        Assert.That(score.ScoreInfo.Files, Is.Empty);
        original.SetResult();
        var callback = scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Assert.That(attachment.IsCompleted, Is.False);
        callback();
        attachment.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Assert.That(score.ScoreInfo.Files, Has.Count.EqualTo(1));
        Assert.That(notices, Is.Empty);
    }
    [Test]
    public void InstalledPlayerPatchSavesAReplayThroughNativeImportAndScheduler() => AssertPlayerReplaySavingWithInstalledPatches();

    internal static void AssertPlayerReplaySavingWithInstalledPatches()
    {
        using var update = new SavingUpdateThreadScope();
        using var storage = new TemporaryNativeStorage($"player-replay-save-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset);
        using var api = new DummyAPIAccess();
        var manager = new ScoreManager(rulesets, () => null!, storage, realm, api);
        var score = createSavingScore(createBudgetBeatmap(realm, ruleset), ruleset);
        var notices = new List<Notification>();
        manager.PostNotification = notices.Add;
        using var drawable = new DrawableManiaRuleset(new ManiaRuleset(), new ManiaBeatmap(new StageDefinition(7)));
        using var player = new SavingTestPlayer();
        typeof(Player).GetProperty("scoreManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, manager);
        typeof(Player).GetProperty("DrawableRuleset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, drawable);
        var import = player.Save(score);
        player.Pump();
        import.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        var restored = manager.GetScore(score.ScoreInfo)!;
        Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
        Assert.That(restored.ScoreInfo.Files.Single().File.Hash, Is.EqualTo(score.ScoreInfo.Hash));
        Assert.That(notices, Is.Empty);
    }
    private static Score createSavingScore(BeatmapInfo beatmap, RulesetInfo ruleset) => new()
    {
        ScoreInfo = new ScoreInfo(beatmap, ruleset, new RealmUser { OnlineID = -1, Username = "offline" })
        {
            TotalScore = 12345,
            TotalScoreWithoutMods = 12345,
            Accuracy = 0.9,
            MaxCombo = 3,
            Rank = ScoreRank.F,
            Statistics = new Dictionary<HitResult, int> { [HitResult.Meh] = 1 },
        },
        Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
    };

    private static void importSavingScore(ScoreManager manager, Score score)
    {
        var imported = manager.Import(score.ScoreInfo.DeepClone());
        Assert.That(imported, Is.Not.Null);
        imported!.PerformRead(model =>
        {
            score.ScoreInfo.ID = model.ID;
            score.ScoreInfo.Hash = model.Hash;
            score.ScoreInfo.Files.AddRange(model.Files.Detach());
        });
    }

    private sealed partial class SavingTestPlayer : Player
    {
        public Task Save(Score score) => base.ImportScore(score);
        public void Pump() => Scheduler.Update();
        protected override ResultsScreen CreateResults(ScoreInfo score) => throw new NotSupportedException();
    }

    private sealed class SavingUpdateThreadScope : IDisposable
    {
        private readonly bool previous = ThreadSafety.IsUpdateThread;
        private static readonly PropertyInfo updateProperty = typeof(ThreadSafety).GetProperty(nameof(ThreadSafety.IsUpdateThread))!;
        public SavingUpdateThreadScope() => updateProperty.SetValue(null, true);
        public void Dispose() => updateProperty.SetValue(null, previous);
    }
}