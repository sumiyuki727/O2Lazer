using osu.Framework.Audio;

namespace osu.Game.Rulesets.O2Lazer.Audio;

/// <summary>
/// Keeps the OJM sample store on the audio thread without inheriting the global effect-volume adjustment.
/// </summary>
internal sealed class O2JamDetachedAudioHost(AudioComponent component) : AudioComponent
{
    public override bool IsAlive => base.IsAlive && component.IsAlive;

    protected override void UpdateChildren()
    {
        base.UpdateChildren();

        if (component.IsAlive)
            component.Update();
    }

    protected override void Dispose(bool disposing)
    {
        if (!component.IsDisposed)
            component.Dispose();

        base.Dispose(disposing);
    }
}
