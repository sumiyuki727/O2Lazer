using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics.Audio;
using osu.Framework.IO.Stores;
using osu.Framework.Threading;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamBeatmapSkinSampleLifetimeTest
{
    [Test]
    public void RetryCanGetChannelAfterPreviousGameplayDrawableWasDisposed()
    {
        using var fixture = new SampleFixture();
        var previous = fixture.GetSample();
        using (var drawable = new DrawableSample(previous))
        {
        }
        previous.Update();
        Assert.That(previous.IsDisposed, Is.True, "The native drawable owns and disposes the returned sample.");

        var retry = fixture.GetSample();
        Assert.That(() => retry.GetChannel(), Throws.Nothing);
        Assert.That(retry, Is.Not.SameAs(previous));
    }

    [Test]
    public void DisposingOneDrawableDoesNotInvalidateAnotherExistingDrawable()
    {
        using var fixture = new SampleFixture();
        var first = fixture.GetSample();
        var second = fixture.GetSample();
        using var survivingDrawable = new DrawableSample(second);
        using (var disposedDrawable = new DrawableSample(first))
        {
        }
        first.Update();

        Assert.That(() => survivingDrawable.GetChannel(), Throws.Nothing);
        Assert.That(second.IsDisposed, Is.False);
    }

    [Test]
    public void IndependentSamplesReuseThePreloadedNativeFactory()
    {
        using var fixture = new SampleFixture();
        fixture.Skin.PrefetchSamples([7]);
        var first = fixture.GetSample();
        var factoryField = first.GetType().GetField("factory", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var factory = factoryField.GetValue(first);
        Assert.That(factory, Is.Not.Null);

        for (var i = 0; i < 4; i++)
        {
            var next = fixture.GetSample();
            Assert.Multiple(() =>
            {
                Assert.That(next, Is.Not.SameAs(first));
                Assert.That(factoryField.GetValue(next), Is.SameAs(factory), "New ownership must not decode the audio again.");
            });
            next.Dispose();
            next.Update();
        }

        Assert.That(() => first.GetChannel(), Throws.Nothing);
    }

    private sealed class SampleFixture : IDisposable
    {
        private readonly AudioThread audioThread = new();
        private readonly ResourceStore<byte[]> resources = new();
        private readonly AudioManager manager;
        private readonly List<Sample> samples = [];
        public O2JamBeatmapSkin Skin { get; }

        public SampleFixture()
        {
            // Do not start the audio thread or a sound device: this regression concerns native
            // SampleBass ownership before decoding/playback, not output latency or audio quality.
            manager = new AudioManager(audioThread, resources, resources, null);
            Skin = new O2JamBeatmapSkin(new OjmArchive(new Dictionary<int, OjmSample>
            {
                [7] = new OjmSample(7, "test", ".wav", []),
            }), manager);
        }

        public Sample GetSample()
        {
            var sample = (Sample)Skin.GetSample(new O2JamHitSampleInfo(7, 100, 0))!;
            Assert.That(sample, Is.Not.Null);
            samples.Add(sample);
            return sample;
        }

        public void Dispose()
        {
            foreach (var sample in samples)
            {
                if (sample.IsDisposed)
                    continue;
                sample.Dispose();
                sample.Update();
            }
            Skin.Dispose();
            manager.Dispose();
            manager.Update();
            // The framework only closes a GameThread from its running state. Its manager has
            // already been unregistered, so this final start/exit cannot initialise audio output.
            audioThread.Start();
            audioThread.Exit();
            Assert.That(() => audioThread.Exited, Is.True.After(2000, 10));
            resources.Dispose();
        }
    }
}
