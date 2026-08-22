using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Mixing.Pcm;

// Keeping slice and pitch in the domain avoids changing retrigger identity when O2LAZERON support is added.
internal readonly record struct O2LazerTerminationDomain(
    ushort SampleKey,
    int Pitch = 0,
    long SliceStartFrame = 0,
    long SliceFrameCount = -1);

internal readonly record struct O2LazerVoicePlay(
    O2LazerPcmAsset Asset,
    O2LazerTerminationDomain Domain,
    long TargetFrame,
    float Gain = 1,
    long SourceOffsetFrame = 0,
    int Epoch = 0,
    long VoiceId = 0);

internal enum O2LazerVoiceCommandType : byte
{
    Play,
    Pause,
    Resume,
    SetMasterGain,
    ReplaceEpoch,
    StopVoice,
    SetVoiceGain,
}

internal readonly record struct O2LazerVoiceCommand(
    O2LazerVoiceCommandType Type,
    long TargetFrame,
    int Epoch,
    O2LazerVoicePlay Play = default,
    float Value = 0,
    long VoiceId = 0);
