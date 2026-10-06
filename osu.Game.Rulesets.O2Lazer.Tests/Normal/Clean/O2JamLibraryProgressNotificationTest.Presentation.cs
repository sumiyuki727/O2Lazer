using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLibraryProgressNotificationTest
{
    [Test]
    public void SourcePassesShareOneContinuousPresentationAndEmptyStagesStayHidden()
    {
        var read = O2JamLibrarySettingsSession.PresentRefreshProgress(new O2JamLibraryProgress(100, 100, O2JamLibraryStage.ReadingSourceFiles));
        var match = O2JamLibrarySettingsSession.PresentRefreshProgress(new O2JamLibraryProgress(0, 100, O2JamLibraryStage.MatchingSources));
        Assert.That(match, Is.EqualTo(read));
        var finished = O2JamLibrarySettingsSession.PresentRefreshProgress(new O2JamLibraryProgress(100, 100, O2JamLibraryStage.MatchingSources));
        Assert.That(finished, Is.EqualTo(new O2JamLibraryProgress(200, 200, O2JamLibraryStage.ReadingSourceFiles)));
        foreach (var stage in new[] { O2JamLibraryStage.RefreshingCharts, O2JamLibraryStage.SynchronisingCollections, O2JamLibraryStage.CalculatingDifficulties })
            Assert.That(O2JamLibrarySettingsSession.PresentRefreshProgress(new O2JamLibraryProgress(0, 0, stage)), Is.Null);
        var calculation = new O2JamLibraryProgress(5000, 7000, O2JamLibraryStage.CalculatingDifficulties);
        Assert.That(O2JamLibrarySettingsSession.PresentRefreshProgress(calculation), Is.EqualTo(calculation));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FirstVisibleDifficultyCountAnimatesFromZeroToConfirmedWork(bool difficulty)
    {
        var stage = difficulty ? O2JamLibraryStage.CalculatingDifficulties : O2JamLibraryStage.RefreshingCharts;
        using var notification = new Probe();
        notification.Report(new O2JamLibraryProgress(7000, 7000, O2JamLibraryStage.ReadingSourceFiles));
        notification.Step(0);
        notification.Report(new O2JamLibraryProgress(5000, 7000, stage));
        notification.Step(100);
        Assert.That(notification.DisplayedProcessed, Is.Zero);
        notification.Step(400);
        Assert.That(notification.DisplayedProcessed, Is.GreaterThan(0).And.LessThan(5000));
        notification.Step(700);
        Assert.That(notification.DisplayedProcessed, Is.EqualTo(5000));
        notification.Report(new O2JamLibraryProgress(5016, 7000, stage));
        notification.Step(800);
        Assert.That(notification.DisplayedProcessed, Is.GreaterThanOrEqualTo(5000).And.LessThanOrEqualTo(5016));
    }
}