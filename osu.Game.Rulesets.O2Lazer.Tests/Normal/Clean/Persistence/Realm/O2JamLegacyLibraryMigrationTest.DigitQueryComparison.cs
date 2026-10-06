using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using Realms;
using System.Text.RegularExpressions;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    [Category("LocalDiagnostics")]
    [Category("Benchmark")]
    [NonParallelizable]
    [NUnit.Framework.Explicit("Requires user approval. Reads only a temporary copy of a stopped-client backup and at most 32 OJN inputs.")]
    public void CompareDigitQueriesOnReadOnlyClientBackup() => runProjectionTest((temporary, storage) =>
    {
        var backup = Environment.GetEnvironmentVariable("O2JAM_QUERY_BENCHMARK_REALM");
        var corpus = Environment.GetEnvironmentVariable("O2JAM_QUERY_BENCHMARK_CORPUS");
        if (string.IsNullOrWhiteSpace(backup) || string.IsNullOrWhiteSpace(corpus))
            Assert.Ignore("Set O2JAM_QUERY_BENCHMARK_REALM and O2JAM_QUERY_BENCHMARK_CORPUS after obtaining approval.");
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".artifacts", "client-backups"));
        var fullBackup = Path.GetFullPath(backup!);
        Assert.That(fullBackup.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True,
            "Only a stopped-client backup under this repository is accepted, never the installed database.");
        var corpusRoot = Path.GetFullPath(corpus!);
        var budget = Stopwatch.StartNew();
        using var backupGuard = new FileStream(fullBackup, FileMode.Open, FileAccess.Read, FileShare.Read);
        var originalHash = SHA256.HashData(backupGuard);
        backupGuard.Position = 0;
        var copy = storage.GetFullPath("query-backup.realm");
        using (var destination = File.Create(copy))
            backupGuard.CopyTo(destination);
        var copyHash = readonlyFileHash(copy);
        var schemaVersion = temporary.Run(database => database.Config.SchemaVersion);
        var registered = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        using (var database = Realm.GetInstance(new RealmConfiguration(copy) { IsReadOnly = true, SchemaVersion = schemaVersion }))
        {
            var ruleset = database.Find<RulesetInfo>(O2LazerIdentity.ShortName)!;
            Assert.That(ruleset, Is.Not.Null);
            Assert.That(database.Config.DatabasePath, Is.EqualTo(copy));
            var ownAll = database.All<BeatmapInfo>().Filter("Ruleset.ShortName == $0", O2LazerIdentity.ShortName);
            var nullParents = ownAll.Filter("BeatmapSet == NULL").Count();
            var activeSets = database.All<BeatmapSetInfo>().Filter("DeletePending == false AND ANY Beatmaps.Ruleset.ShortName == $0", O2LazerIdentity.ShortName).Count();
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"readonly lookup tables: sets={database.All<BeatmapSetInfo>().Count()}, deleted_sets={database.All<BeatmapSetInfo>().Count(set => set.DeletePending)}, beatmaps={database.All<BeatmapInfo>().Count()}, own_all={ownAll.Count()}, own_null_parents={nullParents}, own_active_sets={activeSets}"));
            var owned = database.All<BeatmapInfo>().Filter("Ruleset == $0 AND BeatmapSet != NULL AND BeatmapSet.DeletePending == false", ruleset);
            foreach (var beatmap in owned)
            {
                checkBudget();
                if (!O2JamLibraryWriter.tryGetSourcePath(beatmap, out var path))
                    continue;
                if (registered.TryGetValue(path, out var id) && id != beatmap.BeatmapSet!.ID)
                    Assert.Inconclusive("The backup contains conflicting path owners; query comparison must not assume uniqueness.");
                registered[path] = beatmap.BeatmapSet!.ID;
            }

            var knownPaths = registered.Keys.Where(path => withinCorpus(path) && File.Exists(path))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var nameOwned = database.All<BeatmapInfo>().Filter("Ruleset.ShortName == $0 AND BeatmapSet != NULL AND BeatmapSet.DeletePending == false", O2LazerIdentity.ShortName).Count();
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"readonly lookup preparation: owned_link={owned.Count()}, owned_name={nameOwned}, paths={registered.Count}, within_corpus={registered.Keys.Count(withinCorpus)}, readable={knownPaths.Length}"));
            if (knownPaths.Length < 16)
                Assert.Inconclusive("Fewer than 16 readable registered source paths are available.");
            var numericGroup = knownPaths.Where(path => Regex.IsMatch(Path.GetFileName(path), @"^o2ma[0-9]+\.ojn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .GroupBy(path => Path.GetFileName(path)[..^5], StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() == 10);
            if (numericGroup == null)
                Assert.Inconclusive("No complete readable decimal filename group is available in the backup.");
            var selected = numericGroup!.Concat(Enumerable.Range(0, 16).Select(index => knownPaths[index * knownPaths.Length / 16]))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
            var known = selected.Select(readRequest).ToArray();
            var fresh = new List<O2JamLibraryWriteRequest>();
            foreach (var path in Directory.EnumerateFiles(corpusRoot, "*.ojn", SearchOption.AllDirectories).Take(8192))
            {
                checkBudget();
                if (!string.Equals(Path.GetExtension(path), ".ojn", StringComparison.OrdinalIgnoreCase) || registered.ContainsKey(path))
                    continue;
                fresh.Add(readRequest(path));
                if (fresh.Count == 16)
                    break;
            }
            if (fresh.Count != 16)
                Assert.Inconclusive("Fewer than 16 unregistered OJN paths are available within the bounded enumeration.");

            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"readonly lookup dataset: sets={database.All<BeatmapSetInfo>().Count()}, beatmaps={database.All<BeatmapInfo>().Count()}, set_usages={database.All<BeatmapSetInfo>().AsEnumerable().Sum(set => set.Files.Count)}, owned_paths={registered.Count}, legacy_paths={owned.Filter("NOT Metadata.AudioFile ENDSWITH[c] $0", ".ojn").Count()}, preparation={budget.Elapsed.TotalSeconds:F1}s"));
            foreach (var requests in new[] { fresh.ToArray(), known })
            {
                var isKnown = ReferenceEquals(requests, known);
                var samples = new[] { new List<double>(), new List<double>() };
                for (var iteration = -1; iteration < 3; iteration++)
                {
                    (Guid? Path, Guid? Content, bool SetHash)[]? baseline = null;
                    (Guid? Path, Guid? Content, bool SetHash)[]? current = null;
                    for (var offset = 0; offset < 2; offset++)
                    {
                        checkBudget();
                        var shape = (offset + Math.Max(0, iteration)) % 2;
                        var timer = Stopwatch.StartNew();
                        if (shape == 0)
                            baseline = digitBaselineLookup(database, requests);
                        else
                        {
                            var lookup = new O2JamLibraryLookup(database, requests);
                            if (isKnown && lookup.PathWildcardGroupCount == 0)
                                Assert.Inconclusive("The selected case did not activate grouping.");
                            if (iteration == -1)
                                TestContext.Out.WriteLine($"digit comparison case={(isKnown ? "existing-group" : "unregistered-paths")}, predicates={lookup.PathPredicateCount}, groups={lookup.PathWildcardGroupCount}");
                            current = requests.Select(request => (lookup.FindPath(request.Plan.SourcePath)?.ID,
                                lookup.FindContent(request.Plan.SourceHash)?.ID, lookup.ContainsSetHash(request.Plan.SetHash))).ToArray();
                        }
                        timer.Stop();
                        if (iteration >= 0)
                            samples[shape].Add(timer.Elapsed.TotalMilliseconds);
                    }
                    Assert.That(current, Is.EqualTo(baseline), "Path, content and Hash results must agree before comparing speed.");
                    Assert.That(current!.Select(result => result.Path).ToArray(), Is.EqualTo(requests
                        .Select(request => registered.TryGetValue(request.Plan.SourcePath, out var id) ? (Guid?)id : null).ToArray()));
                }
                for (var shape = 0; shape < 2; shape++)
                    TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"readonly lookup case={(isKnown ? "existing" : "unregistered-paths")}, shape={(shape == 0 ? "native-link-v1" : "native-link-digit-v2")}, sources=16, samples={string.Join(",", samples[shape].Select(value => value.ToString("F1", CultureInfo.InvariantCulture)))}, median={samples[shape].Order().ElementAt(1):F1} ms"));
            }
        }
        Assert.That(readonlyFileHash(copy), Is.EqualTo(copyHash));
        backupGuard.Position = 0;
        Assert.That(SHA256.HashData(backupGuard), Is.EqualTo(originalHash));

        bool withinCorpus(string path) => path.StartsWith(corpusRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        O2JamLibraryWriteRequest readRequest(string path)
        {
            checkBudget();
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
                Assert.Inconclusive("A selected OJN exceeds the 16 MiB input bound.");
            var bytes = File.ReadAllBytes(path);
            // Encoding is irrelevant to identity; a fixed choice avoids directory sampling.
            var document = new OjnReader(OjnMetadataEncoding.Utf8).Read(bytes);
            var hashes = document.Charts.Where(chart => chart.Notes.Any(note => note.IsPlayable))
                .Select(chart => O2JamBeatmapIdentity.Md5FromSource(bytes, chart.Difficulty.ToGameplay())).ToArray();
            Assert.That(hashes, Is.Not.Empty);
            var plan = new O2JamImportPlan(path, Path.GetDirectoryName(path)!, Path.GetFileName(path), [],
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), O2JamBeatmapIdentity.SetFromMd5Hashes(hashes),
                0, string.Empty, string.Empty, string.Empty, 120, [], []);
            return new O2JamLibraryWriteRequest(plan, registered.TryGetValue(path, out var id) ? id : null);
        }

        void checkBudget()
        {
            if (budget.Elapsed > TimeSpan.FromSeconds(120))
                Assert.Inconclusive("Read-only comparison exceeded its 120-second budget; no further query was started.");
        }
    });

    private static (Guid? Path, Guid? Content, bool SetHash)[] digitBaselineLookup(Realm database, O2JamLibraryWriteRequest[] requests)
    {
        var lookup = new FrozenNativeLinkLookup(database, requests);
        return requests.Select(request => (lookup.FindPath(request.Plan.SourcePath)?.ID,
            lookup.FindContent(request.Plan.SourceHash)?.ID, lookup.ContainsSetHash(request.Plan.SetHash))).ToArray();
    }
}