// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal static class DockItemFactory
{
    public static ListItem CreateCreateTimerItem(TimerScheduler scheduler)
    {
        return new ListItem(new EditTimerPage(scheduler)) { Title = Strings.Action_NewTimer, Icon = Icons.NewTimer };
    }

    public static ICommandContextItem[] CreateTimerStartCommands(
        TimerScheduler scheduler,
        IReadOnlyList<TimerDefinition> templates)
    {
        return
        [
            new CommandContextItem(new EditTimerPage(scheduler))
            {
                Title = Strings.Action_NewTimer, Icon = Icons.NewTimer
            },
            .. templates.Select(template => new CommandContextItem(CreateStartTemplateCommand(template, scheduler))
            {
                Title = LabelFormatter.FormatTemplateTitle(template.Name, template.Duration),
                Icon = Icons.GenericTimer
            })
        ];
    }

    public static ListItem CreateCreateStopwatchItem(StopwatchScheduler scheduler)
    {
        return new ListItem(new CreateStopwatchCommand(scheduler))
        {
            Title = Strings.Action_StartStopwatch, Icon = Icons.NewStopwatch
        };
    }

    public static CommandContextItem CreateStartStopwatchContextItem(StopwatchScheduler scheduler) =>
        new(new CreateStopwatchCommand(scheduler)) { Title = Strings.Action_StartNewStopwatch };

    public static ListItem CreateTimerOverviewItem(
        TimerInstance timer,
        TimerState state,
        TimerScheduler scheduler,
        bool nameInTitle,
        ICommandContextItem[] startCommands)
    {
        var item = new ListItem(new TimerDetailPage(timer, scheduler));
        UpdateTimerOverviewItem(item, timer, state, scheduler, true, nameInTitle, startCommands);
        return item;
    }

    public static ListItem CreateTimerTemplateItem(TimerDefinition template, TimerScheduler scheduler)
    {
        return new ListItem(CreateStartTemplateCommand(template, scheduler))
        {
            Title = LabelFormatter.FormatTemplateName(template),
            Subtitle = LabelFormatter.FormatTemplateSubtitle(template),
            Icon = Icons.GenericTimer,
            MoreCommands = BuildTemplateMoreCommands(template, scheduler)
        };
    }

    private static StartTimerCommand CreateStartTemplateCommand(TimerDefinition template, TimerScheduler scheduler) =>
        new(template with { IsTemplate = false }, scheduler, CommandIds.TimerTemplate(template.Id));

    public static ListItem CreateStopwatchItem(
        StopwatchInstance stopwatch,
        StopwatchState state,
        StopwatchScheduler scheduler,
        bool nameInTitle)
    {
        var item = new ListItem(new StopwatchDetailPage(stopwatch, scheduler));
        UpdateStopwatchItem(item, stopwatch, state, scheduler, true, nameInTitle);
        return item;
    }

    public static void UpdateTimerOverviewItem(
        ListItem item,
        TimerInstance timer,
        TimerState state,
        TimerScheduler scheduler,
        bool updateMoreCommands,
        bool nameInTitle,
        ICommandContextItem[] startCommands)
    {
        var remaining = timer.Remaining;
        var time = DurationParser.FormatCountdown(remaining, TimerServices.Settings.ShouldShowTimerSeconds(remaining));
        item.Title = FormatDockTitle(time,
            nameInTitle ? LabelFormatter.FormatTimerName(timer.Name, timer.Duration) : null,
            state == TimerState.Paused);
        item.Subtitle = LabelFormatter.FormatTimerDockSubtitle(timer, nameInTitle);
        item.Icon = GetTimerIcon(timer, state);
        if (updateMoreCommands)
        {
            item.MoreCommands = BuildTimerMoreCommands(timer, state, scheduler, startCommands);
        }
    }

    public static void UpdateStopwatchItem(
        ListItem item,
        StopwatchInstance stopwatch,
        StopwatchState state,
        StopwatchScheduler scheduler,
        bool updateMoreCommands,
        bool nameInTitle)
    {
        var name = LabelFormatter.FormatStopwatchName(stopwatch.Name);
        item.Title = FormatDockTitle(DurationParser.FormatClock(stopwatch.Elapsed), nameInTitle ? name : null,
            state == StopwatchState.Paused);
        item.Subtitle = nameInTitle
            ? state switch
            {
                StopwatchState.Stopped => Strings.State_Finished,
                StopwatchState.Pending => Strings.State_Ready,
                _ => Strings.Label_Elapsed
            }
            : name;
        item.Icon = GetStopwatchIcon(state);
        if (updateMoreCommands)
        {
            item.MoreCommands = BuildStopwatchMoreCommands(stopwatch, state, scheduler);
        }
    }

    private static string FormatDockTitle(string time, string? name, bool paused)
    {
        var title = name is null ? time : ResourceFormatter.Format(Strings.Label_NameAndTimeFormat, name, time);
        return paused ? ResourceFormatter.Format(Strings.Dock_PausedTitleFormat, title) : title;
    }

    private static IContextItem[] BuildTimerMoreCommands(
        TimerInstance timer,
        TimerState state,
        TimerScheduler scheduler,
        ICommandContextItem[] startCommands)
    {
        var commands = new List<IContextItem>();

        var pauseCommand = new AnonymousCommand(() => scheduler.Pause(timer.Id))
        {
            Name = Strings.Action_Pause, Icon = new IconInfo("\uE769"), Result = CommandResult.KeepOpen()
        };

        var resumeCommand = new AnonymousCommand(() => scheduler.Resume(timer.Id))
        {
            Name = Strings.Action_Resume, Icon = new IconInfo("\uE768"), Result = CommandResult.KeepOpen()
        };

        var stopCommand = new AnonymousCommand(() => scheduler.Stop(timer.Id))
        {
            Name = Strings.Action_Stop, Icon = new IconInfo("\uE71A"), Result = CommandResult.KeepOpen()
        };

        var restartCommand = new AnonymousCommand(() => scheduler.Restart(timer.Id))
        {
            Name = Strings.Action_Restart, Icon = new IconInfo("\uE777"), Result = CommandResult.KeepOpen()
        };

        var deleteCommand = new AnonymousCommand(() => scheduler.RemoveTimer(timer.Id))
        {
            Name = Strings.Action_Delete, Icon = Icons.Delete, Result = CommandResult.KeepOpen()
        };

        var confirmedDeleteCommand = new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteTimer_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteTimer_Description,
                LabelFormatter.FormatTimerName(timer.Name, timer.Duration)));

        switch (state)
        {
            case TimerState.Running:
                commands.Add(new CommandContextItem(pauseCommand) { RequestedShortcut = Chords.Pause });
                commands.Add(new CommandContextItem(stopCommand) { RequestedShortcut = Chords.Stop });
                commands.Add(new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart });
                break;

            case TimerState.Paused:
                commands.Add(new CommandContextItem(resumeCommand) { RequestedShortcut = Chords.Resume });
                commands.Add(new CommandContextItem(stopCommand) { RequestedShortcut = Chords.Stop });
                commands.Add(new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart });
                break;

            case TimerState.Finished:
            case TimerState.Cancelled:
                commands.Add(new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart });
                break;
        }

        commands.AddRange(AddTimerTimeCommand.CreateMenuGroup(timer.Id, state, scheduler));
        commands.Add(new CommandContextItem(confirmedDeleteCommand) { RequestedShortcut = Chords.Delete });
        return [.. commands, new Separator(), .. startCommands];
    }

    private static ICommandContextItem[] BuildTemplateMoreCommands(TimerDefinition template, TimerScheduler scheduler)
    {
        var deleteCommand = new AnonymousCommand(() => scheduler.RemoveTemplate(template.Id))
        {
            Name = Strings.Action_Delete, Icon = Icons.Delete, Result = CommandResult.KeepOpen()
        };
        var confirmedDeleteCommand = new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteTemplate_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteTemplate_Description,
                LabelFormatter.FormatTemplateName(template)));

        return
        [
            new CommandContextItem(new EditTemplatePage(template, scheduler))
            {
                Title = Strings.Action_Edit, RequestedShortcut = Chords.Edit
            },
            new CommandContextItem(confirmedDeleteCommand) { RequestedShortcut = Chords.Delete }
        ];
    }

    private static IContextItem[] BuildStopwatchMoreCommands(
        StopwatchInstance stopwatch,
        StopwatchState state,
        StopwatchScheduler scheduler)
    {
        var commands = new List<ICommandContextItem>();

        switch (state)
        {
            case StopwatchState.Running:
                commands.Add(
                    new CommandContextItem(new LapStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Lap
                    });
                commands.Add(
                    new CommandContextItem(new PauseStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Pause
                    });
                commands.Add(
                    new CommandContextItem(new StopStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Stop
                    });
                break;

            case StopwatchState.Paused:
                commands.Add(
                    new CommandContextItem(new ResumeStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Resume
                    });
                commands.Add(
                    new CommandContextItem(new StopStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Stop
                    });
                commands.Add(
                    new CommandContextItem(new ResetStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Reset
                    });
                break;

            case StopwatchState.Pending:
                commands.Add(
                    new CommandContextItem(new StartStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Start
                    });
                break;

            case StopwatchState.Stopped:
                commands.Add(
                    new CommandContextItem(new ResetStopwatchCommand(stopwatch.Id, scheduler))
                    {
                        RequestedShortcut = Chords.Reset
                    });
                break;
        }

        var deleteCommand = new DeleteStopwatchCommand(stopwatch.Id, scheduler);
        var confirmedDeleteCommand = new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteStopwatch_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteStopwatch_Description,
                LabelFormatter.FormatStopwatchName(stopwatch.Name)));

        commands.Add(new CommandContextItem(confirmedDeleteCommand) { RequestedShortcut = Chords.Delete });
        return [.. commands, new Separator(), CreateStartStopwatchContextItem(scheduler)];
    }

    private static IconInfo GetTimerIcon(TimerInstance timer, TimerState state) => state switch
    {
        TimerState.Running => TimerProgressIconHelper.GetRunningTimerIcon(
            timer.CountdownDuration,
            timer.Remaining,
            TimerServices.Settings.AnimatedTimerIcons),
        TimerState.Paused => Icons.PausedTimer,
        TimerState.Finished => Icons.FinishedTimer,
        TimerState.Cancelled => Icons.CancelledTimer,
        _ => Icons.GenericTimer
    };

    private static IconInfo GetStopwatchIcon(StopwatchState state) => state switch
    {
        StopwatchState.Running => Icons.StopwatchRunning,
        StopwatchState.Paused => Icons.StopwatchPaused,
        StopwatchState.Stopped => Icons.StopwatchStopped,
        _ => Icons.Stopwatch
    };
}