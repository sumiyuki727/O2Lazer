using System;
using System.Threading.Tasks;
using System.Threading;
using osu.Framework.Localisation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

/// <summary>
/// Connects persisted preferences and host notifications to an application owned by the config cache.
/// </summary>
internal sealed class O2JamLibrarySettingsSession : IDisposable
{
    private readonly Bindable<string> path;
    private readonly Bindable<bool> syncCollections;
    private INotificationOverlay? notifications;

    public O2JamLibraryApplication Application { get; }

    public static O2JamLibrarySettingsSession Get(O2JamRulesetConfigManager config,
                                                   Func<IO2JamLibraryBackend> createBackend,
                                                   INotificationOverlay? notifications)
    {
        var session = config.GetLibrarySession(() => new O2JamLibrarySettingsSession(config, createBackend()));
        session.notifications = notifications ?? session.notifications;
        return session;
    }

    internal O2JamLibrarySettingsSession(O2JamRulesetConfigManager config, IO2JamLibraryBackend backend)
    {
        Application = new O2JamLibraryApplication(backend);
        path = config.GetBindable<string>(O2JamRulesetSetting.LastImportPath);
        syncCollections = config.GetBindable<bool>(O2JamRulesetSetting.SyncSourceFolderCollections);
        path.ValueChanged += onPathChanged;
        syncCollections.ValueChanged += onSyncChanged;
        _ = updateSettings();
    }

    private void onPathChanged(ValueChangedEvent<string> change) => _ = updateSettings();
    private void onSyncChanged(ValueChangedEvent<bool> change) => _ = updateSettings();

    private async Task updateSettings()
    {
        try
        {
            await Application.UpdateSettingsAsync(path.Value, syncCollections.Value).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            reportFailure(exception);
        }
    }

    public async Task RefreshAsync()
    {
        if (!Application.CanRefresh)
            return;

        var notification = new O2JamLibraryProgressNotification(formatProgress)
        {
            Text = O2LazerStrings.CheckingBeatmaps,
            Progress = 0,
            State = ProgressNotificationState.Active,
        };
        notifications?.Post(notification);
        var imported = 0;

        try
        {
            var summary = await Application.RefreshAsync(progress =>
            {
                // Concurrent star work must not replace the import counter or flicker stages.
                if (progress.Stage == O2JamLibraryStage.CalculatingDifficulties && Volatile.Read(ref imported) == 0)
                    return;
                if (progress.Stage == O2JamLibraryStage.SynchronisingCollections)
                    Volatile.Write(ref imported, 1);
                if (PresentRefreshProgress(progress) is { } visible)
                    notification.Report(visible);
            }, notification.CancellationToken).ConfigureAwait(false);
            notification.CompletionText = summary.RulesetUnavailable ? O2LazerStrings.RulesetUnavailable
                : summary.Failed > 0 ? O2LazerStrings.RefreshWithFailures(summary.Imported, summary.Updated, summary.Failed)
                : summary.FailedDifficultyCalculations > 0 ? O2LazerStrings.RefreshDifficultiesPending
                : summary.PendingNotifications > 0 ? O2LazerStrings.RefreshUpdatesPending : O2LazerStrings.RefreshComplete;
            notification.Flush();
            notification.State = summary.RulesetUnavailable ? ProgressNotificationState.Cancelled : ProgressNotificationState.Completed;
        }
        catch (O2JamImportCancelledException exception)
        {
            // Native cancelled notifications do not post CompletionText. Keep partial results
            // on the existing notification instead of suggesting the committed batch vanished.
            notification.Text = O2LazerStrings.RefreshStopped(exception.Summary.Imported, exception.Summary.Updated);
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (OperationCanceledException)
        {
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "O2Jam library refresh failed.");
            notification.CompletionText = O2LazerStrings.LibraryOperationFailed;
            notification.State = ProgressNotificationState.Cancelled;
        }
    }

    public async Task DeleteAllAsync()
    {
        var notification = new O2JamLibraryProgressNotification(formatProgress)
        {
            Text = O2LazerStrings.ClearingBeatmaps,
            State = ProgressNotificationState.Active,
        };
        notifications?.Post(notification);
        try
        {
            await Application.DeleteAllAsync(notification.Report, notification.CancellationToken).ConfigureAwait(false);
            notification.CompletionText = O2LazerStrings.ClearComplete;
            notification.Flush();
            notification.State = ProgressNotificationState.Completed;
        }
        catch (OperationCanceledException)
        {
            notification.Text = O2LazerStrings.ClearStopped;
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (Exception exception)
        {
            notification.State = ProgressNotificationState.Cancelled;
            reportFailure(exception);
        }
    }

    private static LocalisableString formatProgress(O2JamLibraryProgress progress) => progress.Stage switch
    {
        O2JamLibraryStage.ReadingSourceFiles => O2LazerStrings.CheckingBeatmaps,
        O2JamLibraryStage.CalculatingDifficulties => O2LazerStrings.CalculatingDifficulties(progress.Processed, progress.Total),
        O2JamLibraryStage.ClearingCharts => O2LazerStrings.ClearingProgress(progress.Processed, progress.Total),
        _ => O2LazerStrings.RefreshingProgress(progress.Processed, progress.Total),
    };

    // Keep backend stages intact for diagnosis; only presentation combines the two
    // equally sized source passes and hides stages without any counted work.
    internal static O2JamLibraryProgress? PresentRefreshProgress(O2JamLibraryProgress progress) => progress.Stage switch
    {
        O2JamLibraryStage.ScanningSources or O2JamLibraryStage.ReadingImportedMetadata or O2JamLibraryStage.CheckingStoredFiles
            => new O2JamLibraryProgress(0, 0, O2JamLibraryStage.ReadingSourceFiles),
        O2JamLibraryStage.ReadingSourceFiles => progress with { Total = checked(progress.Total * 2) },
        O2JamLibraryStage.MatchingSources => new O2JamLibraryProgress(checked(progress.Total + progress.Processed),
            checked(progress.Total * 2), O2JamLibraryStage.ReadingSourceFiles),
        O2JamLibraryStage.SynchronisingCollections => null,
        O2JamLibraryStage.RefreshingCharts or O2JamLibraryStage.CalculatingDifficulties when progress.Total == 0 => null,
        _ => progress,
    };

    private void reportFailure(Exception exception)
    {
        Logger.Error(exception, "O2Jam library operation failed.");
        notifications?.Post(new SimpleNotification { Text = O2LazerStrings.LibraryOperationFailed });
    }

    public void Dispose()
    {
        path.ValueChanged -= onPathChanged;
        syncCollections.ValueChanged -= onSyncChanged;
        Application.Dispose();
        path.UnbindAll();
        syncCollections.UnbindAll();
    }
}
