using System.Collections.Generic;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.IO.Input;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

public static class O2LazerKeyBindingConfiguration
{
    public static IEnumerable<int> AvailableVariants =>
    [
        (int)O2LazerLayoutVariant.O2Jam7K,
    ];

    public static KeyBinding[] GetDefaultKeyBindings(int variant) => variant switch
    {
        (int)O2LazerLayoutVariant.O2Jam7K => bindingsO2Jam7K(),
        _ => [],
    };

    public static int? ActionToColumn(O2LazerAction action, O2LazerLayoutVariant variant = O2LazerLayoutVariant.O2Jam7K) => mapO2Jam7K(action);

    public static O2LazerAction? ActionForColumn(O2LazerLayoutVariant layout, int column) => actionForO2Jam7K(column);

    private static KeyBinding[] bindingsO2Jam7K() =>
    [
        new(InputKey.S, O2LazerAction.Key1),
        new(InputKey.D, O2LazerAction.Key2),
        new(InputKey.F, O2LazerAction.Key3),
        new(InputKey.Space, O2LazerAction.Key4),
        new(InputKey.J, O2LazerAction.Key5),
        new(InputKey.K, O2LazerAction.Key6),
        new(InputKey.L, O2LazerAction.Key7),
    ];

    private static int? mapO2Jam7K(O2LazerAction action) => action switch
    {
        O2LazerAction.Key1 => 0,
        O2LazerAction.Key2 => 1,
        O2LazerAction.Key3 => 2,
        O2LazerAction.Key4 => 3,
        O2LazerAction.Key5 => 4,
        O2LazerAction.Key6 => 5,
        O2LazerAction.Key7 => 6,
        _ => null,
    };

    private static O2LazerAction? actionForO2Jam7K(int column) => column switch
    {
        0 => O2LazerAction.Key1,
        1 => O2LazerAction.Key2,
        2 => O2LazerAction.Key3,
        3 => O2LazerAction.Key4,
        4 => O2LazerAction.Key5,
        5 => O2LazerAction.Key6,
        6 => O2LazerAction.Key7,
        _ => null,
    };
}
