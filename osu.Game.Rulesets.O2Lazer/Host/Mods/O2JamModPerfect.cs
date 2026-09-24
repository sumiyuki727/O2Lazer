using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModPerfect : ManiaModPerfect
{
    public override LocalisableString Description => O2LazerStrings.ModPerfectDescription;

    // Keep mania's bindable and serialised setting; only its settings control depends on MS.
    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.ModPerfectRequirePerfectHits), SettingControlType = typeof(O2JamPerfectHitSettingsCheckbox))]
    public new BindableBool RequirePerfectHits => base.RequirePerfectHits;
}
