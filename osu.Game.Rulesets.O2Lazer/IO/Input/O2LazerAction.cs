using System.ComponentModel;
using osu.Framework.Localisation;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.IO.Input;

/// <summary>
/// Native O2Lazer 7K input actions.
/// </summary>
public enum O2LazerAction
{
    [Description("Key 1")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey1))]
    Key1,

    [Description("Key 2")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey2))]
    Key2,

    [Description("Key 3")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey3))]
    Key3,

    [Description("Key 4")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey4))]
    Key4,

    [Description("Key 5")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey5))]
    Key5,

    [Description("Key 6")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey6))]
    Key6,

    [Description("Key 7")]
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.ActionKey7))]
    Key7,

}
