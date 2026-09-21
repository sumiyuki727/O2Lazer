using System.IO;
using System.Text;
using NUnit.Framework;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public static class OjnTestData
{
    public static byte[] CreateChart()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((uint)100);
        writer.Write(new byte[] { (byte)'o', (byte)'j', (byte)'n', 0 });
        writer.Write(3f);
        writer.Write(0);
        writer.Write(120f);
        writer.Write((ushort)5);
        writer.Write((ushort)10);
        writer.Write((ushort)15);
        writer.Write((short)0);
        writeThree(writer, 0);
        writeThree(writer, 0);
        writeThree(writer, 0);
        writer.Write((uint)4);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(new byte[20]);
        writer.Write((uint)0);
        writer.Write((uint)1);
        writeFixed(writer, "Clean O2Jam", 64);
        writeFixed(writer, "Open", 32);
        writeFixed(writer, "Tester", 32);
        writeFixed(writer, "o2ma100.ojm", 32);
        writer.Write((uint)0);
        writeThree(writer, 0);
        writer.Write((uint)300);
        writer.Write((uint)372);
        writer.Write((uint)372);
        writer.Write((uint)372);

        Assert.That(stream.Position, Is.EqualTo(300));

        writeFloatBlock(writer, 0, 0, 0.5f);
        writeNoteBlock(writer, 0, 2, 4, 0, 1, 0x88, 2);
        writeFloatBlock(writer, 1, 1, 240);
        writeNoteBlock(writer, 1, 2, 4, 0, 2, 0x88, 3);

        Assert.That(stream.Position, Is.EqualTo(372));
        return stream.ToArray();
    }

    private static void writeFloatBlock(BinaryWriter writer, uint measure, ushort channel, float value)
    {
        writer.Write(measure);
        writer.Write(channel);
        writer.Write((ushort)1);
        writer.Write(value);
    }

    private static void writeNoteBlock(BinaryWriter writer, uint measure, ushort channel, ushort count, int populatedIndex, ushort id, byte audio, byte type)
    {
        writer.Write(measure);
        writer.Write(channel);
        writer.Write(count);

        for (var index = 0; index < count; index++)
        {
            writer.Write(index == populatedIndex ? id : (ushort)0);
            writer.Write(index == populatedIndex ? audio : (byte)0);
            writer.Write(index == populatedIndex ? type : (byte)0);
        }
    }

    private static void writeFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }

    private static void writeThree(BinaryWriter writer, uint value)
    {
        writer.Write(value);
        writer.Write(value);
        writer.Write(value);
    }
}
