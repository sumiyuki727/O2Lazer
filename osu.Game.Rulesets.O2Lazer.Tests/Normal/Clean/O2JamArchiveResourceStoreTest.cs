using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamArchiveResourceStoreTest
{
    [Test]
    public void ResolvesNamesWithoutLeakingOtherEntries()
    {
        byte[] data = [1, 2, 3];
        using var store = new O2JamArchiveResourceStore(new OjmArchive(new Dictionary<int, OjmSample>
        {
            [42] = new OjmSample(42, "sample", ".wav", data),
        }));

        Assert.Multiple(() =>
        {
            Assert.That(store.Get("o2jam/42.wav"), Is.SameAs(data));
            Assert.That(store.Get("42"), Is.SameAs(data));
            Assert.That(store.Get("o2jam/42.ogg"), Is.Null);
            Assert.That(store.Get("o2jam/43.wav"), Is.Null);
            Assert.That(store.GetAvailableResources().Single(), Is.EqualTo("o2jam/42.wav"));
        });
    }
}
