using NUnit.Framework;
using osu.Framework.Timing;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamLibraryProgressNotificationTest
{
    [Test]
    public void NativeTransformInterpolatesConfirmedCountsAndResetsAtStageBoundary()
    {
        using var notification = new Probe();
        notification.Report(new O2JamLibraryProgress(0, 64));
        notification.Step(0);
        notification.Report(new O2JamLibraryProgress(16, 64));
        notification.Step(500);
        notification.Step(750);
        Assert.That(notification.DisplayedProcessed, Is.GreaterThan(0).And.LessThan(16));
        notification.Step(1000);
        Assert.That(notification.DisplayedProcessed, Is.EqualTo(16));
        notification.Report(new O2JamLibraryProgress(0, 10, O2JamLibraryStage.ClearingCharts));
        notification.Step(1000);
        notification.Step(2000);
        Assert.That(notification.DisplayedProcessed, Is.Zero, "An old import transform must not leak into a new stage.");
    }

    [Test]
    public void CancelledNotificationCannotBeOverwrittenByInFlightAnimation()
    {
        using var notification = new Probe();
        notification.Report(new O2JamLibraryProgress(0, 64));
        notification.Step(0);
        notification.Report(new O2JamLibraryProgress(16, 64));
        notification.Step(500);
        notification.State = ProgressNotificationState.Cancelled;
        notification.Text = O2LazerStrings.ClearStopped;
        notification.AdvanceTransforms(1500);
        Assert.That(notification.Text, Is.EqualTo(O2LazerStrings.ClearStopped));
    }

    private partial class Probe : O2JamLibraryProgressNotification
    {
        private readonly ManualClock manual = new() { Rate = 1 };
        private readonly FramedClock framed;

        public Probe() : base(progress => O2LazerStrings.RefreshingProgress(progress.Processed, progress.Total))
        {
            framed = new FramedClock(manual);
            Clock = framed;
            setClock(MainContent);
            setClock(IconContent);
        }

        public void Step(double time)
        {
            manual.CurrentTime = time;
            framed.ProcessFrame();
            Scheduler.Update();
            UpdateTransforms();
            Scheduler.Update();
        }

        public void AdvanceTransforms(double time)
        {
            // Exercise the count transform independently of native icon/audio rendering.
            manual.CurrentTime = time;
            framed.ProcessFrame();
            UpdateTransforms();
        }

        private void setClock(Drawable drawable)
        {
            drawable.Clock = framed;
            if (drawable is Container container)
                foreach (var child in container.Children)
                    setClock(child);
        }
    }
}