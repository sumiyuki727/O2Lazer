using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

/// <summary>
///     External-facing view of a O2LAZER column, exposing only the members
///     consumed by <see cref="O2LazerPlayfield"/> and mods.
/// </summary>
public interface IO2LazerColumn
{
    int ColumnIndex { get; }

    bool IsScratch { get; }

    HitObjectContainer HitObjectContainer { get; }

    Drawable KeyArea { get; }

    Container KeyAreaUnderNotesLayer { get; }

    Container HitExplosionArea { get; }

    bool Hidden { get; set; }

    void TriggerHitExplosion(bool isLongNote);

    void PlaySample(ushort? sampleKey, int volume);

    PressOutcome HandlePress(double time);

    void HandleRelease(double time);
}
