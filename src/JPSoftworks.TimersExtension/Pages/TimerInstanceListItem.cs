// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerInstanceListItem : ListItem, IDisposable
{
    private readonly TimerInstance _timer;
    private readonly TimerScheduler _scheduler;

    private TimerState _oldState;
    private bool _uiUpdatesEnabled;

    public TimerInstanceListItem(TimerInstance timer, TimerScheduler scheduler)
    {
        this._timer = timer;
        this._scheduler = scheduler;
        TimerServices.Settings.TimerPresentationChanged += this.Settings_TimerPresentationChanged;
        this.RefreshOpenCommand();
        this.UpdateInfo();
    }

    public void SetUIUpdatesEnabled(bool enabled)
    {
        if (enabled == this._uiUpdatesEnabled)
        {
            return;
        }

        this._uiUpdatesEnabled = enabled;

        if (enabled)
        {
            this.RefreshOpenCommand();
            this._timer.PropertyChanged += this.Timer_PropertyChanged;
            this.UpdateInfo();
        }
        else
        {
            this._timer.PropertyChanged -= this.Timer_PropertyChanged;
        }
    }

    private void Timer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerInstance.Remaining))
        {
            this.UpdateTime();
        }
        else
        {
            this.UpdateInfo();
        }
    }

    private void Settings_TimerPresentationChanged(object? sender, EventArgs e)
    {
        this.UpdateInfo();
    }

    private void RefreshOpenCommand()
    {
        this.Command = new TimerDetailPage(this._timer, this._scheduler);
    }

    private void UpdateInfo()
    {
        this.Title = LabelFormatter.FormatTimerName(this._timer.Name, this._timer.Duration);

        var state = this._timer.State;
        if (this._oldState != state)
        {
            var pauseCommand = new AnonymousCommand(() => this._scheduler.Pause(this._timer.Id))
            {
                Name = Strings.Action_Pause, Icon = new IconInfo("\uE769"), Result = CommandResult.KeepOpen()
            };

            var resumeCommand = new AnonymousCommand(() => this._scheduler.Resume(this._timer.Id))
            {
                Name = Strings.Action_Resume, Icon = new IconInfo("\uE768"), Result = CommandResult.KeepOpen()
            };
            var stopCommand = new AnonymousCommand(() => this._scheduler.Stop(this._timer.Id))
            {
                Name = Strings.Action_Stop, Icon = new IconInfo("\uE71A"), Result = CommandResult.KeepOpen()
            };
            var restartCommand = new AnonymousCommand(() => this._scheduler.Restart(this._timer.Id))
            {
                Name = state == TimerState.Finished ? Strings.Action_StartAgain : Strings.Action_Restart,
                Icon = new IconInfo("\uE777"),
                Result = CommandResult.KeepOpen()
            };
            var deleteCommand = new AnonymousCommand(() => this._scheduler.RemoveTimer(this._timer.Id))
            {
                Name = Strings.Action_Delete, Icon = new IconInfo("\uE74D"), Result = CommandResult.KeepOpen()
            };
            var confirmedDeleteCommand = new DeletionConfirmationCommand(
                deleteCommand,
                Strings.Confirm_DeleteTimer_Title,
                ResourceFormatter.Format(Strings.Confirm_DeleteTimer_Description,
                    LabelFormatter.FormatTimerName(this._timer.Name, this._timer.Duration)));

            IContextItem[] actions;
            switch (state)
            {
                case TimerState.Running:
                    actions =
                    [
                        new CommandContextItem(pauseCommand) { RequestedShortcut = Chords.Pause },
                        new CommandContextItem(stopCommand) { RequestedShortcut = Chords.Stop },
                        new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart }
                    ];
                    this.Tags = [StateTags.CreateRunning()];
                    break;
                case TimerState.Paused:
                    actions =
                    [
                        new CommandContextItem(resumeCommand) { RequestedShortcut = Chords.Resume },
                        new CommandContextItem(stopCommand) { RequestedShortcut = Chords.Stop },
                        new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart }
                    ];
                    this.Tags = [StateTags.CreatePaused()];
                    break;
                case TimerState.Finished:
                    actions = [new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart }];
                    this.Tags = [StateTags.CreateFinished()];
                    break;
                case TimerState.Cancelled:
                    actions = [new CommandContextItem(restartCommand) { RequestedShortcut = Chords.Restart }];
                    this.Tags =
                    [
                        new Tag(Strings.State_Stopped)
                        {
                            Background = new OptionalColor(true, new Color(180, 10, 0, 80))
                        }
                    ];
                    break;
                default:
                    actions = [];
                    break;
            }

            this.MoreCommands =
            [
                .. actions,
                .. AddTimerTimeCommand.CreateMenuGroup(this._timer.Id, state, this._scheduler),
                new CommandContextItem(confirmedDeleteCommand) { RequestedShortcut = Chords.Delete }
            ];
            this._oldState = state;
        }

        this.UpdateTime();
    }

    private void UpdateTime()
    {
        this.Subtitle = LabelFormatter.FormatTimerListSubtitle(this._timer,
            TimerServices.Settings.ShouldShowTimerSeconds(this._timer.Remaining));
        this.Icon = this.GetIconForCurrentState();
    }

    private IconInfo GetIconForCurrentState() =>
        this._timer.State switch
        {
            TimerState.Running => TimerProgressIconHelper.GetRunningTimerIcon(this._timer.CountdownDuration,
                this._timer.Remaining,
                TimerServices.Settings.AnimatedTimerIcons),
            TimerState.Paused => Icons.PausedTimer,
            TimerState.Finished => Icons.FinishedTimer,
            TimerState.Cancelled => Icons.CancelledTimer,
            _ => Icons.GenericTimer
        };

    public void Dispose()
    {
        this._timer.PropertyChanged -= this.Timer_PropertyChanged;
        TimerServices.Settings.TimerPresentationChanged -= this.Settings_TimerPresentationChanged;
        this._uiUpdatesEnabled = false;
    }
}