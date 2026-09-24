using System;
using System.Threading.Tasks;
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

        var notification = new ProgressNotification
        {
            Text = O2LazerStrings.RefreshingProgress(0, 0),
            Progress = 0,
            State = ProgressNotificationState.Active,
        };
        notifications?.Post(notification);

        try
        {
            var summary = await Application.RefreshAsync(progress =>
            {
                notification.Text = progress.SynchronisingCollections
                    ? O2LazerStrings.SynchronisingCollections
                    : O2LazerStrings.RefreshingProgress(progress.Processed, progress.Total);
                notification.Progress = progress.Total == 0 ? 1 : (float)progress.Processed / progress.Total;
            }, notification.CancellationToken).ConfigureAwait(false);
            notification.CompletionText = summary.RulesetUnavailable ? O2LazerStrings.RulesetUnavailable : O2LazerStrings.RefreshComplete;
            notification.State = summary.RulesetUnavailable ? ProgressNotificationState.Cancelled : ProgressNotificationState.Completed;
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
        try
        {
            await Application.DeleteAllAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            reportFailure(exception);
        }
    }

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
