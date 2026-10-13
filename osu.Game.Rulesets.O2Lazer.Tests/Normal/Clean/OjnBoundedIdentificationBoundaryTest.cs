using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class OjnBoundedIdentificationBoundaryTest
{
    [Test]
    public void HotContextsAndDocumentsNeverEnumerateTheDirectoryOrReopenHeaders()
    {
        using var pack = new Pack();
        var enumerations = 0;
        var opens = 0;
        var resolver = new OjnDirectoryEncoding(path => { Interlocked.Increment(ref opens); return File.OpenRead(path); }, directory =>
        {
            enumerations++;
            return Directory.EnumerateFiles(directory);
        });
        var cache = new OjnDocumentCache(resolver);
        var document = cache.Get(pack.Chart, O2JamDifficulty.EX);
        var context = resolver.GetContext(pack.Chart);
        Assert.That(document.Metadata.Title, Is.EqualTo("蛇神"));
        for (var index = 0; index < 4; index++)
        {
            Assert.That(resolver.GetContext(pack.Chart), Is.SameAs(context));
            Assert.That(resolver.IsCurrent(context), Is.True);
            Assert.That(cache.Get(pack.Chart, O2JamDifficulty.EX), Is.SameAs(document));
            Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, document), Is.True);
        }
        Assert.That(enumerations, Is.EqualTo(1));
        Assert.That(opens, Is.EqualTo(5));
    }

    [TestCase(0, OjnMetadataEncoding.Automatic)]
    [TestCase(3, OjnMetadataEncoding.Automatic)]
    [TestCase(4, OjnMetadataEncoding.Cp949)]
    public void CompleteAutomaticAndDecidedVotesAreSessionCacheable(int evidence, OjnMetadataEncoding expected)
    {
        using var pack = new Pack(evidence);
        var resolver = new OjnDirectoryEncoding();
        var first = resolver.GetContext(pack.Chart);
        Assert.That(first.Encoding, Is.EqualTo(expected));
        Assert.That(first.Reusable, Is.True);
        Assert.That(resolver.GetContext(pack.Chart), Is.SameAs(first));
    }

    [Test]
    public void SamplingCapAndSpreadRemainUnchanged()
    {
        using var pack = new Pack(100);
        var opened = new System.Collections.Concurrent.ConcurrentBag<string>();
        var resolver = new OjnDirectoryEncoding(path => { opened.Add(path); return File.OpenRead(path); });
        Assert.That(resolver.GetForFile(pack.Chart), Is.EqualTo(OjnMetadataEncoding.Cp949));
        var names = Directory.GetFiles(pack.Root).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.That(opened, Is.EquivalentTo(Enumerable.Range(0, 96).Select(index => names[(int)((long)index * (names.Length - 1) / 95)])));
        Assert.That(resolver.GetForFile(pack.Chart), Is.EqualTo(OjnMetadataEncoding.Cp949));
        Assert.That(opened.Count, Is.EqualTo(96));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OtherSampleEditsWithStableDirectoryStampWaitForExplicitRefresh(bool restoreSourceStamps)
    {
        using var pack = new Pack();
        var resolver = new OjnDirectoryEncoding();
        var cache = new OjnDocumentCache(resolver);
        var first = cache.Get(pack.Chart, O2JamDifficulty.EX);
        var context = resolver.GetContext(pack.Chart);
        pack.MakeChinese(restoreSourceStamps);
        Assert.That(resolver.GetContext(pack.Chart), Is.SameAs(context));
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, first), Is.True);
        Assert.That(cache.Get(pack.Chart, O2JamDifficulty.EX), Is.SameAs(first));
        resolver.Clear();
        Assert.That(context.Active, Is.False);
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, first), Is.False);
        var next = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(next, Is.Not.SameAs(first));
        Assert.That(resolver.GetForFile(pack.Chart), Is.EqualTo(OjnMetadataEncoding.Gbk));
        Assert.That(next.Metadata.Title, Is.Not.EqualTo(first.Metadata.Title));
    }

    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("rename")]
    public void NormalDirectoryChangesRejectReuseWithoutDoingHeaderInspection(string change)
    {
        using var pack = new Pack();
        var opens = 0;
        var resolver = new OjnDirectoryEncoding(path => { Interlocked.Increment(ref opens); return File.OpenRead(path); });
        var first = resolver.GetContext(pack.Chart);
        var stamp = Directory.GetLastWriteTimeUtc(pack.Root);
        changeInventory(pack, change);
        Directory.SetLastWriteTimeUtc(pack.Root, stamp.AddSeconds(2));
        Assert.That(resolver.IsCurrent(first), Is.False);
        Assert.That(first.Active, Is.False);
        Assert.That(opens, Is.EqualTo(5));
        var next = resolver.GetContext(pack.Chart);
        Assert.That(next, Is.Not.SameAs(first));
        Assert.That(resolver.IsCurrent(next), Is.True);
    }

    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("rename")]
    public void RestoredDirectoryStampsHaveTheDocumentedNativeRefreshBoundary(string change)
    {
        using var pack = new Pack();
        var resolver = new OjnDirectoryEncoding();
        var first = resolver.GetContext(pack.Chart);
        var stamp = Directory.GetLastWriteTimeUtc(pack.Root);
        changeInventory(pack, change);
        Directory.SetLastWriteTimeUtc(pack.Root, stamp);
        Assert.That(resolver.GetContext(pack.Chart), Is.SameAs(first));
        Assert.That(resolver.IsCurrent(first), Is.True);
        resolver.Clear();
        Assert.That(resolver.GetContext(pack.Chart), Is.Not.SameAs(first));
    }

    [TestCase("ascii")]
    [TestCase("bom")]
    [TestCase("evidence")]
    public void UnambiguousFieldsDoNotRequestAnyDirectoryContext(string type)
    {
        using var pack = new Pack();
        var bytes = OjnTestData.CreateChart();
        if (type == "bom") Pack.SetField(bytes, 108, 64, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("中文 테스트")]);
        if (type == "evidence") Pack.SetField(bytes, 108, 64, Encoding.GetEncoding(949).GetBytes("あなた"));
        pack.Write("clear.ojn", bytes);
        var resolver = new OjnDirectoryEncoding(_ => throw new AssertionException("Unexpected header open."),
            _ => throw new AssertionException("Unexpected directory enumeration."));
        var cache = new OjnDocumentCache(resolver);
        var path = Path.Combine(pack.Root, "clear.ojn");
        var first = cache.Get(path, O2JamDifficulty.EX);
        Assert.That(cache.Get(path, O2JamDifficulty.EX), Is.SameAs(first));
        Assert.That(cache.IsCurrent(path, O2JamDifficulty.EX, first), Is.True);
    }

    [Test]
    public void PartialHeaderFailureIsNotRetainedInContextOrDocumentCaches()
    {
        using var pack = new Pack();
        var fail = true;
        var resolver = new OjnDirectoryEncoding(path =>
        {
            if (fail && Path.GetFileName(path) == "context0000.ojn") throw new UnauthorizedAccessException();
            return File.OpenRead(path);
        });
        var cache = new OjnDocumentCache(resolver);
        var first = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, first), Is.False);
        fail = false;
        var next = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(next, Is.Not.SameAs(first));
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, next), Is.True);
        Assert.That(resolver.GetForFile(pack.Chart), Is.EqualTo(OjnMetadataEncoding.Cp949));
    }

    [Test]
    public void SourceDirectoryFailureCanRecoverWithoutExplicitClear()
    {
        using var pack = new Pack();
        var missing = Path.Combine(pack.Root, "not-created", "chart.ojn");
        var resolver = new OjnDirectoryEncoding();
        var result = resolver.GetContext(missing);
        Assert.That(result.Encoding, Is.EqualTo(OjnMetadataEncoding.Automatic));
        Assert.That(result.Reusable, Is.False);
        Directory.CreateDirectory(Path.GetDirectoryName(missing)!);
        File.Copy(pack.Chart, missing);
        Assert.That(resolver.GetContext(missing).Reusable, Is.True);
    }

    [Test]
    public void OrdinarySelectedSourceEditsInvalidateWithoutAWholeDirectoryScan()
    {
        using var pack = new Pack();
        var scans = 0;
        var resolver = new OjnDirectoryEncoding(enumerateFiles: directory => { scans++; return Directory.EnumerateFiles(directory); });
        var cache = new OjnDocumentCache(resolver);
        var first = cache.Get(pack.Chart, O2JamDifficulty.EX);
        var sourceStamp = File.GetLastWriteTimeUtc(pack.Chart);
        var directoryStamp = Directory.GetLastWriteTimeUtc(pack.Root);
        pack.Write(Path.GetFileName(pack.Chart), Pack.Bytes("[国服]莎娜塔", 936));
        File.SetLastWriteTimeUtc(pack.Chart, sourceStamp.AddSeconds(2));
        Directory.SetLastWriteTimeUtc(pack.Root, directoryStamp);
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, first), Is.False);
        var next = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(next, Is.Not.SameAs(first));
        Assert.That(next.Metadata.Title, Is.EqualTo("[国服]莎娜塔"));
        Assert.That(scans, Is.EqualTo(1));
    }

    [Test]
    public void ChangedSamplesDuringInspectionRetryOnceBeforePublishingVotes()
    {
        using var pack = new Pack();
        var opens = 0;
        var changed = 0;
        var resolver = new OjnDirectoryEncoding(path =>
        {
            Interlocked.Increment(ref opens);
            var bytes = File.ReadAllBytes(path);
            if (Interlocked.Exchange(ref changed, 1) == 0) pack.MakeChinese(false);
            return new MemoryStream(bytes);
        });
        Assert.That(resolver.GetForFile(pack.Chart), Is.EqualTo(OjnMetadataEncoding.Gbk));
        Assert.That(opens, Is.EqualTo(10));
    }

    [Test]
    public void ContinuouslyChangingSamplesHaveBoundedRetriesAndNoReusableResult()
    {
        using var pack = new Pack();
        var opens = 0;
        var resolver = new OjnDirectoryEncoding(path =>
        {
            Interlocked.Increment(ref opens);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));
            return File.OpenRead(path);
        });
        var result = resolver.GetContext(pack.Chart);
        Assert.That(result.Encoding, Is.EqualTo(OjnMetadataEncoding.Automatic));
        Assert.That(result.Reusable, Is.False);
        Assert.That(opens, Is.EqualTo(10));
    }

    [Test]
    [CancelAfter(15000)]
    public void ConcurrentLoadsShareOneInspection()
    {
        using var pack = new Pack();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var opens = 0;
        var scans = 0;
        var resolver = new OjnDirectoryEncoding(path =>
        {
            if (Interlocked.Increment(ref opens) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new AssertionException("Load was not released.");
            }
            return File.OpenRead(path);
        }, directory => { Interlocked.Increment(ref scans); return Directory.EnumerateFiles(directory); });
        var loads = Enumerable.Range(0, 4).Select(_ => Task.Run(() => resolver.GetContext(pack.Chart))).ToArray();
        try { Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True); }
        finally { release.Set(); }
        Assert.That(Task.WaitAll(loads, TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(loads.All(load => ReferenceEquals(load.Result, loads[0].Result)), Is.True);
        Assert.That(opens, Is.EqualTo(5));
        Assert.That(scans, Is.EqualTo(1));
    }

    [Test]
    [CancelAfter(15000)]
    public void RefreshDuringInspectionCannotRepublishOldGeneration()
    {
        using var pack = new Pack();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var opens = 0;
        var resolver = new OjnDirectoryEncoding(path =>
        {
            var bytes = File.ReadAllBytes(path);
            if (Interlocked.Increment(ref opens) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new AssertionException("Load was not released.");
            }
            return new MemoryStream(bytes);
        });
        var load = Task.Run(() => resolver.GetContext(pack.Chart));
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            pack.MakeChinese(true);
            resolver.Clear();
        }
        finally { release.Set(); }
        Assert.That(load.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(load.Result.Encoding, Is.EqualTo(OjnMetadataEncoding.Gbk));
        Assert.That(resolver.IsCurrent(load.Result), Is.True);
        Assert.That(opens, Is.EqualTo(10));
    }

    [Test]
    [CancelAfter(15000)]
    public void InvalidationDuringDocumentReadCannotRepublishRemovedEntry()
    {
        using var pack = new Pack();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reads = 0;
        var cache = new OjnDocumentCache(new OjnDirectoryEncoding(), path =>
        {
            var bytes = File.ReadAllBytes(path);
            if (Interlocked.Increment(ref reads) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new AssertionException("Read was not released.");
            }
            return new MemoryStream(bytes);
        });
        var load = Task.Run(() => cache.Get(pack.Chart, O2JamDifficulty.EX));
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            cache.Invalidate(pack.Chart);
        }
        finally { release.Set(); }
        Assert.That(load.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(cache.Get(pack.Chart, O2JamDifficulty.EX), Is.SameAs(load.Result));
    }

    [Test]
    public void ContextRefreshChangesBothTitleAndNonAsciiArchivePath()
    {
        using var pack = new Pack();
        var bytes = File.ReadAllBytes(pack.Chart);
        Pack.SetField(bytes, 236, 32, [.. Convert.FromHexString("DEEFE3EA"), .. ".ojm"u8.ToArray()]);
        pack.Write(Path.GetFileName(pack.Chart), bytes);
        var resolver = new OjnDirectoryEncoding();
        var cache = new OjnDocumentCache(resolver);
        var first = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(first.Metadata.OjmFileName, Is.EqualTo("蛇神.ojm"));
        pack.MakeChinese(false);
        resolver.Clear();
        var next = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(next.Metadata.Title, Is.Not.EqualTo(first.Metadata.Title));
        Assert.That(next.Metadata.OjmFileName, Is.Not.EqualTo(first.Metadata.OjmFileName));
    }

    [Test]
    public void LoadedResourceRejectsExplicitRefreshWithoutLoadingAnArchive()
    {
        using var pack = new Pack();
        OjnDirectoryEncoding.Shared.Clear();
        var resources = new O2JamExternalChartResources(pack.Chart, O2JamDifficulty.EX);
        var first = resources.Document;
        Assert.That(resources.CanReuse, Is.True);
        pack.MakeChinese(false);
        Assert.That(resources.CanReuse, Is.True);
        OjnDirectoryEncoding.Shared.Clear();
        Assert.That(resources.CanReuse, Is.False);
        Assert.That(archiveLazy(resources).IsValueCreated, Is.False);
        var next = new O2JamExternalChartResources(pack.Chart, O2JamDifficulty.EX);
        Assert.That(next.Document.Metadata.Title, Is.Not.EqualTo(first.Metadata.Title));
    }

    [Test]
    public void WrapperCacheReplacesLoadedDocumentAfterExplicitRefresh()
    {
        using var pack = new Pack();
        OjnDirectoryEncoding.Shared.Clear();
        var info = new BeatmapInfo(new O2LazerRuleset().RulesetInfo) { ID = Guid.NewGuid(), DifficultyName = "EX" };
        var inner = new FlatWorkingBeatmap(new Beatmap { BeatmapInfo = info });
        var cache = new O2JamWorkingBeatmapCache(new WorkingBeatmapCache(null!, null!, null!, null!), null!);
        var first = cache.Wrap(inner, pack.Chart);
        var resources = (O2JamExternalChartResources)typeof(O2JamWorkingBeatmap).GetField("resources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(first)!;
        var old = resources.Document;
        Assert.That(cache.Wrap(inner, pack.Chart), Is.SameAs(first));
        pack.MakeChinese(false);
        OjnDirectoryEncoding.Shared.Clear();
        var next = cache.Wrap(inner, pack.Chart);
        Assert.That(next, Is.Not.SameAs(first));
        var nextResources = (O2JamExternalChartResources)typeof(O2JamWorkingBeatmap).GetField("resources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(next)!;
        Assert.That(nextResources.Document.Metadata.Title, Is.Not.EqualTo(old.Metadata.Title));
        Assert.That(cache.Wrap(inner, pack.Chart), Is.SameAs(next));
        Assert.That(archiveLazy(resources).IsValueCreated, Is.False);
        Assert.That(archiveLazy(nextResources).IsValueCreated, Is.False);
    }

    [Test]
    public void EvictedContextCannotServeAStillLoadedDocument()
    {
        using var pack = new Pack();
        var resolver = new OjnDirectoryEncoding();
        var first = resolver.GetContext(pack.Chart);
        for (var index = 0; index < 128; index++)
        {
            var directory = Path.Combine(pack.Root, $"pack{index}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "chart.ojn");
            File.Copy(pack.Chart, path);
            resolver.GetContext(path);
        }
        Assert.That(first.Active, Is.False);
        Assert.That(resolver.IsCurrent(first), Is.False);
        Assert.That(resolver.GetContext(pack.Chart), Is.Not.SameAs(first));
    }

    [Test]
    public void DirectoryEnumerationFailureDoesNotFreezeAutomaticFallback()
    {
        using var pack = new Pack();
        var fail = true;
        var resolver = new OjnDirectoryEncoding(enumerateFiles: directory =>
        {
            if (fail) throw new IOException("Synthetic directory failure.");
            return Directory.EnumerateFiles(directory);
        });
        var cache = new OjnDocumentCache(resolver);
        var first = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, first), Is.False);
        fail = false;
        var next = cache.Get(pack.Chart, O2JamDifficulty.EX);
        Assert.That(next, Is.Not.SameAs(first));
        Assert.That(cache.IsCurrent(pack.Chart, O2JamDifficulty.EX, next), Is.True);
    }

    [Test]
    public void DirectoryChangeDuringInspectionRetriesBeforePublishing()
    {
        using var pack = new Pack();
        var opens = 0;
        var resolver = new OjnDirectoryEncoding(path =>
        {
            if (Interlocked.Increment(ref opens) == 1)
                Directory.SetLastWriteTimeUtc(pack.Root, Directory.GetLastWriteTimeUtc(pack.Root).AddSeconds(2));
            return File.OpenRead(path);
        });
        var context = resolver.GetContext(pack.Chart);
        Assert.That(context.Encoding, Is.EqualTo(OjnMetadataEncoding.Cp949));
        Assert.That(resolver.IsCurrent(context), Is.True);
        Assert.That(opens, Is.EqualTo(10));
    }

    private static Lazy<Task<O2Jam.Formats.Ojm.OjmArchive>> archiveLazy(O2JamExternalChartResources resources) =>
        (Lazy<Task<O2Jam.Formats.Ojm.OjmArchive>>)typeof(O2JamExternalChartResources).GetField("archive", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resources)!;

    private static void changeInventory(Pack pack, string change)
    {
        var source = Path.Combine(pack.Root, "context0000.ojn");
        switch (change)
        {
            case "add": pack.Write("new.ojn", Pack.Bytes("あなた", 949)); break;
            case "remove": File.Delete(source); break;
            case "rename": File.Move(source, Path.Combine(pack.Root, "renamed.ojn")); break;
        }
    }

    internal sealed class Pack : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "o2lazer-bounded-identification-" + Guid.NewGuid().ToString("N"));
        internal string Chart => Path.Combine(Root, "ambiguous.ojn");
        internal Pack(int count = 4)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Directory.CreateDirectory(Root);
            for (var index = 0; index < count; index++) Write($"context{index:0000}.ojn", Bytes("あなた", 949));
            var bytes = OjnTestData.CreateChart();
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), 2.9f);
            SetField(bytes, 108, 64, Convert.FromHexString("DEEFE3EA"));
            Write("ambiguous.ojn", bytes);
        }
        internal void Write(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(Root, name), bytes);
        internal void MakeChinese(bool restoreStamps)
        {
            var directoryStamp = Directory.GetLastWriteTimeUtc(Root);
            foreach (var path in Directory.GetFiles(Root, "context*.ojn"))
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                File.WriteAllBytes(path, Bytes("[国服]莎娜塔", 936));
                File.SetLastWriteTimeUtc(path, restoreStamps ? stamp : stamp.AddSeconds(2));
            }
            Directory.SetLastWriteTimeUtc(Root, directoryStamp);
        }
        internal static byte[] Bytes(string title, int codePage)
        {
            var bytes = OjnTestData.CreateChart();
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), 2.9f);
            SetField(bytes, 108, 64, Encoding.GetEncoding(codePage).GetBytes(title));
            return bytes;
        }
        internal static void SetField(byte[] bytes, int offset, int size, byte[] value)
        {
            Array.Clear(bytes, offset, size);
            value.CopyTo(bytes, offset);
        }
        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(root).StartsWith("o2lazer-bounded-identification-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected temporary pack cleanup target.");
            Directory.Delete(root, true);
        }
    }
}
