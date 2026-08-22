using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.O2Lazer;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Select;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public static class O2LazerRankedDisplayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.RankedDisplay";

    private static readonly object install_lock = new();

    private static FieldInfo? rankingDisplayField;
    private static FieldInfo? beatmapAttributesField;
    private static FieldInfo? modDisplayBarField;
    private static FieldInfo? unrankedBadgeField;
    private static FieldInfo? modDisplayField;
    private static PropertyInfo? multiplierTextProperty;
    private static PropertyInfo? coloursProperty;
    private static PropertyInfo? beatmapProperty;
    private static FieldInfo? buttonWidthField;

    public static bool IsInstalled { get; private set; }

    private static readonly ConditionalWeakTable<FooterButtonMods, BadgeState> badge_states = new();

    private enum BadgePosition
    {
        LeftUpper,
        RightUpper,
        RightLower,
    }

    private sealed class BadgeState
    {
        public BadgePosition? Position { get; set; }
        public bool Initialized { get; set; }
    }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var footerTarget = AccessTools.Method(typeof(ModSelectFooterContent), "updateInformation");
            var footerPrefixMethod = AccessTools.Method(typeof(O2LazerRankedDisplayPatcher), nameof(footerPrefix));
            rankingDisplayField = AccessTools.Field(typeof(ModSelectFooterContent), "rankingInformationDisplay");
            beatmapAttributesField = AccessTools.Field(typeof(ModSelectFooterContent), "beatmapAttributesDisplay");

            var footerInstalled = false;
            var footerMissing = new[]
            {
                (name: "ModSelectFooterContent.updateInformation", member: (MemberInfo?)footerTarget),
                (name: "O2LazerRankedDisplayPatcher.footerPrefix", member: footerPrefixMethod),
                (name: "ModSelectFooterContent.rankingInformationDisplay", member: rankingDisplayField),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (footerMissing.Length == 0)
            {
                new Harmony(harmony_id).Patch(footerTarget, prefix: new HarmonyMethod(footerPrefixMethod));
                footerInstalled = true;
            }
            else
                O2LazerLogger.Log("O2LAZER RankedDisplayPatcher: Cannot install footer patch. Missing: " + string.Join(", ", footerMissing), level: LogLevel.Error);

            var modsTarget = AccessTools.Method(typeof(FooterButtonMods), "updateDisplay");
            var modsPrefixMethod = AccessTools.Method(typeof(O2LazerRankedDisplayPatcher), nameof(modsPrefix));
            modDisplayBarField = AccessTools.Field(typeof(FooterButtonMods), "modDisplayBar");
            unrankedBadgeField = AccessTools.Field(typeof(FooterButtonMods), "unrankedBadge");
            modDisplayField = AccessTools.Field(typeof(FooterButtonMods), "modDisplay");
            multiplierTextProperty = AccessTools.Property(typeof(FooterButtonMods), "multiplierText");
            coloursProperty = AccessTools.Property(typeof(FooterButtonMods), "colours");
            beatmapProperty = AccessTools.Property(typeof(FooterButtonMods), "beatmap");
            buttonWidthField = AccessTools.Field(typeof(ScreenFooterButton), "BUTTON_WIDTH");

            var modsInstalled = false;
            var modsMissing = new[]
            {
                (name: "FooterButtonMods.updateDisplay", member: (MemberInfo?)modsTarget),
                (name: "O2LazerRankedDisplayPatcher.modsPrefix", member: modsPrefixMethod),
                (name: "FooterButtonMods.modDisplayBar", member: modDisplayBarField),
                (name: "FooterButtonMods.unrankedBadge", member: unrankedBadgeField),
                (name: "FooterButtonMods.modDisplay", member: modDisplayField),
                (name: "FooterButtonMods.multiplierText", member: multiplierTextProperty),
                (name: "FooterButtonMods.colours", member: coloursProperty),
                (name: "FooterButtonMods.beatmap", member: beatmapProperty),
                (name: "ScreenFooterButton.BUTTON_WIDTH", member: buttonWidthField),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (modsMissing.Length == 0)
            {
                new Harmony(harmony_id).Patch(modsTarget, prefix: new HarmonyMethod(modsPrefixMethod));
                modsInstalled = true;
            }
            else
                O2LazerLogger.Log("O2LAZER RankedDisplayPatcher: Cannot install mods footer badge patch. Missing: " + string.Join(", ", modsMissing), level: LogLevel.Error);

            IsInstalled = footerInstalled || modsInstalled;
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool footerPrefix(ModSelectFooterContent __instance)
    {
        if (__instance.Ruleset.Value?.ShortName != Constant.SHORT_NAME)
            return true;

        if (rankingDisplayField?.GetValue(__instance) is not RankingInformationDisplay display)
            return true;

        var workingBeatmap = __instance.Beatmap.Value;
        if (workingBeatmap == null)
            return true;

        var scoreMultiplierCalculator = __instance.Ruleset.Value?.CreateInstance().CreateScoreMultiplierCalculator(
            new ScoreMultiplierContext(workingBeatmap.BeatmapInfo.Difficulty));
        display.ModMultiplier.Value = scoreMultiplierCalculator?.CalculateFor(__instance.ActiveMods.Value) ?? 1;
        display.Ranked.Value = O2LazerRulesetRuntime.CanAwardPerformancePoints(__instance.ActiveMods.Value);

        if (beatmapAttributesField?.GetValue(__instance) is BeatmapAttributesDisplay attributes)
            attributes.Mods.Value = __instance.ActiveMods.Value;

        return false;
    }

    // ReSharper disable once InconsistentNaming
    private static bool modsPrefix(FooterButtonMods __instance)
    {
        var buttonWidth = Convert.ToSingle(buttonWidthField!.GetRawConstantValue());
        var badgeState = badge_states.GetOrCreateValue(__instance);
        var firstSet = !badgeState.Initialized;
        var previous = badgeState.Position;

        var isO2Lazer = __instance.Ruleset.Value?.ShortName == Constant.SHORT_NAME;
        var isO2LazerLike = __instance.Ruleset.Value?.CreateInstance() is IO2LazerStyleUnrankedBadgeRuleset;
        var hasMods = __instance.Mods.Value.Count > 0;
        var hasUnrankedMods = __instance.Mods.Value.Any(mod => !mod.Ranked);

        BadgePosition target;

        if (isO2Lazer)
        {
            if (O2LazerRulesetRuntime.CanAwardPerformancePoints(__instance.Mods.Value))
                target = BadgePosition.RightLower;
            else if (hasMods)
                target = BadgePosition.RightUpper;
            else
                target = BadgePosition.LeftUpper;
        }
        else if (isO2LazerLike)
            target = !hasMods ? BadgePosition.LeftUpper : hasUnrankedMods ? BadgePosition.RightUpper : BadgePosition.RightLower;
        else
            target = hasUnrankedMods ? BadgePosition.RightUpper : BadgePosition.RightLower;

        badgeState.Initialized = true;
        badgeState.Position = target;

        if (modDisplayBarField!.GetValue(__instance) is not Drawable modDisplayBar
            || unrankedBadgeField!.GetValue(__instance) is not Drawable unrankedBadge
            || modDisplayField!.GetValue(__instance) is not Drawable modDisplay
            || multiplierTextProperty!.GetValue(__instance) is not OsuSpriteText multiplierText
            || coloursProperty!.GetValue(__instance) is not OsuColour colours)
            return true;

        const double duration = 240;
        const Easing easing = Easing.OutQuint;

        if (hasMods)
        {
            modDisplayBar.MoveToY(-5, duration, Easing.OutQuint);
            modDisplayBar.FadeIn(duration, easing);
            modDisplay.FadeIn(duration, easing);

            var extraWidth = target == BadgePosition.RightUpper ? 5 + unrankedBadge.DrawWidth : 0;
            __instance.ResizeWidthTo(buttonWidth + extraWidth, duration, easing);
        }
        else
        {
            modDisplayBar.MoveToY(20, duration, Easing.OutQuint);
            modDisplayBar.FadeOut(duration, easing);
            modDisplay.FadeOut(duration, easing);
            __instance.ResizeWidthTo(buttonWidth, duration, easing);
        }

        if (firstSet)
            setBadgePosition(unrankedBadge, target, buttonWidth);
        else if (previous != target)
            animateBadgeTransition(unrankedBadge, previous!.Value, target, buttonWidth, duration, easing);

        var workingBeatmap = beatmapProperty?.GetValue(__instance) as WorkingBeatmap;
        var scoreMultiplierCalculator = __instance.Ruleset.Value?.CreateInstance().CreateScoreMultiplierCalculator(
            new ScoreMultiplierContext(workingBeatmap?.BeatmapInfo.Difficulty ?? new BeatmapDifficulty()));
        double multiplier = scoreMultiplierCalculator?.CalculateFor(__instance.Mods.Value) ?? 1;
        multiplierText.Text = ModUtils.FormatScoreMultiplier(multiplier);

        if (multiplier > 1)
            multiplierText.FadeColour(colours.Red1, duration, easing);
        else if (multiplier < 1)
            multiplierText.FadeColour(colours.Lime1, duration, easing);
        else
            multiplierText.FadeColour(Color4.White, duration, easing);

        return false;
    }

    private static void setBadgePosition(Drawable badge, BadgePosition position, float buttonWidth)
    {
        badge.ClearTransforms();
        badge.X = 0;

        switch (position)
        {
            case BadgePosition.LeftUpper:
                badge.Margin = new MarginPadding();
                badge.Y = -5;
                badge.Alpha = 1;
                break;

            case BadgePosition.RightUpper:
                badge.Margin = new MarginPadding { Left = buttonWidth + 5f };
                badge.Y = -5;
                badge.Alpha = 1;
                break;

            case BadgePosition.RightLower:
                badge.Margin = new MarginPadding { Left = buttonWidth + 5f };
                badge.Y = 20;
                badge.Alpha = 0;
                break;
        }
    }

    private static void animateBadgeTransition(
        Drawable badge,
        BadgePosition from,
        BadgePosition to,
        float buttonWidth,
        double duration,
        Easing easing)
    {
        var rightMargin = new MarginPadding { Left = buttonWidth + 5f };
        var leftMargin = new MarginPadding();

        switch ((from, to))
        {
            case (BadgePosition.RightUpper, BadgePosition.LeftUpper):
                badge.ClearTransforms();
                badge.X = 0;
                badge.Margin = rightMargin;
                badge.Y = -5;
                badge.Alpha = 1;
                badge.TransformTo("Margin", leftMargin, duration, easing);
                break;

            case (BadgePosition.LeftUpper, BadgePosition.RightUpper):
                badge.ClearTransforms();
                badge.X = 0;
                badge.Margin = leftMargin;
                badge.Y = -5;
                badge.Alpha = 1;
                badge.TransformTo("Margin", rightMargin, duration, easing);
                break;

            case (BadgePosition.RightLower, BadgePosition.RightUpper):
                badge.ClearTransforms();
                badge.X = 0;
                badge.Margin = rightMargin;
                badge.Y = 20;
                badge.Alpha = 0;
                badge.MoveToY(-5, duration, easing);
                badge.FadeIn(duration, easing);
                break;

            case (BadgePosition.RightUpper, BadgePosition.RightLower):
                badge.ClearTransforms();
                badge.X = 0;
                badge.Margin = rightMargin;
                badge.Y = -5;
                badge.Alpha = 1;
                badge.MoveToY(20, duration, easing);
                badge.FadeOut(duration, easing);
                break;

            case (BadgePosition.LeftUpper, BadgePosition.RightLower):
                badge.ClearTransforms();
                badge.X = 0;
                badge.Margin = leftMargin;
                badge.Y = -5;
                badge.Alpha = 1;
                badge.MoveToY(20, duration, easing);
                badge.FadeOut(duration, easing);
                badge.Delay(duration).Then().TransformTo("Margin", rightMargin, 0);
                break;

            case (BadgePosition.RightLower, BadgePosition.LeftUpper):
                badge.ClearTransforms();
                badge.X = 0;
                badge.Margin = leftMargin;
                badge.Y = 20;
                badge.Alpha = 0;
                badge.MoveToY(-5, duration, easing);
                badge.FadeIn(duration, easing);
                break;

            default:
                setBadgePosition(badge, to, buttonWidth);
                break;
        }
    }
    // ReSharper restore InconsistentNaming
}



