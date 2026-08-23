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
    private readonly Bindable<double>? scrollSpeed;
    private readonly Bindable<bool>? constantScrollSpeed;

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

            scrollSpeed = config.GetBindable<double>(O2LazerRulesetSetting.ScrollSpeed);
            scrollSpeed.BindValueChanged(value => playfield.ScrollController.SetConfiguredScrollSpeed(value.NewValue), true);

            constantScrollSpeed = config.GetBindable<bool>(O2LazerRulesetSetting.ConstantScrollSpeed);
            constantScrollSpeed.BindValueChanged(value =>
            {
                playfield.ScrollController.ConstantScrollActive = value.NewValue;
                playfield.RefreshAllLifetimes();
            }, true);

            unlockFrameRateLimit = config.GetBindable<bool>(O2LazerRulesetSetting.UnlockFrameRateLimit);
            unlockFrameRateLimit.BindValueChanged(onUnlockFrameRateLimitChanged, true);
        }

        playfield.ScrollController.SetPlaybackRate(playbackRate);
    }

    public void Dispose()
    {
        scrollSpeed?.UnbindAll();
        constantScrollSpeed?.UnbindAll();
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
