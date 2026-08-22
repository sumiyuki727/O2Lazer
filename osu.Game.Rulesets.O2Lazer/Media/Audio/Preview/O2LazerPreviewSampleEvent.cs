using osu.Game.Rulesets.O2Lazer.Parsing;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;

internal readonly record struct O2LazerPreviewSampleEvent(O2LazerSampleEvent Event, bool ResumeAfterSeek);
