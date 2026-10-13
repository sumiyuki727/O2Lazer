using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using O2Jam.Core;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamOjmSourceRecoveryTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PayloadFailureInvalidatesSuccessfulIndexAndRestoredStampRequiresFreshWrapper(bool deleteSource)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"o2lazer-r02-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var chartPath = Path.Combine(directory, "chart.ojn");
        var archivePath = Path.Combine(directory, "o2ma100.ojm");
        byte[] firstPayload = [1, 2, 3, 4];
        byte[] secondPayload = [5, 6, 7, 8];
        var original = createArchive(firstPayload, secondPayload);
        try
        {
            File.WriteAllBytes(chartPath, OjnTestData.CreateChart());
            File.WriteAllBytes(archivePath, original);
            var indexedStamp = File.GetLastWriteTimeUtc(archivePath);
            var resources = new O2JamExternalChartResources(chartPath, O2JamDifficulty.EX);
            var archive = await resources.Archive.WaitAsync(TimeSpan.FromSeconds(5));
            using var store = new O2JamArchiveResourceStore(archive);
            using var retained = store.GetStream("o2jam/0.ogg")!;
            Assert.That(archive.Samples[1].IsLoaded, Is.False);
            if (deleteSource)
                File.Delete(archivePath);
            else
            {
                File.WriteAllBytes(archivePath, createArchive([9, 10, 11, 12], [13, 14, 15, 16]));
                File.SetLastWriteTimeUtc(archivePath, indexedStamp.AddSeconds(2));
            }
            Assert.That(() => store.GetStream("o2jam/1.ogg"), Throws.InstanceOf<IOException>());
            File.WriteAllBytes(archivePath, original);
            File.SetLastWriteTimeUtc(archivePath, indexedStamp);
            Assert.Multiple(() =>
            {
                Assert.That(resources.Archive.IsCompletedSuccessfully, Is.True);
                Assert.That(archive.IsSourceInvalidated, Is.True);
                Assert.That(resources.CanReuse, Is.False,
                    "Restoring metadata must not make a cached failed payload reusable.");
                Assert.That(OjmArchiveCache.Shared.IsCurrentArchive(chartPath, archive), Is.False);
                Assert.That(store.Get("o2jam/0.ogg"), Is.EqualTo(firstPayload));
                Assert.That(store.Get("o2jam/2.ogg"), Is.Null,
                    "Missing samples must not fall through to another valid ID.");
            });
            var retainedBytes = new byte[firstPayload.Length];
            retained.ReadExactly(retainedBytes);
            Assert.That(retainedBytes, Is.EqualTo(firstPayload));
            var refreshed = new O2JamExternalChartResources(chartPath, O2JamDifficulty.EX);
            var freshArchive = await refreshed.Archive.WaitAsync(TimeSpan.FromSeconds(5));
            using var freshStore = new O2JamArchiveResourceStore(freshArchive);
            Assert.Multiple(() =>
            {
                Assert.That(freshArchive, Is.Not.SameAs(archive));
                Assert.That(refreshed.CanReuse, Is.True);
                Assert.That(freshStore.Get("o2jam/0.ogg"), Is.EqualTo(firstPayload));
                Assert.That(freshStore.Get("o2jam/1.ogg"), Is.EqualTo(secondPayload));
            });
        }
        finally
        {
            OjmArchiveCache.Shared.InvalidateSource(chartPath);
            File.Delete(archivePath);
            File.Delete(chartPath);
            Directory.Delete(directory);
        }
    }

    private static byte[] createArchive(params byte[][] payloads)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("M30\0"u8);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write((uint)payloads.Length);
        writer.Write(28u);
        var payloadLength = 0;
        foreach (var payload in payloads)
            payloadLength += 52 + payload.Length;
        writer.Write((uint)payloadLength);
        writer.Write(0u);
        for (var id = 0; id < payloads.Length; id++)
        {
            writer.Write(new byte[32]);
            writer.Write((uint)payloads[id].Length);
            writer.Write((ushort)5);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write((ushort)id);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(payloads[id]);
        }
        return stream.ToArray();
    }
}