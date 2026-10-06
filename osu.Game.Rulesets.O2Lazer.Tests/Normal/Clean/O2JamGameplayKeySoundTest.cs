using System;
using NUnit.Framework;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Audio;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamGameplayKeySoundTest
{
    [SetUp]
    public void SetUp() => Assert.That(O2JamHitSampleLookupPatch.InstallOnce(), Is.True);

    [Test]
    public void EmptyHitsAndJudgementReplaceTheSameKeySoundAcrossNativeInstances()
    {
        using var fixture = new PlaybackFixture();
        using var emptyHit = fixture.AddSample("o2jam/7");
        using var judgement = fixture.AddSample("o2jam/7");
        var first = (TestChannel)emptyHit.Play();
        var repeated = (TestChannel)emptyHit.Play();
        Assert.That(first.Playing, Is.False);
        Assert.That(first.Stops, Is.EqualTo(1));
        var resolved = (TestChannel)judgement.Play();
        Assert.Multiple(() =>
        {
            Assert.That(repeated.Playing, Is.False);
            Assert.That(repeated.Stops, Is.EqualTo(1));
            Assert.That(resolved.Playing, Is.True);
            Assert.That(resolved.Plays, Is.EqualTo(1));
        });
    }

    [Test]
    public void DifferentSamplesAndDifferentGameplayOwnersDoNotInterruptEachOther()
    {
        using var fixture = new PlaybackFixture();
        using var otherGameplay = new PlaybackFixture();
        using var first = fixture.AddSample("o2jam/7");
        using var differentSample = fixture.AddSample("o2jam/8");
        using var differentOwner = otherGameplay.AddSample("o2jam/7");
        var channel = first.Play();
        var otherSampleChannel = differentSample.Play();
        var otherOwnerChannel = differentOwner.Play();
        first.Play();
        Assert.Multiple(() =>
        {
            Assert.That(channel.Playing, Is.False);
            Assert.That(otherSampleChannel.Playing, Is.True);
            Assert.That(otherOwnerChannel.Playing, Is.True);
        });
    }

    [Test]
    public void PreviewAndOrdinarySoundsRetainNativeConcurrency()
    {
        using var fixture = new PlaybackFixture();
        using var preview = new DrawableSample(new TestSample("o2jam/7"));
        using var ordinary = fixture.AddSample("menu-click");
        var previewChannel = preview.Play();
        preview.Play();
        var ordinaryChannel = ordinary.Play();
        ordinary.Play();
        using var gameplay = fixture.AddSample("o2jam/7");
        gameplay.Play();
        fixture.Paused.Value = true;
        Assert.Multiple(() =>
        {
            Assert.That(previewChannel.Playing, Is.True);
            Assert.That(ordinaryChannel.Playing, Is.True);
            Assert.That(previewChannel.AggregateFrequency.Value, Is.EqualTo(1));
            Assert.That(ordinaryChannel.AggregateFrequency.Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void DetachedLatestVoicePausesAndResumesWithoutRevivingTheReplacedVoice()
    {
        using var fixture = new PlaybackFixture();
        using var sample = fixture.AddSample("o2jam/7");
        var old = (TestChannel)sample.Play();
        var latest = (TestChannel)sample.Play();
        latest.Frequency.Value = 1.5;
        setParent(sample, null);
        fixture.Paused.Value = true;
        Assert.Multiple(() =>
        {
            Assert.That(latest.AggregateFrequency.Value, Is.Zero);
            Assert.That(latest.Playing, Is.True);
            Assert.That(latest.Stops, Is.Zero);
            Assert.That(old.Playing, Is.False);
        });
        fixture.Paused.Value = false;
        Assert.Multiple(() =>
        {
            Assert.That(latest.AggregateFrequency.Value, Is.EqualTo(1.5));
            Assert.That(latest.Plays, Is.EqualTo(1));
            Assert.That(old.Playing, Is.False);
        });
    }

    [Test]
    public void SeekClearsTheReplacementState()
    {
        using var fixture = new PlaybackFixture();
        using var sample = fixture.AddSample("o2jam/7");
        var beforeSeek = (TestChannel)sample.Play();
        fixture.Adjustments.StopActiveChannels();
        var stops = beforeSeek.Stops;
        var afterSeek = (TestChannel)sample.Play();
        fixture.Paused.Value = true;
        fixture.Paused.Value = false;
        Assert.Multiple(() =>
        {
            Assert.That(beforeSeek.Playing, Is.False);
            Assert.That(beforeSeek.Stops, Is.EqualTo(stops));
            Assert.That(afterSeek.Playing, Is.True);
            Assert.That(afterSeek.Plays, Is.EqualTo(1));
        });
    }

    [Test]
    public void LeavingGameplayStopsTailsAndRemovesTheRegistration()
    {
        using var fixture = new PlaybackFixture();
        using var sample = fixture.AddSample("o2jam/7");
        var active = sample.Play();
        fixture.Adjustments.UnbindAll();
        var first = sample.Play();
        sample.Play();
        Assert.Multiple(() =>
        {
            Assert.That(active.Playing, Is.False);
            Assert.That(first.Playing, Is.True);
        });
    }

    private static void setParent(Drawable drawable, Drawable? parent) =>
        typeof(Drawable).GetProperty(nameof(Drawable.Parent))!.SetValue(drawable, parent);

    private sealed class PlaybackFixture : IDisposable
    {
        private readonly Container root = new();
        public O2JamHitSoundRateAdjustments Adjustments { get; } = new();
        public BindableBool Paused { get; } = new();

        public PlaybackFixture()
        {
            Adjustments.Configure([]);
            Adjustments.BindPlaybackDisabled(Paused);
            Adjustments.RegisterSoundContainer(root);
        }

        public DrawableSample AddSample(string name)
        {
            var sample = new DrawableSample(new TestSample(name));
            // Audio-only fixtures skip native drawable loading, which normally assigns Parent.
            setParent(sample, root);
            return sample;
        }

        public void Dispose()
        {
            Adjustments.UnbindAll();
            root.Dispose();
        }
    }

    private sealed class TestSample(string name) : Sample(name)
    {
        public override double Length => 1000;
        protected override SampleChannel CreateChannel() => new TestChannel(Name);
    }

    private sealed class TestChannel(string name) : SampleChannel(name)
    {
        private bool playing;
        public override bool Playing => playing;
        public int Plays { get; private set; }
        public int Stops { get; private set; }

        public override void Play()
        {
            base.Play();
            playing = true;
            Plays++;
        }

        public override void Stop()
        {
            base.Stop();
            playing = false;
            Stops++;
        }
    }
}
