using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using O2Jam.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class OjmArchiveCacheTest
{
    [Test]
    public void PayloadFailureReplacesTheCachedIndexEvenAfterOriginalTimestampIsRestored()
    {
        var directory = createDirectory();
        try
        {
            var source = createFile(directory, "chart.ojn", [1]);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write("M30\0"u8); writer.Write(1u); writer.Write(0u); writer.Write(1u); writer.Write(28u); writer.Write(56u); writer.Write(0u);
            writer.Write(new byte[32]); writer.Write(4u); writer.Write((ushort)5); writer.Write((ushort)0); writer.Write(0u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u); writer.Write("zero"u8);
            var bytes = stream.ToArray();
            var path = createFile(directory, "audio.ojm", bytes);
            var stamp = File.GetLastWriteTimeUtc(path);
            var loads = 0;
            var cache = new OjmArchiveCache(3, 1024, (file, _) => { loads++; return new OjmReader().ReadLazy(file); });
            var old = cache.GetAll(source, path);
            var changed = bytes.ToArray(); changed[^1] ^= 0x7f;
            File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, stamp.AddSeconds(5));
            Assert.Throws<IOException>(() => _ = old.Samples[0].Data);
            File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, stamp);
            Assert.That(cache.IsCurrentArchive(source, old), Is.False);
            var fresh = cache.GetAll(source, path);
            Assert.That(fresh, Is.Not.SameAs(old));
            Assert.That(fresh.Samples[0].Data, Is.EqualTo("zero"u8.ToArray()));
            Assert.That(cache.GetAll(source, path), Is.SameAs(fresh));
            Assert.That(loads, Is.EqualTo(2));
        }
        finally { Directory.Delete(directory, true); }
    }
}
