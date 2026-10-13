using System.Threading;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

internal sealed class OjnEncodingContext(string directory, long directoryWriteTicks,
    OjnMetadataEncoding encoding, long generation, bool reusable)
{
    internal string Directory { get; } = directory;
    internal long DirectoryWriteTicks { get; } = directoryWriteTicks;
    internal OjnMetadataEncoding Encoding { get; } = encoding;
    internal long Generation { get; } = generation;
    internal bool Reusable { get; } = reusable;
    private int retired;
    internal bool Active => Volatile.Read(ref retired) == 0;
    internal void Retire() => Interlocked.Exchange(ref retired, 1);
}
