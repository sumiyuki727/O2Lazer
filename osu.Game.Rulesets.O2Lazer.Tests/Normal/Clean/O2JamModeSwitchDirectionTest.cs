using System;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Rulesets.O2Lazer.Audio;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamModeSwitchDirectionTest
{
    [Test]
    public void UsesTheSamePlaylistOrderAsNativeMusicController()
    {
        var first = createSet();
        var second = createSet();
        var third = createSet();
        BeatmapSetInfo[] sets = [first, second, third];

        Assert.Multiple(() =>
        {
            Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection(sets, third, first, out var previous), Is.True);
            Assert.That(previous, Is.EqualTo(TrackChangeDirection.Prev));
            Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection(sets, first, third, out var next), Is.True);
            Assert.That(next, Is.EqualTo(TrackChangeDirection.Next));
        });
    }

    [Test]
    public void FallsBackToNativeScanIfDetachedStoreHasNotCaughtUp()
    {
        var first = createSet();
        var imported = createSet();

        Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection([first], first, imported, out _), Is.False);
    }

    [Test]
    public void SkipsProtectedAndDeletedSetsLikeNativePlaylist()
    {
        var first = createSet();
        var protectedSet = createSet();
        protectedSet.Protected = true;
        var deletedSet = createSet();
        deletedSet.DeletePending = true;
        var last = createSet();

        Assert.Multiple(() =>
        {
            Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection([first, protectedSet, deletedSet, last], last, first, out var direction), Is.True);
            Assert.That(direction, Is.EqualTo(TrackChangeDirection.Prev));
            Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection([first, protectedSet, deletedSet, last], first, protectedSet, out _), Is.False);
        });
    }

    [Test]
    public void DummyIntermediateUsesNativeEndOfPlaylistRank()
    {
        var first = createSet();
        var last = createSet();
        var dummy = new BeatmapSetInfo();
        BeatmapSetInfo[] sets = [first, last];

        Assert.Multiple(() =>
        {
            Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection(sets, last, dummy, out var toDummy, nextIsDummy: true), Is.True);
            Assert.That(toDummy, Is.EqualTo(TrackChangeDirection.Next));
            Assert.That(O2JamModeSwitchDirectionPatch.TryGetDirection(sets, dummy, first, out var fromDummy, previousIsDummy: true), Is.True);
            Assert.That(fromDummy, Is.EqualTo(TrackChangeDirection.Prev));
        });
    }
    private static BeatmapSetInfo createSet() => new() { ID = Guid.NewGuid() };
}
