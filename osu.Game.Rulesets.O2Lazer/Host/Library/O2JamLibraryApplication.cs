using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal enum O2JamLibraryStage
{
    ScanningSources,
    ReadingImportedMetadata,
    CheckingStoredFiles,
    ReadingSourceFiles,
    MatchingSources,
    RefreshingCharts,
    SynchronisingCollections,
    CalculatingDifficulties,
    ClearingCharts,
}

internal readonly record struct O2JamLibraryProgress(int Processed, int Total, O2JamLibraryStage Stage = O2JamLibraryStage.RefreshingCharts)
{
    public void Publish(Action<O2JamLibraryProgress>? observer)
    {
        try { observer?.Invoke(this); }
        catch (Exception exception) { Logger.Error(exception, "O2Jam library progress observer failed."); }
    }
}

internal interface IO2JamLibraryBackend : IDisposable
{
    bool DirectoryExists(string path);
    O2JamImportSummary Refresh(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken,
                              O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update);
    void DeleteAll(Action<O2JamLibraryProgress>? progress = null, CancellationToken cancellationToken = default);
    void UpdateCollections(string path, bool enabled);
    (int Failed, int PendingNotifications) CalculateDifficulties(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken);
}

internal interface IO2JamConcurrentDifficultyBackend
{
    IDisposable StartDifficultyProcessing(Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Serialises library mutations independently of settings-page and notification lifetimes.
/// </summary>
internal sealed class O2JamLibraryApplication(IO2JamLibraryBackend backend) : IDisposable
{
    private readonly object operationLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task queue = Task.CompletedTask;
    private Task<O2JamImportSummary>? refreshTask;
    private O2JamLibraryRefreshMode refreshMode;
    private string libraryPath = string.Empty;
    private bool collectionsEnabled;
    private int pending;
    private bool disposed;

    public event Action? ActivityChanged;

    public bool IsBusy
    {
        get { lock (operationLock) return pending != 0; }
    }

    public bool CanRefresh
    {
        get
        {
            lock (operationLock)
                return !disposed && pending == 0 && !string.IsNullOrWhiteSpace(libraryPath) && backend.DirectoryExists(libraryPath);
        }
    }

    public Task UpdateSettingsAsync(string path, bool syncCollections)
    {
        Task result;
        lock (operationLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            libraryPath = path;
            collectionsEnabled = syncCollections;
            result = enqueue(() =>
            {
                updateCollections();
                return true;
            });
        }
        notifyActivity();
        return result;
    }

    public Task<O2JamImportSummary> RefreshAsync(Action<O2JamLibraryProgress>? progress = null, CancellationToken cancellationToken = default,
                                                O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update)
    {
        Task<O2JamImportSummary> result;
        lock (operationLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (refreshTask is { IsCompleted: false } && refreshMode == mode)
                return refreshTask;

            var path = libraryPath;
            if (string.IsNullOrWhiteSpace(path) || !backend.DirectoryExists(path))
                throw new InvalidOperationException("The library directory is unavailable.");

            refreshMode = mode;
            result = refreshTask = enqueue(() =>
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                cancellation.Token.ThrowIfCancellationRequested();
                using var difficultyWork = (backend as IO2JamConcurrentDifficultyBackend)?.StartDifficultyProcessing(progress, cancellation.Token);
                var summary = backend.Refresh(path, progress, cancellation.Token, mode);
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    new O2JamLibraryProgress(0, 0, O2JamLibraryStage.SynchronisingCollections).Publish(progress);
                    updateCollections();
                }
                catch (OperationCanceledException) when (summary.Imported + summary.Updated > 0)
                {
                    throw new O2JamImportCancelledException(summary, cancellation.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    Logger.Error(exception, "O2Jam collection synchronisation failed after refreshing the library; retry on the next refresh.");
                    summary = summary with { PendingNotifications = summary.PendingNotifications + 1 };
                }
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!summary.RulesetUnavailable)
                    {
                        var result = backend.CalculateDifficulties(path, progress, cancellation.Token);
                        summary = summary with
                        {
                            FailedDifficultyCalculations = result.Failed,
                            PendingNotifications = summary.PendingNotifications + result.PendingNotifications,
                        };
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (summary.Imported + summary.Updated > 0)
                {
                    throw new O2JamImportCancelledException(summary, cancellation.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    // Star caches are supplementary: failure must not discard a playable import.
                    Logger.Error(exception, "O2Jam difficulty processing failed after importing the library; retry on the next refresh.");
                    summary = summary with { FailedDifficultyCalculations = summary.FailedDifficultyCalculations + 1 };
                }
                return summary;
            });
        }
        notifyActivity();
        return result;
    }

    public Task DeleteAllAsync(Action<O2JamLibraryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Task result;
        lock (operationLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            result = enqueue(() =>
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                cancellation.Token.ThrowIfCancellationRequested();
                try { backend.DeleteAll(progress, cancellation.Token); }
                finally { updateCollections(); }
                return true;
            });
        }
        notifyActivity();
        return result;
    }

    // The queue survives a failed or cancelled predecessor, so a retry or clear still runs.
    private Task<T> enqueue<T>(Func<T> operation)
    {
        pending++;
        var task = queue.ContinueWith(_ =>
        {
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                return operation();
            }
            finally
            {
                lock (operationLock)
                    pending--;
                notifyActivity();
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        queue = task;
        return task;
    }

    private void updateCollections()
    {
        string path;
        bool enabled;
        lock (operationLock)
        {
            path = libraryPath;
            enabled = collectionsEnabled;
        }
        lifetime.Token.ThrowIfCancellationRequested();
        // Read the latest preference when work runs; an old refresh must not re-enable collections.
        backend.UpdateCollections(path, enabled);
    }

    private void notifyActivity()
    {
        var recipients = ActivityChanged;
        if (recipients == null)
            return;
        foreach (Action recipient in recipients.GetInvocationList())
        {
            try { recipient(); }
            catch (Exception exception) { Logger.Error(exception, "O2Jam library activity observer failed."); }
        }
    }

    public void Dispose()
    {
        Task pendingWork;
        lock (operationLock)
        {
            if (disposed)
                return;
            disposed = true;
            pendingWork = queue;
            ActivityChanged = null;
        }

        lifetime.Cancel();
        // Config disposal precedes host storage shutdown. Finish any entered transaction first.
        try { pendingWork.GetAwaiter().GetResult(); }
        catch (Exception) { /* Command callers observe the original failure. */ }
        backend.Dispose();
        lifetime.Dispose();
    }
}
