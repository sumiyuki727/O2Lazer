using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Audio;

/// <summary>
/// Exposes an OJM archive through the resource-store contract used by osu!framework's audio decoders.
/// </summary>
public sealed class O2JamArchiveResourceStore : IResourceStore<byte[]>
{
    private const string resource_prefix = "o2jam/";

    private readonly OjmArchive archive;

    public O2JamArchiveResourceStore(OjmArchive archive)
    {
        this.archive = archive;
    }

    public byte[] Get(string name) => tryResolve(name, out var sample) ? sample.Data : null!;

    public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Get(name));
    }

    public Stream GetStream(string name)
    {
        var data = Get(name);
        return data == null ? null! : new MemoryStream(data, writable: false);
    }

    public IEnumerable<string> GetAvailableResources() =>
        archive.Samples.Values.Select(sample => $"{resource_prefix}{sample.Id}{sample.Extension}");

    public void Dispose()
    {
    }

    private bool tryResolve(string name, out OjmSample sample)
    {
        sample = null!;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalised = name.Replace('\\', '/');
        if (normalised.StartsWith(resource_prefix, StringComparison.OrdinalIgnoreCase))
            normalised = normalised[resource_prefix.Length..];

        var extension = Path.GetExtension(normalised);
        if (extension.Length > 0)
            normalised = normalised[..^extension.Length];

        return int.TryParse(normalised, out var id)
               && archive.TryGetSample(id, out sample)
               && (extension.Length == 0 || string.Equals(extension, sample.Extension, StringComparison.OrdinalIgnoreCase));
    }
}
