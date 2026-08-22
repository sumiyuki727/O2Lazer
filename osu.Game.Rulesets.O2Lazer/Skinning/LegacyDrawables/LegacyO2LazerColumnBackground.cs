using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Legacy lane background and separator lines for one O2LAZER column.
/// </summary>
/// <remarks>
/// The lane background and separator widths come from legacy mania/O2LAZER skin.ini values.
/// </remarks>
internal sealed partial class LegacyO2LazerColumnBackground : CompositeDrawable
{
    internal O2LazerColumnSeparator LeftSeparator { get; }

    internal O2LazerColumnSeparator RightSeparator { get; }

    internal O2LazerHitTargetInsetContainer SeparatorContainer { get; }

    public LegacyO2LazerColumnBackground(O2LazerLegacySkinTransformer transformer, O2LazerSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, lookup)?.Value ?? Color4.White;
        var backgroundColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)?.Value ?? Color4.Black;

        var totalColumns = O2LazerLayout.GetTotalColumns(lookup.LayoutVariant);
        var isLastColumn = lookup.ColumnIndex == (O2LazerLayout.Is2P(lookup.LayoutVariant) ? 0 : totalColumns - 1);

        float leftLineWidth;
        float rightLineWidth;

        if (O2LazerLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int colIdx)
        {
            var (lIdx, rIdx) = O2LazerLayout.RemapColum2PGapIdx(colIdx, totalColumns);

            leftLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth,
                new O2LazerSkinComponentLookup(lookup.Component, lookup.LayoutVariant, lIdx))?.Value ?? 1;

            rightLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth,
                new O2LazerSkinComponentLookup(lookup.Component, lookup.LayoutVariant, rIdx))?.Value ?? 1;
        }
        else
        {
            leftLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, lookup)?.Value ?? 1;
            rightLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth, lookup)?.Value ?? 1;
        }

        var hasRightLine = (rightLineWidth > 0
                            && transformer.GetConfig<SkinConfiguration.LegacySetting, decimal>(SkinConfiguration.LegacySetting.Version)?.Value >= 2.4m)
                           || isLastColumn;

        InternalChildren =
        [
            LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box
            {
                RelativeSizeAxes = Axes.Both,
            }, backgroundColour),
            SeparatorContainer = new O2LazerHitTargetInsetContainer
            {
                Children =
                [
                    LeftSeparator = new O2LazerColumnSeparator(leftLineWidth, lineColour)
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                    },
                    RightSeparator = new O2LazerColumnSeparator(rightLineWidth, lineColour)
                    {
                        X = isLastColumn ? -0.16f : 0,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopLeft,
                        Alpha = hasRightLine ? 1 : 0,
                    },
                ],
            },
        ];
    }
}
