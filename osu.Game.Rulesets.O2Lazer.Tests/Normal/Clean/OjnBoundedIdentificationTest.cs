using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class OjnBoundedIdentificationTest
{
    [Test]
    [CancelAfter(15000)]
    public void OwnedStreamsOverlapWithinFourAndAllCloseBeforePublication()
    {
        using var pack = new OjnBoundedIdentificationBoundaryTest.Pack(100);
        using var secondReader = new ManualResetEventSlim();
        var state = new StreamState();
        var reads = 0;
        var resolver = new OjnDirectoryEncoding(path => new OwnedStream(File.ReadAllBytes(path), state, () =>
        {
            var order = Interlocked.Increment(ref reads);
            if (order == 2) secondReader.Set();
            if (order <= 2 && !secondReader.Wait(TimeSpan.FromSeconds(5)))
                throw new AssertionException("The other header reader did not enter.");
        }));
        var result = resolver.GetContext(pack.Chart);
        Assert.That(result.Encoding, Is.EqualTo(OjnMetadataEncoding.Cp949));
        Assert.That(result.Reusable, Is.True);
        Assert.That(state.Peak, Is.InRange(2, 4));
        Assert.That(state.Opened, Is.EqualTo(96));
        Assert.That(state.Closed, Is.EqualTo(96));
        Assert.That(state.Active, Is.Zero);
    }

    [TestCase(86, 9, OjnMetadataEncoding.Cp949)]
    [TestCase(85, 10, OjnMetadataEncoding.Automatic)]
    [TestCase(9, 86, OjnMetadataEncoding.Gbk)]
    [TestCase(10, 85, OjnMetadataEncoding.Automatic)]
    public void CompletionOrderCannotChangeTheExistingNinetyPercentDecision(int korean, int chinese, OjnMetadataEncoding expected)
    {
        using var pack = new OjnBoundedIdentificationBoundaryTest.Pack(korean + chinese);
        for (var index = korean; index < korean + chinese; index++)
            pack.Write($"context{index:0000}.ojn", OjnBoundedIdentificationBoundaryTest.Pack.Bytes("[国服]莎娜塔", 936));
        var state = new StreamState();
        var resolver = new OjnDirectoryEncoding(path => new OwnedStream(File.ReadAllBytes(path), state, () => Thread.Yield()));
        var context = resolver.GetContext(pack.Chart);
        Assert.That(context.Encoding, Is.EqualTo(expected));
        Assert.That(context.Reusable, Is.True);
        Assert.That(state.Opened, Is.EqualTo(96));
        Assert.That(state.Active, Is.Zero);
        Assert.That(state.Closed, Is.EqualTo(state.Opened));
    }

    [Test]
    public void ShortHeadersCannotInheritEvidenceFromAnotherWorkersBuffer()
    {
        using var pack = new OjnBoundedIdentificationBoundaryTest.Pack(100);
        var state = new StreamState();
        var resolver = new OjnDirectoryEncoding(path => new OwnedStream(File.ReadAllBytes(path)[..100], state));
        var context = resolver.GetContext(pack.Chart);
        Assert.That(context.Encoding, Is.EqualTo(OjnMetadataEncoding.Automatic));
        Assert.That(context.Reusable, Is.True);
        Assert.That(state.Opened, Is.EqualTo(96));
        Assert.That(state.Active, Is.Zero);
    }

    [Test]
    public void ExpectedDisposeFailureCannotBecomeAReusableContext()
    {
        using var pack = new OjnBoundedIdentificationBoundaryTest.Pack();
        var state = new StreamState();
        var fail = true;
        var resolver = new OjnDirectoryEncoding(path => new OwnedStream(File.ReadAllBytes(path), state,
            failOnDispose: fail && Path.GetFileName(path) == "context0000.ojn"));
        var first = resolver.GetContext(pack.Chart);
        Assert.That(first.Reusable, Is.False);
        Assert.That(state.Active, Is.Zero);
        fail = false;
        var next = resolver.GetContext(pack.Chart);
        Assert.That(next, Is.Not.SameAs(first));
        Assert.That(next.Reusable, Is.True);
        Assert.That(next.Encoding, Is.EqualTo(OjnMetadataEncoding.Cp949));
        Assert.That(state.Active, Is.Zero);
        Assert.That(state.Closed, Is.EqualTo(state.Opened));
    }

    [Test]
    public void UnexpectedReaderFailureIsNotHiddenAndDoesNotLeaveOwnedStreamsOpen()
    {
        using var pack = new OjnBoundedIdentificationBoundaryTest.Pack(100);
        var state = new StreamState();
        var fail = true;
        var resolver = new OjnDirectoryEncoding(path => new OwnedStream(File.ReadAllBytes(path), state, () =>
        {
            if (fail && Path.GetFileName(path) == "context0000.ojn")
                throw new InvalidOperationException("Synthetic reader failure.");
        }));
        var exception = Assert.Throws<AggregateException>(() => resolver.GetContext(pack.Chart))!;
        Assert.That(exception.Flatten().InnerExceptions.Any(error => error is InvalidOperationException), Is.True);
        Assert.That(state.Active, Is.Zero);
        Assert.That(state.Closed, Is.EqualTo(state.Opened));
        fail = false;
        resolver.Clear();
        Assert.That(resolver.GetContext(pack.Chart).Reusable, Is.True);
        Assert.That(state.Active, Is.Zero);
    }

    private sealed class StreamState
    {
        internal int Active;
        internal int Peak;
        internal int Opened;
        internal int Closed;

        internal void Enter()
        {
            var active = Interlocked.Increment(ref Active);
            Interlocked.Increment(ref Opened);
            var peak = Volatile.Read(ref Peak);
            while (active > peak)
            {
                var previous = Interlocked.CompareExchange(ref Peak, active, peak);
                if (previous == peak) break;
                peak = previous;
            }
        }
    }

    private sealed class OwnedStream(byte[] bytes, StreamState state, Action? beforeRead = null, bool failOnDispose = false) : MemoryStream(bytes)
    {
        private readonly StreamState state = enter(state);
        private bool disposed;

        private static StreamState enter(StreamState value)
        {
            value.Enter();
            return value;
        }

        public override int Read(Span<byte> buffer)
        {
            beforeRead?.Invoke();
            return base.Read(buffer[..Math.Min(buffer.Length, 17)]);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposed) return;
            disposed = true;
            Interlocked.Decrement(ref state.Active);
            Interlocked.Increment(ref state.Closed);
            if (failOnDispose) throw new IOException("Synthetic close failure.");
        }
    }
}
