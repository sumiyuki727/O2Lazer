namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public sealed record O2LazerJudgementProfile(
    O2LazerJudgementWindowTable Normal,
    O2LazerJudgementWindowTable Scratch,
    O2LazerJudgementWindowTable LongNoteTail,
    O2LazerJudgementWindowTable LongScratchTail);
