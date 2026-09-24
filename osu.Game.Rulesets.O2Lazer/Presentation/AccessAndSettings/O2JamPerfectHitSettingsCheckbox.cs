using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.UI;

public sealed partial class O2JamPerfectHitSettingsCheckbox : SettingsCheckbox
{
    [Resolved(canBeNull: true)]
    private Bindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

    public O2JamPerfectHitSettingsCheckbox()
    {
        CanBeShown.Value = false;
        MatchingFilter = false;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        selectedMods?.BindValueChanged(e =>
        {
            CanBeShown.Value = e.NewValue.OfType<O2JamModManiaScore>().Any();
            // The native mod customisation panel does not apply a search filter to its settings.
            MatchingFilter = CanBeShown.Value;
        }, true);
    }
}
