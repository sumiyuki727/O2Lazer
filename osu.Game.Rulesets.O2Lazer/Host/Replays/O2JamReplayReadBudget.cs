using System;
using System.IO;
using Newtonsoft.Json;

namespace osu.Game.Rulesets.O2Lazer.Replays;

internal readonly record struct O2JamReplayReadFailure(bool ResourceLimitExceeded, bool IdentifiedO2JamEnvelope);

/// <summary>
/// Counts bytes at each compression boundary without taking ownership of the caller's stream.
/// </summary>
internal sealed class O2JamReplayReadStream(Stream source, long limit, O2JamReplayResourceBudget budget, ReadOnlyMemory<byte> prefix = default) : Stream
{
    private long consumed;
    private int prefixOffset;

    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        // One sentinel byte distinguishes an exact-limit EOF from an oversized stream.
        var allowed = (int)Math.Min(buffer.Length, Math.Max(1, limit - consumed + 1));
        var count = 0;
        if (prefixOffset < prefix.Length)
        {
            count = Math.Min(allowed, prefix.Length - prefixOffset);
            prefix.Span.Slice(prefixOffset, count).CopyTo(buffer);
            prefixOffset += count;
        }
        else
            count = source.Read(buffer[..allowed]);

        consumed += count;
        if (consumed > limit)
            budget.Reject();
        return count;
    }

    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return Read(value) == 0 ? -1 : value[0];
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Uses the native JSON reader's depth policy and counts entries before the serializer adds them.
/// </summary>
internal sealed class O2JamReplayJsonReader(TextReader source, O2JamReplayResourceLimits limits, O2JamReplayResourceBudget budget) : JsonTextReader(source)
{
    private readonly O2JamReplayJsonBudget jsonBudget = new(limits, budget);
    private string? rootProperty;
    private string? ruleset;
    private int version;

    internal bool IdentifiedO2JamEnvelope => version == O2JamReplayArchive.FormatVersion
                                           && string.Equals(ruleset, O2LazerIdentity.ShortName, StringComparison.Ordinal);

    public override bool Read()
    {
        if (!base.Read())
            return false;

        checkToken();
        return true;
    }

    // JsonTextReader's typed reads bypass Read(). Count the scalar paths used by the
    // native serializer too, before it appends pauses/actions or assigns metadata.
    public override int? ReadAsInt32() => checkedValue(base.ReadAsInt32());
    public override string? ReadAsString() => checkedValue(base.ReadAsString());
    public override bool? ReadAsBoolean() => checkedValue(base.ReadAsBoolean());
    public override double? ReadAsDouble() => checkedValue(base.ReadAsDouble());
    public override decimal? ReadAsDecimal() => checkedValue(base.ReadAsDecimal());
    public override DateTime? ReadAsDateTime() => checkedValue(base.ReadAsDateTime());
    public override DateTimeOffset? ReadAsDateTimeOffset() => checkedValue(base.ReadAsDateTimeOffset());

    private T checkedValue<T>(T value)
    {
        checkToken();
        return value;
    }

    private void checkToken()
    {
        if (TokenType == JsonToken.None)
            return;

        jsonBudget.Check(TokenType, Depth, Value as string);

        if (TokenType == JsonToken.Comment)
            return;

        if (TokenType == JsonToken.PropertyName)
        {
            if (Depth == 1)
                rootProperty = (string?)Value;
        }
        else
        {
            if (Depth == 1 && rootProperty != null)
            {
                if (rootProperty.Equals("ruleset", StringComparison.OrdinalIgnoreCase))
                    ruleset = Value as string;
                else if (rootProperty.Equals("version", StringComparison.OrdinalIgnoreCase))
                    version = TokenType == JsonToken.Integer && Value is int or long ? Convert.ToInt32(Value) : 0;
                rootProperty = null;
            }
        }
    }
}
