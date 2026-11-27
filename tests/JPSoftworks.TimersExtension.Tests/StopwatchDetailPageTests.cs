// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Pages;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class StopwatchDetailPageTests
{
    [Fact]
    public void ElapsedPrimaryRecordsLapsAndFollowsTheStopwatchState()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var stopwatch = scheduler.Create();
            using var page = new StopwatchDetailPage(stopwatch, scheduler);
            var refreshes = new ConcurrentQueue<int>();
            ((INotifyItemsChanged)page).ItemsChanged += (_, args) => refreshes.Enqueue(args.TotalItems);

            var elapsed = page.GetItems()[0];
            Assert.DoesNotContain(page.GetItems(), item => item.Title == Strings.Label_CurrentLap);
            var primary = Assert.IsAssignableFrom<InvokableCommand>(elapsed.Command);
            Assert.Equal(Strings.Action_Start, primary.Name);
            primary.Invoke();
            Assert.Equal(Strings.Action_Lap, primary.Name);
            var lapShortcut = elapsed.MoreCommands.OfType<ICommandContextItem>()
                .Single(item => item.RequestedShortcut == Chords.Lap);
            Assert.IsType<LapStopwatchCommand>(lapShortcut.Command).Invoke();
            Assert.Contains(page.GetItems(), item => item.Title == Strings.Label_CurrentLap);
            primary.Invoke();
            Assert.Equal(2, stopwatch.Laps.Count);
            Assert.Equal(StopwatchState.Running, stopwatch.State);

            scheduler.Pause(stopwatch.Id);
            Assert.Equal(Strings.Action_Resume, primary.Name);
            var copy = Assert.IsType<CopyTextCommand>(((ICommandContextItem)elapsed.MoreCommands[0]).Command);
            Assert.Equal(elapsed.Subtitle, copy.Text);
            primary.Invoke();
            Assert.Equal(StopwatchState.Running, stopwatch.State);
            Assert.Equal(Strings.Action_Lap, primary.Name);

            scheduler.Stop(stopwatch.Id);
            Assert.Equal(Strings.Action_CopyElapsedTime, primary.Name);
            Assert.Equal(2, stopwatch.Laps.Count);
            Assert.DoesNotContain(page.GetItems(), item => item.Title == Strings.Label_CurrentLap);
            scheduler.Reset(stopwatch.Id);
            Assert.Equal(Strings.Action_Start, primary.Name);
            Assert.Same(primary, elapsed.Command);
            Assert.NotEmpty(refreshes);
            Assert.All(refreshes, total => Assert.Equal(-2, total));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PauseAndResumeRemainInTheSameActionPosition()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var stopwatch = scheduler.CreateAndStart();

            using var page = new StopwatchDetailPage(stopwatch, scheduler);
            Assert.Equal(
                [
                    Strings.Action_Pause,
                    Strings.Action_Lap,
                    Strings.Action_FinishStopwatch,
                    Strings.Action_Delete
                ],
                GetActionTitles(page));

            var pauseResumeItem = page.GetItems()[2];
            var pauseResumeShortcutItem = page.GetItems()[0].MoreCommands[0];
            var notifications = new ConcurrentQueue<int>();
            TypedEventHandler<object, IItemsChangedEventArgs> handler =
                (_, args) => notifications.Enqueue(args.TotalItems);
            var notifier = (INotifyItemsChanged)page;
            notifier.ItemsChanged += handler;
            try
            {
                scheduler.Pause(stopwatch.Id);

                Assert.Same(pauseResumeItem, page.GetItems()[2]);
                Assert.Same(pauseResumeShortcutItem, page.GetItems()[0].MoreCommands.OfType<ICommandContextItem>()
                    .Single(item => item.RequestedShortcut == Chords.Resume));
                Assert.Equal(
                    [
                        Strings.Action_Resume,
                        Strings.Action_Reset,
                        Strings.Action_FinishStopwatch,
                        Strings.Action_Delete
                    ],
                    GetActionTitles(page));
                Assert.Contains(-2, notifications);

                notifications.Clear();
                scheduler.Resume(stopwatch.Id);

                Assert.Same(pauseResumeItem, page.GetItems()[2]);
                Assert.Same(pauseResumeShortcutItem, page.GetItems()[0].MoreCommands[0]);
                Assert.Equal(Strings.Action_Pause, page.GetItems()[2].Title);
                Assert.Contains(-2, notifications);

                notifications.Clear();
                stopwatch.NotifyTimeChanged();
                Assert.Empty(notifications);
            }
            finally
            {
                notifier.ItemsChanged -= handler;
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StoppingReplacesTheRunningActionsInTheNextItemSnapshot()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var stopwatch = scheduler.CreateAndStart();
            using var page = new StopwatchDetailPage(stopwatch, scheduler);
            string[] actions = [];
            ((INotifyItemsChanged)page).ItemsChanged += (_, _) => actions = GetActionTitles(page);

            scheduler.Stop(stopwatch.Id);

            Assert.Equal([Strings.Action_Reset, Strings.Action_Delete], actions);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FinishingDuringPageOpeningLeavesTheFinishedActionsVisible()
    {
        var directory = CreateTemporaryDirectory();
        var timeout = TimeSpan.FromSeconds(5);
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage, new ManualRefreshTimeProvider());
            var stopwatch = scheduler.Create();
            using var page = new StopwatchDetailPage(stopwatch, scheduler);
            var primary = Assert.IsAssignableFrom<InvokableCommand>(page.GetItems()[0].Command);
            var openingRunningRows = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseOpening = new ManualResetEventSlim();
            primary.PropChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(primary.Name) && primary.Name == Strings.Action_Lap)
                {
                    openingRunningRows.TrySetResult();
                    releaseOpening.Wait(timeout * 2);
                }
            };

            scheduler.Start(stopwatch.Id);
            var opening = Task.Run(() => ((INotifyItemsChanged)page).ItemsChanged += (_, _) => { });
            try
            {
                await openingRunningRows.Task.WaitAsync(timeout);
                await Task.Run(() => scheduler.Stop(stopwatch.Id)).WaitAsync(timeout);
            }
            finally
            {
                releaseOpening.Set();
                await opening.WaitAsync(timeout);
            }

            Assert.Equal(StopwatchState.Stopped, stopwatch.State);
            Assert.Equal(Strings.Action_CopyElapsedTime, primary.Name);
            Assert.Equal([Strings.Action_Reset, Strings.Action_Delete], GetActionTitles(page));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StopwatchTicksContinueAfterAnObserverThrows()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var stopwatch = scheduler.CreateAndStart();
            var tickCount = 0;
            var tickedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stopwatch.PropertyChanged += (_, _) => throw new InvalidOperationException("The observer disconnected.");
            stopwatch.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(StopwatchInstance.Elapsed) && Interlocked.Increment(ref tickCount) == 2)
                {
                    tickedTwice.TrySetResult();
                }
            };

            await tickedTwice.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StopwatchRefreshFollowsRunningItemsAcrossRestorePauseAndReset()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var clock = new ManualRefreshTimeProvider();
            Guid id;
            using (var storage = new StopwatchStorage(directory))
            using (var scheduler = new StopwatchScheduler(storage, clock))
            {
                scheduler.Initialize();
                var pending = scheduler.Create();
                Assert.False(clock.Fire());
                scheduler.Start(pending.Id);
                id = scheduler.CreateAndStart().Id;
                scheduler.Remove(pending.Id);
                Assert.True(clock.Fire());
                Assert.Equal(TimeSpan.FromMilliseconds(50), clock.DueTime);
            }

            clock = new ManualRefreshTimeProvider();
            using var restoredStorage = new StopwatchStorage(directory);
            using var restored = new StopwatchScheduler(restoredStorage, clock);
            restored.Initialize();
            Assert.True(clock.Fire());
            restored.Pause(id);
            Assert.False(clock.Fire());
            restored.Resume(id);
            Assert.True(clock.Fire());
            restored.Reset(id);
            Assert.False(clock.Fire());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DisposingDuringAStopwatchTickDoesNotWaitForTheObserver()
    {
        var directory = CreateTemporaryDirectory();
        var timeout = TimeSpan.FromSeconds(5);
        try
        {
            using var storage = new StopwatchStorage(directory);
            var clock = new ManualRefreshTimeProvider();
            using var scheduler = new StopwatchScheduler(storage, clock);
            var stopwatch = scheduler.CreateAndStart();
            var observingTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseObserver = new ManualResetEventSlim();
            stopwatch.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(StopwatchInstance.Elapsed) && observingTick.TrySetResult())
                {
                    releaseObserver.Wait(timeout * 2);
                }
            };

            var tick = Task.Run(clock.Fire);
            try
            {
                await observingTick.Task.WaitAsync(timeout);
                await Task.Run(scheduler.Dispose).WaitAsync(timeout);
                Assert.True(clock.Disposed);
            }
            finally
            {
                releaseObserver.Set();
                Assert.True(await tick.WaitAsync(timeout));
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string[] GetActionTitles(StopwatchDetailPage page)
    {
        return page.GetItems()
            .SkipWhile(item => item is not GroupHeaderListItem { Title: var title } || title != Strings.Page_Actions)
            .Skip(1)
            .TakeWhile(item => item is not GroupHeaderListItem)
            .Select(item => item.Title)
            .ToArray();
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "JPSoftworks.TimersExtension.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}