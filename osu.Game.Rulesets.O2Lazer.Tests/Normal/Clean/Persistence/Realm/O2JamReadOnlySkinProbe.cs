using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Realms;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

// Diagnostics use an unmanaged skin and read-only file lookups, so loading the user's assets
// cannot migrate or persist changes to their live library.
internal sealed class O2JamReadOnlySkinProbe : LegacySkin
{
    private readonly TextureStore textures;

    private O2JamReadOnlySkinProbe(IRenderer renderer, FileStore files)
        : base(new SkinInfo("Read-only visual probe"), null, files)
    {
        textures = new TextureStore(renderer, new TextureLoaderStore(files));
    }

    public static O2JamReadOnlySkinProbe Load(IRenderer renderer, string realmPath, Guid skinId)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var realm = Realm.GetInstance(new RealmConfiguration(realmPath) { IsDynamic = true, IsReadOnly = true });
        foreach (dynamic skin in realm.DynamicApi.All("Skin").Filter("ID == $0", skinId))
        {
            foreach (dynamic file in skin.Files)
            {
                string hash = file.File.Hash;
                files[(string)file.Filename] = Path.Combine(Path.GetDirectoryName(realmPath)!, "files", hash[..1], hash[..2], hash);
            }
        }
        if (files.Count == 0)
            throw new InvalidOperationException("Diagnostic skin was not found.");
        return new O2JamReadOnlySkinProbe(renderer, new FileStore(files));
    }

    public override Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        => textures.Get(componentName, wrapModeS, wrapModeT);

    protected override void Dispose(bool disposing)
    {
        textures.Dispose();
        base.Dispose(disposing);
    }

    private sealed class FileStore(Dictionary<string, string> files) : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => files.TryGetValue(name, out var path) ? File.ReadAllBytes(path) : null!;
        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));
        public Stream GetStream(string name) => files.TryGetValue(name, out var path) ? File.OpenRead(path) : null!;
        public IEnumerable<string> GetAvailableResources() => files.Keys;
        public void Dispose() { }
    }
}
