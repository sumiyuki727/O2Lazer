using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.UI;

public sealed partial class O2JamRandomAlgorithmDropdown : SettingsEnumDropdown<O2JamRandomAlgorithm>
{
    public O2JamRandomAlgorithmDropdown()
    {
        Items = [O2JamRandomAlgorithm.Native, O2JamRandomAlgorithm.RRandom, O2JamRandomAlgorithm.Panic];
        Current.BindValueChanged(change =>
        {
            // Replacing Items would reset hidden replay values to the first item. Native incremental
            // changes preserve the recorded algorithm while exposing the retired O2Jam value only when selected.
            if (change.NewValue == O2JamRandomAlgorithm.O2Jam && change.OldValue != O2JamRandomAlgorithm.O2Jam)
                Control.AddDropdownItem(O2JamRandomAlgorithm.O2Jam);
            else if (change.OldValue == O2JamRandomAlgorithm.O2Jam && change.NewValue != O2JamRandomAlgorithm.O2Jam)
                Control.RemoveDropdownItem(O2JamRandomAlgorithm.O2Jam);
        });
    }

    protected override OsuDropdown<O2JamRandomAlgorithm> CreateDropdown() => new AlgorithmControl();

    private partial class AlgorithmControl : DropdownControl
    {
        protected override DropdownMenu CreateMenu() => base.CreateMenu().With(menu => menu.MaxHeight = 100);
    }
}
