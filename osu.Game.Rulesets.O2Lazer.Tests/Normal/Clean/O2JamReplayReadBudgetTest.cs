using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayReadBudgetTest
{
    private const string envelope = """
        {"version":5,"ruleset":"o2lazer","frames":[{"time":-10,"actions":[0,6]}]}
        """;

    private static O2JamReplayResourceLimits limits => new(1024 * 1024, 1024 * 1024, 100, 10000, 4096);

    [TestCase(false, -1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, -1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void EncodedBudgetIncludesHeaderAndAllowsExactLimit(bool compressed, int margin)
    {
        var bytes = encode(envelope, compressed);
        using var source = new ShortReadStream(bytes, 1);
        var accepted = O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out var score, out var failure,
            limits with { EncodedBytes = bytes.Length + margin });
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(margin >= 0));
            Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(margin < 0));
            Assert.That(source.BytesRead, Is.LessThanOrEqualTo(bytes.Length));
            Assert.That(source.CanRead, Is.True, "The archive does not own its caller's stream.");
            if (accepted)
                Assert.That(score.Replay.Frames[0].Time, Is.EqualTo(-10));
        });
    }

    [TestCase(false, -1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, -1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void DecodedBudgetAllowsExactLimit(bool compressed, int margin)
    {
        using var source = new MemoryStream(encode(envelope, compressed));
        var accepted = O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { DecodedBytes = Encoding.UTF8.GetByteCount(envelope) + margin });
        Assert.That(accepted, Is.EqualTo(margin >= 0));
        Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(margin < 0));
    }

    [Test]
    public void HighlyCompressedContentStopsAtDecodedBudget()
    {
        var json = envelope + new string(' ', 100000);
        var bytes = encode(json, true);
        Assert.That(bytes.Length, Is.LessThan(1024));
        using var source = new ShortReadStream(bytes, 3);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { DecodedBytes = 1024 }), Is.False);
        Assert.That(failure.ResourceLimitExceeded, Is.True);
    }

    [Test]
    public void ByteLimiterReadsOnlyOneSentinelBeyondItsLimit()
    {
        using var source = new ShortReadStream(new byte[100000], 4096);
        var budget = new O2JamReplayResourceBudget();
        using var bounded = new O2JamReplayReadStream(source, 200, budget);
        Assert.Throws<InvalidDataException>(() => bounded.CopyTo(Stream.Null));
        Assert.That(source.BytesRead, Is.EqualTo(201));
    }

    [TestCase("frames", "[{\"time\":0,\"actions\":[]},{\"time\":1,\"actions\":[]},{\"time\":2,\"actions\":[]},{\"time\":3,\"actions\":[]},{\"time\":4,\"actions\":[]}]")]
    [TestCase("frames", "[{\"time\":0,\"actions\":[0,1,2,3,4]}]")]
    [TestCase("pauses", "[1,2,3,4,5]")]
    [TestCase("statistics", "{\"Perfect\":1,\"Great\":1,\"Good\":1,\"Ok\":1,\"Meh\":1}")]
    [TestCase("unknown", "[0,1,2,3,4]")]
    [TestCase("unknown", "{\"a\":0,\"b\":0,\"c\":0,\"d\":0,\"e\":0}")]
    public void EveryCollectionIsBoundedIncludingTypedScalarsAndUnknownFields(string property, string value)
    {
        var framePart = property == "frames" ? value : "[{\"time\":0,\"actions\":[]}]";
        var extra = property == "frames" ? "" : $",\"{property}\":{value}";
        var json = $"{{\"version\":5,\"ruleset\":\"o2lazer\",\"frames\":{framePart}{extra}}}";
        using var source = new ShortReadStream(encode(json), 1);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { CollectionEntries = 4 }), Is.False);
        Assert.That(failure, Is.EqualTo(new O2JamReplayReadFailure(true, true)));
    }

    [Test]
    public void ExactCollectionBudgetStillAllowsSevenKeyChords()
    {
        var json = "{\"version\":5,\"ruleset\":\"o2lazer\",\"frames\":[{\"time\":0,\"actions\":[0,1,2,3,4,5,6]}],\"pauses\":[0,1,2,3,4,5,6]}";
        using var source = new MemoryStream(encode(json));
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out var score, out _,
            limits with { CollectionEntries = 7 }), Is.True);
        Assert.That(((O2JamReplayFrame)score.Replay.Frames[0]).Actions, Has.Count.EqualTo(7));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void TokenBudgetCountsNativeTypedReadsOnce(int margin)
    {
        var json = "{\"version\":5,\"ruleset\":\"o2lazer\",\"frames\":[{\"time\":0.5,\"actions\":[0,6]}],\"has_received_all_frames\":true,\"player\":\"offline\",\"date\":\"2026-10-08T00:00:00+09:00\",\"pauses\":[1,2]}";
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text);
        var tokens = 0;
        while (reader.Read())
            tokens++;
        using var source = new MemoryStream(encode(json));
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { Tokens = tokens + margin }), Is.EqualTo(margin == 0));
        Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(margin < 0));
    }

    [Test]
    public void DuplicatePropertiesDoNotResetTotalTokenBudget()
    {
        var json = envelope[..^1] + string.Concat(Enumerable.Repeat(",\"unknown\":[0,1]", 20)) + "}";
        using var source = new MemoryStream(encode(json));
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { Tokens = 40 }), Is.False);
        Assert.That(failure.ResourceLimitExceeded, Is.True);
    }

    [TestCase(32, true)]
    [TestCase(33, false)]
    public void MetadataTextUsesNativeTypedStringRead(int length, bool accepted)
    {
        var json = envelope[..^1] + ",\"player\":\"" + new string('x', length) + "\"}";
        using var source = new MemoryStream(encode(json));
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { TextCharacters = 32 }), Is.EqualTo(accepted));
        Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(!accepted));
    }

    [TestCase("o2lazer", true)]
    [TestCase("bms", false)]
    [TestCase("", false)]
    public void OversizeFailureClaimsOnlyTheKnownO2JamEnvelope(string ruleset, bool identified)
    {
        var json = $"{{\"version\":5,\"ruleset\":\"{ruleset}\",\"player\":\"{new string('x', 33)}\"}}";
        using var source = new ShortReadStream(encode(json), 1);
        Assert.That(O2JamReplayArchive.TryReadMetadata(source, out _, out var failure,
            limits with { TextCharacters = 32 }), Is.False);
        Assert.That(failure, Is.EqualTo(new O2JamReplayReadFailure(true, identified)));
    }

    [TestCase(40, true)]
    [TestCase(70, false)]
    public void NativeDepthPolicyIsPreserved(int depth, bool accepted)
    {
        var json = envelope[..^1] + ",\"unknown\":" + new string('[', depth) + "0" + new string(']', depth) + "}";
        using var source = new MemoryStream(encode(json));
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure, limits), Is.EqualTo(accepted));
        Assert.That(failure.ResourceLimitExceeded, Is.False, "The native depth error is not a custom quota error.");
    }

    [TestCase("trailing-json")]
    [TestCase("{}")]
    public void AdditionalContentStillFailsNativeDeserialization(string suffix)
    {
        using var source = new MemoryStream(encode(envelope + suffix));
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure, limits), Is.False);
        Assert.That(failure.ResourceLimitExceeded, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TruncatedPayloadsRemainUnavailable(bool compressed)
    {
        using var source = new ShortReadStream(encode(envelope[..^8], compressed), 1);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure, limits), Is.False);
        Assert.That(failure.ResourceLimitExceeded, Is.False);
    }

    [Test]
    public void LongTimeSpanAndOfflineIdentityHaveNoArtificialDurationLimit()
    {
        var score = new Score
        {
            ScoreInfo = new ScoreInfo { BeatmapHash = "long-chart", MaxCombo = 1, User = new APIUser { Id = -1, Username = "offline" } },
            Replay = new Replay { Frames = [new O2JamReplayFrame(-100, ManiaAction.Key1), new O2JamReplayFrame(86400000)] },
        };
        using var source = new ShortReadStream(O2JamReplayArchive.Create(score), 2);
        Assert.That(O2JamReplayArchive.TryReadMetadata(source, out var metadata, out _, limits), Is.True);
        Assert.That(metadata.UserId, Is.EqualTo(-1));
    }

    [Test]
    public void GzipTrailingBytesAlsoCountTowardTheEncodedBudget()
    {
        var gzip = encode(envelope, true);
        var bytes = gzip.Concat(new byte[10000]).ToArray();
        using var source = new ShortReadStream(bytes, 1);
        var maximum = gzip.Length + 20;
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out _, out var failure,
            limits with { EncodedBytes = maximum }), Is.False);
        Assert.That(failure.ResourceLimitExceeded, Is.True);
        Assert.That(source.BytesRead, Is.EqualTo(maximum + 1));
    }
    private static byte[] encode(string json, bool compressed = false)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!compressed)
            return bytes;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private sealed class ShortReadStream(byte[] bytes, int chunk) : Stream
    {
        private readonly MemoryStream source = new(bytes, writable: false);
        public int BytesRead { get; private set; }
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = source.Read(buffer[..Math.Min(chunk, buffer.Length)]);
            BytesRead += read;
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                source.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}