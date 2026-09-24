using System;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public static class O2JamPreviewCoordinator
{
    private static WeakReference<O2JamPreviewTrack>? activeTrack;

    public static void Activate(O2JamPreviewTrack track)
    {
        activeTrack = new WeakReference<O2JamPreviewTrack>(track);
    }

    public static O2JamPreviewTrack? EnterGameplay()
    {
        if (tryGetTrack(out var track))
        {
            track.PlaybackMode = O2JamPreviewPlaybackMode.Gameplay;
            return track;
        }

        return null;
    }

    public static void ExitGameplay(O2JamPreviewTrack? gameplayTrack)
    {
        if (gameplayTrack != null && tryGetTrack(out var track) && ReferenceEquals(track, gameplayTrack))
            track.PlaybackMode = O2JamPreviewPlaybackMode.Preview;
    }

    private static bool tryGetTrack(out O2JamPreviewTrack track)
    {
        if (activeTrack?.TryGetTarget(out track!) == true && !track.IsDisposed)
            return true;

        track = null!;
        return false;
    }
}
