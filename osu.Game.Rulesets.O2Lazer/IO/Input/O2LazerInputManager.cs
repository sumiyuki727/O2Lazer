using System.Linq;
using osu.Framework.Input.Bindings;
using osu.Game.Screens.Play.HUD;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.IO.Input;

/// <summary>
///     Input manager for native O2LAZER actions.
/// </summary>
/// <remarks>
///     This is intentionally small: it only provides O2LAZER action binding infrastructure so the ruleset
///     no longer borrows osu!mania's input manager. Column-to-action gameplay routing will live in the
///     native O2LAZER playfield/drawable layer as that grows.
/// </remarks>
public partial class O2LazerInputManager(RulesetInfo ruleset, int variant)
    : RulesetInputManager<O2LazerAction>(ruleset, variant, SimultaneousBindingMode.Unique), ICanAttachHUDPieces
{
    void ICanAttachHUDPieces.Attach(InputCountController inputCountController) => attach(inputCountController);

    private void attach(InputCountController inputCountController)
    {
        var triggers = KeyBindingContainer.DefaultKeyBindings
                                          .Select(binding => binding.GetAction<O2LazerAction>())
                                          .Distinct()
                                          .Where(action => action is not O2LazerAction.IncreaseScrollSpeed and not O2LazerAction.DecreaseScrollSpeed)
                                          .Select(createTrigger)
                                          .ToArray();

        KeyBindingContainer.AddRange(triggers);
        inputCountController.AddRange(triggers);
    }

    private static KeyCounterActionTrigger<O2LazerAction> createTrigger(O2LazerAction action)
    {
        var trigger = new KeyCounterActionTrigger<O2LazerAction>(action);

        // The generic trigger names are derived from the enum value.
        // Rename gameplay keys so the HUD shows B1..B7.
        if (action is >= O2LazerAction.Key1 and <= O2LazerAction.Key7)
            trigger.Name = $"B{(int)action - (int)O2LazerAction.Key1 + 1}";

        return trigger;
    }
}

