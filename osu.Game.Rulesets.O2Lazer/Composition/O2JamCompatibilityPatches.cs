using System.Collections.Generic;
using osu.Game.Rulesets;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Icons;
using static osu.Game.Rulesets.O2Lazer.O2JamPatchRequirement;

namespace osu.Game.Rulesets.O2Lazer;

internal static class O2JamCompatibilityPatches
{
    private static readonly object installLock = new();

    private static readonly O2JamPatchInstaller[] installers =
    [
        new(nameof(O2JamWorkingBeatmapHook), O2JamWorkingBeatmapHook.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamDifficultyIconPatch), O2JamDifficultyIconPatch.InstallOnce, Optional),
        new(nameof(O2JamComboCompatibilityPatches), O2JamComboCompatibilityPatches.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamHitErrorMeterPatch), O2JamHitErrorMeterPatch.InstallOnce, Optional),
        new(nameof(O2JamSongSelectRankPatch), O2JamSongSelectRankPatch.InstallOnce, Optional),
        new(nameof(O2JamBeatmapBoundaryPatches), O2JamBeatmapBoundaryPatches.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamReplayPersistencePatch), O2JamReplayPersistencePatch.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamPerformanceEligibilityPatch), O2JamPerformanceEligibilityPatch.InstallOnce, Optional),
        new(nameof(O2JamPlayerSettingsPatch), O2JamPlayerSettingsPatch.InstallOnce, Optional),
        new(nameof(O2JamModSelectAttributesPatch), O2JamModSelectAttributesPatch.InstallOnce, Optional),
        new(nameof(O2JamManiaScoreRulesetSelectionPatch), O2JamManiaScoreRulesetSelectionPatch.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamManiaScoreModAvailabilityPatch), O2JamManiaScoreModAvailabilityPatch.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamManiaScoreStatisticsPatch), O2JamManiaScoreStatisticsPatch.InstallOnce, Optional),
        new(nameof(O2JamDifficultyCachePatch), O2JamDifficultyCachePatch.InstallOnce, Optional),
        new(nameof(O2JamNativeDifficultyPersistencePatch), O2JamNativeDifficultyPersistencePatch.InstallOnce, Optional),
        new(nameof(O2JamFileVerificationPatch), O2JamFileVerificationPatch.InstallOnce, Optional),
        new(nameof(O2JamStarRatingDisplayPatch), O2JamStarRatingDisplayPatch.InstallOnce, Optional),
        new(nameof(O2JamStarRatingPresentationPatch), O2JamStarRatingPresentationPatch.InstallOnce, Optional),
        new(nameof(O2JamCarouselTransitionPatch), O2JamCarouselTransitionPatch.InstallOnce, Optional),
        new(nameof(O2JamModeSwitchDirectionPatch), O2JamModeSwitchDirectionPatch.InstallOnce, Optional),
        new(nameof(O2JamLevelFilterPatch), O2JamLevelFilterPatch.InstallOnce, Optional),
        new(nameof(O2JamLevelSortPatch), O2JamLevelSortPatch.InstallOnce, Optional),
        new(nameof(O2JamLevelGroupPatch), O2JamLevelGroupPatch.InstallOnce, Optional),
        new(nameof(O2JamHitSampleLookupPatch), O2JamHitSampleLookupPatch.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamResolvedKeySoundPatch), O2JamResolvedKeySoundPatch.InstallOnce, RequiredForGameplay),
        new(nameof(O2JamEditorAccessPatch), O2JamEditorAccessPatch.InstallOnce, Optional),
    ];

    private static bool attempted;
    internal static bool IsInstalled { get; private set; }
    internal static bool CanPlay { get; private set; }

    internal static IReadOnlyList<string> FailedPatches { get; private set; } = [];
    internal static IReadOnlyList<string> FailedGameplayPatches { get; private set; } = [];

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (attempted)
                return CanPlay;

            attempted = true;
            var result = O2JamPatchInstallationPolicy.Install(installers);
            FailedPatches = result.FailedPatches;
            FailedGameplayPatches = O2JamPatchRollback.HasFailed
                ? [.. result.FailedGameplayPatches, "Harmony rollback"]
                : result.FailedGameplayPatches;
            IsInstalled = FailedPatches.Count == 0;
            CanPlay = FailedGameplayPatches.Count == 0;
            if (!CanPlay)
                Logger.Log($"O2Lazer gameplay is unavailable because required patches failed: {string.Join(", ", FailedGameplayPatches)}", LoggingTarget.Runtime, LogLevel.Error);
            if (!IsInstalled && CanPlay)
                Logger.Log($"O2Lazer presentation features are incomplete: {string.Join(", ", FailedPatches)}", LoggingTarget.Runtime, LogLevel.Important);

            return CanPlay;
        }
    }

    internal static void RequireGameplay()
    {
        if (!InstallOnce())
            throw new RulesetLoadException($"O2Lazer cannot start gameplay while required compatibility patches are unavailable: {string.Join(", ", FailedGameplayPatches)}");
    }
}
