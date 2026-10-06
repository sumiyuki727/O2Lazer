using System;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

internal partial class O2JamLibraryProgressNotification(Func<O2JamLibraryProgress, LocalisableString> format) : ProgressNotification
{
    private O2JamLibraryProgress target;
    private bool hasTarget;
    private double lastReport;
    private double displayed;
    private int displayedInteger = -1;

    public double DisplayedProcessed
    {
        get => displayed;
        set
        {
            // Animate only between confirmed counts. This never predicts a transaction
            // succeeding and leaves rendering/easing to the native transform machinery.
            displayed = Math.Clamp(value, 0, target.Processed);
            if (State == ProgressNotificationState.Cancelled)
                return;
            Progress = target.Total == 0 ? 0 : (float)(displayed / target.Total);
            var integer = (int)displayed;
            if (displayedInteger == integer)
                return;
            displayedInteger = integer;
            Text = format(target with { Processed = integer });
        }
    }

    internal void Report(O2JamLibraryProgress progress) => Scheduler.AddOnce(updateTarget, progress);

    private void updateTarget(O2JamLibraryProgress progress)
    {
        if (State == ProgressNotificationState.Cancelled)
            return;
        var reset = !hasTarget || progress.Stage != target.Stage || progress.Total != target.Total || progress.Processed < target.Processed;
        var enteringCountedStage = (!hasTarget || progress.Stage != target.Stage)
                                   && (progress.Stage is O2JamLibraryStage.CalculatingDifficulties or O2JamLibraryStage.RefreshingCharts)
                                   && progress.Total > 0;
        var duration = enteringCountedStage ? 600 : reset || progress.Total == 0 ? 0 : Math.Clamp(Time.Current - lastReport, 100, 700);
        target = progress;
        hasTarget = true;
        lastReport = Time.Current;
        if (reset)
            displayedInteger = -1;
        if (enteringCountedStage)
        {
            this.ClearTransforms(targetMember: nameof(DisplayedProcessed));
            DisplayedProcessed = 0;
        }
        this.TransformTo(nameof(DisplayedProcessed), (double)progress.Processed, duration, Easing.None);
    }

    internal void Flush() => Scheduler.AddOnce(() => this.TransformTo(nameof(DisplayedProcessed), (double)target.Processed, 0));
}
