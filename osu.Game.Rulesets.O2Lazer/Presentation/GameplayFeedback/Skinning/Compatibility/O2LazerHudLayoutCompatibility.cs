using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI.HudComponents;

/// <summary>
/// Keeps pre-rewrite skin layouts loadable while using the native mania combo implementation.
/// </summary>
public partial class O2LazerComboCounter : O2JamComboCounter, ISerialisableDrawable
{
    public bool IsEditable => false;
}

/// <summary>
/// Retains the old serialised type name. Current gameplay renders judgements in the native mania
/// playfield, so the former second judgement renderer intentionally has no visual content.
/// </summary>
public sealed partial class O2LazerJudgementDisplay : CompositeDrawable, ISerialisableDrawable
{
    public bool IsEditable => false;

    public bool UsesFixedAnchor { get; set; }

    public O2LazerJudgementDisplay()
    {
        AlwaysPresent = true;
        RelativeSizeAxes = Axes.Both;
    }
}
