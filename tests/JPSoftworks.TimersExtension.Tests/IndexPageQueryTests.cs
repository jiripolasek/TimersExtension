// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

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

public sealed class IndexPageQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimerIndexFiltersItemsAndOffersNaturalCreation(bool searchChangedOnStart)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            using var alarmService = new AlarmService();
            scheduler.StartTimer(TimeSpan.FromMinutes(10), "Focus");
            scheduler.StartTimer(TimeSpan.FromMinutes(5), "Tea");
            using var page = new TimersIndexPage(
                scheduler,
                alarmService,
                new RepeatLastTimerCommand(scheduler));

            Assert.Equal("Try tea 4m or search timers", page.PlaceholderText);

            page.SearchText = "no matching timer xyz";
            Assert.Empty(page.GetItems());
            Assert.Equal(Strings.Page_Timers_NoMatches, page.EmptyContent?.Title);

            page.SearchText = "tea";

            var filteredItems = page.GetItems();
            Assert.Contains(filteredItems, item => item.Title == "Tea");
            Assert.DoesNotContain(filteredItems, item => item.Title == "Focus");

            page.SearchText = "focsu";
            Assert.Contains(page.GetItems(), item => item.Title == "Focus");

            page.SearchText = "tea 4m";

            var creationItem = Assert.IsType<ListItem>(page.GetItems()[0]);
            Assert.Equal("tea 4m", creationItem.Title);
            Assert.StartsWith("Start timer · Ends at ", creationItem.Subtitle);
            var command = Assert.IsType<TimerFallbackCommand>(creationItem.Command);
            var searchResetNotified = false;
            page.PropChanged += (_, args) =>
                searchResetNotified |= args.PropertyName == nameof(page.SearchText) && page.SearchText.Length == 0;
            if (searchChangedOnStart)
            {
                scheduler.TimersChanged += (_, _) => page.SearchText = "Focus";
            }

            var result = command.Invoke();
            var timer = scheduler.GetTimersSnapshot()[0];

            Assert.Equal("tea", timer.Name);
            Assert.Equal(TimeSpan.FromMinutes(4), timer.Duration);
            Assert.Equal(searchChangedOnStart ? "Focus" : string.Empty, page.SearchText);
            Assert.Equal(!searchChangedOnStart, searchResetNotified);
            Assert.DoesNotContain(page.GetItems(), item => item.Command is TimerFallbackCommand);
            var toast = Assert.IsType<ToastArgs>(result.Args);
            Assert.Equal(CommandResultKind.Dismiss, Assert.IsType<CommandResult>(toast.Result).Kind);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StopwatchIndexFiltersItemsAndOffersNamedCreation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            scheduler.CreateAndStart("Focus");
            scheduler.CreateAndStart("Tea");
            using var page = new StopwatchIndexPage(scheduler);

            Assert.Equal("Search stopwatches or type a name to start one", page.PlaceholderText);

            page.SearchText = "focsu";

            var filteredItems = page.GetItems();
            Assert.DoesNotContain(filteredItems, item => item.Title == "Tea");
            Assert.IsType<StopwatchFallbackCommand>(filteredItems[0].Command);
            Assert.Equal("Focus", Assert.IsType<StopwatchInstanceListItem>(filteredItems[^1]).Title);

            page.SearchText = "rehearsal drill";

            var creationItem = Assert.IsType<ListItem>(page.GetItems()[0]);
            var command = Assert.IsType<StopwatchFallbackCommand>(creationItem.Command);
            command.Invoke();
            var stopwatch = scheduler.GetStopwatchesSnapshot()[0];

            Assert.Equal("rehearsal drill", stopwatch.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TimerPinUpdatesDedicatedDockItemAndDisappearsWithDeletedTimer()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            using var alarmService = new AlarmService();
            var timer = scheduler.StartTimer(TimeSpan.FromMinutes(10), "Focus");
            using var page = new TimersIndexPage(
                scheduler,
                alarmService,
                new RepeatLastTimerCommand(scheduler));

            var indexItem = Assert.IsType<TimerInstanceListItem>(
                page.GetItems().Single(item => item.Command?.Id == CommandIds.TimerDetail(timer.Id)));
            var pinnedItem = Assert.IsType<CommandItem>(page.GetTimerDockItem(timer.Id));
            using var dock = Assert.IsType<TimerDockPage>(pinnedItem.Command);
            TypedEventHandler<object, IItemsChangedEventArgs> handler = (_, _) => { };
            var notifier = (INotifyItemsChanged)dock;
            notifier.ItemsChanged += handler;
            var wrappedItem = Assert.IsType<ListItem>(Assert.Single(dock.GetItems()));

            Assert.NotSame(indexItem, wrappedItem);
            Assert.StartsWith(indexItem.Title + " · ", wrappedItem.Title);
            Assert.NotNull(wrappedItem.Command);
            Assert.Equal(CommandIds.TimerDetail(timer.Id), wrappedItem.Command.Id);
            Assert.Equal(CommandIds.TimerDetail(timer.Id), dock.Id);

            await AssertTimeUpdatesAsync(wrappedItem,
                () => timer.StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2));

            scheduler.Pause(timer.Id);
            Assert.Equal(Strings.Action_Resume, Assert.IsType<CommandContextItem>(wrappedItem.MoreCommands[0]).Title);

            var refreshedPin = Assert.IsType<CommandItem>(page.GetTimerDockItem(timer.Id));
            using var refreshedDock = Assert.IsType<TimerDockPage>(refreshedPin.Command);
            var refreshedItem = Assert.IsType<ListItem>(Assert.Single(refreshedDock.GetItems()));
            Assert.NotSame(pinnedItem, refreshedPin);
            Assert.Equal(
                Strings.Action_Resume,
                Assert.IsType<CommandContextItem>(refreshedItem.MoreCommands[0]).Title);

            scheduler.RemoveTimer(timer.Id);

            Assert.Null(page.GetTimerDockItem(timer.Id));
            Assert.Empty(dock.GetItems());
            notifier.ItemsChanged -= handler;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("pauesd")]
    public void StopwatchSearchRefreshesWhenStateChanges(string query)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var stopwatch = scheduler.CreateAndStart("Focus");
            using var page = new StopwatchIndexPage(scheduler);
            var changeCount = 0;
            TypedEventHandler<object, IItemsChangedEventArgs> handler = (_, _) => changeCount++;
            var notifier = (INotifyItemsChanged)page;
            notifier.ItemsChanged += handler;
            try
            {
                page.SearchText = query;
                Assert.DoesNotContain(page.GetItems(), item => item.Title == "Focus");
                changeCount = 0;

                scheduler.Pause(stopwatch.Id);

                Assert.True(changeCount > 0);
                Assert.Contains(page.GetItems(), item => item.Title == "Focus");
                changeCount = 0;

                scheduler.Resume(stopwatch.Id);

                Assert.True(changeCount > 0);
                Assert.DoesNotContain(page.GetItems(), item => item.Title == "Focus");
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
    public async Task StopwatchPinUpdatesDedicatedDockItemAndDisappearsWithDeletedStopwatch()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var stopwatch = scheduler.CreateAndStart("Focus");
            using var page = new StopwatchIndexPage(scheduler);

            var indexItem = Assert.IsType<StopwatchInstanceListItem>(
                page.GetItems().Single(item => item.Command?.Id == CommandIds.StopwatchDetail(stopwatch.Id)));
            var pinnedItem = Assert.IsType<CommandItem>(page.GetStopwatchDockItem(stopwatch.Id));
            using var dock = Assert.IsType<StopwatchDockPage>(pinnedItem.Command);
            TypedEventHandler<object, IItemsChangedEventArgs> handler = (_, _) => { };
            var notifier = (INotifyItemsChanged)dock;
            notifier.ItemsChanged += handler;
            var wrappedItem = Assert.IsType<ListItem>(Assert.Single(dock.GetItems()));

            Assert.NotSame(indexItem, wrappedItem);
            Assert.StartsWith(indexItem.Title + " · ", wrappedItem.Title);
            Assert.NotNull(wrappedItem.Command);
            Assert.Equal(CommandIds.StopwatchDetail(stopwatch.Id), wrappedItem.Command.Id);
            Assert.Equal(CommandIds.StopwatchDetail(stopwatch.Id), dock.Id);

            await AssertTimeUpdatesAsync(wrappedItem,
                () => stopwatch.StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2));

            scheduler.Pause(stopwatch.Id);
            Assert.Equal(Strings.Action_Resume, Assert.IsType<CommandContextItem>(wrappedItem.MoreCommands[0]).Title);

            var refreshedPin = Assert.IsType<CommandItem>(page.GetStopwatchDockItem(stopwatch.Id));
            using var refreshedDock = Assert.IsType<StopwatchDockPage>(refreshedPin.Command);
            var refreshedItem = Assert.IsType<ListItem>(Assert.Single(refreshedDock.GetItems()));
            Assert.NotSame(pinnedItem, refreshedPin);
            Assert.Equal(
                Strings.Action_Resume,
                Assert.IsType<CommandContextItem>(refreshedItem.MoreCommands[0]).Title);

            scheduler.Remove(stopwatch.Id);

            Assert.Null(page.GetStopwatchDockItem(stopwatch.Id));
            Assert.Empty(dock.GetItems());
            notifier.ItemsChanged -= handler;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EditingCachedTemplateRefreshesItsNameAndDuration()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            using var alarmService = new AlarmService();
            using var page = new TimersIndexPage(scheduler, alarmService, new RepeatLastTimerCommand(scheduler));
            var template = new TimerDefinition("Tea", TimeSpan.FromMinutes(4), true);
            scheduler.AddTemplate(template);
            var templateItem
                = page.GetItems().Single(item => item.Command?.Id == CommandIds.TimerTemplate(template.Id));
            var editAction = Assert.IsType<CommandContextItem>(templateItem.MoreCommands[0]);
            var edit = Assert.IsType<EditTemplatePage>(editAction.Command);
            Assert.Equal(Strings.Action_Edit, edit.Name);
            Assert.IsType<EditTimerForm>(edit.GetContent()[0])
                .SubmitForm("{\"name\":\"Tea updated\",\"duration\":\"8m\"}");

            var item = page.GetItems().Single(item => item.Command?.Id == CommandIds.TimerTemplate(template.Id));
            Assert.Equal("Tea updated", item.Title);
            Assert.IsType<StartTimerCommand>(item.Command).Invoke();
            Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(scheduler.GetTimersSnapshot()).Duration);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TimerOverviewKeepsTheLatestResultAndClearsOnlyConfirmedHistory()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduled = new SemaphoreSlim(0);
            using var scheduler = new TimerScheduler(storage, (_, token) =>
            {
                scheduled.Release();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, new ManualRefreshTimeProvider());
            using var alarmService = new AlarmService();
            var repeat = new RepeatLastTimerCommand(scheduler);
            using var page = new TimersIndexPage(scheduler, alarmService, repeat);
            ((INotifyItemsChanged)page).ItemsChanged += (_, _) => { };
            var fiveMinutes = Assert.IsType<StartTimerCommand>(page.GetItems()[0].Command);
            fiveMinutes.Invoke();
            var active = Assert.Single(scheduler.GetTimersSnapshot());
            Assert.Equal(TimeSpan.FromMinutes(5), active.Duration);

            var duration = TimeSpan.FromMinutes(10);
            var template = new TimerDefinition("Reusable timer", duration, true);
            scheduler.AddTemplate(template);
            var latest = scheduler.StartTimer(duration, "Focus");
            var oldest = scheduler.StartTimer(duration, "Tea");
            var restarted = scheduler.StartTimer(duration, "Break");
            for (var i = 0; i < 4; i++)
            {
                Assert.True(scheduled.Wait(TimeSpan.FromSeconds(5)));
            }

            var now = DateTimeOffset.UtcNow;
            latest.TransitionToFinished(now, duration);
            oldest.TransitionToCancelled(now.AddHours(-2), duration);
            restarted.TransitionToFinished(now.AddHours(-1), duration);

            var overview = page.GetItems().OfType<TimerInstanceListItem>().ToList();
            Assert.Equal(["Timer 5m", "Focus"], overview.Select(item => item.Title));
            Assert.Equal(CommandIds.TimerTemplate(template.Id), page.GetItems()[^1].Command?.Id);
            Assert.Equal(Strings.Action_NewTimer, page.GetItems()[0].Title);
            Assert.StartsWith("Finished at ", overview[1].Subtitle);
            page.SearchText = "Tea";
            Assert.Contains(page.GetItems(), item => item.Title == "Tea");
            page.SearchText = string.Empty;

            var showOlder = Assert.Single(page.GetItems(), item => item.Title == "Show older results (2)");
            Assert.Same(Icons.ChevronDown, showOlder.Icon);
            Assert.IsAssignableFrom<InvokableCommand>(showOlder.Command).Invoke();
            Assert.Equal(TimerIndexFilters.All, Assert.IsType<TimerIndexFilters>(page.Filters).CurrentFilterId);
            Assert.Equal(["Timer 5m", "Focus", "Break", "Tea"],
                page.GetItems().OfType<TimerInstanceListItem>().Select(item => item.Title));
            var expanded = page.GetItems();
            Assert.Equal(
                [
                    "Tea", Strings.Action_HideOlderResults, Strings.Action_ClearFinishedTimers,
                    Strings.Section_SavedTemplates, template.Name
                ],
                expanded[^5..].Select(item => item.Title));
            var hideOlder = expanded[^4];
            Assert.Same(Icons.ChevronUp, hideOlder.Icon);
            Assert.IsAssignableFrom<InvokableCommand>(hideOlder.Command).Invoke();
            Assert.Equal(["Timer 5m", "Focus"],
                page.GetItems().OfType<TimerInstanceListItem>().Select(item => item.Title));
            Assert.IsAssignableFrom<InvokableCommand>(showOlder.Command).Invoke();
            var clear = Assert.Single(page.GetItems(), item => item.Title == Strings.Action_ClearFinishedTimers);
            var confirm = Assert.IsType<ConfirmationArgs>(Assert.IsType<DeletionConfirmationCommand>(clear.Command)
                .Invoke().Args);
            Assert.StartsWith("Remove 3 from timer history?", confirm.Description);

            scheduler.Restart(restarted.Id);
            var newCompletion = scheduler.StartTimer(duration, "New result");
            Assert.True(scheduled.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(scheduled.Wait(TimeSpan.FromSeconds(5)));
            newCompletion.TransitionToFinished(now.AddMinutes(1), duration);
            Assert.IsAssignableFrom<InvokableCommand>(confirm.PrimaryCommand).Invoke();

            Assert.Null(scheduler.TryGetTimer(latest.Id));
            Assert.Null(page.GetTimerDockItem(oldest.Id));
            Assert.Equal(TimerState.Running, restarted.State);
            Assert.Equal(TimerState.Running, active.State);
            Assert.Same(newCompletion, scheduler.TryGetTimer(newCompletion.Id));

            Assert.IsType<TimerIndexFilters>(page.Filters).CurrentFilterId = TimerIndexFilters.Finished;
            clear = Assert.Single(page.GetItems(), item => item.Title == Strings.Action_ClearFinishedTimers);
            confirm = Assert.IsType<ConfirmationArgs>(Assert.IsType<DeletionConfirmationCommand>(clear.Command).Invoke()
                .Args);
            Assert.IsAssignableFrom<InvokableCommand>(confirm.PrimaryCommand).Invoke();
            scheduler.Pause(restarted.Id);
            repeat.Invoke();
            Assert.Equal(TimerState.Running, restarted.State);
            Assert.True(scheduled.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StopwatchOverviewUpdatesOnFinishAndKeepsOlderResultsSearchable()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var latest = scheduler.CreateAndStart("Latest");
            var first = scheduler.CreateAndStart("First");
            scheduler.CreateAndStart("Active test");
            scheduler.Stop(first.Id);
            using var page = new StopwatchIndexPage(scheduler);
            var refreshes = 0;
            ((INotifyItemsChanged)page).ItemsChanged += (_, _) => refreshes++;
            Assert.Contains(page.GetItems(), item => item.Title == "First");
            refreshes = 0;

            scheduler.Stop(latest.Id);

            Assert.True(refreshes > 0);
            Assert.DoesNotContain(page.GetItems(), item => item.Title == "First");
            Assert.Equal(Strings.Action_StartStopwatch, page.GetItems()[0].Title);
            var result = Assert.Single(page.GetItems(), item => item.Title == "Latest");
            var copy = Assert.IsType<CopyTextCommand>(Assert
                .IsAssignableFrom<ICommandContextItem>(result.MoreCommands[0]).Command);
            Assert.Equal(LabelFormatter.FormatStopwatchTime(latest.Elapsed), copy.Text);

            page.SearchText = "First";
            var searchItems = page.GetItems();
            Assert.IsType<StopwatchFallbackCommand>(searchItems[0].Command);
            Assert.Equal("First", Assert.Single(searchItems.OfType<StopwatchInstanceListItem>()).Title);
            page.SearchText = string.Empty;
            var showOlder = Assert.Single(page.GetItems(), item => item.Title == "Show older results (1)");
            Assert.Same(Icons.ChevronDown, showOlder.Icon);
            Assert.IsAssignableFrom<InvokableCommand>(showOlder.Command).Invoke();
            Assert.Equal(StopwatchIndexFilters.All, Assert.IsType<StopwatchIndexFilters>(page.Filters).CurrentFilterId);
            Assert.Equal(["Active test", "Latest", "First"],
                page.GetItems().OfType<StopwatchInstanceListItem>().Select(item => item.Title));
            var hideOlder = page.GetItems()[^1];
            Assert.Equal(Strings.Action_HideOlderResults, hideOlder.Title);
            Assert.Same(Icons.ChevronUp, hideOlder.Icon);
            Assert.IsAssignableFrom<InvokableCommand>(hideOlder.Command).Invoke();
            Assert.Equal(["Active test", "Latest"],
                page.GetItems().OfType<StopwatchInstanceListItem>().Select(item => item.Title));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task AssertTimeUpdatesAsync(ListItem item, Action advanceTime)
    {
        var previousTitle = item.Title;
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TypedEventHandler<object, IPropChangedEventArgs> handler = (_, args) =>
        {
            if (args.PropertyName == nameof(ListItem.Title) && item.Title != previousTitle)
            {
                updated.TrySetResult();
            }
        };
        item.PropChanged += handler;
        try
        {
            advanceTime();
            await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            item.PropChanged -= handler;
        }
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