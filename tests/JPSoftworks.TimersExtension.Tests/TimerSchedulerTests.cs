// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Pages;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class TimerSchedulerTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "JPSoftworks.TimersExtension.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100000000)]
    public void StartRejectsInvalidDurationWithoutAddingATimer(double hours)
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.StartTimer(TimeSpan.FromHours(hours)));
        Assert.Empty(scheduler.GetTimersSnapshot());
    }

    [Fact]
    public void OversizedSavedTemplateCannotStartATimer()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var command = new StartTimerCommand(new TimerDefinition("Old template", TimeSpan.FromHours(100000000)),
            scheduler);

        Assert.NotNull(command.Invoke());
        Assert.Empty(scheduler.GetTimersSnapshot());
    }

    [Fact]
    public void UnnamedTemplatesStayUnnamedInStorageAndOnReload()
    {
        var duration = TimeSpan.FromMinutes(4);
        var editedTemplate = new TimerDefinition("Tea", duration, true);
        var namedTemplate = new TimerDefinition("Timer 4m", duration, true);
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            scheduler.StartTimer(new TimerDefinition("  ", duration), true);
            scheduler.AddTemplate(new TimerDefinition(string.Empty, duration));
            scheduler.AddTemplate(editedTemplate);
            Assert.True(scheduler.TryUpdateTemplate(editedTemplate with { Name = string.Empty }));
            scheduler.AddTemplate(namedTemplate);
        }

        using var reopened = new TimerStorage(this._directory);
        var saved = reopened.LoadTemplates();
        Assert.Equal(4, saved.Count);
        Assert.All(saved,
            template => Assert.Equal(template.Id == namedTemplate.Id ? namedTemplate.Name : string.Empty,
                template.Name));

        using var restored = new TimerScheduler(reopened);
        restored.Initialize();
        Assert.Equal(saved, restored.GetTemplatesSnapshot());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4320, true)]
    public void RestoringAnOverdueTimerRaisesItsAlarmOnceAndSavesTheFinishTime(int minutesOverdue, bool isMissed)
    {
        var duration = TimeSpan.FromMinutes(2);
        var finishedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesOverdue);
        var startedAt = finishedAt - duration;
        var expired = new TimerInstance { StartedAt = startedAt, Duration = duration, State = TimerState.Running };
        var paused = new TimerInstance { StartedAt = startedAt, Duration = duration, State = TimerState.Paused };
        var running = new TimerInstance
        {
            StartedAt = DateTimeOffset.UtcNow, Duration = TimeSpan.FromHours(1), State = TimerState.Running
        };
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([expired, paused, running]);
        }

        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            List<TimerFinishedEventArgs> alarms = [];
            scheduler.TimerFinished += (_, args) => alarms.Add(args);
            scheduler.Initialize();
            scheduler.Initialize();

            var alarm = Assert.Single(alarms);
            Assert.Equal(expired.Id, alarm.Timer.Id);
            Assert.Equal(TimerState.Finished, alarm.Timer.State);
            Assert.Equal(finishedAt, alarm.Timer.PausedAt);
            var request = AlarmRequest.FromTimer(alarm);
            Assert.Equal(isMissed, request.IsMissed);
            Assert.Equal(isMissed, request.IsSilent);
            var localFinish = finishedAt.ToLocalTime();
            var dateFormat = localFinish.Date == alarm.DetectedAt!.Value.LocalDateTime.Date ? "t" : "g";
            Assert.Contains(localFinish.ToString(dateFormat, CultureInfo.CurrentCulture), request.Message);
            Assert.Equal(TimerState.Paused, scheduler.TryGetTimer(paused.Id)?.State);
            Assert.Equal(TimerState.Running, scheduler.TryGetTimer(running.Id)?.State);
        }

        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            Assert.Equal(TimerState.Finished,
                Assert.Single(storage.LoadTimers(), timer => timer.Id == expired.Id).State);
            var alarms = 0;
            scheduler.TimerFinished += (_, _) => alarms++;
            scheduler.Initialize();
            Assert.Equal(0, alarms);
        }
    }

    [Theory]
    [InlineData("restart")]
    [InlineData("extend")]
    [InlineData("delete")]
    [InlineData("clear")]
    public void LateStopKeepsTheAlarmUntilTheCompletionIsSuperseded(string action)
    {
        var first = FinishedTimer();
        var second = FinishedTimer();
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([first, second]);
        }

        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        scheduler.Initialize();
        var shown = new HashSet<Guid>();
        using var alarms = new AlarmService(toast => shown.Add(Guid.Parse(toast.Tag)), id => shown.Remove(id));
        using var page = new TimersIndexPage(scheduler, alarms, new RepeatLastTimerCommand(scheduler));
        var firstAlarm = AlarmRequest.FromTimer(first, isSilent: true);
        var secondAlarm = AlarmRequest.FromTimer(second, isSilent: true);
        alarms.ShowAlarm(firstAlarm);
        alarms.ShowAlarm(secondAlarm);

        var changes = 0;
        scheduler.TimersChanged += (_, _) => changes++;
        scheduler.Stop(first.Id);
        Assert.Equal(0, changes);
        Assert.True(scheduler.IsCurrentCompletion(first.Id, firstAlarm.FinishedAt));
        Assert.Contains(firstAlarm.AlarmId, shown);

        switch (action)
        {
            case "restart":
                scheduler.Restart(first.Id);
                break;
            case "extend":
                Assert.True(scheduler.TryExtend(first.Id, TimeSpan.FromMinutes(1)));
                break;
            case "delete":
                scheduler.RemoveTimer(first.Id);
                break;
            case "clear":
                scheduler.ClearAll();
                break;
        }

        Assert.DoesNotContain(firstAlarm.AlarmId, shown);
        Assert.Equal(action != "clear", shown.Contains(secondAlarm.AlarmId));
        var count = scheduler.GetTimersSnapshot().Count;
        var deadline = scheduler.TryGetTimer(first.Id)?.ExpectedEnd;
        scheduler.HandleAlarmClosed(firstAlarm.ToResult(AlarmClosedKind.Restart));
        scheduler.HandleAlarmClosed(firstAlarm.ToResult(AlarmClosedKind.Snooze));
        Assert.Equal(count, scheduler.GetTimersSnapshot().Count);
        Assert.Equal(deadline, scheduler.TryGetTimer(first.Id)?.ExpectedEnd);

        static TimerInstance FinishedTimer() => new()
        {
            Duration = TimeSpan.FromMinutes(5), State = TimerState.Finished, PausedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void RestartDuringToastDeliveryRemovesTheToastAfterItIsShown()
    {
        var overdue = new TimerInstance
        {
            Duration = TimeSpan.FromMinutes(5),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            State = TimerState.Running
        };
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([overdue]);
        }

        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var shown = new HashSet<Guid>();
        using var alarms = new AlarmService(toast =>
        {
            scheduler.Restart(overdue.Id);
            shown.Add(Guid.Parse(toast.Tag));
        }, id => shown.Remove(id));
        using var page = new TimersIndexPage(scheduler, alarms, new RepeatLastTimerCommand(scheduler));

        scheduler.Initialize();

        Assert.Equal(TimerState.Running, scheduler.TryGetTimer(overdue.Id)?.State);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task ExtendingARunningOrPausedTimerChangesOnlyTheCurrentRun()
    {
        var original = TimeSpan.FromMinutes(4);
        var extra = TimeSpan.FromMinutes(5);
        TimeSpan remaining;
        Guid id;
        using (var storage = new TimerStorage(this._directory))
        {
            var delays = new ControlledDelay();
            using var scheduler = new TimerScheduler(storage, delays.WaitAsync, new ManualRefreshTimeProvider());
            var timer = scheduler.StartTimer(new TimerDefinition("Tea", original), true);
            id = timer.Id;
            var oldWait = await delays.NextAsync();
            var oldEnd = timer.ExpectedEnd;
            var alarms = 0;
            scheduler.TimerFinished += (_, _) => alarms++;
            Assert.True(scheduler.TryExtend(id, extra));
            Assert.Equal(oldEnd + extra, timer.ExpectedEnd);
            Assert.Equal(original, timer.Duration);
            await AssertCanceledAsync(oldWait);

            var extendedWait = await delays.NextAsync();
            timer.StartedAt -= original + TimeSpan.FromSeconds(5);
            extendedWait.Completion.TrySetResult();
            await delays.NextAsync();
            Assert.Equal(0, alarms);
            scheduler.Pause(id);
            remaining = timer.Remaining + TimeSpan.FromMinutes(1);
            Assert.True(scheduler.TryExtend(id, TimeSpan.FromMinutes(1)));
            Assert.Equal(TimerState.Paused, timer.State);
            Assert.Equal(remaining, timer.Remaining);
            Assert.Equal(original, Assert.Single(scheduler.GetTemplatesSnapshot()).Duration);
        }

        using var restoredStorage = new TimerStorage(this._directory);
        using var restored = new TimerScheduler(restoredStorage);
        restored.Initialize();
        var restoredTimer = Assert.Single(restored.GetTimersSnapshot());
        Assert.Equal(id, restoredTimer.Id);
        Assert.Equal(remaining, restoredTimer.Remaining);
        Assert.False(restored.TryExtend(id, TimeSpan.MaxValue));
        Assert.Equal(remaining, restoredTimer.Remaining);
        restored.Restart(id);
        Assert.Equal(original, restoredTimer.CountdownDuration);
        Assert.Null(restoredTimer.RunDuration);
        restored.Stop(id);
        Assert.False(restored.TryExtend(id, extra));
    }

    [Fact]
    public async Task StopNextTimerLeavesElapsedCountdownsForAlarmDelivery()
    {
        using var storage = new TimerStorage(this._directory);
        var delays = new ControlledDelay();
        var clock = new ManualRefreshTimeProvider();
        using var scheduler = new TimerScheduler(storage, delays.WaitAsync, clock);
        var elapsed = scheduler.StartTimer(TimeSpan.FromMinutes(4), "Tea");
        await delays.NextAsync();
        var next = scheduler.StartTimer(TimeSpan.FromMinutes(10), "Break");
        await delays.NextAsync();
        elapsed.StartedAt -= TimeSpan.FromMinutes(5);

        Assert.Same(next, scheduler.StopNextRunningTimer());
        Assert.Equal(TimerState.Cancelled, next.State);
        Assert.Equal(TimerState.Running, elapsed.State);

        var finished = new List<TimerFinishedEventArgs>();
        scheduler.TimerFinished += (_, completion) => finished.Add(completion);
        Assert.True(clock.Fire());
        Assert.Equal(TimerState.Finished, elapsed.State);
        Assert.Same(elapsed, Assert.Single(finished).Timer);
    }

    [Fact]
    public async Task ExtendingAfterTheDeadlineBeforeDeliveryCancelsTheOldAlarm()
    {
        using var storage = new TimerStorage(this._directory);
        var delays = new ControlledDelay();
        var clock = new ManualRefreshTimeProvider();
        using var scheduler = new TimerScheduler(storage, delays.WaitAsync, clock);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(4), "Tea");
        var wait = await delays.NextAsync();
        var completions = 0;
        scheduler.TimerFinished += (_, _) => completions++;
        timer.StartedAt -= TimeSpan.FromMinutes(5);

        var extra = TimeSpan.FromMinutes(1);
        Assert.True(scheduler.TryExtend(timer.Id, extra));
        await AssertCanceledAsync(wait);
        Assert.True(clock.Fire());
        Assert.Equal(0, completions);
        Assert.Equal(timer.StartedAt + extra, timer.ExpectedEnd);
        Assert.Same(timer, Assert.Single(scheduler.GetTimersSnapshot()));
    }

    [Fact]
    public async Task ExtendingAFinishedTimerRunsOnlyTheExtraTimeAndIgnoresTheOldAlarm()
    {
        var original = TimeSpan.FromMinutes(25);
        var extra = TimeSpan.FromMinutes(1);
        var timer = new TimerInstance
        {
            State = TimerState.Running, Duration = original, StartedAt = DateTimeOffset.UtcNow - original - extra
        };
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([timer]);
        }

        using var storage = new TimerStorage(this._directory);
        var delays = new ControlledDelay();
        var clock = new ManualRefreshTimeProvider();
        using var scheduler = new TimerScheduler(storage, delays.WaitAsync, clock);
        var alarms = new List<TimerFinishedEventArgs>();
        scheduler.TimerFinished += (_, completion) => alarms.Add(completion);
        scheduler.Initialize();
        var oldAlarm = AlarmRequest.FromTimer(Assert.Single(alarms));
        var item = Assert.Single(scheduler.GetTimersSnapshot());
        Assert.True(scheduler.TryExtend(item.Id, extra));
        Assert.False(scheduler.IsCurrentCompletion(item.Id, oldAlarm.FinishedAt));
        var deadline = item.ExpectedEnd;
        Assert.Equal(item.StartedAt + extra, deadline);
        Assert.Equal(original, item.Duration);
        scheduler.HandleAlarmClosed(oldAlarm.ToResult(AlarmClosedKind.Restart));
        Assert.Equal(deadline, item.ExpectedEnd);

        var wait = await delays.NextAsync();
        item.StartedAt -= extra + TimeSpan.FromSeconds(1);
        Assert.True(clock.Fire());
        await AssertCanceledAsync(wait);
        Assert.False(clock.Fire());
        Assert.Equal(2, alarms.Count);
        Assert.Equal(extra, alarms[1].CountdownDuration);
        scheduler.HandleAlarmClosed(oldAlarm.ToResult(AlarmClosedKind.Snooze));
        Assert.Equal(TimerState.Finished, item.State);
        scheduler.HandleAlarmClosed(AlarmRequest.FromTimer(alarms[1]).ToResult(AlarmClosedKind.Snooze));
        Assert.True(scheduler.TryExtend(item.Id, extra));
        Assert.Equal(AlarmService.SnoozeDuration + extra, item.CountdownDuration);
        scheduler.Restart(item.Id);
        Assert.Equal(original, item.CountdownDuration);
    }

    [Fact]
    public async Task RestartAfterSnoozeUsesTheOriginalDurationAfterReload()
    {
        var original = new TimerInstance
        {
            Name = "Focus", Duration = TimeSpan.FromMinutes(35), State = TimerState.Finished
        };
        var snooze = AlarmService.SnoozeDuration;
        using (var seed = new TimerStorage(this._directory))
        {
            seed.SaveTimers([original]);
        }

        DateTimeOffset? snoozeEnds;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            scheduler.Initialize();
            var focus = Assert.Single(scheduler.GetTimersSnapshot());
            var request = AlarmRequest.FromTimer(focus);
            var snoozeAction = request.ToResult(AlarmClosedKind.Snooze);
            scheduler.HandleAlarmClosed(snoozeAction);

            Assert.Same(focus, Assert.Single(scheduler.GetTimersSnapshot()));
            Assert.Equal(TimerState.Running, focus.State);
            Assert.Equal(original.Duration, focus.Duration);
            Assert.Equal(snooze, focus.CountdownDuration);
            snoozeEnds = focus.ExpectedEnd;
            Assert.Equal(focus.StartedAt + snooze, snoozeEnds);

            scheduler.HandleAlarmClosed(snoozeAction);
            Assert.Equal(snoozeEnds, focus.ExpectedEnd);
        }

        using var reopened = new TimerStorage(this._directory);
        var delays = new ControlledDelay();
        using var restartedScheduler = new TimerScheduler(reopened, delays.WaitAsync);
        var finished
            = new TaskCompletionSource<TimerFinishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        restartedScheduler.TimerFinished += (_, args) => finished.TrySetResult(args);
        restartedScheduler.Initialize();

        var restored = Assert.Single(restartedScheduler.GetTimersSnapshot());
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(snooze, restored.CountdownDuration);
        Assert.Equal(snoozeEnds, restored.ExpectedEnd);
        var snoozeWait = await delays.NextAsync();
        restored.StartedAt -= snooze + TimeSpan.FromSeconds(1);
        snoozeWait.Completion.TrySetResult();

        var snoozeFinished = await finished.Task.WaitAsync(TestTimeout);
        Assert.Same(restored, snoozeFinished.Timer);
        Assert.Equal(TimerState.Finished, restored.State);
        var snoozeRequest = AlarmRequest.FromTimer(snoozeFinished);
        Assert.Equal(original.Duration, snoozeRequest.OriginalDuration);
        Assert.StartsWith(DurationParser.FormatCompact(snooze), snoozeRequest.Message);

        restartedScheduler.HandleAlarmClosed(snoozeRequest.ToResult(AlarmClosedKind.Restart));

        Assert.Same(restored, Assert.Single(restartedScheduler.GetTimersSnapshot()));
        Assert.Equal(TimerState.Running, restored.State);
        Assert.Equal(original.Duration, restored.CountdownDuration);
        Assert.Equal(restored.StartedAt + original.Duration, restored.ExpectedEnd);
        Assert.Null(restored.SnoozeDuration);
    }

    [Theory]
    [InlineData(AlarmClosedKind.Snooze, 5)]
    [InlineData(AlarmClosedKind.Restart, 4)]
    public void AlarmActionKeepsADeletedTimersNameAndDuration(AlarmClosedKind action, int countdownMinutes)
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var originalDuration = TimeSpan.FromMinutes(4);
        var original = scheduler.StartTimer(originalDuration, "Tea");
        var request = AlarmRequest.FromTimer(original);
        scheduler.RemoveTimer(original.Id);

        scheduler.HandleAlarmClosed(request.ToResult(action));
        scheduler.HandleAlarmClosed(new AlarmResult(Guid.Empty, TimeSpan.Zero, AlarmClosedKind.Snooze));

        var timer = Assert.Single(scheduler.GetTimersSnapshot());
        Assert.Equal("Tea", timer.Name);
        Assert.NotEqual(original.Id, timer.Id);
        Assert.Equal(originalDuration, timer.Duration);
        Assert.Equal(TimeSpan.FromMinutes(countdownMinutes), timer.CountdownDuration);
    }

    [Fact]
    public async Task TimerSnapshotRemainsAvailableDuringAPauseNotification()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(5));
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseNotification = new ManualResetEventSlim();
        timer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TimerInstance.State))
            {
                notified.TrySetResult();
                releaseNotification.Wait(TestTimeout * 2);
            }
        };

        var pause = Task.Run(() => scheduler.Pause(timer.Id));
        try
        {
            await notified.Task.WaitAsync(TestTimeout);
            var timers = await Task.Run(scheduler.GetTimersSnapshot).WaitAsync(TestTimeout);
            Assert.Equal(TimerState.Paused, Assert.Single(timers).State);
        }
        finally
        {
            releaseNotification.Set();
            await pause.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public void CountdownRefreshFollowsRunningTimersAcrossRestorePauseAndRemoval()
    {
        var clock = new ManualRefreshTimeProvider();
        Guid firstId;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage, Task.Delay, clock))
        {
            scheduler.Initialize();
            Assert.False(clock.Fire());
            firstId = scheduler.StartTimer(TimeSpan.FromHours(1)).Id;
            var second = scheduler.StartTimer(TimeSpan.FromHours(2));
            scheduler.Pause(second.Id);
            Assert.True(clock.Fire());
            Assert.Equal(TimeSpan.FromSeconds(1), clock.DueTime);
        }

        clock = new ManualRefreshTimeProvider();
        using var restoredStorage = new TimerStorage(this._directory);
        using var restored = new TimerScheduler(restoredStorage, Task.Delay, clock);
        restored.Initialize();
        Assert.True(clock.Fire());
        restored.Pause(firstId);
        Assert.False(clock.Fire());
        restored.Resume(firstId);
        Assert.True(clock.Fire());
        restored.RemoveTimer(firstId);
        Assert.False(clock.Fire());
    }

    [Fact]
    public async Task ShuttingDownDuringATimerTickStillFlushesPendingTimerState()
    {
        var clock = new ManualRefreshTimeProvider();
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage, Task.Delay, clock))
        {
            var timer = scheduler.StartTimer(TimeSpan.FromMinutes(5));
            var observingTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseObserver = new ManualResetEventSlim();
            timer.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TimerInstance.Remaining) && observingTick.TrySetResult())
                {
                    releaseObserver.Wait(TestTimeout * 2);
                }
            };

            var tick = Task.Run(clock.Fire);
            try
            {
                await observingTick.Task.WaitAsync(TestTimeout);
                scheduler.Pause(timer.Id);
                await Task.Run(() =>
                {
                    scheduler.Dispose();
                    storage.Dispose();
                }).WaitAsync(TestTimeout);
            }
            finally
            {
                releaseObserver.Set();
                Assert.True(await tick.WaitAsync(TestTimeout));
                Assert.True(clock.Disposed);
            }
        }

        using var reopened = new TimerStorage(this._directory);
        Assert.Equal(TimerState.Paused, Assert.Single(reopened.LoadTimers()).State);
    }

    [Fact]
    public async Task ConcurrentDeleteCannotBeOverwrittenByAnEarlierStateSnapshot()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromHours(1), "Delete during save");
        var timerStorage = (JsonStorageService<TimerInstance>)typeof(TimerStorage)
            .GetField("_timersStorage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(storage)!;
        var persistGate = (Lock)typeof(JsonStorageService<TimerInstance>)
            .GetField("_persistGate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(timerStorage)!;
        var holderReady = new ManualResetEventSlim();
        var releaseHolder = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using (persistGate.EnterScope())
            {
                holderReady.Set();
                releaseHolder.Wait(TestTimeout);
            }
        });

        holder.Start();
        Assert.True(holderReady.Wait(TestTimeout));
        var pause = Task.Run(() => scheduler.Pause(timer.Id));
        Assert.True(SpinWait.SpinUntil(() => timer.State == TimerState.Paused, TestTimeout));
        var delete = Task.Run(() => scheduler.RemoveTimer(timer.Id));

        releaseHolder.Set();
        Assert.True(holder.Join(TestTimeout));
        await Task.WhenAll(pause, delete).WaitAsync(TestTimeout);
        await ((Task)typeof(JsonStorageService<TimerInstance>)
            .GetField("_persistTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(timerStorage)!).WaitAsync(TestTimeout);

        Assert.Empty(storage.LoadTimers());
    }

    [Fact]
    public async Task ResumingBeforeWritesDrainPreservesPausedBackup()
    {
        TimerInstance timer;
        DateTimeOffset? pausedAt;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            scheduler.Initialize();
            var timerStorage = (JsonStorageService<TimerInstance>)typeof(TimerStorage)
                .GetField("_timersStorage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(storage)!;
            var writeMutex = (SemaphoreSlim)typeof(JsonStorageService<TimerInstance>)
                .GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(timerStorage)!;

            Assert.True(await writeMutex.WaitAsync(TestTimeout));
            try
            {
                timer = scheduler.StartTimer(TimeSpan.FromMinutes(30), "Resume before save");
                scheduler.Pause(timer.Id);
                pausedAt = timer.PausedAt;
                scheduler.Resume(timer.Id);
            }
            finally
            {
                writeMutex.Release();
            }
        }

        using var restored = new TimerStorage(this._directory);
        var saved = Assert.Single(restored.LoadTimers());
        Assert.Equal(timer.Id, saved.Id);
        Assert.Equal(TimerState.Running, saved.State);
        Assert.Null(saved.PausedAt);

        File.Delete(Path.Combine(this._directory, "timers.json"));
        var backup = Assert.Single(restored.LoadTimers());
        Assert.Equal(timer.Id, backup.Id);
        Assert.Equal(TimerState.Paused, backup.State);
        Assert.Equal(pausedAt, backup.PausedAt);
        Assert.Equal(timer.ElapsedBeforePause, backup.ElapsedBeforePause);
    }

    [Fact]
    public void SavingAnOpenTemplateAfterDeletionReportsTheConflict()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var template = new TimerDefinition("Tea", TimeSpan.FromMinutes(5), true);
        scheduler.AddTemplate(template);
        var page = new EditTemplatePage(template, scheduler);
        var form = Assert.IsType<EditTimerForm>(Assert.Single(page.GetContent()));
        scheduler.RemoveTemplate(template.Id);

        var result = form.SubmitForm("""{"name":"Green tea","duration":"3m"}""");

        var toast = Assert.IsType<ToastArgs>(result.Args);
        Assert.Equal(Strings.Toast_TemplateDeleted, toast.Message);
        Assert.Equal(CommandResultKind.GoBack, Assert.IsType<CommandResult>(toast.Result).Kind);
        Assert.Empty(scheduler.GetTemplatesSnapshot());
    }

    [Fact]
    public void AThrowingSubscriberDoesNotInterruptTimerTemplateOrRemovalNotifications()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        scheduler.Initialize();
        EventHandler disconnected = (_, _) => throw new InvalidOperationException("Observer disconnected.");
        scheduler.TimersChanged += disconnected;
        scheduler.TemplatesChanged += disconnected;
        scheduler.TimersRemoved += disconnected;
        List<string> received = [];
        scheduler.TimersChanged += (_, _) => received.Add("timers");
        scheduler.TemplatesChanged += (_, _) => received.Add("templates");
        scheduler.TimersRemoved += (_, _) => received.Add("removed");

        var timer = scheduler.StartTimer(new TimerDefinition("Tea", TimeSpan.FromMinutes(4)), true);
        Assert.True(scheduler.TryExtend(timer.Id, TimeSpan.FromMinutes(1)));
        scheduler.RemoveTimer(timer.Id);

        Assert.Equal(["timers", "templates", "timers", "timers", "removed"], received);
    }

    [Fact]
    public void UpdatingATemplateKeepsItsPositionAndPreviousVersion()
    {
        var first = new TimerDefinition("First", TimeSpan.FromMinutes(4), true);
        var second = new TimerDefinition("Second", TimeSpan.FromMinutes(5), true);
        var updated = first with { Name = "Updated", Duration = TimeSpan.FromMinutes(8) };
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            scheduler.AddTemplate(first);
            scheduler.AddTemplate(second);
            var changes = 0;
            scheduler.TemplatesChanged += (_, _) => changes++;

            Assert.True(scheduler.TryUpdateTemplate(updated));

            Assert.Equal(1, changes);
        }

        using var restored = new TimerStorage(this._directory);
        Assert.Equal(new[] { updated, second }, restored.LoadTemplates());

        File.Delete(Path.Combine(this._directory, "templates.json"));
        Assert.Equal(new[] { first, second }, restored.LoadTemplates());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongTimerFinishesAfterBoundedWaits(bool restoreFromStorage)
    {
        if (restoreFromStorage)
        {
            using var originalStorage = new TimerStorage(this._directory);
            originalStorage.SaveTimers([
                new TimerInstance
                {
                    Name = "Long timer",
                    Duration = TimeSpan.FromDays(50),
                    StartedAt = DateTimeOffset.UtcNow,
                    State = TimerState.Running
                }
            ]);
        }

        using var storage = new TimerStorage(this._directory);
        var delays = new ControlledDelay();
        using var scheduler = new TimerScheduler(storage, delays.WaitAsync);
        var finished = new TaskCompletionSource<TimerInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.TimerFinished += (_, args) => finished.TrySetResult(args.Timer);
        scheduler.Initialize();
        var timer = restoreFromStorage
            ? Assert.Single(scheduler.GetTimersSnapshot())
            : scheduler.StartTimer(TimeSpan.FromDays(50), "Long timer");

        var firstWait = await delays.NextAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(firstWait.Duration, new CancellationToken(true)));
        Assert.True(firstWait.Duration < timer.Remaining);
        Assert.False(finished.Task.IsCompleted);

        timer.StartedAt -= TimeSpan.FromDays(49);
        firstWait.Completion.SetResult();

        var finalWait = await delays.NextAsync();
        Assert.InRange(finalWait.Duration, TimeSpan.FromTicks(1), TimeSpan.FromDays(1));
        Assert.Equal(TimerState.Running, timer.State);
        Assert.False(finished.Task.IsCompleted);

        timer.StartedAt -= TimeSpan.FromDays(2);
        finalWait.Completion.TrySetResult();

        Assert.Same(timer, await finished.Task.WaitAsync(TestTimeout));
        Assert.Equal(TimerState.Finished, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(4320, true, false)]
    [InlineData(4320, true, true)]
    public async Task OverdueRunningTimerUsesItsDeadline(int minutesOverdue, bool isMissed, bool delayElapsed)
    {
        var delays = new ControlledDelay();
        var finished
            = new TaskCompletionSource<TimerFinishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedEnd = DateTimeOffset.UtcNow.AddMinutes(-minutesOverdue);
        Guid timerId;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage, delays.WaitAsync))
        {
            scheduler.TimerFinished += (_, args) => finished.TrySetResult(args);
            var timer = scheduler.StartTimer(TimeSpan.FromHours(1));
            timerId = timer.Id;
            var wait = await delays.NextAsync();
            timer.StartedAt = expectedEnd - timer.Duration;
            if (delayElapsed)
            {
                wait.Completion.TrySetResult();
            }

            var completion = await finished.Task.WaitAsync(TestTimeout);
            Assert.Same(timer, completion.Timer);
            Assert.Equal(TimerState.Finished, timer.State);
            Assert.Equal(expectedEnd, timer.PausedAt);
            Assert.Equal(expectedEnd, completion.FinishedAt);
            var request = AlarmRequest.FromTimer(completion);
            Assert.Equal(isMissed, request.IsMissed);
            Assert.Equal(isMissed, request.IsSilent);
            if (!delayElapsed)
            {
                await AssertCanceledAsync(wait);
            }
        }

        using var reopened = new TimerStorage(this._directory);
        var saved = Assert.Single(reopened.LoadTimers());
        Assert.Equal(timerId, saved.Id);
        Assert.Equal(TimerState.Finished, saved.State);
        Assert.Equal(expectedEnd, saved.PausedAt);
    }

    [Fact]
    public async Task LongTimerCancelsOldWaitsWhenPausedStoppedOrRestarted()
    {
        using var storage = new TimerStorage(this._directory);
        var delays = new ControlledDelay();
        using var scheduler = new TimerScheduler(storage, delays.WaitAsync);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCount = 0;
        scheduler.TimerFinished += (_, _) =>
        {
            Interlocked.Increment(ref finishCount);
            finished.TrySetResult();
        };
        var timer = scheduler.StartTimer(TimeSpan.FromDays(50), "Long timer");
        var initialWait = await delays.NextAsync();

        scheduler.Pause(timer.Id);
        await AssertCanceledAsync(initialWait);
        Assert.Equal(TimerState.Paused, timer.State);

        scheduler.Resume(timer.Id);
        var resumedWait = await delays.NextAsync();
        scheduler.Restart(timer.Id);
        await AssertCanceledAsync(resumedWait);
        var restartedWait = await delays.NextAsync();

        scheduler.Stop(timer.Id);
        await AssertCanceledAsync(restartedWait);
        Assert.Equal(TimerState.Cancelled, timer.State);
        Assert.False(finished.Task.IsCompleted);

        scheduler.Restart(timer.Id);
        var finalWait = await delays.NextAsync();
        timer.StartedAt -= TimeSpan.FromDays(51);
        finalWait.Completion.TrySetResult();

        await finished.Task.WaitAsync(TestTimeout);
        Assert.Equal(TimerState.Finished, timer.State);
        Assert.Equal(1, finishCount);
    }

    private static async Task AssertCanceledAsync(DelayRequest request)
    {
        Assert.True(request.Token.IsCancellationRequested);
        await request.Returned.Task.WaitAsync(TestTimeout);
        Assert.True(request.Completion.Task.IsCanceled);
    }

    public void Dispose()
    {
        Directory.Delete(this._directory, true);
    }

    private sealed class ControlledDelay
    {
        private readonly Channel<DelayRequest> _requests = Channel.CreateUnbounded<DelayRequest>();

        public async Task WaitAsync(TimeSpan duration, CancellationToken token)
        {
            var request = new DelayRequest(duration, token);
            using var registration = token.Register(() => request.Completion.TrySetCanceled(token));
            this._requests.Writer.TryWrite(request);
            try
            {
                await request.Completion.Task;
            }
            finally
            {
                request.Returned.TrySetResult();
            }
        }

        public Task<DelayRequest> NextAsync() => this._requests.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
    }

    private sealed record DelayRequest(TimeSpan Duration, CancellationToken Token)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}