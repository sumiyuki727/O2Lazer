using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojm;

public sealed class OjmArchive(IReadOnlyDictionary<int, OjmSample> samples)
{
    public IReadOnlyDictionary<int, OjmSample> Samples { get; } = samples;

    public long DecodedByteLength { get; } = samples.Values.Sum(sample => sample.ByteLength);

    public bool TryGetSample(int id, out OjmSample sample) => Samples.TryGetValue(id, out sample!);
}

public sealed record OjmArchiveIndex(IReadOnlySet<int> SampleIds);

public sealed class OjmSample
{
    private readonly System.Lazy<byte[]> data;

    public int Id { get; }
    public string Name { get; }
    public string Extension { get; }
    public byte[] Data => data.Value;
    public long ByteLength { get; }
    public bool IsLoaded => data.IsValueCreated;

    public OjmSample(int id, string name, string extension, byte[] data)
    {
        Id = id;
        Name = name;
        Extension = extension;
        this.data = new System.Lazy<byte[]>(() => data);
        ByteLength = data.LongLength;
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
