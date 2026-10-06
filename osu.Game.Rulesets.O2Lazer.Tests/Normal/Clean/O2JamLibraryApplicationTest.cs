using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLibraryApplicationTest
{
    [Test]
    public async Task RefreshAndClearAreSerialisedAndLatestCollectionPreferenceWins()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            RefreshAction = (_, token) =>
            {
                entered.TrySetResult();
                release.Wait(token);
            },
        };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("old", true);
        var refresh = application.RefreshAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var preference = application.UpdateSettingsAsync("new", false);
            var clear = application.DeleteAllAsync();
            Assert.That(backend.Clears, Is.Zero, "Clear must not race an import transaction.");
            Assert.That(application.IsBusy, Is.True);
            release.Set();
            await Task.WhenAll(refresh, preference, clear).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(backend.RefreshedPath, Is.EqualTo("old"), "A running refresh retains its selected source.");
                Assert.That(backend.Collections, Is.EqualTo(("new", false)));
                Assert.That(backend.Clears, Is.EqualTo(1));
                Assert.That(application.IsBusy, Is.False);
            });
        }
        finally { release.Set(); }
    }

    [Test]
    public async Task InternalRepairQueuesAfterOrdinaryRefreshRatherThanSharingItsResult()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            RefreshAction = (_, token) =>
            {
                entered.TrySetResult();
                release.Wait(token);
            },
        };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", false);
        var update = application.RefreshAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var repair = application.RefreshAsync(mode: O2JamLibraryRefreshMode.Repair);
            Assert.That(repair, Is.Not.SameAs(update), "A normal result cannot satisfy a requested deep verification.");
            Assert.That(application.RefreshAsync(mode: O2JamLibraryRefreshMode.Repair), Is.SameAs(repair));
            release.Set();
            await Task.WhenAll(update, repair).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(backend.Modes, Is.EqualTo(new[] { O2JamLibraryRefreshMode.Update, O2JamLibraryRefreshMode.Repair }));
            Assert.That(application.IsBusy, Is.False);
        }
        finally { release.Set(); }
    }
    [Test]
    public async Task RepeatedRefreshSharesTheRunningOperation()
    {
        using var release = new ManualResetEventSlim();
        var backend = new FakeBackend { RefreshAction = (_, token) => release.Wait(token) };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", false);
        var first = application.RefreshAsync();
        try
        {
            Assert.That(application.RefreshAsync(), Is.SameAs(first));
        }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(backend.Refreshes, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellationDoesNotPreventTheNextOperation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            RefreshAction = (_, token) =>
            {
                entered.TrySetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
            },
        };
        using var application = new O2JamLibraryApplication(backend);
        using var cancellation = new CancellationTokenSource();
        await application.UpdateSettingsAsync("library", false);
        var refresh = application.RefreshAsync(cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await refresh);
        await application.DeleteAllAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(backend.Clears, Is.EqualTo(1));
        Assert.That(application.IsBusy, Is.False);
    }

    [Test]
    public async Task FailedRefreshCanBeRetried()
    {
        var backend = new FakeBackend { RefreshAction = (_, _) => throw new IOException("fixture failure") };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", false);
        Assert.ThrowsAsync<IOException>(async () => await application.RefreshAsync());
        backend.RefreshAction = null;
        await application.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(backend.Refreshes, Is.EqualTo(2));
        Assert.That(application.CanRefresh, Is.True);
    }

    [Test]
    public async Task InvalidPathDoesNotStartImport()
    {
        var backend = new FakeBackend { PathExists = false };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("missing", false);
        Assert.That(application.CanRefresh, Is.False);
        Assert.Throws<InvalidOperationException>(() => application.RefreshAsync());
        Assert.That(backend.Refreshes, Is.Zero);
    }

    [Test]
    public async Task DisposalCancelsWorkBeforeDisposingBackend()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            RefreshAction = (_, token) =>
            {
                entered.TrySetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
            },
        };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", false);
        var refresh = application.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(application.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await refresh);
        Assert.That(backend.Disposed, Is.True);
        Assert.That(backend.ActiveAtDisposal, Is.False);
        Assert.Throws<ObjectDisposedException>(() => application.DeleteAllAsync());
    }

    [Test]
    public async Task ConfigOwnsOneSessionAndPreferencesWorkWithoutASettingsPage()
    {
        var backend = new FakeBackend();
        var config = new O2JamRulesetConfigManager(null, new RulesetInfo { ShortName = "o2lazer" });
        try
        {
            var session = config.GetLibrarySession(() => new O2JamLibrarySettingsSession(config, backend));
            Assert.That(config.GetLibrarySession(() => throw new InvalidOperationException()), Is.SameAs(session));
            config.SetValue(O2JamRulesetSetting.LastImportPath, "changed");
            config.SetValue(O2JamRulesetSetting.SyncSourceFolderCollections, true);
            await session.Application.DeleteAllAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(backend.Collections, Is.EqualTo(("changed", true)));
            config.SetValue(O2JamRulesetSetting.SyncSourceFolderCollections, false);
            await session.Application.DeleteAllAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(backend.Collections, Is.EqualTo(("changed", false)));
        }
        finally { config.Dispose(); }
        Assert.That(backend.Disposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() => config.GetLibrarySession(() => throw new InvalidOperationException()));
    }

    [Test]
    public void ChartScanIncludesNestedCaseInsensitiveExtensionsAndHonoursCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "o2lazer-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "first.OJN"), "");
            File.WriteAllText(Path.Combine(directory, "nested", "second.ojn"), "");
            File.WriteAllText(Path.Combine(directory, "sound.ojm"), "");
            Assert.That(O2JamChartSourceScanner.Enumerate(directory, CancellationToken.None), Has.Length.EqualTo(2));
            Assert.Throws<OperationCanceledException>(() =>
                O2JamChartSourceScanner.Enumerate(directory, new CancellationToken(true)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void ReopeningSettingsReusesBackendWithoutRealmDependencies()
    {
        using var config = new O2JamRulesetConfigManager(null, new O2LazerRuleset().RulesetInfo);
        var creations = 0;
        IO2JamLibraryBackend createBackend()
        {
            creations++;
            return new FakeBackend();
        }

        var first = O2JamLibrarySettingsSession.Get(config, createBackend, null);
        var second = O2JamLibrarySettingsSession.Get(config, createBackend, null);
        Assert.That(second, Is.SameAs(first));
        Assert.That(creations, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellationAfterBackendCommitRetainsSummary()
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new FakeBackend { RefreshAction = (_, _) => cancellation.Cancel() };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", false);
        var exception = Assert.ThrowsAsync<O2JamImportCancelledException>(async () =>
            await application.RefreshAsync(cancellationToken: cancellation.Token))!;
        Assert.That(exception.Summary.Imported, Is.EqualTo(1));
        Assert.That(exception.Summary.Failed, Is.Zero);
        Assert.That(application.IsBusy, Is.False);
    }

    [Test]
    public async Task PostRefreshCollectionAndActivityFailuresDoNotReplaceWriteResults()
    {
        var backend = new FakeBackend();
        using var application = new O2JamLibraryApplication(backend);
        application.ActivityChanged += () => throw new InvalidOperationException("Injected activity observer failure.");
        await application.UpdateSettingsAsync("library", false);
        backend.FailCollections = true;
        var summary = await application.RefreshAsync();
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(summary.PendingNotifications, Is.EqualTo(1));
        Assert.That(application.IsBusy, Is.False);
        backend.FailCollections = false;
        Assert.That((await application.RefreshAsync()).PendingNotifications, Is.Zero);
    }
    [Test]
    public async Task DifficultiesStartOnlyAfterPlayableImportAndCollectionSync()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend();
        backend.DifficultyAction = token =>
        {
            Assert.That(backend.Refreshes, Is.EqualTo(1));
            Assert.That(backend.CollectionUpdates, Is.EqualTo(3));
            Assert.That(backend.Collections, Is.EqualTo(("library", true)));
            entered.TrySetResult();
            release.Wait(token);
        };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", false);
        await application.UpdateSettingsAsync("library", true);
        var stages = new List<O2JamLibraryStage>();
        var refresh = application.RefreshAsync(progress => stages.Add(progress.Stage));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(refresh.IsCompleted, Is.False);
            Assert.That(stages, Is.EqualTo(new[] { O2JamLibraryStage.RefreshingCharts, O2JamLibraryStage.SynchronisingCollections, O2JamLibraryStage.CalculatingDifficulties }));
        }
        finally { release.Set(); }
        Assert.That((await refresh).Imported, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellingDeferredStarsRetainsCommittedImportAndCollections()
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new FakeBackend { DifficultyAction = _ => cancellation.Cancel() };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", true);
        var exception = Assert.ThrowsAsync<O2JamImportCancelledException>(async () =>
            await application.RefreshAsync(cancellationToken: cancellation.Token))!;
        Assert.That(exception.Summary.Imported, Is.EqualTo(1));
        Assert.That(exception.Summary.Failed, Is.Zero);
        Assert.That(backend.Collections, Is.EqualTo(("library", true)));
    }

    [Test]
    public async Task DeferredStarFailureDoesNotReportPlayableImportAsFailed()
    {
        var backend = new FakeBackend { DifficultyAction = _ => throw new IOException("Injected star calculation failure.") };
        using var application = new O2JamLibraryApplication(backend);
        await application.UpdateSettingsAsync("library", true);
        var summary = await application.RefreshAsync();
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(summary.FailedDifficultyCalculations, Is.EqualTo(1));
    }
    private sealed class FakeBackend : IO2JamLibraryBackend
    {
        public Action<string, CancellationToken>? RefreshAction;
        public Action<CancellationToken>? DifficultyAction;
        public bool PathExists = true;
        public bool FailCollections;
        public List<O2JamLibraryRefreshMode> Modes { get; } = [];
        public int Refreshes;
        public int CollectionUpdates;
        public int Clears;
        public string? RefreshedPath;
        public (string, bool) Collections;
        public bool Disposed;
        public bool ActiveAtDisposal;
        private bool active;

        public bool DirectoryExists(string path) => PathExists;

        public O2JamImportSummary Refresh(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken, O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update)
        {
            active = true;
            try
            {
                Refreshes++;
                Modes.Add(mode);
                RefreshedPath = path;
                progress?.Invoke(new O2JamLibraryProgress(1, 2));
                RefreshAction?.Invoke(path, cancellationToken);
                return new O2JamImportSummary(1, 0, 0, 0, false);
            }
            finally { active = false; }
        }

        public (int Failed, int PendingNotifications) CalculateDifficulties(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken)
        {
            progress?.Invoke(new O2JamLibraryProgress(0, 1, O2JamLibraryStage.CalculatingDifficulties));
            DifficultyAction?.Invoke(cancellationToken);
            return (0, 0);
        }
        public void DeleteAll(Action<O2JamLibraryProgress>? progress = null, CancellationToken cancellationToken = default) => Clears++;
        public void UpdateCollections(string path, bool enabled)
        {
            CollectionUpdates++;
            if (FailCollections)
                throw new InvalidOperationException("Injected collection failure.");
            Collections = (path, enabled);
        }
        public void Dispose()
        {
            ActiveAtDisposal = active;
            Disposed = true;
        }
    }
}
