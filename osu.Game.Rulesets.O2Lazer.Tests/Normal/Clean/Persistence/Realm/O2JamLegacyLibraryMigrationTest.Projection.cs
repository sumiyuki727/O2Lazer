using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void ProjectionRefreshRepairsFieldsAndMediaWithoutReplacingUserRecords() => runProjectionTest((realm, storage) =>
    {
        var sourcePath = storage.GetFullPath("projection.ojn");
        byte[] image = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10];
        var bytes = OjnTestData.CreateChart();
        BitConverter.GetBytes((uint)image.Length).CopyTo(bytes, 268);
        File.WriteAllBytes(sourcePath, [.. bytes, .. image]);
        var plan = new O2JamImportPlanner().Create(sourcePath);
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.Imported));
        var original = realm.Run(database => database.All<BeatmapInfo>().Single().Detach());
        var setId = original.BeatmapSet!.ID;
        var dateAdded = original.BeatmapSet.DateAdded;
        var scoreId = Guid.NewGuid();

        realm.Write(database =>
        {
            var beatmap = database.Find<BeatmapInfo>(original.ID)!;
            beatmap.Metadata.Title = "Stale";
            beatmap.Metadata.Artist = "Stale";
            beatmap.Metadata.Author.Username = "Stale";
            beatmap.Metadata.TitleUnicode = "User Unicode";
            beatmap.Metadata.Tags += " keep-user-tag";
            beatmap.Metadata.Tags = beatmap.Metadata.Tags.Replace("projection:1:20261001", "projection:1:20260930");
            beatmap.DifficultyName = "Edited name";
            beatmap.Difficulty.CircleSize = 4;
            beatmap.Difficulty.OverallDifficulty = 1;
            beatmap.Difficulty.DrainRate = 1;
            beatmap.Difficulty.ApproachRate = 1;
            beatmap.Length = 999;
            beatmap.BPM = 1;
            beatmap.TotalObjectCount = 99;
            beatmap.EndTimeObjectCount = 99;
            beatmap.StarRating = 99;
            beatmap.Metadata.BackgroundFile = "stale.jpg";
            beatmap.Metadata.PreviewTime = 10;
            beatmap.BeatmapSet!.Files.Remove(beatmap.BeatmapSet.Files.Single(file => file.Filename.EndsWith(".png")));
            beatmap.UserSettings.Offset = 12.5;
            beatmap.Hidden = true;
            beatmap.BeatDivisor = 8;
            database.Add(new ScoreInfo(beatmap, beatmap.Ruleset, new RealmUser { Username = "Test" })
            {
                ID = scoreId,
                BeatmapHash = beatmap.Hash,
                TotalScore = 12345,
            });
            var collection = new BeatmapCollection { Name = "User collection" };
            collection.BeatmapMD5Hashes.Add(beatmap.MD5Hash);
            database.Add(collection);
        });

        var notifications = 0;
        writer.BeatmapUpdated += _ => notifications++;
        Assert.That(writer.GetImportedSources()[sourcePath].HasCurrentMetadata, Is.False);
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        realm.Run(database =>
        {
            var beatmap = database.Find<BeatmapInfo>(original.ID)!;
            var score = database.Find<ScoreInfo>(scoreId)!;
            Assert.Multiple(() =>
            {
                Assert.That(beatmap.BeatmapSet!.ID, Is.EqualTo(setId));
                Assert.That(beatmap.BeatmapSet.DateAdded, Is.EqualTo(dateAdded));
                Assert.That(beatmap.Hash, Is.EqualTo(original.Hash));
                Assert.That(beatmap.MD5Hash, Is.EqualTo(original.MD5Hash));
                Assert.That(beatmap.Metadata.Title, Is.EqualTo(original.Metadata.Title));
                Assert.That(beatmap.Metadata.Artist, Is.EqualTo(original.Metadata.Artist));
                Assert.That(beatmap.Metadata.Author.Username, Is.EqualTo(original.Metadata.Author.Username));
                Assert.That(beatmap.Metadata.TitleUnicode, Is.EqualTo("User Unicode"));
                Assert.That(beatmap.Metadata.Tags, Does.Contain("keep-user-tag"));
                Assert.That(beatmap.DifficultyName, Is.EqualTo(original.DifficultyName));
                Assert.That(O2JamStarRatingMetadata.ResolveLevel(beatmap), Is.EqualTo(5));
                Assert.That(beatmap.Difficulty.CircleSize, Is.EqualTo(original.Difficulty.CircleSize));
                Assert.That(beatmap.Difficulty.OverallDifficulty, Is.EqualTo(original.Difficulty.OverallDifficulty));
                Assert.That(beatmap.Difficulty.DrainRate, Is.EqualTo(original.Difficulty.DrainRate));
                Assert.That(beatmap.Difficulty.ApproachRate, Is.EqualTo(original.Difficulty.ApproachRate));
                Assert.That(beatmap.Length, Is.EqualTo(original.Length));
                Assert.That(beatmap.BPM, Is.EqualTo(original.BPM));
                Assert.That(beatmap.TotalObjectCount, Is.EqualTo(original.TotalObjectCount));
                Assert.That(beatmap.EndTimeObjectCount, Is.EqualTo(original.EndTimeObjectCount));
                Assert.That(beatmap.StarRating, Is.EqualTo(original.StarRating));
                Assert.That(beatmap.LastLocalUpdate, Is.EqualTo(plan.SourceTimestamp));
                Assert.That(beatmap.Metadata.BackgroundFile, Is.EqualTo("o2jam-background.png"));
                Assert.That(beatmap.BeatmapSet.Files.Count(file => file.Filename == "o2jam-background.png"), Is.EqualTo(1));
                Assert.That(beatmap.Metadata.PreviewTime, Is.Zero);
                Assert.That(beatmap.UserSettings.Offset, Is.EqualTo(12.5));
                Assert.That(beatmap.Hidden, Is.True);
                Assert.That(beatmap.BeatDivisor, Is.EqualTo(8));
                Assert.That(score.BeatmapInfo!.ID, Is.EqualTo(original.ID));
                Assert.That(score.BeatmapHash, Is.EqualTo(original.Hash));
                Assert.That(score.TotalScore, Is.EqualTo(12345));
                Assert.That(database.All<BeatmapCollection>().Single().BeatmapMD5Hashes, Is.EqualTo(new[] { original.MD5Hash }));
            });
        });
        Assert.That(writer.GetImportedSources()[sourcePath].HasCurrentMetadata, Is.True);
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
        Assert.That(notifications, Is.EqualTo(1));

        // Removing an obsolete generated cover only releases our native usage.
        Assert.That(writer.Write(plan with { Background = [] }), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        realm.Run(database =>
        {
            var beatmap = database.Find<BeatmapInfo>(original.ID)!;
            Assert.That(beatmap.Metadata.BackgroundFile, Is.Empty);
            Assert.That(beatmap.BeatmapSet!.Files.Select(file => file.Filename), Is.EqualTo(new[] { plan.FileName }));
        });
    });

    [Test]
    public void FutureOrConflictingProjectionIsNotOverwritten() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("protected.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var original = realm.Run(database => database.All<BeatmapInfo>().Single().Detach());
        var notifications = 0;
        writer.BeatmapUpdated += _ => notifications++;
        foreach (var tags in new[]
                 {
                     original.Metadata.Tags.Replace("projection:1:20261001", "projection:2:20261001"),
                     original.Metadata.Tags + " o2lazer-level:1:5",
                     original.Metadata.Tags.Replace("chart:1:0", "chart:1:1"),
                 })
        {
            realm.Write(database => database.Find<BeatmapInfo>(original.ID)!.Metadata.Tags = tags);
            Assert.That(writer.GetImportedSources()[path].HasCurrentMetadata, Is.False);
            Assert.Throws<InvalidDataException>(() => writer.Write(plan));
            realm.Run(database =>
            {
                Assert.That(database.All<BeatmapInfo>().Count(), Is.EqualTo(1));
                var beatmap = database.Find<BeatmapInfo>(original.ID)!;
                Assert.That(beatmap.Metadata.Tags, Is.EqualTo(tags));
                Assert.That(beatmap.Hash, Is.EqualTo(original.Hash));
                Assert.That(beatmap.BeatmapSet!.DeletePending, Is.False);
            });
        }
        Assert.That(notifications, Is.Zero);
    });

    [Test]
    public void ChangedSourceAfterPreparationCannotBePublishedWithTheOldTimestamp() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("changed.ojn");
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(path, bytes);
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        BitConverter.GetBytes(180f).CopyTo(bytes, 16);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, plan.SourceTimestamp!.Value.UtcDateTime);
        Assert.Throws<IOException>(() => writer.Write(plan));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.Zero);
        Assert.That(writer.Write(new O2JamImportPlanner().Create(path)), Is.EqualTo(O2JamLibraryWriteResult.Imported));

        plan.SourceData[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => writer.Write(plan));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(1));
    });

    private static void runProjectionTest(Action<RealmAccess, Storage> work)
    {
        using var host = new TestRunHeadlessGameHost($"projection-{Guid.NewGuid():N}");
        Exception? failure = null;
        host.Run(new MigrationTestGame(() =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"projection-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var info = new O2LazerRuleset().RulesetInfo;
                realm.Write(database => database.Add(new RulesetInfo(info.ShortName, info.Name, info.InstantiationInfo, info.OnlineID) { Available = true }));
                work(realm, storage);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            return Task.CompletedTask;
        }));
        if (failure != null)
            throw failure;
    }
}
