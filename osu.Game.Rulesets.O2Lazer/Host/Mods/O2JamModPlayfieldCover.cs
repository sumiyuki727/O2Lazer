using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Mods;

internal static class O2JamModPlayfieldCover
{
    internal static void Apply(DrawableRuleset<ManiaHitObject> drawableRuleset, BindableNumber<float> coverage,
                               CoverExpandDirection direction, Func<Drawable, PlayfieldCoveringWrapper> createCover)
    {
        var playfield = (ManiaPlayfield)drawableRuleset.Playfield;

        foreach (var column in playfield.Stages.SelectMany(stage => stage.Columns))
        {
            var hitObjectContainer = column.HitObjectContainer;
            if (hitObjectContainer.Parent is not Container parent)
                throw new InvalidOperationException($"Column {column.Index} hit object container is not attached.");
            parent.Remove(hitObjectContainer, false);

            var cover = createCover(hitObjectContainer);
            cover.RelativeSizeAxes = Axes.Both;
            cover.Direction = direction;
            cover.Coverage.BindTo(coverage);

            parent.Add(cover);
        }
    }
}
