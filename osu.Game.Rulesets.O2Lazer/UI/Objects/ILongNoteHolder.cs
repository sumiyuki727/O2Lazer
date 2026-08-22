using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

/// <summary>
///     Exposes long-note-specific capabilities
/// </summary>
public interface ILongNoteHolder
{
    bool IsHoldingLongNote { get; }

    bool IsAutomaticallyHeld { get; set; }

    bool TryRelease(double releaseOffset, O2LazerJudgementWindowTable tailTable);

    void UpdateBodyGeometry(float headY, float endY);
}
