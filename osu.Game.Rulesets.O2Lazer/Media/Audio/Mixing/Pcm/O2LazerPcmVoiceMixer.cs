using System;
using System.Threading;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Processing;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Mixing.Pcm;

internal sealed class O2LazerPcmVoiceMixer
{
    // The initial segment absorbs normal polyphony without increasing callback traversal work.
    private const int default_voice_segment_capacity = 512;

    // Leaving a small margin avoids clipping during downstream float-to-device conversion.
    private const float limiter_ceiling = 0.98f;

    // A slower recovery avoids audible gain pumping immediately after dense chords.
    private const float limiter_release_ms = 50;

    // Short envelopes suppress discontinuities without delaying the requested start frame.
    private const float attack_fade_ms = 2;

    // Same-domain retriggers crossfade briefly instead of hard-stopping the previous voice.
    private const float retrigger_fade_ms = 2;

    // Timeline replacement drains all old voices more conservatively because many may end at once.
    private const float epoch_replacement_fade_ms = 5;

    // Samples fade near their natural end to avoid discontinuities in source files with non-zero tails.
    private const float tail_fade_ms = 2;

    private readonly int voiceSegmentCapacity;
    private readonly VoiceSegment firstVoiceSegment;
    private readonly O2LazerVoiceCommandQueue commands;
    private readonly int attackFadeFrames;
    private readonly int retriggerFadeFrames;
    private readonly int epochReplacementFadeFrames;
    private readonly int tailFadeFrames;
    private readonly float limiterRecoveryPerFrame;

    private int currentEpoch;
    private bool paused;
    private float masterGain = 1;
    private float limiterGain = 1;
    private int reservedVoices;
    private int activeVoices;
    private int voiceCapacity;
    private VoiceSegment lastVoiceSegment;

    internal long RenderedFrames { get; private set; }

    internal O2LazerPcmVoiceMixer()
    {
        voiceSegmentCapacity = default_voice_segment_capacity;
        firstVoiceSegment = lastVoiceSegment = new VoiceSegment(default_voice_segment_capacity);
        voiceCapacity = default_voice_segment_capacity;
        commands = new O2LazerVoiceCommandQueue();
        attackFadeFrames = millisecondsToFrames(attack_fade_ms);
        retriggerFadeFrames = millisecondsToFrames(retrigger_fade_ms);
        epochReplacementFadeFrames = millisecondsToFrames(epoch_replacement_fade_ms);
        tailFadeFrames = millisecondsToFrames(tail_fade_ms);
        limiterRecoveryPerFrame = 1 / (O2LazerFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE * limiter_release_ms / 1000);
    }

    internal void SubmitPlayBatch(ReadOnlySpan<O2LazerVoicePlay> plays)
    {
        if (plays.IsEmpty)
            return;

        var batch = new O2LazerVoiceCommand[plays.Length];
        var count = 0;

        for (var i = 0; i < plays.Length; i++)
        {
            var play = sanitise(plays[i]);
            var superseded = false;

            for (var later = i + 1; later < plays.Length; later++)
            {
                if (plays[later].TargetFrame == play.TargetFrame && plays[later].Domain == play.Domain)
                {
                    superseded = true;
                    break;
                }
            }

            if (!superseded)
                batch[count++] = new O2LazerVoiceCommand(O2LazerVoiceCommandType.Play, play.TargetFrame, play.Epoch, play);
        }

        if (count == 0)
            return;

        reserveVoiceCapacity(count);

        commands.Enqueue(batch.AsSpan(0, count));
    }

    internal void SubmitControl(O2LazerVoiceCommandType type, long targetFrame, int epoch, float value = 0)
    {
        if (type is not (O2LazerVoiceCommandType.Pause
            or O2LazerVoiceCommandType.Resume
            or O2LazerVoiceCommandType.SetMasterGain
            or O2LazerVoiceCommandType.ReplaceEpoch))
            throw new ArgumentOutOfRangeException(nameof(type));

        commands.Enqueue(new O2LazerVoiceCommand(type, Math.Max(targetFrame, RenderedFrames), epoch, Value: value));
    }

    internal void Render(Span<float> output)
    {
        if (output.Length % 2 != 0)
            throw new ArgumentException(@"PCM output must contain complete stereo frames.", nameof(output));

        output.Clear();
        var frameCount = output.Length / 2;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var absoluteFrame = RenderedFrames + frame;
            consumeCommands(absoluteFrame);

            var left = 0f;
            var right = 0f;

            if (!paused)
            {
                for (var segment = firstVoiceSegment; segment != null; segment = Volatile.Read(ref segment.Next))
                {
                    var voiceIndex = 0;

                    while (voiceIndex < segment.ActiveCount)
                    {
                        ref var voice = ref segment.Voices[voiceIndex];
                        var asset = voice.Asset!;
                        if (voice.SourceFrame >= voice.EndFrame || !asset.TryReadStereoFrame(voice.SourceFrame, out var voiceLeft, out var voiceRight))
                        {
                            releaseVoice(segment, voiceIndex);
                            continue;
                        }

                        var gain = voice.BaseGain * getEnvelopeGain(ref voice, absoluteFrame);
                        left += voiceLeft * gain;
                        right += voiceRight * gain;
                        voice.SourceFrame++;

                        if (voice.SourceFrame >= voice.EndFrame || gain <= 0)
                        {
                            releaseVoice(segment, voiceIndex);
                            continue;
                        }

                        voiceIndex++;
                    }
                }
            }

            left *= masterGain;
            right *= masterGain;
            applyLimiter(ref left, ref right);

            var outputIndex = frame * 2;
            output[outputIndex] = left;
            output[outputIndex + 1] = right;
        }

