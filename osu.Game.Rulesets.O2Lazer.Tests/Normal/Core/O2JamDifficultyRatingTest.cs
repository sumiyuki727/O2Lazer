using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

[TestFixture]
public class O2JamDifficultyRatingTest
{
    [TestCase(1, 0.1)]
    [TestCase(41, 4.1)]
    [TestCase(119, 11.9)]
    [TestCase(255, 25.5)]
    public void O2JamLevelUsesOneTenthScale(double level, double expected)
    {
        Assert.That(O2JamDifficultyRating.FromLevel(level), Is.EqualTo(expected).Within(0.000001));
    }
}
