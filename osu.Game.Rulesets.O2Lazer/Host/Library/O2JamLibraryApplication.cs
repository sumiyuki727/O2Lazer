using System;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal readonly record struct O2JamLibraryProgress(int Processed, int Total, bool SynchronisingCollections = false);

internal interface IO2JamLibraryBackend : IDisposable
{
    bool DirectoryExists(string path);
    O2JamImportSummary Refresh(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken);
    void DeleteAll();
    void UpdateCollections(string path, bool enabled);
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
        ActivityChanged?.Invoke();
        return result;
    }

    public Task<O2JamImportSummary> RefreshAsync(Action<O2JamLibraryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Task<O2JamImportSummary> result;
        lock (operationLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (refreshTask is { IsCompleted: false })
                return refreshTask;

            var path = libraryPath;
            if (string.IsNullOrWhiteSpace(path) || !backend.DirectoryExists(path))
                throw new InvalidOperationException("The library directory is unavailable.");

            result = refreshTask = enqueue(() =>
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                cancellation.Token.ThrowIfCancellationRequested();
                var summary = backend.Refresh(path, progress, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                progress?.Invoke(new O2JamLibraryProgress(0, 0, true));
                updateCollections();
                return summary;
            });
        }
        ActivityChanged?.Invoke();
        return result;
    }

    public Task DeleteAllAsync()
    {
        Task result;
        lock (operationLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            result = enqueue(() =>
            {
                backend.DeleteAll();
                updateCollections();
                return true;
            });
        }
        ActivityChanged?.Invoke();
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
                ActivityChanged?.Invoke();
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
