using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio.Track;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal static class O2JamTrackPreparation
{
    internal static Task<Track?> LoadAsync(ITrackStore store, string name, CancellationToken token) => Task.Run(async () =>
    {
        var track = store.Get(name);
        if (track == null)
            return null;

        try
        {
            // GetAsync only creates the object. A queued Stop is a public-API fence after decoder
            // creation AND mixer attachment. It must not run inline on the audio thread.
            await track.StopAsync().WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (track.IsLoaded && !track.IsDisposed)
                return track;

            track.Dispose();
            return null;
        }
        catch
        {
            track.Dispose();
            throw;
        }
    }, token);
}
