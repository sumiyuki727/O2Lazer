using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

internal sealed class O2LazerGameplayCompletionController : IDisposable
{
    private readonly ScoreProcessor scoreProcessor;
    private readonly HealthProcessor healthProcessor;
    private readonly GameplayState gameplayState;
    private readonly Score? replayScore;
    private readonly O2LazerRulesetConfigManager? config;

    internal O2LazerGameplayCompletionController(
        ScoreProcessor scoreProcessor,
        HealthProcessor healthProcessor,
        GameplayState gameplayState,
        Score? replayScore,
        O2LazerRulesetConfigManager? config)
    {
        this.scoreProcessor = scoreProcessor;
        this.healthProcessor = healthProcessor;
        this.gameplayState = gameplayState;
        this.replayScore = replayScore;
        this.config = config;
        scoreProcessor.HasCompleted.BindValueChanged(onPlayCompleted);
    }

    public void Dispose() => scoreProcessor.HasCompleted.ValueChanged -= onPlayCompleted;

    internal static double AddVisualOffsetSuggestion(O2LazerRulesetConfigManager config, double medianHitError)
    {
        var suggestion = O2LazerRulesetRuntime.VisualOffsetSuggestions.Add(
            medianHitError,
            config.Get<double>(O2LazerRulesetSetting.VisualOffset));

        if (config.Get<bool>(O2LazerRulesetSetting.AutomaticallyAdjustVisualOffset))
            config.SetValue(O2LazerRulesetSetting.VisualOffset, suggestion);

        return suggestion;
    }

    private void onPlayCompleted(ValueChangedEvent<bool> _)
    {
        if (healthProcessor is O2LazerHealthProcessor o2lazerHealthProcessor)
        {
            var passed = o2lazerHealthProcessor.HasPassedAtEnd();
            scoreProcessor.PopulateScore(gameplayState.Score.ScoreInfo);

            if (!passed)
                scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);

            recordVisualOffsetSuggestion();
            return;
        }

        if (healthProcessor.Health.Value < 0.8)
            scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);

        recordVisualOffsetSuggestion();
    }

    private void recordVisualOffsetSuggestion()
    {
        if (replayScore != null || config == null || gameplayState.Mods.Any(mod => !mod.UserPlayable))
            return;

        var hitEvents = gameplayState.Score.ScoreInfo.HitEvents;

        if (hitEvents.Count(HitEventExtensions.AffectsUnstableRate) < 50
            || hitEvents.CalculateMedianHitError() is not double medianHitError)
            return;

        AddVisualOffsetSuggestion(config, medianHitError);
    }
}
