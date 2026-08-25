using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public readonly record struct O2LazerJudgementCandidate(
    double StartTime,
    double EndTime,
    int Column,
    double JudgementRate,
    bool IsLongNote,
    double Bpm);

public readonly record struct O2LazerJudgementSelection(
    O2LazerJudgementCandidate? Candidate,
    HitResult Result,
    bool IsEmptyPoor);
