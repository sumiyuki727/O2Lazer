using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjmArchiveCacheTest
{
    [Test]
    public void ReusesUnchangedArchiveAndInvalidatesChangedFile()
    {
        var directory = createDirectory();

        try
        {
            var source = createFile(directory, "chart.ojn", [1]);
            var archivePath = createFile(directory, "song.ojm", [1]);
            var loadCount = 0;
            var cache = new OjmArchiveCache(3, 1024, (_, ids) =>
            {
                loadCount++;
                return createArchive(ids!);
            });
            var ids = new HashSet<int> { 1 };

            var first = cache.Get(source, archivePath, ids);
            var second = cache.Get(source, archivePath, ids);
            File.WriteAllBytes(archivePath, [1, 2]);
            var changed = cache.Get(source, archivePath, ids);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.SameAs(first));
                Assert.That(changed, Is.Not.SameAs(first));
                Assert.That(loadCount, Is.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void EntryLimitDoesNotRetainEveryVisitedSong()
    {
        var directory = createDirectory();

        try
        {
            var sourceA = createFile(directory, "a.ojn", [1]);
            var archiveA = createFile(directory, "a.ojm", [1]);
            var sourceB = createFile(directory, "b.ojn", [1]);
            var archiveB = createFile(directory, "b.ojm", [1]);
            var loadCount = 0;
            var cache = new OjmArchiveCache(1, 1024, (_, ids) =>
            {
                loadCount++;
                return createArchive(ids!);
            });
            var ids = new HashSet<int> { 1 };

            cache.Get(sourceA, archiveA, ids);
            cache.Get(sourceB, archiveB, ids);
            cache.Get(sourceA, archiveA, ids);

            Assert.That(loadCount, Is.EqualTo(3));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void RetainsDifferentDifficultySubsetsForSameSong()
    {
        var directory = createDirectory();

        try
        {
            var source = createFile(directory, "chart.ojn", [1]);
            var archivePath = createFile(directory, "song.ojm", [1]);
            var loadCount = 0;
            var cache = new OjmArchiveCache(3, 1024, (_, ids) =>
            {
                loadCount++;
                return createArchive(ids!);
            });
            var ex = new HashSet<int> { 1, 2 };
            var nx = new HashSet<int> { 2, 3 };

            var firstEx = cache.Get(source, archivePath, ex);
            var firstNx = cache.Get(source, archivePath, nx);
            var secondEx = cache.Get(source, archivePath, ex);

            Assert.Multiple(() =>
            {
                Assert.That(secondEx, Is.SameAs(firstEx));
                Assert.That(firstNx, Is.Not.SameAs(firstEx));
                Assert.That(loadCount, Is.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void FullArchiveIndexIsSharedAcrossDifficulties()
    {
        var directory = createDirectory();

        try
        {
            var source = createFile(directory, "chart.ojn", [1]);
            var archivePath = createFile(directory, "song.ojm", [1]);
            var loadCount = 0;
            var cache = new OjmArchiveCache(3, 1024, (_, ids) =>
            {
                Assert.That(ids, Is.Null);
                loadCount++;
                return createArchive([1, 2, 3]);
            });

            var firstDifficulty = cache.GetAll(source, archivePath);
            var secondDifficulty = cache.GetAll(source, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(secondDifficulty, Is.SameAs(firstDifficulty));
                Assert.That(firstDifficulty.Samples.Keys, Is.EquivalentTo(new[] { 1, 2, 3 }));
                Assert.That(loadCount, Is.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static OjmArchive createArchive(IEnumerable<int> ids)
    {
        var samples = new Dictionary<int, OjmSample>();
        foreach (var id in ids)
            samples[id] = new OjmSample(id, id.ToString(), ".ogg", [1]);
        return new OjmArchive(samples);
    }

    private static string createDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"o2lazer-ojm-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string createFile(string directory, string name, byte[] data)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, data);
        return path;
    }
}
