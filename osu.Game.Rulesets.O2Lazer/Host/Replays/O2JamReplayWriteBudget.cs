using System;
using System.IO;
using Newtonsoft.Json;

namespace osu.Game.Rulesets.O2Lazer.Replays;

internal readonly record struct O2JamReplayWriteFailure(bool ResourceLimitExceeded, Exception? Error);

/// <summary>
/// Rejects writes before growing the destination and leaves its disposal to the compression pipeline.
/// </summary>
internal sealed class O2JamReplayWriteStream(Stream destination, long limit, O2JamReplayResourceBudget budget) : Stream
{
    private long written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => destination.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > limit - written)
            budget.Reject();
        destination.Write(buffer);
        written += buffer.Length;
    }

    public override void Flush() => destination.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// Counts the native serializer's actual tokens, including omitted defaults and dictionary keys.
/// </summary>
internal sealed class O2JamReplayJsonWriter(TextWriter destination, O2JamReplayResourceLimits limits, O2JamReplayResourceBudget budget) : JsonTextWriter(destination)
{
    private readonly O2JamReplayJsonBudget jsonBudget = new(limits, budget);

    public override void WriteStartObject()
    {
        check(JsonToken.StartObject);
        base.WriteStartObject();
    }

    public override void WriteStartArray()
    {
        check(JsonToken.StartArray);
        base.WriteStartArray();
    }

    protected override void WriteEnd(JsonToken token)
    {
        check(token);
        base.WriteEnd(token);
    }

    public override void WritePropertyName(string name)
    {
        check(JsonToken.PropertyName, name);
        base.WritePropertyName(name);
    }

    public override void WritePropertyName(string name, bool escape)
    {
        check(JsonToken.PropertyName, name);
        base.WritePropertyName(name, escape);
    }

    public override void WriteNull()
    {
        check(JsonToken.Null);
        base.WriteNull();
    }

    public override void WriteValue(string? value)
    {
        if (value == null)
        {
            WriteNull();
            return;
        }

        check(JsonToken.String, value);
        base.WriteValue(value);
    }

    public override void WriteValue(int value)
    {
        check(JsonToken.Integer);
        base.WriteValue(value);
    }

    public override void WriteValue(long value)
    {
        check(JsonToken.Integer);
        base.WriteValue(value);
    }

    public override void WriteValue(double value)
    {
        check(JsonToken.Float);
        base.WriteValue(value);
    }

    public override void WriteValue(bool value)
    {
        check(JsonToken.Boolean);
        base.WriteValue(value);
    }

    public override void WriteValue(DateTimeOffset value)
    {
        check(JsonToken.Date);
        base.WriteValue(value);
    }

    private void check(JsonToken token, string? text = null) => jsonBudget.Check(token, Top, text);
}
