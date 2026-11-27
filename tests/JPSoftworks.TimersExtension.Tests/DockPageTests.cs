// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Globalization;
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

public sealed class DockPageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "JPSoftworks.TimersExtension.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProviderGroupsDockBandsByTypeAndNamesTheirPurpose()
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var alarms = new AlarmService();
        using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);

        var bands = provider.GetDockBands()!;
        Assert.Equal(
            [
                CommandIds.ActiveTimersDockBand,
                CommandIds.TimerDockBand,
                CommandIds.AllTimersDockBand,
                CommandIds.ActiveStopwatchesDockBand,
                CommandIds.StopwatchDockBand,
                CommandIds.AllStopwatchesDockBand
            ],
            bands.Select(band => band.Command!.Id).ToArray());
        Assert.Equal(
            [
                "Active timers",
                "Latest active timer",
                "Open timer list",
                "Active stopwatches",
                "Latest active stopwatch",
                "Open stopwatch list"
            ],
            bands.Select(band => band.Title).ToArray());
        foreach (var band in bands)
        {
            Assert.Same(band, provider.GetCommandItem(band.Command!.Id));
        }

        const string dockCommandIdProperty = "Microsoft.CommandPalette.DockCommandId";
        var topLevelItems = provider.TopLevelCommands();
        var timersItem = Assert.IsAssignableFrom<IExtendedAttributesProvider>(
            Assert.Single(topLevelItems, item => item.Command?.Id == TimersIndexPage.PageId));
        var stopwatchesItem = Assert.IsAssignableFrom<IExtendedAttributesProvider>(
            Assert.Single(topLevelItems, item => item.Command?.Id == StopwatchIndexPage.PageId));
        Assert.Same(timersItem, provider.GetCommandItem(TimersIndexPage.PageId));
        Assert.Same(stopwatchesItem, provider.GetCommandItem(StopwatchIndexPage.PageId));
        Assert.Equal(CommandIds.AllTimersDockBand, timersItem.GetProperties()[dockCommandIdProperty]);
        Assert.Equal(CommandIds.AllStopwatchesDockBand, stopwatchesItem.GetProperties()[dockCommandIdProperty]);
    }

    [Fact]
    public void TimerDockMenusOfferNewTimerAndRefreshSavedTemplates()
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var alarms = new AlarmService();
        using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
        var focus = timers.StartTimer(TimeSpan.FromMinutes(10), "Focus");
        timers.Pause(focus.Id);
        using var pinnedPage
            = Assert.IsType<TimerDockPage>(provider.GetCommandItem(CommandIds.TimerDetail(focus.Id))?.Command);
        IListPage[] pages =
        [
            Assert.IsAssignableFrom<IListPage>(provider.GetCommandItem(CommandIds.TimerDockBand)?.Command),
            Assert.IsAssignableFrom<IListPage>(provider.GetCommandItem(CommandIds.ActiveTimersDockBand)?.Command),
            pinnedPage,
            Assert.IsAssignableFrom<IListPage>(provider.GetCommandItem(CommandIds.AllTimersDockBand)?.Command)
        ];
        foreach (var page in pages.OfType<TimerDockPage>())
        {
            ((INotifyItemsChanged)page).ItemsChanged += (_, _) => { };
        }

        var buttons = pages.Select(page => Assert.Single(page.GetItems())).ToArray();
        Assert.IsType<TimersIndexPage>(buttons[^1].Command);
        Assert.Equal("Open timer list", buttons[^1].Title);
        Assert.IsType<Separator>(buttons[^1].MoreCommands[0]);
        Assert.Equal(Strings.Action_NewTimer,
            Assert.IsAssignableFrom<ICommandContextItem>(buttons[^1].MoreCommands[1]).Title);
        Assert.All(buttons, button =>
        {
            var newTimer = Assert.Single(button.MoreCommands.OfType<ICommandContextItem>(),
                action => action.Title == Strings.Action_NewTimer);
            Assert.IsType<EditTimerPage>(newTimer.Command);
            Assert.DoesNotContain(button.MoreCommands.OfType<ICommandContextItem>(),
                action => action.Command is StartTimerCommand);
        });

        var tea = new TimerDefinition("Tea", TimeSpan.FromMinutes(4), true);
        var unnamed = new TimerDefinition(string.Empty, TimeSpan.FromMinutes(7), true);
        timers.AddTemplate(tea);
        timers.AddTemplate(unnamed);
        Assert.All(buttons, button => Assert.Equal(
            ["Tea · 4 min", "Timer 7m"],
            button.MoreCommands.OfType<ICommandContextItem>().Where(action => action.Command is StartTimerCommand)
                .Select(action => action.Title)));

        Assert.True(timers.TryUpdateTemplate(tea with { Name = "Break", Duration = TimeSpan.FromMinutes(5) }));
        timers.RemoveTemplate(unnamed.Id);
        Assert.All(buttons, button =>
        {
            var start = Assert.Single(button.MoreCommands.OfType<ICommandContextItem>(),
                action => action.Command is StartTimerCommand);
            Assert.Equal("Break · 5 min", start.Title);
        });
        Assert.Equal(buttons, pages.Select(page => Assert.Single(page.GetItems())).ToArray());

        timers.Resume(focus.Id);
        var shortcut = Assert.Single(buttons[0].MoreCommands.OfType<ICommandContextItem>(),
            action => action.Command is StartTimerCommand);
        var result = Assert.IsType<StartTimerCommand>(shortcut.Command).Invoke();
        var started = Assert.Single(timers.GetTimersSnapshot(), timer => timer.Id != focus.Id);
        Assert.Equal("Break", started.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), started.Duration);
        Assert.Equal(TimerState.Running, focus.State);
        Assert.Single(timers.GetTemplatesSnapshot());
        Assert.Equal("Started Break · 5 min", Assert.IsType<ToastArgs>(result.Args).Message);
    }

    [Fact]
    public void StopwatchDockMenusCanStartAnotherStopwatch()
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var alarms = new AlarmService();
        using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
        var existing = stopwatches.CreateAndStart("Focus");
        using var pinnedPage
            = Assert.IsType<StopwatchDockPage>(
                provider.GetCommandItem(CommandIds.StopwatchDetail(existing.Id))?.Command);
        IListPage[] pages =
        [
            Assert.IsAssignableFrom<IListPage>(provider.GetCommandItem(CommandIds.StopwatchDockBand)?.Command),
            Assert.IsAssignableFrom<IListPage>(provider.GetCommandItem(CommandIds.ActiveStopwatchesDockBand)?.Command),
            pinnedPage,
            Assert.IsAssignableFrom<IListPage>(provider.GetCommandItem(CommandIds.AllStopwatchesDockBand)?.Command)
        ];

        var buttons = pages.Select(page => Assert.Single(page.GetItems())).ToArray();
        Assert.IsType<StopwatchIndexPage>(buttons[^1].Command);
        Assert.Equal("Open stopwatch list", buttons[^1].Title);
        Assert.All(buttons, button =>
        {
            Assert.IsType<Separator>(button.MoreCommands[^2]);
            var start = Assert.IsAssignableFrom<ICommandContextItem>(button.MoreCommands[^1]);
            Assert.Equal("Start new stopwatch", start.Title);
            Assert.Equal(CommandIds.StopwatchCreate, start.Command?.Id);
        });

        var shortcut = Assert.IsAssignableFrom<ICommandContextItem>(buttons[0].MoreCommands[^1]);
        Assert.IsAssignableFrom<InvokableCommand>(shortcut.Command).Invoke();
        var started = Assert.Single(stopwatches.GetStopwatchesSnapshot(), stopwatch => stopwatch.Id != existing.Id);
        Assert.Equal(StopwatchState.Running, started.State);
        Assert.Equal(StopwatchState.Running, existing.State);
    }

    [Fact]
    public void RecentButtonsShowTheClockAndKeepTheNameInTheSubtitle()
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var timerDock = new TimerDockPage(timers);
        var timer = timers.StartTimer(TimeSpan.FromMinutes(8), "Tea");
        timers.Pause(timer.Id);
        timer.ElapsedBeforePause = TimeSpan.FromSeconds(31);
        var timerButton = Assert.Single(timerDock.GetItems());
        var clock = DurationParser.FormatCountdown(timer.Remaining,
            TimerServices.Settings.ShouldShowTimerSeconds(timer.Remaining));
        Assert.Equal($"Paused · {clock}", timerButton.Title);
        Assert.Equal("Tea", timerButton.Subtitle);

        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var stopwatchDock = new StopwatchDockPage(stopwatches);
        var stopwatch = stopwatches.CreateAndStart("Focus");
        stopwatches.Pause(stopwatch.Id);
        stopwatch.ElapsedBeforePause = TimeSpan.FromSeconds(12);
        var stopwatchButton = Assert.Single(stopwatchDock.GetItems());
        Assert.Equal("Paused · 00:12", stopwatchButton.Title);
        Assert.Equal("Focus", stopwatchButton.Subtitle);
    }

    [Fact]
    public void TimerListDockAndDetailsExtendTheSameTimerAndKeepTheTemplateMenu()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        using var alarms = new AlarmService();
        var timer = scheduler.StartTimer(new TimerDefinition("Tea", TimeSpan.FromMinutes(4)), true);
        using var index = new TimersIndexPage(scheduler, alarms, new RepeatLastTimerCommand(scheduler));
        ((INotifyItemsChanged)index).ItemsChanged += (_, _) => { };
        var timerRow = Assert.Single(index.GetItems().OfType<TimerInstanceListItem>());
        Assert.Equal(
        [
            Strings.Action_Pause, Strings.Action_Stop, Strings.Action_Restart, "|", "Add 1 min", "Add 5 min", "|",
            Strings.Action_Delete
        ], MenuTitles(timerRow.MoreCommands));
        var addFromList = Assert.Single(timerRow.MoreCommands.OfType<ICommandContextItem>(),
            item => item.Title == "Add 1 min");
        Assert.IsAssignableFrom<InvokableCommand>(addFromList.Command).Invoke();

        scheduler.Pause(timer.Id);
        using var dock = new TimerDockPage(scheduler, timer);
        using var page = new TimerDetailPage(timer, scheduler);
        ((INotifyItemsChanged)page).ItemsChanged += (_, _) => { };
        var button = Assert.Single(dock.GetItems());
        string[] pausedMenu =
        [
            Strings.Action_Resume, Strings.Action_Stop, Strings.Action_Restart, "|", "Add 1 min", "Add 5 min", "|",
            Strings.Action_Delete
        ];
        Assert.Equal(pausedMenu, MenuTitles(timerRow.MoreCommands));
        Assert.Equal(pausedMenu, MenuTitles(page.GetItems()[0].MoreCommands));
        Assert.Equal([.. pausedMenu, "|", Strings.Action_NewTimer, "Tea · 4 min"], MenuTitles(button.MoreCommands));

        var addMinute = Assert.Single(button.MoreCommands.OfType<ICommandContextItem>(),
            item => item.Title == "Add 1 min");
        Assert.IsAssignableFrom<InvokableCommand>(addMinute.Command).Invoke();
        var addFive = Assert.Single(page.GetItems(), item => item.Title == "Add 5 min");
        Assert.IsAssignableFrom<InvokableCommand>(addFive.Command).Invoke();
        Assert.Equal(TimeSpan.FromMinutes(11), timer.CountdownDuration);
        Assert.Equal("11:00 • original 04:00", page.GetItems()[1].Subtitle);
        Assert.Same(button, Assert.Single(dock.GetItems()));
        Assert.StartsWith("Paused · Tea · ", button.Title);
        scheduler.Stop(timer.Id);
        Assert.Equal([Strings.Action_Restart, Strings.Action_Delete], MenuTitles(timerRow.MoreCommands));
        Assert.Equal([Strings.Action_StartAgain, Strings.Action_Delete], MenuTitles(page.GetItems()[0].MoreCommands));
        Assert.Equal([Strings.Action_Restart, Strings.Action_Delete, "|", Strings.Action_NewTimer, "Tea · 4 min"],
            MenuTitles(Assert.Single(dock.GetItems()).MoreCommands));

        static string[] MenuTitles(IContextItem[] menu) =>
            menu.Select(item =>
                    item is ISeparatorContextItem ? "|" : Assert.IsAssignableFrom<ICommandContextItem>(item).Title)
                .ToArray();
    }

    [Fact]
    public void UnsubscribedTimerDockReusesMenusUntilTemplatesChange()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(10), "Focus");
        using var dock = new TimerDockPage(scheduler);
        var button = Assert.Single(dock.GetItems());
        var commands = button.MoreCommands;
        Assert.Equal("Focus", button.Subtitle);
        var title = button.Title;

        timer.StartedAt -= TimeSpan.FromMinutes(1);
        Assert.Same(button, Assert.Single(dock.GetItems()));
        Assert.NotEqual(title, button.Title);
        Assert.Equal("Focus", button.Subtitle);
        Assert.Same(commands, button.MoreCommands);

        var tea = new TimerDefinition("Tea", TimeSpan.FromMinutes(4), true);
        scheduler.AddTemplate(tea);
        Assert.Same(button, Assert.Single(dock.GetItems()));
        Assert.NotSame(commands, button.MoreCommands);
        Assert.Contains(button.MoreCommands.OfType<ICommandContextItem>(), action => action.Title == "Tea · 4 min");

        commands = button.MoreCommands;
        Assert.True(scheduler.TryUpdateTemplate(tea with { }));
        Assert.Same(commands, Assert.Single(dock.GetItems()).MoreCommands);

        scheduler.RemoveTemplate(tea.Id);
        Assert.Same(button, Assert.Single(dock.GetItems()));
        Assert.NotSame(commands, button.MoreCommands);
        Assert.DoesNotContain(button.MoreCommands.OfType<ICommandContextItem>(),
            action => action.Command is StartTimerCommand);
    }

    [Fact]
    public async Task ActiveTimerBandKeepsButtonsInOrderWhileTimeUpdates()
    {
        var timeout = TimeSpan.FromSeconds(5);
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        using var dock = new TimerDockPage(scheduler, showActive: true);
        Assert.Equal(Strings.Action_NewTimer, Assert.Single(dock.GetItems()).Title);

        var focus = scheduler.StartTimer(TimeSpan.FromMinutes(10), "Focus");
        var tea = scheduler.StartTimer(TimeSpan.FromMinutes(4), "Tea");
        var stopped = scheduler.StartTimer(TimeSpan.FromMinutes(5), "Stopped");
        scheduler.Pause(tea.Id);
        scheduler.Stop(stopped.Id);
        var buttons = dock.GetItems();
        var expectedOrder = new[] { focus, tea }.OrderBy(timer => timer.CreatedAt).ThenBy(timer => timer.Id);
        Assert.Equal(expectedOrder.Select(timer => CommandIds.TimerDetail(timer.Id)),
            buttons.Select(item => item.Command?.Id));
        var pausedButton = Assert.Single(buttons, item => item.Command?.Id == CommandIds.TimerDetail(tea.Id));
        Assert.Equal(
            $"Paused · Tea · {DurationParser.FormatCountdown(tea.Remaining, TimerServices.Settings.ShouldShowTimerSeconds(tea.Remaining))}",
            pausedButton.Title);
        var resume = Assert.Single(pausedButton.MoreCommands.OfType<ICommandContextItem>(),
            item => item.Command?.Name == Strings.Action_Resume);

        var tickingButton
            = Assert.IsType<ListItem>(Assert.Single(buttons,
                item => item.Command?.Id == CommandIds.TimerDetail(focus.Id)));
        var titleBefore = tickingButton.Title;
        var ticked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var itemsChangedCount = 0;
        tickingButton.PropChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListItem.Title) && tickingButton.Title != titleBefore)
            {
                ticked.TrySetResult();
            }
        };
        ((INotifyItemsChanged)dock).ItemsChanged += (_, _) => Interlocked.Increment(ref itemsChangedCount);
        focus.StartedAt -= TimeSpan.FromMinutes(1);

        await ticked.Task.WaitAsync(timeout);
        Assert.Equal(0, Volatile.Read(ref itemsChangedCount));
        Assert.Equal(buttons, dock.GetItems());

        Assert.IsAssignableFrom<InvokableCommand>(resume.Command).Invoke();
        Assert.Equal(TimerState.Running, tea.State);
        scheduler.Stop(focus.Id);
        Assert.Same(pausedButton, Assert.Single(dock.GetItems()));
        scheduler.Stop(tea.Id);
        Assert.IsType<EditTimerPage>(Assert.Single(dock.GetItems()).Command);
    }

    [Fact]
    public void ActiveStopwatchBandIncludesPausedItemsAndStartsOneWhenEmpty()
    {
        using var storage = new StopwatchStorage(this._directory);
        using var scheduler = new StopwatchScheduler(storage);
        using var dock = new StopwatchDockPage(scheduler, showActive: true);
        var first = scheduler.CreateAndStart("Focus");
        var tea = scheduler.CreateAndStart("Tea");
        scheduler.Pause(tea.Id);
        scheduler.Create("Pending");

        var buttons = dock.GetItems();
        var expectedOrder = new[] { first, tea }.OrderBy(stopwatch => stopwatch.CreatedAt)
            .ThenBy(stopwatch => stopwatch.Id);
        Assert.Equal(expectedOrder.Select(stopwatch => CommandIds.StopwatchDetail(stopwatch.Id)),
            buttons.Select(item => item.Command?.Id));
        var pausedButton = Assert.Single(buttons, item => item.Command?.Id == CommandIds.StopwatchDetail(tea.Id));
        Assert.Equal($"Paused · Tea · {DurationParser.FormatClock(tea.Elapsed)}", pausedButton.Title);
        var resume = Assert.Single(pausedButton.MoreCommands.OfType<ICommandContextItem>(),
            item => item.Command?.Name == Strings.Action_Resume);
        var tickingButton = Assert.Single(buttons, item => item.Command?.Id == CommandIds.StopwatchDetail(first.Id));
        var titleBefore = tickingButton.Title;
        first.StartedAt -= TimeSpan.FromMinutes(1);

        Assert.Equal(buttons, dock.GetItems());
        Assert.NotEqual(titleBefore, tickingButton.Title);

        Assert.IsAssignableFrom<InvokableCommand>(resume.Command).Invoke();
        Assert.Equal(StopwatchState.Running, tea.State);
        Assert.Equal(buttons, dock.GetItems());
        Assert.StartsWith("Tea · ", pausedButton.Title);
        scheduler.StopAll();

        var empty = Assert.Single(dock.GetItems());
        Assert.Equal(Strings.Action_StartStopwatch, empty.Title);
        Assert.IsAssignableFrom<InvokableCommand>(empty.Command).Invoke();
        Assert.IsType<StopwatchDetailPage>(Assert.Single(dock.GetItems()).Command);
    }

    [Theory]
    [InlineData("Tea", "Tea", "4 min", "Started Tea · 4 min")]
    [InlineData("", "Timer 4m", "Saved template", "Started Timer 4m")]
    [InlineData("Timer 4m", "Timer 4m", "4 min", "Started Timer 4m · 4 min")]
    public void PinnedTemplateStartsItsTimerWithTheExpectedConfirmation(
        string name,
        string title,
        string subtitle,
        string confirmation)
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var alarms = new AlarmService();
        using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
        var template = new TimerDefinition(name, TimeSpan.FromMinutes(4), true);
        timers.AddTemplate(template);

        var pin = provider.GetCommandItem(CommandIds.TimerTemplate(template.Id));
        Assert.NotNull(pin);
        var item = Assert.Single(Assert.IsAssignableFrom<IListPage>(pin.Command).GetItems());
        Assert.Equal(title, item.Title);
        Assert.Equal(subtitle, item.Subtitle);
        var result = Assert.IsType<StartTimerCommand>(item.Command).Invoke();

        var timer = Assert.Single(timers.GetTimersSnapshot());
        Assert.Equal(title, timer.Name);
        Assert.Equal(TimeSpan.FromMinutes(4), timer.Duration);
        Assert.Single(timers.GetTemplatesSnapshot());
        Assert.Equal(CommandResultKind.ShowToast, result.Kind);
        Assert.Equal(confirmation, Assert.IsType<ToastArgs>(result.Args).Message);
    }

    [Fact]
    public void NewTimerCanBeResolvedAsAPinnedCommandItem()
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var alarms = new AlarmService();
        using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);

        var command = Assert.IsType<EditTimerPage>(provider.GetCommand(EditTimerPage.PageId));
        var item = provider.GetCommandItem(command.Id);

        Assert.NotNull(item);
        Assert.Equal(command.Id, Assert.IsType<EditTimerPage>(item.Command).Id);
        Assert.Same(item, provider.GetCommandItem(command.Id.ToUpperInvariant()));
    }

    [Fact]
    public void ProviderReloadsWhenPinsChange()
    {
        using var timerStorage = new TimerStorage(this._directory);
        using var timers = new TimerScheduler(timerStorage);
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatches = new StopwatchScheduler(stopwatchStorage);
        using var alarms = new AlarmService();
        using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
        timers.Initialize();
        stopwatches.Initialize();
        var reloads = 0;
        provider.ItemsChanged += (_, _) => reloads++;

        var timer = timers.StartTimer(TimeSpan.FromMinutes(5));
        var stopwatch = stopwatches.CreateAndStart();
        timers.Pause(timer.Id);
        stopwatches.RecordLap(stopwatch.Id);
        Assert.Equal(0, reloads);

        timers.StartTimer(new TimerDefinition("Template", TimeSpan.FromMinutes(4)), true);
        Assert.Equal(1, reloads);
        timers.RemoveTimer(timer.Id);
        stopwatches.ClearAll();
        Assert.Equal(3, reloads);
    }

    [Fact]
    public async Task DockReadsCanRaceWithClearingTimersAndDisposal()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        scheduler.StartTimer(TimeSpan.FromMinutes(10));
        using var dock = new TimerDockPage(scheduler);
        ((INotifyItemsChanged)dock).ItemsChanged += (_, _) => { };

        var reads = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                Assert.All(dock.GetItems(), item => Assert.NotNull(item));
            }
        });

        scheduler.ClearAll();
        Assert.IsType<EditTimerPage>(Assert.Single(dock.GetItems()).Command);
        dock.Dispose();

        await reads.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(dock.GetItems());
    }

    [Fact]
    public async Task DockShowsResumeWhenTimerPausesDuringRefresh()
    {
        var timeout = TimeSpan.FromSeconds(5);
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(10));
        using var dock = new TimerDockPage(scheduler);
        ((INotifyItemsChanged)dock).ItemsChanged += (_, _) => { };
        var item = Assert.IsType<ListItem>(Assert.Single(dock.GetItems()));
        var refreshing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRefresh = new ManualResetEventSlim();
        item.PropChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListItem.Title) && refreshing.TrySetResult())
            {
                releaseRefresh.Wait(timeout * 2);
            }
        };
        timer.PropertyChanged += RecordPause;

        timer.StartedAt -= TimeSpan.FromMinutes(1);
        var read = Task.Run(dock.GetItems);
        Task pause = Task.CompletedTask;
        try
        {
            await refreshing.Task.WaitAsync(timeout);
            pause = Task.Run(() => scheduler.Pause(timer.Id));
            await paused.Task.WaitAsync(timeout);
            Assert.Same(item, Assert.Single(await Task.Run(dock.GetItems).WaitAsync(timeout)));
            await pause.WaitAsync(timeout);
        }
        finally
        {
            releaseRefresh.Set();
            await Task.WhenAll(read, pause).WaitAsync(timeout);
            timer.PropertyChanged -= RecordPause;
        }

        Assert.Contains(item.MoreCommands.OfType<ICommandContextItem>(),
            contextItem => contextItem.Command?.Name == Strings.Action_Resume);

        void RecordPause(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(TimerInstance.State) && timer.State == TimerState.Paused)
            {
                paused.TrySetResult();
            }
        }
    }

    [Fact]
    public void StoppingATimerUpdatesTheListRowToTheStoppedIcon()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(5), "Tea");
        using var item = new TimerInstanceListItem(timer, scheduler);
        item.SetUIUpdatesEnabled(true);

        scheduler.Stop(timer.Id);

        var icon = Assert.IsType<IconInfo>(item.Icon);
        Assert.Equal("Timer_Stopped.light.png", Path.GetFileName(icon.Light.Icon));
        Assert.Equal("Timer_Stopped.dark.png", Path.GetFileName(icon.Dark.Icon));
        Assert.True(File.Exists(icon.Light.Icon));
        Assert.True(File.Exists(icon.Dark.Icon));
    }

    [Fact]
    public void StopwatchListRowUsesTimerTagColorsAsItsStateChanges()
    {
        using var stopwatchStorage = new StopwatchStorage(this._directory);
        using var stopwatchScheduler = new StopwatchScheduler(stopwatchStorage);
        var stopwatch = stopwatchScheduler.Create("Focus");
        using var stopwatchRow = new StopwatchInstanceListItem(stopwatch, stopwatchScheduler);
        var ready = Assert.Single(stopwatchRow.Tags);
        Assert.Equal(Strings.State_Ready, ready.Text);
        Assert.False(ready.Background.HasValue);
        Assert.False(ready.Foreground.HasValue);
        stopwatchRow.SetUIUpdatesEnabled(true);

        using var timerStorage = new TimerStorage(this._directory);
        using var timerScheduler = new TimerScheduler(timerStorage);
        var timer = new TimerInstance { Duration = TimeSpan.FromMinutes(5) };
        using var timerRow = new TimerInstanceListItem(timer, timerScheduler);
        timerRow.SetUIUpdatesEnabled(true);

        stopwatchScheduler.Start(stopwatch.Id);
        AssertMatchingTag(TimerState.Running);
        stopwatchScheduler.Pause(stopwatch.Id);
        AssertMatchingTag(TimerState.Paused);
        stopwatchScheduler.Stop(stopwatch.Id);
        AssertMatchingTag(TimerState.Finished);

        void AssertMatchingTag(TimerState state)
        {
            timer.State = state;
            var expected = Assert.Single(timerRow.Tags);
            var actual = Assert.Single(stopwatchRow.Tags);
            Assert.Equal(expected.Text, actual.Text);
            Assert.True(actual.Background.HasValue);
            Assert.Equal(expected.Background, actual.Background);
            Assert.Equal(expected.Foreground, actual.Foreground);
        }
    }

    [Fact]
    public async Task ListRowShowsPauseWhenTimerResumesDuringAnItemNotification()
    {
        var timeout = TimeSpan.FromSeconds(5);
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(10));
        using var item = new TimerInstanceListItem(timer, scheduler);
        item.SetUIUpdatesEnabled(true);
        var updatingActions = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseUpdate = new ManualResetEventSlim();
        item.PropChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListItem.MoreCommands) && updatingActions.TrySetResult())
            {
                releaseUpdate.Wait(timeout * 2);
            }
        };

        var pause = Task.Run(() => scheduler.Pause(timer.Id));
        try
        {
            await updatingActions.Task.WaitAsync(timeout);
            await Task.Run(() => scheduler.Resume(timer.Id)).WaitAsync(timeout);
        }
        finally
        {
            releaseUpdate.Set();
            await pause.WaitAsync(timeout);
        }

        Assert.Contains(item.MoreCommands.OfType<ICommandContextItem>(),
            action => action.Command?.Name == Strings.Action_Pause);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopwatchDetailRemainsLiveWhenRemovedFromRecentBand(bool replaceWithNewStopwatch)
    {
        using var storage = new StopwatchStorage(this._directory);
        using var scheduler = new StopwatchScheduler(storage);
        var stopwatch = scheduler.CreateAndStart("Task");
        using var dock = new StopwatchDockPage(scheduler);
        TypedEventHandler<object, IItemsChangedEventArgs> handler = (_, _) => { };
        ((INotifyItemsChanged)dock).ItemsChanged += handler;
        using var detail = Assert.IsType<StopwatchDetailPage>(dock.GetItems().Single().Command);
        ((INotifyItemsChanged)detail).ItemsChanged += handler;
        try
        {
            if (replaceWithNewStopwatch)
            {
                scheduler.CreateAndStart("Another task");
            }
            else
            {
                scheduler.Stop(stopwatch.Id);
            }

            scheduler.Reset(stopwatch.Id);
            Assert.Contains(detail.GetItems(), item => item.Title == Strings.Action_Start);
        }
        finally
        {
            ((INotifyItemsChanged)detail).ItemsChanged -= handler;
            ((INotifyItemsChanged)dock).ItemsChanged -= handler;
        }
    }

    [Fact]
    public void TimerDetailShowsOneLiveSummaryWithTheDeadline()
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(35), "Focus");
        using var detail = new TimerDetailPage(timer, scheduler);
        var items = detail.GetItems();
        var summary = Assert.IsType<ListItem>(items[0]);

        Assert.Equal([Strings.Label_Status, Strings.Label_Duration, Strings.Setting_AlarmSound_Title],
            items.TakeWhile(item => item is not GroupHeaderListItem).Select(item => item.Title).ToArray());
        Assert.Contains(timer.ExpectedEnd!.Value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
            summary.Subtitle);
        Assert.Equal(DurationParser.FormatClock(timer.Duration), items[1].Subtitle);
        Assert.Contains(summary.MoreCommands.OfType<ICommandContextItem>(),
            action => action.Command?.Name == Strings.Action_Pause);

        ((INotifyItemsChanged)detail).ItemsChanged += (_, _) => { };
        scheduler.Pause(timer.Id);

        Assert.Same(summary, detail.GetItems()[0]);
        Assert.Equal(
            LabelFormatter.FormatTimerListSubtitle(timer,
                TimerServices.Settings.ShouldShowTimerSeconds(timer.Remaining)), summary.Subtitle);
        Assert.Contains(summary.MoreCommands.OfType<ICommandContextItem>(),
            action => action.Command?.Name == Strings.Action_Resume);

        timer.TransitionToFinished(DateTimeOffset.UtcNow, timer.Duration);
        scheduler.HandleAlarmClosed(AlarmRequest.FromTimer(timer).ToResult(AlarmClosedKind.Snooze));

        Assert.Same(summary, detail.GetItems()[0]);
        Assert.StartsWith("Snooze • ", summary.Subtitle);
        Assert.Equal([Strings.Label_Status, Strings.Label_Snooze, Strings.Setting_AlarmSound_Title],
            detail.GetItems().TakeWhile(item => item is not GroupHeaderListItem).Select(item => item.Title).ToArray());
        Assert.Equal("05:00 • original 35:00", detail.GetItems()[1].Subtitle);

        scheduler.Restart(timer.Id);
        Assert.Equal(Strings.Label_Duration, detail.GetItems()[1].Title);
        Assert.Equal("35:00", detail.GetItems()[1].Subtitle);
    }

    [Fact]
    public void TimerAndTemplateKeepTheirOwnAlarmSoundAcrossRestartAndReload()
    {
        Guid timerId;
        using (var storage = new TimerStorage(this._directory))
        using (var scheduler = new TimerScheduler(storage))
        {
            var create = new EditTimerPage(scheduler);
            var form = Assert.IsType<EditTimerForm>(Assert.Single(create.GetContent()));
            form.SubmitForm("""{"name":"Tea","duration":"5m","alarmSound":"silent","saveAsTemplate":"true"}""");

            var timer = Assert.Single(scheduler.GetTimersSnapshot());
            timerId = timer.Id;
            Assert.Equal("silent", timer.AlarmSoundId);
            using var detail = new TimerDetailPage(timer, scheduler);
            ((INotifyItemsChanged)detail).ItemsChanged += (_, _) => { };
            var row = Assert.Single(detail.GetItems(), item => item.Title == Strings.Setting_AlarmSound_Title);
            Assert.Equal(Strings.AlarmSound_Silent, row.Subtitle);
            var soundPage = Assert.IsType<TimerSoundPage>(row.Command);
            var bell = Assert.Single(soundPage.GetItems(), item => item.Title == Strings.AlarmSound_BrightBell);
            Assert.Equal(CommandResultKind.GoBack,
                Assert.IsAssignableFrom<InvokableCommand>(bell.Command).Invoke().Kind);
            Assert.Equal(Strings.AlarmSound_BrightBell, row.Subtitle);
            scheduler.Restart(timerId);
            Assert.Equal("bright-bell", timer.AlarmSoundId);
        }

        using var reopenedStorage = new TimerStorage(this._directory);
        using var reopened = new TimerScheduler(reopenedStorage);
        reopened.Initialize();
        Assert.Equal("bright-bell", Assert.Single(reopened.GetTimersSnapshot()).AlarmSoundId);
        var template = Assert.Single(reopened.GetTemplatesSnapshot());
        Assert.Equal("silent", template.AlarmSoundId);
        new StartTimerCommand(template.Id, reopened).Invoke();
        Assert.Equal("silent", Assert.Single(reopened.GetTimersSnapshot(), timer => timer.Id != timerId).AlarmSoundId);
        Assert.Single(reopened.GetTemplatesSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimerDetailRemainsLiveWhenRemovedFromRecentBand(bool replaceWithNewTimer)
    {
        using var storage = new TimerStorage(this._directory);
        using var scheduler = new TimerScheduler(storage);
        var timer = scheduler.StartTimer(TimeSpan.FromMinutes(10), "Task");
        using var dock = new TimerDockPage(scheduler);
        TypedEventHandler<object, IItemsChangedEventArgs> handler = (_, _) => { };
        ((INotifyItemsChanged)dock).ItemsChanged += handler;
        using var detail = Assert.IsType<TimerDetailPage>(dock.GetItems().Single().Command);
        ((INotifyItemsChanged)detail).ItemsChanged += handler;
        try
        {
            if (replaceWithNewTimer)
            {
                scheduler.StartTimer(TimeSpan.FromMinutes(5), "Another task");
            }
            else
            {
                scheduler.Stop(timer.Id);
            }

            scheduler.Restart(timer.Id);
            scheduler.Pause(timer.Id);
            Assert.Contains(detail.GetItems(), item => item.Title == Strings.Action_Resume);
        }
        finally
        {
            ((INotifyItemsChanged)detail).ItemsChanged -= handler;
            ((INotifyItemsChanged)dock).ItemsChanged -= handler;
        }
    }

    public void Dispose()
    {
        Directory.Delete(this._directory, true);
    }
}