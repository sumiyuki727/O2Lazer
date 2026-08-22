using System;
using osu.Framework.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal sealed class O2LazerResolvedDrawableFactory(Func<Drawable?> create)
{
    public Drawable? Create() => create();
}
