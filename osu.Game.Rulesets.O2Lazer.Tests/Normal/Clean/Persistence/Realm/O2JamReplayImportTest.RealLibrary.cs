using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Formats.Ojm;
using O2Jam.Formats.Ojn;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamReplayImportTest
{
    [TestCase("SongA", "o2ma100.ojn")]
    [TestCase("SongC", "o2ma3267.ojn")]
    [TestCase("o2ma1079", "o2ma1079.ojn")]
    [Category("LocalDiagnostics")]
    [Explicit("Fully decodes three configured external OJN files and bounded OJM references; writes only a temporary native library.")]
    public void BoundedRealSourcesSurviveRefreshMediaAndFormalReplayRestart(string directory, string filename)
    {
        var corpus = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(corpus))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to run the bounded temporary-library check.");
        var path = Path.GetFullPath(Path.Combine(corpus!, directory, filename));
        Assert.That(File.Exists(path), Is.True);
        var externalBytes = File.ReadAllBytes(path);
        var externalStamp = File.GetLastWriteTimeUtc(path);
        var document = new OjnReader().Read(externalBytes);
        var planner = new O2JamImportPlanner();
        var plan = planner.Create(path);
        using var storage = new TemporaryNativeStorage($"real-library-{Guid.NewGuid():N}");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        Assert.That(O2JamReplayPersistencePatch.InstallOnce(), Is.True);
        var savedScores = new List<Guid>();
        BeatmapInfo[] beatmaps;
        using (var realm = new RealmAccess(storage, "client.realm"))
        {
            realm.Write(database => database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID) { Available = true }));
            var writer = new O2JamLibraryWriter(realm, storage);
            var service = new O2JamImportService(planner, writer);
            Assert.That(service.Refresh([path], writer.GetImportedSources()).Imported, Is.EqualTo(1));
            Assert.That(service.Refresh([path], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(1));
            beatmaps = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.Detach()).ToArray());
            Assert.That(beatmaps, Has.Length.EqualTo(plan.Charts.Count));
            var importer = new TestScoreImporter(rulesets, storage, realm);
            foreach (var beatmap in beatmaps)
            {
                var chart = plan.Charts.Single(candidate => candidate.Md5Hash == beatmap.MD5Hash);
                var playable = new OjnBeatmapFactory().Create(document, chart.Difficulty);
                Assert.That(playable.HitObjects, Has.Count.EqualTo(chart.TotalObjectCount));
                var first = playable.HitObjects[0];
                var score = new Score
                {
                    ScoreInfo = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = "Temporary archive test" })
                    {
                        BeatmapHash = beatmap.Hash,
                        TotalScore = 123456,
                        Accuracy = 0.9,
                        MaxCombo = 42,
                    },
                    Replay = new Replay
                    {
                        Frames = [new O2JamReplayFrame(first.StartTime, (ManiaAction)((int)ManiaAction.Key1 + first.Column)),
                                  new O2JamReplayFrame(first.StartTime + 5)],
                    },
                };
                using var archive = new ByteArrayArchiveReader(O2JamReplayArchive.Create(score), "formal.osr");
                var imported = importer.ReadHeaders(archive);
                Assert.That(imported, Is.Not.Null);
                Assert.That(imported!.BeatmapInfo!.ID, Is.EqualTo(beatmap.ID));
                savedScores.Add(importer.StoreReplay(imported, O2JamReplayArchive.Create(score)));
            }
            assertRealMedia(realm, storage, plan, document);
        }

        using (var restarted = new RealmAccess(storage, "client.realm"))
        {
            var writer = new O2JamLibraryWriter(restarted, storage);
            var service = new O2JamImportService(planner, writer);
            Assert.That(service.Refresh([path], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(1));
            Assert.That(writer.GetImportedSources()[path].CanReuseFiles, Is.True);
            Assert.That(restarted.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.ID).ToArray()),
                Is.EquivalentTo(beatmaps.Select(beatmap => beatmap.ID)));
            var importer = new TestScoreImporter(rulesets, storage, restarted);
            foreach (var id in savedScores)
            {
                var stored = restarted.Run(database => database.Find<ScoreInfo>(id)!.Detach());
                Assert.That(stored.BeatmapInfo, Is.Not.Null);
                Assert.That(stored.BeatmapHash, Is.EqualTo(stored.BeatmapInfo!.Hash));
                var replay = importer.GetScore(stored);
                Assert.That(replay.Replay.Frames, Has.Count.EqualTo(2));
                using var exported = new ByteArrayArchiveReader(O2JamReplayArchive.Create(replay), "exported.osr");
                Assert.That(importer.ReadHeaders(exported)!.BeatmapInfo!.ID, Is.EqualTo(stored.BeatmapInfo.ID));
                Assert.That(replay.ScoreInfo.TotalScore, Is.EqualTo(123456));
            }
            assertRealMedia(restarted, storage, plan, document);
        }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(externalBytes));
        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(externalStamp));
        TestContext.Progress.WriteLine($"{directory}/{filename}: {plan.Charts.Count} charts, {savedScores.Count} formal v5 replay imports/exports, native restart and media references passed.");
    }

    private static void assertRealMedia(RealmAccess realm, osu.Framework.Platform.Storage storage, O2JamImportPlan plan, OjnDocument document)
    {
        var store = new RealmFileStore(realm, storage);
        var snapshot = realm.Run(database =>
        {
            var set = database.All<BeatmapSetInfo>().Single();
            return (Files: set.Files.Select(usage => (usage.Filename, usage.File.Hash)).ToArray(), Background: set.Metadata.BackgroundFile);
        });
        foreach (var usage in snapshot.Files)
        {
            using var stream = store.Store.GetStream(new RealmFile { Hash = usage.Hash }.GetStoragePath());
            Assert.That(stream, Is.Not.Null);
            Assert.That(stream.ComputeSHA2Hash(), Is.EqualTo(usage.Hash));
        }
        if (plan.Background.Length > 0)
        {
            var cover = snapshot.Files.Single(usage => usage.Filename == snapshot.Background);
            using var textures = new TextureLoaderStore(store.Store);
            using var texture = textures.Get(new RealmFile { Hash = cover.Hash }.GetStoragePath());
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture!.Width, Is.GreaterThan(0));
            Assert.That(texture.Height, Is.GreaterThan(0));
        }
        Assert.That(O2JamExternalChart.TryResolveResource(plan.SourcePath, document.Metadata.OjmFileName, out var archivePath), Is.True);
        var reader = new OjmReader();
        using var archiveStream = File.OpenRead(archivePath);
        var index = reader.ReadIndex(archiveStream);
        Assert.That(index.SampleIds, Is.Not.Empty);
        // Exercise both authored audio layers without loading an unbounded sample archive.
        var referenced = plan.Charts.SelectMany(chart => O2JamPreviewSchedule.Create(new OjnBeatmapFactory().Create(document, chart.Difficulty), true).PreviewEvents)
                             .Where(evt => evt.Volume > 0 && index.SampleIds.Contains(evt.SampleId)).GroupBy(evt => evt.IsKeySound)
                             .SelectMany(group => group.Select(evt => evt.SampleId).Distinct().Take(4)).ToHashSet();
        Assert.That(referenced, Is.Not.Empty);
        var archive = reader.ReadLazy(archivePath, referenced);
        foreach (var id in referenced)
        {
            using var sample = archive.Samples[id].OpenRead();
            Assert.That(sample.Length, Is.GreaterThan(0));
        }
        TestContext.Progress.WriteLine($"{plan.FileName}: cover {snapshot.Background}, {index.SampleIds.Count} indexed OJM samples; {referenced.Count} referenced payloads read.");
    }
}
