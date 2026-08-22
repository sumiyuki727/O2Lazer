using osu.Framework;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Mixing;

/// <summary>
///     Describes the platforms where the ruleset can use its native BASS mixer path.
/// </summary>
internal static class O2LazerAudioPlatform
{
    internal static bool SupportsNativeBass =>
        RuntimeInfo.OS is RuntimeInfo.Platform.Linux or RuntimeInfo.Platform.Windows;
}