        RenderedFrames += frameCount;
    }

    internal int ActiveVoiceCount => Volatile.Read(ref activeVoices);

    private void consumeCommands(long frame)
    {
        while (commands.TryPeek(out var command) && command.TargetFrame <= frame)
        {
            commands.TryDequeue(out command);

            switch (command.Type)
            {
                case O2LazerVoiceCommandType.Play:
                    if (command.Epoch == currentEpoch && !paused)
                        startVoice(command.Play, frame);
                    else
                        releaseVoiceReservation();
                    break;

                case O2LazerVoiceCommandType.Pause:
                    if (command.Epoch == currentEpoch)
                        paused = true;
                    break;

                case O2LazerVoiceCommandType.Resume:
                    if (command.Epoch == currentEpoch)
                        paused = false;
                    break;

                case O2LazerVoiceCommandType.SetMasterGain:
                    if (command.Epoch == currentEpoch)
                        masterGain = sanitiseGain(command.Value);
                    break;

                case O2LazerVoiceCommandType.ReplaceEpoch:
                    currentEpoch = command.Epoch;
                    drainAll(frame, epochReplacementFadeFrames);
                    break;

                case O2LazerVoiceCommandType.StopVoice:
                    if (command.Epoch == currentEpoch)
                        stopVoice(command.VoiceId, frame);
                    break;

                case O2LazerVoiceCommandType.SetVoiceGain:
                    if (command.Epoch == currentEpoch)
                        setVoiceGain(command.VoiceId, sanitiseGain(command.Value));

                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private void startVoice(O2LazerVoicePlay play, long frame)
    {
        for (var segment = firstVoiceSegment; segment != null; segment = Volatile.Read(ref segment.Next))
        {
            for (var i = 0; i < segment.ActiveCount; i++)
            {
                ref var existing = ref segment.Voices[i];
                if (existing.Domain != play.Domain)
                    continue;

                beginFade(ref existing, frame, retriggerFadeFrames);
            }
        }

        VoiceSegment? freeSegment = null;

        for (var segment = firstVoiceSegment; segment != null; segment = Volatile.Read(ref segment.Next))
        {
            if (segment.ActiveCount < segment.Voices.Length)
            {
                freeSegment = segment;
                break;
            }
        }

        if (freeSegment == null)
        {
            releaseVoiceReservation();
            return;
        }

        var sliceStart = Math.Max(0, play.Domain.SliceStartFrame);
        var sourceStart = Math.Max(sliceStart, play.SourceOffsetFrame);
        var assetEnd = play.Asset.TotalFrameCount >= 0 ? play.Asset.TotalFrameCount : long.MaxValue;
        var sliceEnd = play.Domain.SliceFrameCount < 0
            ? assetEnd
            : Math.Min(assetEnd, sliceStart + play.Domain.SliceFrameCount);

        if (sourceStart >= sliceEnd)
        {
            releaseVoiceReservation();
            return;
        }

        freeSegment.Voices[freeSegment.ActiveCount++] = new O2LazerPcmVoice
        {
            State = O2LazerPcmVoiceState.Active,
            Asset = play.Asset,
            Domain = play.Domain,
            SourceFrame = sourceStart,
            EndFrame = sliceEnd,
            BaseGain = sanitiseGain(play.Gain),
            VoiceId = play.VoiceId,
            StartFrame = frame,
            FadeStartGain = 1,
        };
        activeVoices++;
    }

    private float getEnvelopeGain(ref O2LazerPcmVoice voice, long frame)
    {
        var tailFramesRemaining = voice.EndFrame - voice.SourceFrame;
        var tailGain = tailFramesRemaining >= tailFadeFrames
            ? 1
            : Math.Max(0, tailFramesRemaining / (float)tailFadeFrames);
        var attackGain = Math.Clamp((frame - voice.StartFrame + 1) / (float)attackFadeFrames, 0, 1);
        var boundaryGain = Math.Min(tailGain, attackGain);

        if (voice.State != O2LazerPcmVoiceState.Draining)
            return boundaryGain;

        if (frame >= voice.FadeEndFrame)
            return 0;

        var fadeProgress = (frame - voice.FadeStartFrame) / (float)Math.Max(1, voice.FadeEndFrame - voice.FadeStartFrame);
        return Math.Min(boundaryGain, voice.FadeStartGain * (1 - Math.Clamp(fadeProgress, 0, 1)));
    }

    private void beginFade(ref O2LazerPcmVoice voice, long frame, int durationFrames)
    {
        if (voice.State == O2LazerPcmVoiceState.Active)
            activeVoices--;

        var currentGain = getEnvelopeGain(ref voice, frame);
        voice.State = O2LazerPcmVoiceState.Draining;
        voice.FadeStartFrame = frame;
        voice.FadeEndFrame = frame + durationFrames;
        voice.FadeStartGain = currentGain;
    }

    private void drainAll(long frame, int durationFrames)
    {
        for (var segment = firstVoiceSegment; segment != null; segment = Volatile.Read(ref segment.Next))
        {
            for (var i = 0; i < segment.ActiveCount; i++)
            {
                beginFade(ref segment.Voices[i], frame, durationFrames);
            }
        }
    }

    private void stopVoice(long voiceId, long frame)
    {
        for (var segment = firstVoiceSegment; segment != null; segment = Volatile.Read(ref segment.Next))
        {
            for (var i = 0; i < segment.ActiveCount; i++)
            {
                if (segment.Voices[i].VoiceId == voiceId)
                    beginFade(ref segment.Voices[i], frame, retriggerFadeFrames);
            }
        }
    }

    private void setVoiceGain(long voiceId, float gain)
    {
        for (var segment = firstVoiceSegment; segment != null; segment = Volatile.Read(ref segment.Next))
        {
            for (var i = 0; i < segment.ActiveCount; i++)
            {
                if (segment.Voices[i].VoiceId == voiceId)
                    segment.Voices[i].BaseGain = gain;
            }
        }
    }

    private void applyLimiter(ref float left, ref float right)
    {
        var peak = Math.Max(Math.Abs(left), Math.Abs(right));
        var requiredGain = peak > limiter_ceiling ? limiter_ceiling / peak : 1;

        limiterGain = requiredGain < 1
            ? Math.Min(limiterGain, requiredGain)
            : Math.Min(1, limiterGain + limiterRecoveryPerFrame);

        if (limiterGain < 0.999999f)
        {
            left *= limiterGain;
            right *= limiterGain;
        }

        var outputFramePeak = Math.Max(Math.Abs(left), Math.Abs(right));
        if (outputFramePeak > limiter_ceiling)
        {
            var correction = limiter_ceiling / outputFramePeak;
            left = Math.Clamp(left * correction, -limiter_ceiling, limiter_ceiling);
            right = Math.Clamp(right * correction, -limiter_ceiling, limiter_ceiling);
        }

    }

    private void reserveVoiceCapacity(int count)
    {
        var requiredCapacity = checked(Volatile.Read(ref reservedVoices) + count);

        if (requiredCapacity > voiceCapacity)
        {
            var next = new VoiceSegment(Math.Max(voiceSegmentCapacity, requiredCapacity - voiceCapacity));

            // Capacity becomes visible to the callback before commands that reserve it are published.
            Volatile.Write(ref lastVoiceSegment.Next, next);
            lastVoiceSegment = next;
            voiceCapacity = checked(voiceCapacity + next.Voices.Length);
        }

        Interlocked.Add(ref reservedVoices, count);
    }

    private void releaseVoice(VoiceSegment segment, int index)
    {
        ref var voice = ref segment.Voices[index];

        if (voice.State == O2LazerPcmVoiceState.Active)
            activeVoices--;

        var lastIndex = --segment.ActiveCount;
        voice = segment.Voices[lastIndex];
        segment.Voices[lastIndex] = default;
        releaseVoiceReservation();
    }

    private void releaseVoiceReservation() => Interlocked.Decrement(ref reservedVoices);

    private static O2LazerVoicePlay sanitise(O2LazerVoicePlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Asset);
        return play with
        {
            TargetFrame = Math.Max(0, play.TargetFrame),
            SourceOffsetFrame = Math.Max(0, play.SourceOffsetFrame),
            Gain = sanitiseGain(play.Gain),
        };
    }

    private static float sanitiseGain(float gain) => float.IsFinite(gain) ? Math.Max(0, gain) : 0;

    private static int millisecondsToFrames(float milliseconds)
        => Math.Max(1, (int)Math.Round(milliseconds * O2LazerFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE / 1000));

    private enum O2LazerPcmVoiceState : byte
    {
        Free,
        Active,
        Draining,
    }

    private struct O2LazerPcmVoice
    {
        public O2LazerPcmVoiceState State;
        public O2LazerPcmAsset? Asset;
        public O2LazerTerminationDomain Domain;
        public long SourceFrame;
        public long EndFrame;
        public float BaseGain;
        public long VoiceId;
        public long StartFrame;
        public long FadeStartFrame;
        public long FadeEndFrame;
        public float FadeStartGain;
    }

    private sealed class VoiceSegment(int capacity)
    {
        internal readonly O2LazerPcmVoice[] Voices = new O2LazerPcmVoice[capacity];
        internal int ActiveCount;
        internal VoiceSegment? Next;
    }
}
