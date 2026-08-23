using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Bindables;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Native;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

/// <summary>
///     Owns a PCM playback backend and its native audio bridge.
/// </summary>
/// <remarks>
///     Gameplay and preview have different lifecycle facades but share the same backend ownership.
/// </remarks>
internal sealed class O2LazerPcmPlaybackSession : IDisposable
{
    private readonly IReadOnlyDictionary<ushort, string> sampleDefinitions;
    private readonly string? basePath;
    private readonly double rate;
    private readonly bool adjustPitch;
    private readonly IEnumerable<O2LazerSampleUsage>? sampleUsages;
    private readonly AudioManager audioManager;
    private readonly Func<double> currentTime;
    private readonly IBindable<double>? aggregateVolume;

    private O2LazerPcmVoiceMixer? pcmMixer;
    private O2LazerBassMixerBridge? pcmBridge;
    private bool disposed;

    internal O2LazerPcmPlaybackSession(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        double rate,
        IEnumerable<O2LazerSampleUsage>? sampleUsages,
        AudioManager audioManager,
        Func<double> currentTime,
        IBindable<double>? aggregateVolume = null,
        bool adjustPitch = false)
    {
        this.sampleDefinitions = sampleDefinitions;
        this.basePath = basePath;
        this.rate = rate;
        this.adjustPitch = adjustPitch;
        this.sampleUsages = sampleUsages;
        this.audioManager = audioManager;
        this.currentTime = currentTime;
        this.aggregateVolume = aggregateVolume;
    }

    internal bool IsInitialised => Controller?.IsInitialised == true;

    internal O2LazerPcmPlaybackController? Controller { get; private set; }

    internal int ActiveVoiceCount => pcmMixer?.ActiveVoiceCount ?? 0;

    private AudioMixer? outputMixer;

    internal void Initialise(CancellationToken cancellationToken, double chartTime, bool waitForInitialAssets = true)
    {
        O2LazerPcmMixerPatcher.InstallOnce();

        if (!O2LazerPcmMixerPatcher.IsInstalled)
            return;

        outputMixer = audioManager.CreateAudioMixer(O2LazerPcmMixerPatcher.MIXER_IDENTIFIER);
        pcmMixer = new O2LazerPcmVoiceMixer();
        pcmBridge = new O2LazerBassMixerBridge(outputMixer, pcmMixer);
        Controller = new O2LazerPcmPlaybackController(
            sampleDefinitions,
            basePath,
            rate,
            sampleUsages,
            aggregateVolume ?? audioManager.AggregateVolume,
            currentTime,
            pcmMixer,
            adjustPitch);
        Controller.Initialise(cancellationToken, chartTime, waitForInitialAssets);
        pcmBridge.EnsureAttached();
    }

    internal void Update(double chartTime)
    {
        pcmBridge?.EnsureAttached();
        Controller?.Update(chartTime);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        // Detach the native stream before the framework mixer is torn down so teardown
        // never leaves the O2LAZER PCM stream mixed into the global output.
        Controller?.StopAll();
        pcmBridge?.Dispose();
        pcmBridge = null;
        Controller?.Dispose();
        Controller = null;
        pcmMixer = null;
        outputMixer?.Dispose();
        outputMixer = null;
    }
}

