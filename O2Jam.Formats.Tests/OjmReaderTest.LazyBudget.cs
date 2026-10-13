using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using O2Jam.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class OjmReaderTest
{
    [TestCase("OJM", 48)]
    [TestCase("OMC", 48)]
    [TestCase("M30", 4)]
    public void LazyAggregateChargesFirstMaterialisationAndRetainsItAfterStreamClose(string format, long limit)
    {
        withArchive(format, (path, bytes) =>
        {
            var expected = new OjmReader().Read(new MemoryStream(bytes)).Samples[0].Data;
            var archive = new OjmReader(new OjmReadLimits(0, 48, 0, limit)).ReadLazy(path);
            Assert.That(archive.Samples.Values, Has.All.Matches<OjmSample>(sample => !sample.IsLoaded));
            Assert.That(archive.DecodedByteLength, Is.EqualTo(limit * 2), "Unloaded index sizes do not spend the budget.");
            using (var stream = archive.Samples[0].OpenRead())
                Assert.That(stream.Length, Is.EqualTo(limit));
            var copy = archive.Samples[0].Data;
            copy[0] ^= 0xff;
            Assert.That(archive.Samples[0].Data, Is.EqualTo(expected));
            var exception = Assert.Throws<InvalidDataException>(() => _ = archive.Samples[1].OpenRead());
            Assert.That(exception!.Message, Does.Contain("lazy output byte budget"));
            Assert.That(archive.IsSourceInvalidated, Is.False);
            Assert.That(archive.Samples[1].IsLoaded, Is.False);
            Assert.That(archive.Samples[0].Data, Is.EqualTo(expected));
        });
    }

    [TestCase("OJM", 96)]
    [TestCase("OMC", 96)]
    [TestCase("M30", 8)]
    public void LazyExactAggregateWorksInEitherLoadOrder(string format, long limit)
    {
        withArchive(format, (path, bytes) =>
        {
            var expected = new OjmReader().Read(new MemoryStream(bytes));
            var reader = new OjmReader(new OjmReadLimits(0, 48, 0, limit));
            foreach (var reverse in new[] { false, true })
            {
                // Each archive has an independent lifetime even when created by one reader.
                var archive = reader.ReadLazy(path);
                foreach (var id in reverse ? new[] { 1, 0 } : new[] { 0, 1 })
                    Assert.That(archive.Samples[id].Data, Is.EqualTo(expected.Samples[id].Data));
            }
        });
    }

    [Test]
    public void AggregateRejectionDoesNotSpendRemainingAllowanceOrOpenTheSource()
    {
        withArchive("M30", (path, _) =>
        {
            var bytes = createM30(0, [(0, (ushort)5, new byte[5]), (1, (ushort)5, new byte[4]), (2, (ushort)5, new byte[1])]);
            File.WriteAllBytes(path, bytes);
            var stamp = File.GetLastWriteTimeUtc(path);
            var archive = new OjmReader(new OjmReadLimits(0, 5, 0, 6)).ReadLazy(path);
            Assert.That(archive.Samples[0].Data, Has.Length.EqualTo(5));
            File.Delete(path);
            Assert.That(Assert.Throws<InvalidDataException>(() => _ = archive.Samples[1].Data)!.Message, Does.Contain("lazy output byte budget"));
            Assert.That(archive.IsSourceInvalidated, Is.False);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, stamp);
            Assert.That(archive.Samples[2].Data, Has.Length.EqualTo(1));
            Assert.Throws<InvalidDataException>(() => _ = archive.Samples[1].Data);
            Assert.That(archive.Samples[0].Data, Has.Length.EqualTo(5));
        });
    }

    [Test]
    public async Task ConcurrentDifferentSamplesCannotSpendOneReservationTwice()
    {
        var budget = new OjmLazyReadBudget(new OjmReadLimits(0, 4, 0, 6));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new OjmSample(0, "first", ".ogg", 4, () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return new byte[4];
        }, budget);
        var pending = Task.Run(first.OpenRead);
        var rejectedLoaderCalls = 0;
        var second = new OjmSample(1, "second", ".ogg", 4, () =>
        {
            Interlocked.Increment(ref rejectedLoaderCalls);
            return new byte[4];
        }, budget);
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.Throws<InvalidDataException>(() => _ = second.Data);
            Assert.That(rejectedLoaderCalls, Is.Zero);
            // Loading another sound while the first waits proves IO does not hold the accounting lock.
            Assert.That(budget.Load(2, () => new byte[2]), Has.Length.EqualTo(2));
        }
        finally
        {
            release.Set();
            using var stream = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.That(first.Data, Has.Length.EqualTo(4));
        Assert.Throws<InvalidDataException>(() => budget.Load(1, () => new byte[1]));
    }

    [Test]
    public async Task ConcurrentRequestsOfOneSampleMaterialiseAndChargeOnlyOnce()
    {
        var calls = 0;
        var budget = new OjmLazyReadBudget(new OjmReadLimits(0, 4, 0, 4));
        var sample = new OjmSample(0, "shared", ".ogg", 4, () =>
        {
            Interlocked.Increment(ref calls);
            return "zero"u8.ToArray();
        }, budget);
        var values = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => sample.Data)))
                               .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(values, Has.All.EqualTo("zero"u8.ToArray()));
        Assert.That(values.Distinct().Count(), Is.EqualTo(values.Length), "Public Data preserves independent ownership.");
        Assert.Throws<InvalidDataException>(() => budget.Load(1, () => new byte[1]));
    }

    [TestCase("IO")]
    [TestCase("Decode")]
    [TestCase("Cancel")]
    [TestCase("Argument")]
    public void MaterialisationFailureReturnsOnlyItsReservation(string kind)
    {
        var budget = new OjmLazyReadBudget(new OjmReadLimits(0, 4, 0, 4));
        Exception failure = kind switch
        {
            "IO" => new IOException(),
            "Decode" => new InvalidDataException(),
            "Cancel" => new OperationCanceledException(),
            _ => new ArgumentException(),
        };
        var calls = 0;
        var sample = new OjmSample(0, "failed", ".ogg", 4, () => { calls++; throw failure; }, budget);
        Assert.Throws(failure.GetType(), () => _ = sample.Data);
        Assert.That(sample.IsLoaded, Is.False);
        var good = new OjmSample(1, "good", ".ogg", 4, () => new byte[4], budget);
        Assert.That(good.Data, Has.Length.EqualTo(4));
        Assert.Throws(failure.GetType(), () => _ = sample.OpenRead());
        Assert.That(calls, Is.EqualTo(1), "Cached Lazy failures cannot refund a later sample's allocation.");
        Assert.Throws<InvalidDataException>(() => budget.Load(1, () => new byte[1]));
    }

    [TestCase(3)]
    [TestCase(5)]
    public void UnexpectedOutputLengthReturnsReservation(int actualLength)
    {
        var budget = new OjmLazyReadBudget(new OjmReadLimits(0, 5, 0, 4));
        Assert.That(Assert.Throws<InvalidDataException>(() => budget.Load(4, () => new byte[actualLength]))!.Message,
            Does.Contain("indexed byte length"));
        Assert.That(budget.Load(4, () => new byte[4]), Has.Length.EqualTo(4));
    }

    [Test]
    public async Task FailedInFlightSampleRefundsWithoutReleasingAnotherSample()
    {
        var budget = new OjmLazyReadBudget(new OjmReadLimits(0, 4, 0, 6));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failed = Task.Run(() => budget.Load(4, () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            throw new IOException();
        }));
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(budget.Load(2, () => new byte[2]), Has.Length.EqualTo(2));
        }
        finally
        {
            release.Set();
            await Assert.ThatAsync(async () => await failed.WaitAsync(TimeSpan.FromSeconds(5)), Throws.TypeOf<IOException>());
        }
        Assert.That(budget.Load(4, () => new byte[4]), Has.Length.EqualTo(4));
        Assert.Throws<InvalidDataException>(() => budget.Load(1, () => new byte[1]));
    }

    [TestCase("OJM")]
    [TestCase("OMC")]
    [TestCase("M30")]
    public void SelectedLazyArchiveDoesNotReserveSkippedSamples(string format)
    {
        withArchive(format, (path, bytes) =>
        {
            var expected = new OjmReader().Read(new MemoryStream(bytes)).Samples[1].Data;
            var archive = new OjmReader(new OjmReadLimits(0, 48, 0, expected.Length)).ReadLazy(path, new System.Collections.Generic.HashSet<int> { 1 });
            Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 1 }));
            Assert.That(archive.Samples[1].Data, Is.EqualTo(expected));
        });
    }

    [Test]
    public void LazyZeroBudgetCanIndexNonemptySoundsWithoutLoadingThem()
    {
        withArchive("M30", (path, _) =>
        {
            var archive = new OjmReader(new OjmReadLimits(0, 4, 0, 0)).ReadLazy(path);
            Assert.That(archive.Samples, Has.Count.EqualTo(2));
            Assert.That(archive.Samples.Values, Has.All.Matches<OjmSample>(sample => !sample.IsLoaded));
            Assert.Throws<InvalidDataException>(() => _ = archive.Samples[0].Data);
            Assert.That(archive.IsSourceInvalidated, Is.False);
        });
    }

    [TestCase("OJM", 51)]
    [TestCase("OMC", 51)]
    [TestCase("OJM", 52)]
    [TestCase("OMC", 52)]
    public void WaveAndOggShareOneLazyOutputBudget(string format, long limit)
    {
        withArchive(format, (path, _) =>
        {
            var bytes = createOmcEffects("zero"u8.ToArray());
            if (format == "OJM") { bytes[1] = (byte)'J'; bytes[2] = (byte)'M'; }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), 1);
            using var memory = new MemoryStream();
            memory.Write(bytes);
            using (var writer = new BinaryWriter(memory, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                writeFixed(writer, "background", 32);
                writer.Write(4u);
                writer.Write("ogg!"u8);
            }
            File.WriteAllBytes(path, memory.ToArray());
            var archive = new OjmReader(new OjmReadLimits(0, 48, 0, limit)).ReadLazy(path);
            Assert.That(archive.Samples[0].Data, Has.Length.EqualTo(48));
            if (limit < 52)
                Assert.That(Assert.Throws<InvalidDataException>(() => _ = archive.Samples[1000].Data)!.Message, Does.Contain("lazy output byte budget"));
            else
                Assert.That(archive.Samples[1000].Data, Is.EqualTo("ogg!"u8.ToArray()));
            Assert.That(archive.IsSourceInvalidated, Is.False);
        });
    }
}
