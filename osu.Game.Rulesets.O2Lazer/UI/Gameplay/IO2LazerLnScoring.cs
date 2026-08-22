using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.UI.Objects;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

public interface IO2LazerLnScoring
{
    /// <summary>Registers a separate CN/HCN endpoint through the score and health processors.</summary>
    void ApplySyntheticLongNoteEndpoint(DrawableO2LazerHitObject drawable, O2LazerLongNoteEndpointResult endpoint);

    /// <summary>Applies a HellChargeNote body gauge tick for the currently pressed column.</summary>
    void ApplyHellChargeTick(bool holding, double scale);
}
