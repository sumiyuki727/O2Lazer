namespace osu.Game.Rulesets.O2Lazer.Configuration;

/// <summary>
/// Read-only runtime projection for components created by osu! outside the ruleset dependency container.
/// </summary>
public static class O2JamRuntimeOptions
{
    public static bool UseO2JamLongNoteMissVisual { get; internal set; }

    public static bool UsePercyLongNoteBodyRepeat { get; internal set; }

}
