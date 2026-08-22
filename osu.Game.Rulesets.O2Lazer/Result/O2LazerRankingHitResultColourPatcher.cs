using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Ranking.Expanded.Statistics;

namespace osu.Game.Rulesets.O2Lazer.Result;

public static class O2LazerRankingHitResultColourPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.RankingHitResultColours";
    private static readonly object install_lock = new();

    private static FieldInfo? scoreField;
    private static PropertyInfo? headerTextProperty;
    private static PropertyInfo? internalChildrenProperty;
    private static MethodInfo? scheduleMethod;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(ExpandedPanelMiddleContent), "load");
            var postfixMethod = AccessTools.Method(typeof(O2LazerRankingHitResultColourPatcher), nameof(postfix));
            scoreField = AccessTools.Field(typeof(ExpandedPanelMiddleContent), "score");
            headerTextProperty = AccessTools.Property(typeof(StatisticDisplay), "HeaderText");
            internalChildrenProperty = AccessTools.Property(typeof(CompositeDrawable), "InternalChildren");
            scheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]);

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                ("ExpandedPanelMiddleContent.load", target),
                ("O2LazerRankingHitResultColourPatcher.postfix", postfixMethod),
                ("ExpandedPanelMiddleContent.score", scoreField),
                ("StatisticDisplay.HeaderText", headerTextProperty),
                ("CompositeDrawable.InternalChildren", internalChildrenProperty),
                ("Drawable.Schedule", scheduleMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log("O2LAZER RankingHitResultColourPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers), level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                O2LazerLogger.Error(ex, "O2LAZER RankingHitResultColourPatcher: Failed to install Harmony patch. Ranking hit result colours will remain osu! defaults.");
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    private static void postfix(ExpandedPanelMiddleContent __instance)
    {
        if (scoreField?.GetValue(__instance) is not ScoreInfo score || score.Ruleset.ShortName != Constant.SHORT_NAME)
            return;

        scheduleMethod?.Invoke(__instance, [new Action(() => apply(__instance, score))]);
    }

    private static void apply(ExpandedPanelMiddleContent panel, ScoreInfo score)
    {
        foreach (var statistic in childrenOfType<HitResultStatistic>(panel))
        {
            if (headerTextProperty?.GetValue(statistic) is SpriteText headerText)
                headerText.Colour = O2LazerHitResultColours.ForScore(score, statistic.Result);
        }
    }

    private static T[] childrenOfType<T>(Drawable drawable)
    {
        if (drawable is not CompositeDrawable composite || internalChildrenProperty?.GetValue(composite) is not IReadOnlyList<Drawable> children)
            return [];

        return children.SelectMany(child =>
        {
            var nested = childrenOfType<T>(child);
            return child is T match ? nested.Prepend(match) : nested;
        }).ToArray();
    }
}
