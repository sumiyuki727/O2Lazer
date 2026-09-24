using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Seasonal;

namespace osu.Game.Rulesets.O2Lazer.Audio;

/// <summary>
/// Gives MusicController the same playlist direction from osu!'s already-maintained BeatmapStore
/// during a cross-ruleset carousel transition, avoiding synchronous RealmLive scans on the UI thread.
/// </summary>
internal static class O2JamModeSwitchDirectionPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ModeSwitchDirection";
    private static readonly object installLock = new();
    private static FieldInfo detachedBeatmapsField = null!;
    private static WeakReference<IBindableList<BeatmapSetInfo>>? availableSets;
    private static long transitionUntil;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            var harmony = new Harmony(harmony_id);
            try
            {
                var changeBeatmap = AccessTools.Method(typeof(MusicController), "changeBeatmap");
                detachedBeatmapsField = AccessTools.Field(typeof(osu.Game.Screens.Select.BeatmapCarousel), "detachedBeatmaps");
                var currentField = AccessTools.Field(typeof(MusicController), "current");
                var queuedDirectionField = AccessTools.Field(typeof(MusicController), "queuedDirection");
                if (changeBeatmap == null || detachedBeatmapsField == null || currentField == null || queuedDirectionField == null)
                    throw new MissingMemberException("The native music playlist API has changed.");

                harmony.Patch(changeBeatmap, prefix: new HarmonyMethod(typeof(O2JamModeSwitchDirectionPatch), nameof(setDirection)));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its mode-switch direction adapter.");
                return false;
            }
        }
    }

    internal static void BeginTransition(osu.Game.Screens.Select.BeatmapCarousel carousel)
    {
        if (!IsInstalled)
            return;

        // BeatmapCarousel already holds osu!'s live, detached list in Realm order. Its source
        // excludes deleted and protected sets, matching MusicController's implicit-skip query.
        var sets = detachedBeatmapsField.GetValue(carousel) as IBindableList<BeatmapSetInfo>;
        availableSets = sets == null ? null : new WeakReference<IBindableList<BeatmapSetInfo>>(sets);
        Interlocked.Exchange(ref transitionUntil, Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency);
    }

    internal static void EndTransition()
    {
        Interlocked.Exchange(ref transitionUntil, 0);
        availableSets = null;
    }

    private static void setDirection(WorkingBeatmap newWorking, WorkingBeatmap? ___current,
                                     ref TrackChangeDirection? ___queuedDirection)
    {
        if (___current == null || ReferenceEquals(___current, newWorking) || ___queuedDirection.HasValue
            || newWorking.BeatmapInfo?.AudioEquals(___current.BeatmapInfo) == true
            || Stopwatch.GetTimestamp() > Interlocked.Read(ref transitionUntil)
            || availableSets == null || !availableSets.TryGetTarget(out var sets) || sets == null)
            return;

        try
        {
            if (TryGetDirection(sets, ___current.BeatmapSetInfo, newWorking.BeatmapSetInfo, out var direction,
                                ___current is DummyWorkingBeatmap, newWorking is DummyWorkingBeatmap))
                ___queuedDirection = direction;
        }
        catch (InvalidOperationException)
        {
            // A simultaneous library update can invalidate an enumeration. Let native Realm
            // determine direction rather than accepting a possibly stale list position.
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    internal static bool TryGetDirection(IReadOnlyList<BeatmapSetInfo> sets, BeatmapSetInfo previous,
                                         BeatmapSetInfo next, out TrackChangeDirection direction,
                                         bool previousIsDummy = false, bool nextIsDummy = false)
    {
        var previousIndex = -1;
        var nextIndex = -1;
        var index = 0;

        for (var position = 0; position < sets.Count; position++)
        {
            var set = sets[position];
            if (set.DeletePending || set.Protected
                || !SeasonalUIConfig.ENABLED && set.Hash == IntroChristmas.CHRISTMAS_BEATMAP_SET_HASH)
                continue;

            if (set.Equals(previous))
                previousIndex = index;
            if (set.Equals(next))
                nextIndex = index;
            if (previousIndex >= 0 && nextIndex >= 0)
                break;
            index++;
        }

        // Native TakeWhile returns the playlist length for an absent dummy beatmap.
        if (previousIndex < 0 && previousIsDummy)
            previousIndex = index;
        if (nextIndex < 0 && nextIsDummy)
            nextIndex = index;

        // A real chart missing from the detached store may be a fresh import; use Realm.
        if (previousIndex < 0 || nextIndex < 0)
        {
            direction = default;
            return false;
        }

        direction = previousIndex > nextIndex ? TrackChangeDirection.Prev : TrackChangeDirection.Next;
        return true;
    }
}
