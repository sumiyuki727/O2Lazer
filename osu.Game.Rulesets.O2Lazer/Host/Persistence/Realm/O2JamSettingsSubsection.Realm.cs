using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

// The native settings loader is the composition boundary; UI construction never receives a database.
public partial class O2JamSettingsSubsection
{
    [BackgroundDependencyLoader]
    private void load(GameHost host, RealmAccess realm, INotificationOverlay? notifications = null,
                      IWorkingBeatmapCache? workingBeatmaps = null, BeatmapDifficultyCache? difficultyCache = null)
    {
        var rulesetConfig = (O2JamRulesetConfigManager)Config;
        var session = O2JamLibrarySettingsSession.Get(rulesetConfig,
            () => new O2JamRealmLibraryBackend(realm, host.Storage, workingBeatmaps, difficultyCache), notifications);
        initialiseLibrarySettings(rulesetConfig, session);
    }
}
