using System;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Game.Rulesets.O2Lazer.Configuration;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

internal sealed class O2LazerGameplaySettingsController : IDisposable
{
    private readonly Bindable<bool>? unlockFrameRateLimit;
    private readonly GameHost host;
    private readonly FrameworkConfigManager frameworkConfig;

    private IDisposable? frameRateUnlockLease;

    internal O2LazerGameplaySettingsController(
        O2LazerRulesetConfigManager? config,
        O2LazerPlayfield playfield,
        double playbackRate,
        GameHost host,
        FrameworkConfigManager frameworkConfig)
    {
        this.host = host;
        this.frameworkConfig = frameworkConfig;

        if (config != null)
        {
            config.BindWith(O2LazerRulesetSetting.VisualOffset, playfield.VisualOffset);
            playfield.ScrollController.SetConfiguredScrollSpeed(config.Get<double>(O2LazerRulesetSetting.ScrollSpeed));
            playfield.ScrollController.ConstantScrollActive = config.Get<bool>(O2LazerRulesetSetting.ConstantScrollSpeed);

            unlockFrameRateLimit = config.GetBindable<bool>(O2LazerRulesetSetting.UnlockFrameRateLimit);
            unlockFrameRateLimit.BindValueChanged(onUnlockFrameRateLimitChanged, true);
        }

        playfield.ScrollController.SetPlaybackRate(playbackRate);
    }

    public void Dispose()
    {
        unlockFrameRateLimit?.UnbindAll();
        frameRateUnlockLease?.Dispose();
        frameRateUnlockLease = null;
    }

    private void onUnlockFrameRateLimitChanged(ValueChangedEvent<bool> unlocked)
    {
        frameRateUnlockLease?.Dispose();
        frameRateUnlockLease = unlocked.NewValue
            ? O2LazerFrameRateUnlock.Acquire(
                host,
                frameworkConfig.GetBindable<ExecutionMode>(FrameworkSetting.ExecutionMode),
                frameworkConfig.GetBindable<FrameSync>(FrameworkSetting.FrameSync))
            : null;
    }
}
