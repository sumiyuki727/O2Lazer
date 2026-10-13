using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using O2Jam.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[NonParallelizable]
public partial class OjmReaderTest
{
    [TestCase("OJM")]
    [TestCase("OMC")]
    [TestCase("M30")]
    public void UnloadedPayloadRejectsChangedSourceWhileLoadedPayloadKeepsItsVersion(string format)
    {
        withArchive(format, (path, bytes) =>
        {
            var archive = new OjmReader().ReadLazy(path);
            var first = archive.Samples[0].Data;
            var timestamp = File.GetLastWriteTimeUtc(path);
            var changed = bytes.ToArray();
            changed[^1] ^= 0x7f;
            File.WriteAllBytes(path, changed);
            File.SetLastWriteTimeUtc(path, timestamp.AddSeconds(5));
            Assert.Throws<IOException>(() => _ = archive.Samples[1].Data);
            Assert.That(archive.IsSourceInvalidated, Is.True);
            Assert.That(archive.Samples[0].Data, Is.EqualTo(first));
            Assert.Throws<IOException>(() => _ = archive.Samples[1].Data);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LengthChangesAreRejectedEvenWhenTimestampIsPreserved(bool append)
    {
        withArchive("M30", (path, bytes) =>
        {
            var archive = new OjmReader().ReadLazy(path);
            var timestamp = File.GetLastWriteTimeUtc(path);
            File.WriteAllBytes(path, append ? [.. bytes, 0xff] : bytes[..^1]);
            File.SetLastWriteTimeUtc(path, timestamp);
            Assert.Throws<IOException>(() => _ = archive.Samples[0].Data);
            Assert.That(archive.IsSourceInvalidated, Is.True);
        });
    }

    [Test]
    public void DeletedSourceInvalidatesOtherPendingPayloadsEvenAfterSourceRestoration()
    {
        withArchive("M30", (path, bytes) =>
        {
            var archive = new OjmReader().ReadLazy(path);
            var stamp = File.GetLastWriteTimeUtc(path);
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() => _ = archive.Samples[0].Data);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, stamp);
            Assert.Throws<IOException>(() => _ = archive.Samples[1].Data);
            Assert.That(new OjmReader().ReadLazy(path).Samples[1].Data, Is.EqualTo("one!"u8.ToArray()));
        });
    }

    [Test]
    public void CachedAndConcurrentPayloadReadsRetainLoadedVersionWithoutSourceFile()
    {
        withArchive("M30", (path, _) =>
        {
            var archive = new OjmReader().ReadLazy(path);
            var loaded = archive.Samples[0].Data;
            // The removed source makes accidental reopening observable without packaging counters.
            File.Delete(path);
            Parallel.For(0, 16, _ =>
            {
                using var stream = archive.Samples[0].OpenRead();
                var bytes = new byte[loaded.Length];
                stream.ReadExactly(bytes);
                Assert.That(bytes, Is.EqualTo(loaded));
                var ownedCopy = archive.Samples[0].Data;
                Assert.That(ownedCopy, Is.EqualTo(loaded));
                Assert.That(ownedCopy, Is.Not.SameAs(loaded));
            });
            Assert.That(archive.Samples[1].IsLoaded, Is.False);
            using var again = archive.Samples[0].OpenRead();
            Assert.That(again.Length, Is.EqualTo(loaded.Length));
            Assert.That(archive.IsSourceInvalidated, Is.False);
        });
    }

    [Test]
    public void RelativePathIsBoundToTheIndexingDirectory()
    {
        withArchive("M30", (path, _) =>
        {
            var before = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.GetDirectoryName(path)!;
                var archive = new OjmReader().ReadLazy(Path.GetFileName(path));
                Environment.CurrentDirectory = Path.GetTempPath();
                Assert.That(archive.Samples[0].Data, Is.EqualTo("zero"u8.ToArray()));
            }
            finally { Environment.CurrentDirectory = before; }
        });
    }

    [Test]
    public void SameLengthAndTimestampReplacementRemainsAnExplicitManualRefreshBoundary()
    {
        withArchive("M30", (path, bytes) =>
        {
            var archive = new OjmReader().ReadLazy(path);
            var stamp = File.GetLastWriteTimeUtc(path);
            var changed = bytes.ToArray(); changed[^1] ^= 0x7f;
            File.WriteAllBytes(path, changed);
            File.SetLastWriteTimeUtc(path, stamp);
            Assert.That(archive.Samples[1].Data, Is.Not.EqualTo("one!"u8.ToArray()));
            Assert.That(archive.IsSourceInvalidated, Is.False);
        });
    }

    private static void withArchive(string format, Action<string, byte[]> run)
    {
        var path = Path.Combine(Path.GetTempPath(), $"o2lazer-r02-{Guid.NewGuid():N}.ojm");
        var bytes = format == "M30" ? createM30(0, [(0, (ushort)5, "zero"u8.ToArray()), (1, (ushort)5, "one!"u8.ToArray())]) : createOmcEffects("zero"u8.ToArray(), "one!"u8.ToArray());
        if (format == "OJM") { bytes[1] = (byte)'J'; bytes[2] = (byte)'M'; }
        try { File.WriteAllBytes(path, bytes); run(path, bytes); }
        finally { File.Delete(path); }
    }
}
