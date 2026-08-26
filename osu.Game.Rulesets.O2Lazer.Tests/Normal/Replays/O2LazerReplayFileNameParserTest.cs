using System;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Replays;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Replays;

[TestFixture]
public class O2LazerReplayFileNameParserTest
{
    [Test]
    public void TestParsesStandardLazerReplayFileName()
    {
        const string fileName = "manny5354 playing DJ Siesta - Another Day(cut ver.) (Destiny19) [HX Lv.45] (2026-08-26_09-42).osr";

        Assert.That(O2LazerReplayFileNameParser.TryParse(fileName, out var metadata), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Player, Is.EqualTo("manny5354"));
            Assert.That(metadata.Artist, Is.EqualTo("DJ Siesta"));
            Assert.That(metadata.Title, Is.EqualTo("Another Day(cut ver.)"));
            Assert.That(metadata.Difficulty, Is.EqualTo("HX Lv.45"));
            Assert.That(metadata.Date, Is.EqualTo(new DateTimeOffset(2026, 8, 26, 9, 42, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void TestRejectsMissingDate()
    {
        Assert.That(O2LazerReplayFileNameParser.TryParse("player playing Artist - Title (mapper) [diff].osr", out _), Is.False);
    }
}
