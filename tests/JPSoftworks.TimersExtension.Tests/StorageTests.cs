// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class StorageTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "JPSoftworks.TimersExtension.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void StarterTemplatesAreSavedOnceAndStayDeleted()
    {
        var settingsPath = Path.Combine(this._directory, "settings.json");
        Guid[] originalIds;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            storage.InitializeStarterTemplates(settingsPath);
            scheduler.Initialize();
            var starters = scheduler.GetTemplatesSnapshot();
            Assert.Equal([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1)],
                starters.Select(template => template.Duration));
            Assert.All(starters, template => Assert.True(template.IsTemplate));
            Assert.Empty(scheduler.GetTimersSnapshot());
            originalIds = starters.Select(template => template.Id).ToArray();
        }

        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            storage.InitializeStarterTemplates(settingsPath);
            scheduler.Initialize();
            Assert.Equal(originalIds, scheduler.GetTemplatesSnapshot().Select(template => template.Id));
            foreach (var id in originalIds)
            {
                scheduler.RemoveTemplate(id);
            }
        }

        using var reopenedStorage = new TimerStorage(this._directory);
        using var reopenedScheduler = new TimerScheduler(reopenedStorage);
        reopenedStorage.InitializeStarterTemplates(settingsPath);
        reopenedScheduler.Initialize();
        Assert.Empty(reopenedScheduler.GetTemplatesSnapshot());
    }

    [Theory]
    [InlineData("timers.json")]
    [InlineData("stopwatches.json")]
    [InlineData("templates.json")]
    [InlineData("templates.json.bak")]
    [InlineData("settings.json")]
    public void StarterTemplatesAreSkippedForExistingProfiles(string existingFile)
    {
        var profileDirectory = Path.Combine(this._directory, "profile");
        var settingsPath = Path.Combine(this._directory, "settings.json");
        Directory.CreateDirectory(profileDirectory);
        var existingPath = existingFile == "settings.json"
            ? settingsPath
            : Path.Combine(profileDirectory, existingFile);
        File.WriteAllText(existingPath, "[]");

        using var storage = new TimerStorage(profileDirectory);
        storage.InitializeStarterTemplates(settingsPath);

        Assert.Empty(storage.LoadTemplates());
        Assert.Equal("[]", File.ReadAllText(existingPath));
        Assert.Equal(existingFile == "templates.json", File.Exists(Path.Combine(profileDirectory, "templates.json")));
    }

    [Fact]
    public void StopwatchLapsAndOriginalStartSurviveRestartAndSubsequentSaves()
    {
        var recordedAt = DateTimeOffset.UtcNow;
        var startedAt = recordedAt.AddSeconds(-30);
        var stopwatch = new StopwatchInstance();
        stopwatch.TransitionToRunning(startedAt);
        stopwatch.TransitionToPaused(recordedAt, TimeSpan.FromSeconds(30));
        stopwatch.RecordLap(recordedAt);
        stopwatch.ElapsedBeforePause = TimeSpan.FromSeconds(60);
        stopwatch.RecordLap(recordedAt.AddSeconds(30));
        using (var storage = new StopwatchStorage(this._directory))
        {
            storage.Save([stopwatch]);
        }

        using (var storage = new StopwatchStorage(this._directory))
        using (var scheduler = new StopwatchScheduler(storage))
        {
            scheduler.Initialize();
            var restored = Assert.Single(scheduler.GetStopwatchesSnapshot());
            Assert.Equal(stopwatch.Id, restored.Id);
            Assert.Equal(startedAt, restored.RunStartedAt);
            Assert.Equal(2, restored.Laps.Count);
            for (var i = 0; i < stopwatch.Laps.Count; i++)
            {
                Assert.Equal(stopwatch.Laps[i].Number, restored.Laps[i].Number);
                Assert.Equal(stopwatch.Laps[i].LapTime, restored.Laps[i].LapTime);
                Assert.Equal(stopwatch.Laps[i].TotalTime, restored.Laps[i].TotalTime);
                Assert.Equal(stopwatch.Laps[i].RecordedAt, restored.Laps[i].RecordedAt);
            }

            scheduler.Resume(restored.Id);
            var lap = scheduler.RecordLap(restored.Id);
            Assert.NotNull(lap);
            Assert.Equal(3, lap.Number);
            Assert.Equal(lap.TotalTime - TimeSpan.FromSeconds(60), lap.LapTime);
        }

        using var resavedStorage = new StopwatchStorage(this._directory);
        var resaved = Assert.Single(resavedStorage.Load());
        Assert.Equal(3, resaved.Laps.Count);
        Assert.Equal(startedAt, resaved.RunStartedAt);
    }

    [Fact]
    public async Task PersistenceRecoversAfterTemporaryFileCleanupFails()
    {
        var path = Path.Combine(this._directory, "timers.json");
        var storage = new JsonStorageService<TimerInstance>(path, TimerJsonContext.Default.TimerInstanceArray);
        var pendingField
            = typeof(JsonStorageService<TimerInstance>).GetField("_persistTask",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Pending() => (Task)pendingField.GetValue(storage)!;
        try
        {
            using (var fileLock = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                storage.Save([]);
                await Assert.ThrowsAsync<IOException>(() => Pending().WaitAsync(TestTimeout));
            }

            var timer = new TimerInstance { Name = "Recovered", Duration = TimeSpan.FromMinutes(5) };
            storage.Save([timer]);
            await Pending().WaitAsync(TestTimeout);
            var restored = await Task.Run(storage.Load).WaitAsync(TestTimeout);
            Assert.Equal(timer.Id, Assert.Single(restored).Id);
        }
        finally
        {
            if (!Pending().IsCompleted)
            {
                // Unblock a regressed implementation so test cleanup cannot hang.
                var mutex = (SemaphoreSlim)typeof(JsonStorageService<TimerInstance>)
                    .GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
                mutex.Release();
                await Pending().WaitAsync(TestTimeout);
            }

            try
            {
                storage.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedPrimaryAllowsSessionUseAndPreservesSavedTimers(bool hasBackup)
    {
        var primaryTimer = new TimerInstance { Name = "Latest timer", Duration = TimeSpan.FromMinutes(5) };
        var olderRunningTimer = new TimerInstance
        {
            Duration = TimeSpan.FromHours(2), StartedAt = DateTimeOffset.UtcNow, State = TimerState.Running
        };
        var olderExpiredTimer = new TimerInstance
        {
            Duration = TimeSpan.FromMinutes(1),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            State = TimerState.Running
        };
        using (var seed = new TimerStorage(this._directory))
        {
            if (hasBackup)
            {
                seed.SaveTimers([olderRunningTimer, olderExpiredTimer]);
            }

            seed.SaveTimers([primaryTimer]);
        }

        var path = Path.Combine(this._directory, "timers.json");
        var savedPrimary = File.ReadAllText(path);
        var savedBackup = hasBackup ? File.ReadAllText(path + ".bak") : null;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            var alarms = 0;
            scheduler.TimerFinished += (_, _) => alarms++;
            using (var fileLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                scheduler.Initialize();
                Assert.True(storage.TimersAreReadOnly);
                Assert.Equal(hasBackup ? 2 : 0, scheduler.GetTimersSnapshot().Count);
                if (hasBackup)
                {
                    Assert.Equal(TimerState.Paused, scheduler.TryGetTimer(olderRunningTimer.Id)?.State);
                    Assert.Equal(TimerState.Finished, scheduler.TryGetTimer(olderExpiredTimer.Id)?.State);
                }

                var sessionTimer = scheduler.StartTimer(TimeSpan.FromMinutes(1), "Session timer");
                Assert.Same(sessionTimer, scheduler.TryGetTimer(sessionTimer.Id));
                Assert.Equal(0, alarms);
            }

            Assert.Equal(primaryTimer.Id, Assert.Single(storage.LoadTimers()).Id);
            Assert.True(storage.TimersAreReadOnly);
            scheduler.StartTimer(TimeSpan.FromMinutes(2), "Still session-only");
        }

        Assert.Equal(savedPrimary, File.ReadAllText(path));
        Assert.Equal(savedBackup, File.Exists(path + ".bak") ? File.ReadAllText(path + ".bak") : null);

        Guid addedId;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            scheduler.Initialize();
            Assert.False(storage.IsReadOnly);
            Assert.Equal(primaryTimer.Id, Assert.Single(scheduler.GetTimersSnapshot()).Id);
            addedId = scheduler.StartTimer(TimeSpan.FromMinutes(1), "Writable again").Id;
        }

        using var reopened = new TimerStorage(this._directory);
        var afterReload = reopened.LoadTimers();
        Assert.Equal(2, afterReload.Count);
        Assert.Contains(afterReload, timer => timer.Id == addedId);
    }

    [Fact]
    public void InaccessiblePrimaryUsesItsBackupWithoutTryingToReplaceThePrimary()
    {
        var timer = new TimerInstance { Duration = TimeSpan.FromMinutes(1) };
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([timer]);
        }

        var path = Path.Combine(this._directory, "timers.json");
        File.Move(path, path + ".bak");
        Directory.CreateDirectory(path);
        using (var storage = new TimerStorage(this._directory))
        {
            Assert.Equal(timer.Id, Assert.Single(storage.LoadTimers()).Id);
            Assert.True(storage.TimersAreReadOnly);
            storage.SaveTimers([]);
        }

        Assert.True(Directory.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AnUnavailableTemplateStoreDoesNotDisableTimerPersistence()
    {
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTemplates([new TimerDefinition("Saved", TimeSpan.FromMinutes(1), true)]);
        }

        Guid addedId;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        using (var fileLock = new FileStream(Path.Combine(this._directory, "templates.json"), FileMode.Open,
                   FileAccess.Read, FileShare.None))
        {
            scheduler.Initialize();
            Assert.True(storage.IsReadOnly);
            Assert.False(storage.TimersAreReadOnly);
            addedId = scheduler.StartTimer(TimeSpan.FromMinutes(2)).Id;
        }

        using var reopened = new TimerStorage(this._directory);
        Assert.Equal(addedId, Assert.Single(reopened.LoadTimers()).Id);
        Assert.Equal("Saved", Assert.Single(reopened.LoadTemplates()).Name);
    }

    [Fact]
    public async Task LoadRetriesATemporarySharingViolation()
    {
        var timer = new TimerInstance { Duration = TimeSpan.FromMinutes(1) };
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([timer]);
        }

        using var storage = new TimerStorage(this._directory);
        using var fileLock = new FileStream(Path.Combine(this._directory, "timers.json"), FileMode.Open,
            FileAccess.Read, FileShare.None);
        var loaded = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Thread(() =>
        {
            try
            {
                loaded.SetResult(Assert.Single(storage.LoadTimers()).Id);
            }
            catch (Exception ex)
            {
                loaded.SetException(ex);
            }
        });
        reader.IsBackground = true;
        reader.Start();

        // Release the exclusive handle once the reader has reached the retry delay.
        Assert.True(SpinWait.SpinUntil(
            () => (reader.ThreadState & ThreadState.WaitSleepJoin) != 0 || loaded.Task.IsCompleted, TestTimeout));
        fileLock.Dispose();
        Assert.Equal(timer.Id, await loaded.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task DisposeReleasesResourcesWhenPersistenceFailed()
    {
        var path = Path.Combine(this._directory, "timers.json");
        var storage = new JsonStorageService<TimerInstance>(path, TimerJsonContext.Default.TimerInstanceArray);
        var persistTask = typeof(JsonStorageService<TimerInstance>)
            .GetField("_persistTask", BindingFlags.Instance | BindingFlags.NonPublic)!;

        using (var fileLock = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            storage.Save([]);
            await Assert.ThrowsAsync<IOException>(() =>
                ((Task)persistTask.GetValue(storage)!).WaitAsync(TestTimeout));
        }

        Assert.Throws<IOException>(storage.Dispose);
        Assert.Throws<ObjectDisposedException>(() => storage.Load());
        storage.Dispose();
    }

    public void Dispose()
    {
        Directory.Delete(this._directory, true);
    }
}