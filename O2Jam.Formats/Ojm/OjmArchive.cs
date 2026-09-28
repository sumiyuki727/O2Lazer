using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojm;

public sealed class OjmArchive
{
    public IReadOnlyDictionary<int, OjmSample> Samples { get; }
    public long DecodedByteLength { get; }

    public OjmArchive(IReadOnlyDictionary<int, OjmSample> samples)
    {
        Samples = samples.ToImmutableDictionary();
        DecodedByteLength = Samples.Values.Sum(sample => sample.ByteLength);
    }

    public bool TryGetSample(int id, out OjmSample sample) => Samples.TryGetValue(id, out sample!);
}

public sealed record OjmArchiveIndex
{
    public IReadOnlySet<int> SampleIds { get; }

    public OjmArchiveIndex(IReadOnlySet<int> sampleIds) => SampleIds = sampleIds.ToImmutableHashSet();
}

public sealed class OjmSample
{
    private readonly System.Lazy<byte[]> data;

    public int Id { get; }
    public string Name { get; }
    public string Extension { get; }
    public byte[] Data => (byte[])data.Value.Clone();
    public Stream OpenRead() => new MemoryStream(data.Value, writable: false);
    public long ByteLength { get; }
    public bool IsLoaded => data.IsValueCreated;

    public OjmSample(int id, string name, string extension, byte[] data)
    {
        Id = id;
        Name = name;
        Extension = extension;
        var ownedData = (byte[])data.Clone();
        this.data = new System.Lazy<byte[]>(() => ownedData);
        ByteLength = ownedData.LongLength;
    }

    internal OjmSample(int id, string name, string extension, long byteLength, System.Func<byte[]> loader)
    {
        Id = id;
        Name = name;
        Extension = extension;
        ByteLength = byteLength;
        data = new System.Lazy<byte[]>(loader, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
