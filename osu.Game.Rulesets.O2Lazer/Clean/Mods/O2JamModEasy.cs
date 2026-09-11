using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModEasy : ManiaModEasy, IO2JamManiaScoreDependentMod
{
    public override LocalisableString Description => O2LazerStrings.ModEasyDescription;

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.ModEasyExtraLives), nameof(O2LazerStrings.ModEasyExtraLivesDescription))]
    public new Bindable<int> Retries => base.Retries;

    public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
    {
        get
        {
            if (!Retries.IsDefault)
                yield return (O2LazerStrings.ModEasyExtraLives, O2LazerStrings.ModEasyExtraLivesValue(Retries.Value));
        }
    }

    // Difficulty Adjust is part of MS, so the usual native DA exclusion cannot apply here.
    public override Type[] IncompatibleMods => [typeof(O2JamModHardRock), typeof(ModAccuracyChallenge)];
}
