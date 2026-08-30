using System;
using System.IO;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjnDocumentCacheTest
{
    [Test]
    public void ReusesUnchangedDifficultyAndInvalidatesChangedSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"o2lazer-ojn-cache-{Guid.NewGuid():N}.ojn");

        try
        {
            File.WriteAllBytes(path, OjnReaderTest.CreateChart());
            var first = OjnDocumentCache.Shared.Get(path, O2JamDifficulty.EX);
            var reused = OjnDocumentCache.Shared.Get(path, O2JamDifficulty.EX);

            using (var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                stream.WriteByte(0);

            var changed = OjnDocumentCache.Shared.Get(path, O2JamDifficulty.EX);

            Assert.Multiple(() =>
            {
                Assert.That(reused, Is.SameAs(first));
                Assert.That(changed, Is.Not.SameAs(first));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
