using System.Linq;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI.HudComponents;

public static class O2LazerDefaultHud
{
    internal static bool TryGetMainHudWithStage(ISkinComponentLookup lookup, System.Func<Drawable?> getUserLayout, out Drawable? mainHud)
    {
        if (lookup is GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents } directLookup)
        {
            mainHud = GetDrawableComponent(directLookup);
            return mainHud != null;
        }

        if (tryGetO2LazerMainHudLookupFromUserLookup(lookup, out var userLookup))
        {
            mainHud = stripLegacyO2LazerHud(getUserLayout() ?? GetDrawableComponent(userLookup));
            return true;
        }

        mainHud = null;
        return false;
    }

    private static Drawable? stripLegacyO2LazerHud(Drawable? drawable)
    {
        if (drawable is not Container container)
            return drawable;

        // Layouts saved by earlier releases may still contain these injected O2LAZER widgets.
        // Remove them at load time so upgrading does not require resetting the whole HUD.
        foreach (var residual in container.Where(child => child is O2LazerStageHud or O2LazerScoreGraph or O2LazerHitErrorMeter).ToArray())
            container.Remove(residual, true);

        return drawable;
    }

    public static Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is not GlobalSkinnableContainerLookup containerLookup)
            return null;

        switch (containerLookup.Lookup)
        {
            case GlobalSkinnableContainers.MainHUDComponents:
                // Let the currently selected osu! skin provide its native mania-style HUD
                // (including the health bar), so O2Jam does not replace it with a O2LAZER layout.
                return null;

            case GlobalSkinnableContainers.Playfield:
                return maniaStylePlayfield();

            case GlobalSkinnableContainers.SongSelect:
                break;
        }

        return null;
    }

    private static bool tryGetO2LazerMainHudLookupFromUserLookup(ISkinComponentLookup lookup, out GlobalSkinnableContainerLookup mainHudLookup)
    {
        mainHudLookup = null!;

        if (lookup.GetType().FullName != "osu.Game.Skinning.UserSkinComponentLookup")
            return false;

        var component = lookup.GetType().GetField("Component", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(lookup);

        if (component is not GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null } globalLookup)
            return false;

        mainHudLookup = globalLookup;
        return true;
    }

    private static Drawable maniaStylePlayfield()
    {
        return new DefaultSkinComponentsContainer(_ => { })
        {
            Children =
            [
                // O2LAZER notes use their own judgement lookup, so this is the only
                // ruleset-specific drawable needed on top of a mania-style HUD.
                new O2LazerJudgementDisplay(),
            ],
        };
    }

}
