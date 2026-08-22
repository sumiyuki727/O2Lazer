using System;
using osu.Framework.Bindables;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

internal sealed class O2LazerVisualOffsetSuggestionStore
{
    private const int maximum_history_count = 50;

    public IBindableList<DataPoint> History => history;

    private readonly BindableList<DataPoint> history = [];

    public double Add(double medianHitError, double visualOffset)
    {
        if (history.Count >= maximum_history_count)
            history.RemoveAt(0);

        var suggestion = Math.Clamp(
            visualOffset + medianHitError,
            O2LazerRulesetConfigManager.MIN_VISUAL_OFFSET,
            O2LazerRulesetConfigManager.MAX_VISUAL_OFFSET);

        history.Add(new DataPoint(medianHitError, visualOffset, suggestion));
        return suggestion;
    }

    public void Clear() => history.Clear();

    public readonly record struct DataPoint(double MedianHitError, double VisualOffset, double SuggestedVisualOffset);
}
