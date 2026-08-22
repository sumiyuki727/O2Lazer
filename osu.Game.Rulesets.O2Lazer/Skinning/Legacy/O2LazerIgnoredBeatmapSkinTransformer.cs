using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Legacy;

/// <summary>
/// Prevents osu! beatmap skin resources from participating in O2LAZER gameplay lookups.
/// </summary>
internal sealed class O2LazerIgnoredBeatmapSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

    public override Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

    public override ISample? GetSample(ISampleInfo sampleInfo) => null;

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup) => null;
}
