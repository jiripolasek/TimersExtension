// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Pages;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class FallbackNavigationTests
{
    [Fact]
    public void QueryPreviewUsesTheLocalFinishTimeOrDurationAndClearsInvalidInput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            var item = new TimerFallbackCommandItem(scheduler);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
            var beforeSpringForward = new DateTimeOffset(2026, 3, 29, 1, 45, 0, TimeSpan.FromHours(1));
            var start = beforeSpringForward.ToString("t", CultureInfo.CurrentCulture);
            item.UpdateQuery("tea 30m", beforeSpringForward.ToUniversalTime(), zone);
            var end = new DateTime(2026, 3, 29, 3, 15, 0);
            var endClock = end.ToString("t", CultureInfo.CurrentCulture);
            Assert.Equal("tea 30m", item.Title);
            Assert.Equal($"Start timer · Ends at {endClock} if started at {start}", item.Subtitle);
            item.UpdateQuery("  Tea  01m  ", beforeSpringForward, zone);
            Assert.Equal("Tea  01m", item.Title);
            Assert.Equal(
                $"Start timer for 1m · Ends at {beforeSpringForward.AddMinutes(1).ToString("t", CultureInfo.CurrentCulture)} if started at {start}",
                item.Subtitle);
            item.UpdateQuery($"until {endClock}", beforeSpringForward, zone);
            Assert.Equal($"Start timer · 30m if started at {start}", item.Subtitle);
            item.UpdateQuery($"until {end.AddHours(1).ToString("t", CultureInfo.CurrentCulture)}",
                beforeSpringForward.AddSeconds(10), zone);
            Assert.Equal($"Start timer · 1h 30m if started at {start}", item.Subtitle);
            var lastQuarterMinute = beforeSpringForward.AddSeconds(45);
            item.UpdateQuery($"until {beforeSpringForward.AddMinutes(1).ToString("t", CultureInfo.CurrentCulture)}",
                lastQuarterMinute, zone);
            Assert.Equal(
                $"Start timer · 15s if started at {lastQuarterMinute.ToString("T", CultureInfo.CurrentCulture)}",
                item.Subtitle);
            item.UpdateQuery("10s", lastQuarterMinute, zone);
            Assert.Equal(
                $"Start timer · Ends at {lastQuarterMinute.AddSeconds(10).ToString("T", CultureInfo.CurrentCulture)} if started at {lastQuarterMinute.ToString("T", CultureInfo.CurrentCulture)}",
                item.Subtitle);

            var beforeMidnight = new DateTimeOffset(2026, 3, 28, 23, 45, 0, TimeSpan.FromHours(1));
            item.UpdateQuery("30m", beforeMidnight, zone);
            Assert.Equal(
                $"Start timer · Ends at {beforeMidnight.AddMinutes(30).ToString("g", CultureInfo.CurrentCulture)} if started at {beforeMidnight.ToString("t", CultureInfo.CurrentCulture)}",
                item.Subtitle);
            item.UpdateQuery("Start timer", beforeMidnight, zone);
            Assert.False(item.HasCreationQuery);
            Assert.Equal(Strings.Fallback_TypeDurationTitle, item.Title);
            Assert.Equal(Strings.Action_StartTimer, item.DisplayTitle);
            Assert.Empty(item.Subtitle);

            var lastSecond = DateTimeOffset.MaxValue.AddSeconds(-1);
            item.UpdateQuery("1s", lastSecond, TimeZoneInfo.Utc);
            Assert.True(item.HasCreationQuery);
            item.UpdateQuery("2s", lastSecond, TimeZoneInfo.Utc);
            Assert.False(item.HasCreationQuery);
            Assert.Empty(item.Subtitle);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("tea 4m", "tea")]
    [InlineData("  StArT TiMeR tea 4m  ", "tea")]
    [InlineData("start timerkeeper 4m", "start timerkeeper")]
    public void TimerFallbackDismissesAfterStartingTimer(string query, string expectedName)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            var item = new TimerFallbackCommandItem(scheduler);
            item.UpdateQuery(query);
            Assert.Equal(query.Trim(), item.Title);
            var command = Assert.IsType<TimerFallbackCommand>(item.Command);

            var result = command.Invoke();

            var timer = Assert.Single(scheduler.GetTimersSnapshot());
            Assert.Equal(expectedName, timer.Name);
            Assert.Equal(TimeSpan.FromMinutes(4), timer.Duration);
            AssertDismisses(result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("hu-HU")]
    [InlineData("en-US")]
    public void CompactClockQueryDisplaysTheCurrentCulturesTime(string cultureName)
    {
        var directory = CreateTemporaryDirectory();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            var item = new TimerFallbackCommandItem(scheduler);
            var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

            item.UpdateQuery("2300", now, TimeZoneInfo.Utc);

            Assert.True(item.HasCreationQuery);
            Assert.Equal("2300", item.Title);
            var duration = DurationParser.FormatCompact(TimeSpan.FromMinutes(2300));
            Assert.StartsWith($"Start timer for {duration} · ", item.Subtitle);
            var clock = new TimeOnly(23, 0).ToString("t", CultureInfo.CurrentCulture);

            item.UpdateQuery("until 2300", now, TimeZoneInfo.Utc);
            Assert.Equal("until 2300", item.Title);
            Assert.StartsWith($"Start timer until {clock} · ", item.Subtitle);

            item.UpdateQuery("Start timer until 2300", now, TimeZoneInfo.Utc);
            Assert.Equal("Start timer until 2300", item.Title);
            Assert.StartsWith($"Start timer until {clock} · ", item.Subtitle);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StopwatchFallbackDismissesAfterStartingStopwatch()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var item = new StopwatchFallbackCommandItem(scheduler);
            item.UpdateQuery("laps");
            var command = Assert.IsType<StopwatchFallbackCommand>(item.Command);

            var result = command.Invoke();

            Assert.Equal("laps", Assert.Single(scheduler.GetStopwatchesSnapshot()).Name);
            AssertDismisses(result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("  stopwatch  Focus  ", "Focus")]
    [InlineData("START STOPWATCH Focus", "Focus")]
    [InlineData("stopwatch 20m", "20m")]
    public void HomeStopwatchFallbackRequiresAPrefixAndUsesOnlyTheName(string query, string expectedName)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var item = new StopwatchFallbackCommandItem(scheduler, true);
            var command = Assert.IsType<StopwatchFallbackCommand>(item.Command);
            Assert.Empty(item.Title);
            Assert.Empty(command.Name);

            item.UpdateQuery(query);
            Assert.Equal($"Start stopwatch \"{expectedName}\"", item.Title);
            Assert.Equal(Strings.Action_StartStopwatch, command.Name);
            AssertDismisses(command.Invoke());
            Assert.Equal(expectedName, Assert.Single(scheduler.GetStopwatchesSnapshot()).Name);

            foreach (var unrelatedQuery in new[] { "stop next timer", "4m", "start stopwatch", "stopwatching Focus" })
            {
                item.UpdateQuery(unrelatedQuery);
                Assert.Empty(item.Title);
                Assert.Empty(command.Name);
                Assert.Equal(CommandResultKind.KeepOpen, command.Invoke().Kind);
            }

            Assert.Single(scheduler.GetStopwatchesSnapshot());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PartialTranslationUsesTheLongestStopwatchPrefixAndKeepsTheName()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        var directory = CreateTemporaryDirectory();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-SC");
            using var storage = new StopwatchStorage(directory);
            using var scheduler = new StopwatchScheduler(storage);
            var item = new StopwatchFallbackCommandItem(scheduler, true);
            var command = Assert.IsType<StopwatchFallbackCommand>(item.Command);

            item.UpdateQuery("de\u0301marrer le chronome\u0300tre Lecture");
            Assert.Equal("Chronométrer « Lecture »", item.Title);
            AssertDismisses(command.Invoke());

            item.UpdateQuery("start stopwatch Focus");
            Assert.Equal("Chronométrer « Focus »", item.Title);
            AssertDismisses(command.Invoke());

            item.UpdateQuery("start stopwatch");
            Assert.Empty(item.Title);
            Assert.Equal(CommandResultKind.KeepOpen, command.Invoke().Kind);

            var stopwatches = scheduler.GetStopwatchesSnapshot();
            Assert.Contains(stopwatches, stopwatch => stopwatch.Name == "Lecture");
            Assert.Contains(stopwatches, stopwatch => stopwatch.Name == "Focus");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void NewTimerFormReturnsToTheParentAfterStarting()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var scheduler = new TimerScheduler(storage);
            var page = new EditTimerPage(scheduler);
            var form = Assert.IsType<EditTimerForm>(Assert.Single(page.GetContent()));

            var result = form.SubmitForm("""{"name":"Tea","duration":"4m"}""");

            var timer = Assert.Single(scheduler.GetTimersSnapshot());
            Assert.Equal("Tea", timer.Name);
            var toast = Assert.IsType<ToastArgs>(result.Args);
            var endTime = LabelFormatter.FormatEndTime(timer.ExpectedEnd!.Value, timer.StartedAt!.Value,
                TimeZoneInfo.Local);
            Assert.Contains("Tea", toast.Message);
            Assert.Contains(endTime, toast.Message);
            Assert.Equal(CommandResultKind.GoBack, Assert.IsType<CommandResult>(toast.Result).Kind);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void HomePublishesCustomTimerAndResolvesEachSavedTemplate()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            using var stopwatchStorage = new StopwatchStorage(directory);
            using var stopwatches = new StopwatchScheduler(stopwatchStorage);
            using var alarms = new AlarmService();
            using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
            var tea = new TimerDefinition("Tea", TimeSpan.FromMinutes(4), true);
            var focus = new TimerDefinition("Focus", TimeSpan.FromMinutes(25), true);
            timers.AddTemplate(tea);
            timers.AddTemplate(focus);

            var commands = provider.TopLevelCommands();
            var custom = Assert.Single(commands, item => item.Title == "Start custom timer…");
            Assert.Same(custom, provider.GetCommandItem(CommandIds.TimerCreatePage));
            Assert.Same(Icons.NewTimer, custom.Icon);
            var customForm
                = Assert.IsType<EditTimerForm>(Assert.Single(Assert.IsType<EditTimerPage>(custom.Command)
                    .GetContent()));
            customForm.SubmitForm("""{"name":"Custom","duration":"3m"}""");
            Assert.Equal("Custom", Assert.Single(timers.GetTimersSnapshot()).Name);

            var teaItem = Assert.Single(commands, item => item.Title == "Start timer Tea");
            Assert.Equal(CommandIds.HomeTimerTemplate(tea.Id), teaItem.Command?.Id);
            Assert.Same(Icons.NewTimer, teaItem.Icon);
            Assert.Same(Icons.NewTimer, teaItem.Command?.Icon);
            Assert.Equal("4 min", teaItem.Subtitle);
            var focusItem = Assert.Single(commands, item => item.Title == "Start timer Focus");
            Assert.Equal(CommandIds.HomeTimerTemplate(focus.Id), focusItem.Command?.Id);
            var customIndex = Array.IndexOf(commands, custom);
            Assert.Same(teaItem, commands[customIndex + 1]);
            Assert.Same(focusItem, commands[customIndex + 2]);
            Assert.Equal(CommandIds.StopwatchCreate, commands[customIndex + 3].Command?.Id);
            Assert.Equal(teaItem.Title, provider.GetCommandItem(CommandIds.HomeTimerTemplate(tea.Id))?.Title);
            Assert.IsType<StartTimerCommand>(provider.GetCommand(CommandIds.HomeTimerTemplate(focus.Id)));

            var publishCount = 0;
            provider.ItemsChanged += (_, _) => publishCount++;
            Assert.True(timers.TryUpdateTemplate(tea with { Name = "Break", Duration = TimeSpan.FromMinutes(8) }));
            Assert.DoesNotContain(provider.TopLevelCommands(), item => item.Title == "Start timer Tea");
            Assert.Equal("Start timer Break", provider.GetCommandItem(CommandIds.HomeTimerTemplate(tea.Id))?.Title);
            var teaCommand = Assert.IsType<StartTimerCommand>(teaItem.Command);
            AssertDismisses(Assert.IsType<CommandResult>(teaCommand.Invoke()));
            var started = Assert.Single(timers.GetTimersSnapshot(), timer => timer.Name == "Break");
            Assert.Equal(TimeSpan.FromMinutes(8), started.Duration);
            Assert.Equal(2, timers.GetTemplatesSnapshot().Count);

            timers.RemoveTemplate(tea.Id);
            Assert.Equal(2, publishCount);
            Assert.DoesNotContain(provider.TopLevelCommands(),
                item => item.Command?.Id == CommandIds.HomeTimerTemplate(tea.Id));
            Assert.Null(provider.GetCommandItem(CommandIds.HomeTimerTemplate(tea.Id)));
            Assert.Equal(Strings.Toast_TimerTemplateUnavailable,
                Assert.IsType<ToastArgs>(teaCommand.Invoke().Args).Message);
            Assert.Equal(2, timers.GetTimersSnapshot().Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void HomeStartsAStopwatchDirectly()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            using var stopwatchStorage = new StopwatchStorage(directory);
            using var stopwatches = new StopwatchScheduler(stopwatchStorage);
            using var alarms = new AlarmService();
            using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);

            var item = Assert.Single(provider.TopLevelCommands(), item => item.Title == Strings.Action_StartStopwatch);
            Assert.Same(Icons.NewStopwatch, item.Icon);
            Assert.Same(item, provider.GetCommandItem(CommandIds.StopwatchCreate));
            var command = Assert.IsType<CreateStopwatchCommand>(item.Command);
            Assert.Same(command, provider.GetCommand(CommandIds.StopwatchCreate));
            command.Invoke();
            Assert.Equal(StopwatchState.Running, Assert.Single(stopwatches.GetStopwatchesSnapshot()).State);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopeningATimerFormClearsValidationAndUnsubmittedInput(bool editingTemplate)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            ContentPage page = editingTemplate
                ? new EditTemplatePage(new TimerDefinition("Tea", TimeSpan.FromMinutes(4), true), timers)
                : new EditTimerPage(timers);
            var form = Assert.IsType<EditTimerForm>(Assert.Single(page.GetContent()));
            var originalData = form.DataJson;
            Assert.Equal(CommandResultKind.KeepOpen, form.SubmitForm("""{"duration":"abc"}""").Kind);
            Assert.NotEqual(originalData, form.DataJson);

            var reopened = Assert.IsType<EditTimerForm>(Assert.Single(page.GetContent()));
            Assert.Equal(originalData, reopened.DataJson);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RepeatLastTimerSubtitleTracksTheCurrentTargetAndOriginalDuration()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            using var stopwatchStorage = new StopwatchStorage(directory);
            using var stopwatches = new StopwatchScheduler(stopwatchStorage);
            using var alarms = new AlarmService();
            using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
            var item = Assert.IsType<CommandItem>(provider.GetCommandItem(CommandIds.TimerRepeatLast));
            var updates = new List<string>();
            item.PropChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(item.Subtitle))
                {
                    updates.Add(item.Subtitle);
                }
            };
            Assert.Empty(item.Subtitle);

            var tea = timers.StartTimer(TimeSpan.FromMinutes(4), "Tea");
            Assert.Equal("Tea · 4 min", item.Subtitle);
            var unnamed = timers.StartTimer(TimeSpan.FromMinutes(5));
            Assert.Equal("Timer 5m", item.Subtitle);
            timers.RemoveTimer(unnamed.Id);

            var focus = timers.StartTimer(TimeSpan.FromMinutes(35), "Focus");
            Assert.True(timers.TryExtend(focus.Id, TimeSpan.FromMinutes(5)));
            Assert.Equal("Focus · 35 min", item.Subtitle);
            Assert.IsType<RepeatLastTimerCommand>(item.Command).Invoke();
            Assert.Equal(TimeSpan.FromMinutes(35), focus.CountdownDuration);
            timers.Restart(tea.Id);
            Assert.Equal("Tea · 4 min", item.Subtitle);
            timers.RemoveTimer(focus.Id);
            timers.ClearAll();

            Assert.Equal(["Tea · 4 min", "Timer 5m", "Tea · 4 min", "Focus · 35 min", "Tea · 4 min", string.Empty],
                updates);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("list")]
    [InlineData("dock")]
    [InlineData("details")]
    public void RestartingATimerClearsItsAlarmAndKeepsTheOtherAlarmRinging(string surface)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var other = new TimerInstance
            {
                Name = "Tea",
                Duration = TimeSpan.FromMinutes(4),
                CreatedAt = now.AddMinutes(-5),
                PausedAt = now.AddMinutes(-1),
                State = TimerState.Finished
            };
            var target = new TimerInstance
            {
                Name = "Focus",
                Duration = TimeSpan.FromMinutes(2),
                CreatedAt = now.AddMinutes(-3),
                PausedAt = now.AddMinutes(-1),
                State = TimerState.Finished
            };
            using (var seed = new TimerStorage(directory))
            {
                seed.SaveTimers([other, target]);
            }

            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            timers.Initialize();
            using var stopwatchStorage = new StopwatchStorage(directory);
            using var stopwatches = new StopwatchScheduler(stopwatchStorage);
            var shown = new HashSet<Guid>();
            using var alarms = new AlarmService(toast => shown.Add(Guid.Parse(toast.Tag)), id => shown.Remove(id));
            using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
            var targetAlarm = AlarmRequest.FromTimer(target, isSilent: true);
            var otherAlarm = AlarmRequest.FromTimer(other, AlarmSoundCatalog.GetSoundUri("aurora.wav"), 0);
            alarms.ShowAlarm(targetAlarm);

            var restoredTarget = Assert.IsType<TimerInstance>(timers.TryGetTimer(target.Id));
            using var row = new TimerInstanceListItem(restoredTarget, timers);
            using var dock = new TimerDockPage(timers, restoredTarget);
            using var detail = new TimerDetailPage(restoredTarget, timers);
            var restart = surface switch
            {
                "list" => GetMenuCommand(row, Strings.Action_StartAgain),
                "dock" => GetMenuCommand(Assert.Single(dock.GetItems()), Strings.Action_Restart),
                "details" => Assert.Single(detail.GetItems(), item => item.Title == Strings.Action_StartAgain).Command,
                _ => provider.GetCommandItem(CommandIds.TimerRepeatLast)?.Command
            };
            alarms.ShowAlarm(otherAlarm);
            Assert.IsAssignableFrom<InvokableCommand>(restart).Invoke();

            Assert.Equal(TimerState.Running, timers.TryGetTimer(target.Id)?.State);
            Assert.Equal(otherAlarm.AlarmId, Assert.Single(shown));
            Assert.True(alarms.TryDismissRingingAlarm());
            Assert.Empty(shown);
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        static ICommand GetMenuCommand(IListItem item, string title) =>
            Assert.Single(item.MoreCommands.OfType<ICommandContextItem>(), command => command.Title == title).Command!;
    }

    [Fact]
    public void HomePreviewsAndStopsOnlyTheRunningTimerWithTheEarliestDeadline()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            using var stopwatchStorage = new StopwatchStorage(directory);
            using var stopwatches = new StopwatchScheduler(stopwatchStorage);
            using var alarms = new AlarmService();
            using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);

            var item = Assert.IsType<CommandItem>(Assert.Single(provider.TopLevelCommands(),
                item => item.Title == "Stop next timer"));
            Assert.Same(Icons.StopTimer, item.Icon);
            Assert.Same(item, provider.GetCommandItem(CommandIds.StopNextTimer));
            var command = Assert.IsType<StopNextTimerCommand>(item.Command);
            Assert.Same(command, provider.GetCommand(CommandIds.StopNextTimer));
            Assert.Equal(Strings.Toast_NoRunningTimer, item.Subtitle);
            var updates = new List<string>();
            item.PropChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(item.Subtitle))
                {
                    updates.Add(item.Subtitle);
                }
            };

            var extended = timers.StartTimer(TimeSpan.FromMinutes(2), "Focus");
            var next = timers.StartTimer(TimeSpan.FromMinutes(4), "Tea");
            var later = timers.StartTimer(TimeSpan.FromMinutes(12), "Laundry");
            Assert.True(timers.TryExtend(extended.Id, TimeSpan.FromMinutes(20)));
            var paused = timers.StartTimer(TimeSpan.FromMinutes(1), "Break");
            timers.Pause(paused.Id);
            Assert.Equal("Tea · 4 min", item.Subtitle);

            var result = command.Invoke();
            AssertDismisses(result);
            Assert.Equal("Stopped Tea", Assert.IsType<ToastArgs>(result.Args).Message);
            Assert.Equal(TimerState.Cancelled, next.State);
            Assert.Equal(TimerState.Running, extended.State);
            Assert.Equal(TimerState.Running, later.State);
            Assert.Equal(TimerState.Paused, paused.State);

            timers.Stop(extended.Id);
            timers.Stop(later.Id);
            var noRunningTimer = Assert.IsType<ToastArgs>(command.Invoke().Args);
            Assert.Equal(Strings.Toast_NoRunningTimer, noRunningTimer.Message);
            Assert.Equal(CommandResultKind.KeepOpen, Assert.IsType<CommandResult>(noRunningTimer.Result).Kind);
            Assert.Equal(TimerState.Paused, paused.State);
            Assert.Equal(
            [
                "Focus · 2 min", "Tea · 4 min", "Break · 1 min", "Tea · 4 min", "Laundry · 12 min",
                Strings.Toast_NoRunningTimer
            ], updates);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HomeDismissesOnlyTheRingingAlarmAndKeepsTheFinishedTimer(bool dismissDuringDelivery)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var timer = new TimerInstance
            {
                Name = "Tea",
                State = TimerState.Finished,
                Duration = TimeSpan.FromMinutes(4),
                PausedAt = DateTimeOffset.UtcNow
            };
            using (var seed = new TimerStorage(directory))
            {
                seed.SaveTimers([timer]);
            }

            using var storage = new TimerStorage(directory);
            using var timers = new TimerScheduler(storage);
            timers.Initialize();
            using var stopwatchStorage = new StopwatchStorage(directory);
            using var stopwatches = new StopwatchScheduler(stopwatchStorage);
            var shown = new HashSet<Guid>();
            var ringing = AlarmRequest.FromTimer(timer, AlarmSoundCatalog.GetSoundUri("aurora.wav"), 0);
            DismissRingingTimerCommand? dismiss = null;
            CommandResult? dismissedDuringDelivery = null;
            using var alarms = new AlarmService(toast =>
            {
                var alarmId = Guid.Parse(toast.Tag);
                if (alarmId == ringing.AlarmId && dismissDuringDelivery)
                {
                    dismissedDuringDelivery = dismiss!.Invoke();
                }

                shown.Add(alarmId);
            }, id => shown.Remove(id));
            using var provider = new TimersExtensionCommandsProvider(timers, stopwatches, alarms);
            var item = Assert.IsType<CommandItem>(Assert.Single(provider.TopLevelCommands(),
                item => item.Title == "Dismiss ringing timer"));
            dismiss = Assert.IsType<DismissRingingTimerCommand>(item.Command);
            Assert.Same(item, provider.GetCommandItem(CommandIds.DismissRingingTimer));
            Assert.Equal(Strings.Toast_NoRingingTimer, item.Subtitle);
            var updates = new List<string>();
            item.PropChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(item.Subtitle))
                {
                    updates.Add(item.Subtitle);
                }
            };

            var silent = AlarmRequest.FromTimer(timer, isSilent: true);
            alarms.ShowAlarm(silent);
            Assert.Equal(Strings.Toast_NoRingingTimer, Assert.IsType<ToastArgs>(dismiss.Invoke().Args).Message);
            alarms.ShowAlarm(ringing);
            if (!dismissDuringDelivery)
            {
                alarms.ShowAlarm(silent);
                Assert.Equal("Tea · 4 min", item.Subtitle);
            }

            var result = dismissDuringDelivery
                ? Assert.IsType<CommandResult>(dismissedDuringDelivery)
                : dismiss.Invoke();

            AssertDismisses(result);
            Assert.Equal(Strings.Toast_TimerAlarmDismissed, Assert.IsType<ToastArgs>(result.Args).Message);
            Assert.Equal(silent.AlarmId, Assert.Single(shown));
            Assert.Equal(TimerState.Finished, timers.TryGetTimer(timer.Id)?.State);
            Assert.Equal(Strings.Toast_NoRingingTimer, Assert.IsType<ToastArgs>(dismiss.Invoke().Args).Message);
            Assert.Equal(Strings.Toast_NoRingingTimer, item.Subtitle);
            Assert.Equal(["Tea · 4 min", Strings.Toast_NoRingingTimer], updates);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void AssertDismisses(CommandResult result)
    {
        Assert.Equal(CommandResultKind.ShowToast, result.Kind);
        var toast = Assert.IsType<ToastArgs>(result.Args);
        Assert.Equal(CommandResultKind.Dismiss, Assert.IsType<CommandResult>(toast.Result).Kind);
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