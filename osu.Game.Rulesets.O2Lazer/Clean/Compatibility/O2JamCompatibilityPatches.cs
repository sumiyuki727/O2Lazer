using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Icons;

namespace osu.Game.Rulesets.O2Lazer;

internal static class O2JamCompatibilityPatches
{
    private static readonly object installLock = new();

    private static readonly (string Name, Func<bool> Install)[] installers =
    [
        (nameof(O2JamWorkingBeatmapHook), O2JamWorkingBeatmapHook.InstallOnce),
        (nameof(O2JamDifficultyIconPatch), O2JamDifficultyIconPatch.InstallOnce),
        (nameof(O2JamComboCompatibilityPatches), O2JamComboCompatibilityPatches.InstallOnce),
        (nameof(O2JamSongSelectRankPatch), O2JamSongSelectRankPatch.InstallOnce),
        (nameof(O2JamBeatmapBoundaryPatches), O2JamBeatmapBoundaryPatches.InstallOnce),
        (nameof(O2JamReplayPersistencePatch), O2JamReplayPersistencePatch.InstallOnce),
        (nameof(O2JamPerformanceEligibilityPatch), O2JamPerformanceEligibilityPatch.InstallOnce),
        (nameof(O2JamPlayerSettingsPatch), O2JamPlayerSettingsPatch.InstallOnce),
        (nameof(O2JamModSelectAttributesPatch), O2JamModSelectAttributesPatch.InstallOnce),
        (nameof(O2JamManiaScoreRulesetSelectionPatch), O2JamManiaScoreRulesetSelectionPatch.InstallOnce),
        (nameof(O2JamManiaScoreModAvailabilityPatch), O2JamManiaScoreModAvailabilityPatch.InstallOnce),
        (nameof(O2JamManiaScoreStatisticsPatch), O2JamManiaScoreStatisticsPatch.InstallOnce),
        (nameof(O2JamDifficultyCachePatch), O2JamDifficultyCachePatch.InstallOnce),
        (nameof(O2JamStarRatingDisplayPatch), O2JamStarRatingDisplayPatch.InstallOnce),
        (nameof(O2JamStarRatingPresentationPatch), O2JamStarRatingPresentationPatch.InstallOnce),
        (nameof(O2JamCarouselTransitionPatch), O2JamCarouselTransitionPatch.InstallOnce),
        (nameof(O2JamLevelFilterPatch), O2JamLevelFilterPatch.InstallOnce),
        (nameof(O2JamLevelSortPatch), O2JamLevelSortPatch.InstallOnce),
        (nameof(O2JamLevelGroupPatch), O2JamLevelGroupPatch.InstallOnce),
        (nameof(O2JamHitSampleLookupPatch), O2JamHitSampleLookupPatch.InstallOnce),
        (nameof(O2JamEditorAccessPatch), O2JamEditorAccessPatch.InstallOnce),
    ];

    internal static bool IsInstalled { get; private set; }

    internal static IReadOnlyList<string> FailedPatches { get; private set; } = [];

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            FailedPatches = installers.Where(installer => !installer.Install())
                                      .Select(installer => installer.Name)
                                      .ToArray();
            IsInstalled = FailedPatches.Count == 0;
            if (!IsInstalled)
                Logger.Log($"O2Lazer compatibility patch installation is incomplete: {string.Join(", ", FailedPatches)}", LoggingTarget.Runtime, LogLevel.Error);

            return IsInstalled;
        }
    }
}
