using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.O2Lazer.O2Jam;

namespace osu.Game.Rulesets.O2Lazer.IO.ResourceStore;

internal sealed class O2LazerAudioResourceStore(string basePath) : IResourceStore<byte[]>
{
    private static readonly string[] fallback_extensions = ["wav", "ogg", "mp3"];

    private readonly string basePath = Path.GetFullPath(string.IsNullOrEmpty(basePath) ? "." : basePath);
    private readonly O2LazerFileResourceStore fileStore = new(basePath);

    internal static IReadOnlyList<string> Extensions => fallback_extensions;

    public static void AddExtensions(ResourceStore<byte[]> resources)
    {
        foreach (var extension in fallback_extensions)
            resources.AddExtension(extension);
    }

    public byte[] Get(string? name)
    {
        if (!TryResolve(name, out var path))
            return null!;

        if (OjnSampleReference.TryParseResolved(path, out var ojmPath, out var sampleId))
        {
            try
            {
                return OjmDecoder.GetSample(ojmPath, sampleId)!;
            }
            catch (Exception exception)
            {
                O2LazerLogger.LogAudioFailure($"Failed to decode O2Jam sample {sampleId} from '{ojmPath}'.", exception);
                return null!;
            }
        }

        return File.ReadAllBytes(path);
    }

    public Task<byte[]> GetAsync(string? name, CancellationToken ct = default)
        => Task.Run(() => Get(name), ct);

    public Stream? GetStream(string? name)
    {
        if (!TryResolve(name, out var path))
            return null;

        if (OjnSampleReference.TryParseResolved(path, out _, out _))
        {
            var sample = Get(name);
            return sample == null ? null : new MemoryStream(sample, writable: false);
        }

        return File.OpenRead(path);
    }

    public IEnumerable<string> GetAvailableResources() => [];

    internal bool TryResolve(string? name, out string path)
    {
        if (OjnSampleReference.TryParseResolved(name, out var resolvedOjmPath, out _)
            && O2LazerFileResourceStore.IsPathInsideDirectory(resolvedOjmPath, basePath)
            && File.Exists(resolvedOjmPath))
        {
            path = name!;
            return true;
        }

        return OjnSampleReference.TryResolve(name, basePath, out path) || fileStore.TryResolve(name, out path);
    }

    public void Dispose()
    {
        fileStore.Dispose();
    }
}

