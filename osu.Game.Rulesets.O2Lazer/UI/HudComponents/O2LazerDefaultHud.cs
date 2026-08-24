using System.Linq;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.UI.HudComponents;

internal enum O2LazerComboStyle
{
    Legacy,
    Argon,
}

public static class O2LazerDefaultHud
{
    internal static Drawable CreateRulesetMainHud(O2LazerComboStyle style) => style switch
    {
        O2LazerComboStyle.Argon => createArgonMainHud(),
        _ => createLegacyMainHud(),
    };

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

    private static Drawable createLegacyMainHud()
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            var spectatorList = container.OfType<SpectatorList>().FirstOrDefault();
            var leaderboard = container.OfType<DrawableGameplayLeaderboard>().FirstOrDefault();

            if (spectatorList != null)
            {
                spectatorList.Anchor = Anchor.BottomLeft;
                spectatorList.Origin = Anchor.BottomLeft;
                spectatorList.Position = new Vector2(10, -10);
            }

            if (leaderboard != null)
            {
                leaderboard.Anchor = Anchor.CentreLeft;
                leaderboard.Origin = Anchor.CentreLeft;
                leaderboard.X = 10;
            }

            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            new O2LazerComboCounter(),
            new SpectatorList(),
            new DrawableGameplayLeaderboard(),
        };
    }

    private static Drawable createArgonMainHud()
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            var combo = container.OfType<O2LazerArgonComboCounter>().FirstOrDefault();
            var leaderboard = container.OfType<DrawableGameplayLeaderboard>().FirstOrDefault();
            var spectatorList = container.OfType<SpectatorList>().FirstOrDefault();

            if (combo != null)
            {
                combo.ShowLabel.Value = false;
                combo.Anchor = Anchor.TopCentre;
                combo.Origin = Anchor.Centre;
                combo.Y = 200;
            }

            if (leaderboard != null)
                leaderboard.Position = new Vector2(36, 115);

            if (spectatorList != null)
                spectatorList.Position = new Vector2(36, -66);

            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            new DrawableGameplayLeaderboard(),
            new O2LazerArgonComboCounter(),
            new SpectatorList
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
            },
        };
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
