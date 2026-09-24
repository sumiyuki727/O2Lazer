using System.Collections.Generic;
using System.Linq;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

/// <summary>
/// Adapts native autoplay input to the O2Jam replay contract.
/// </summary>
internal sealed class O2JamAutoGenerator(O2JamBeatmap beatmap) : AutoGenerator(beatmap)
{
    public override Replay Generate() => CreateReplayData([]).Replay;

    public ModReplayData CreateReplayData(IReadOnlyList<Mod> mods)
    {
        // Native Mania already supplies the required key timing, including exact hold releases.
        // Only the frame type differs: O2Jam playback and persistence require their own contract.
        var data = new ManiaModAutoplay().CreateReplayData(Beatmap, mods);
        data.Replay.Frames = data.Replay.Frames.Cast<ManiaReplayFrame>()
                                 .Select(frame => (ReplayFrame)new O2JamReplayFrame(frame.Time, frame.Actions.ToArray()))
                                 .ToList();
        return data;
    }
}
