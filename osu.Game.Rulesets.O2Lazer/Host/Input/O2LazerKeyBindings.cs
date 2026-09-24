using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.Mania;

namespace osu.Game.Rulesets.O2Lazer.Input;

public static class O2LazerKeyBindings
{
    public static KeyBinding[] Defaults =>
    [
        new(InputKey.S, ManiaAction.Key1),
        new(InputKey.D, ManiaAction.Key2),
        new(InputKey.F, ManiaAction.Key3),
        new(InputKey.Space, ManiaAction.Key4),
        new(InputKey.J, ManiaAction.Key5),
        new(InputKey.K, ManiaAction.Key6),
        new(InputKey.L, ManiaAction.Key7),
    ];
}
