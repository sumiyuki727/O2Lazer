using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

public static class O2LazerSongSelectPlayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.SongSelectPlay";

    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(SoloSongSelect), "OnStart");
            var prefixMethod = AccessTools.Method(typeof(O2LazerSongSelectPlayPatcher), nameof(prefix));

            var missing = new[]
            {
                (name: "SoloSongSelect.OnStart", member: (MemberInfo?)target),
                (name: "O2LazerSongSelectPlayPatcher.prefix", member: prefixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missing.Length > 0)
            {
                O2LazerLogger.Log("O2LAZER SongSelectPlayPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missing), level: LogLevel.Error);
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
            IsInstalled = true;
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool prefix(SoloSongSelect __instance)
    {
        var beatmapInfo = __instance.Beatmap.Value?.BeatmapInfo;

        if (beatmapInfo?.Ruleset.ShortName == Constant.SHORT_NAME
            && !O2LazerWorkingBeatmap.IsExternalChartAvailable(beatmapInfo))
        {
            return false;
        }

        return true;
    }
    // ReSharper restore InconsistentNaming
}


