using osu.Framework.Localisation;
using osu.Game.Localisation;
using osu.Game.Rulesets.UI.Scrolling;

namespace osu.Game.Rulesets.O2Lazer.UI;

public enum O2LazerScrollingDirection
{
    [LocalisableDescription(typeof(RulesetSettingsStrings), nameof(RulesetSettingsStrings.ScrollingDirectionUp))]
    Up = ScrollingDirection.Up,

    [LocalisableDescription(typeof(RulesetSettingsStrings), nameof(RulesetSettingsStrings.ScrollingDirectionDown))]
    Down = ScrollingDirection.Down,
}
