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

internal sealed partial class StopwatchInstanceListItem : ListItem, IDisposable
{
    private readonly StopwatchInstance _stopwatch;
    private readonly StopwatchScheduler _scheduler;
    private bool _uiUpdatesEnabled;
    private StopwatchState _lastState;
    private string _lastName = string.Empty;

    public StopwatchInstanceListItem(StopwatchInstance stopwatch, StopwatchScheduler scheduler)
    {
        this._stopwatch = stopwatch;
        this._scheduler = scheduler;
        this._lastState = stopwatch.State;
        this._lastName = stopwatch.Name;

        this.RefreshOpenCommand();
        this.Title = LabelFormatter.FormatStopwatchName(stopwatch.Name);
        this.Subtitle = LabelFormatter.FormatStopwatchListSubtitle(stopwatch);
        this.Icon = GetIconForState(stopwatch.State);
        this.Tags = [CreateStateTag(stopwatch.State)];
        this.MoreCommands = this.BuildMoreCommands(stopwatch.State);
    }

    public void SetUIUpdatesEnabled(bool enabled)
    {
        if (this._uiUpdatesEnabled == enabled)
        {
            return;
        }

        this._uiUpdatesEnabled = enabled;

        if (enabled)
        {
            this.RefreshOpenCommand();
            this._stopwatch.PropertyChanged += this.Stopwatch_PropertyChanged;
            this.RefreshSubtitle();
            if (this._lastState != this._stopwatch.State)
            {
                this.UpdateStateRelatedProperties();
            }

            if (this._lastName != this._stopwatch.Name)
            {
                this.UpdateTitle();
            }
        }
        else
        {
            this._stopwatch.PropertyChanged -= this.Stopwatch_PropertyChanged;
        }
    }

    private void RefreshOpenCommand()
    {
        this.Command = new StopwatchDetailPage(this._stopwatch, this._scheduler);
    }

    private void Stopwatch_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StopwatchInstance.Elapsed):
                this.RefreshSubtitle();
                break;

            case nameof(StopwatchInstance.State):
                this.UpdateStateRelatedProperties();
                break;

            case nameof(StopwatchInstance.Name):
                this.UpdateTitle();
                break;
        }
    }

    internal void RefreshSubtitle(DateOnly? today = null)
    {
        this.Subtitle = LabelFormatter.FormatStopwatchListSubtitle(this._stopwatch, today);
    }

    private void UpdateTitle()
    {
        this._lastName = this._stopwatch.Name;
        this.Title = LabelFormatter.FormatStopwatchName(this._stopwatch.Name);
    }

    private void UpdateStateRelatedProperties()
    {
        var state = this._stopwatch.State;
        this.Icon = GetIconForState(state);
        this.Tags = [CreateStateTag(state)];
        this.MoreCommands = this.BuildMoreCommands(state);
        this._lastState = state;
    }

    private ICommandContextItem[] BuildMoreCommands(StopwatchState state)
    {
        var commands = new List<ICommandContextItem>();

        switch (state)
        {
            case StopwatchState.Running:
                commands.Add(new CommandContextItem(new LapStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_Lap, Icon = Icons.Lap, RequestedShortcut = Chords.Lap
                });
                commands.Add(new CommandContextItem(new PauseStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_Pause, Icon = Icons.PauseStopwatch, RequestedShortcut = Chords.Pause
                });
                commands.Add(new CommandContextItem(new StopStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_FinishStopwatch,
                    Icon = Icons.StopStopwatch,
                    RequestedShortcut = Chords.Stop
                });
                break;

            case StopwatchState.Paused:
                commands.Add(new CommandContextItem(new ResumeStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_Resume, Icon = Icons.StartStopwatch, RequestedShortcut = Chords.Resume
                });
                commands.Add(new CommandContextItem(new StopStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_FinishStopwatch,
                    Icon = Icons.StopStopwatch,
                    RequestedShortcut = Chords.Stop
                });
                commands.Add(new CommandContextItem(new ResetStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_Reset, Icon = Icons.ResetStopwatch, RequestedShortcut = Chords.Reset
                });
                break;

            case StopwatchState.Pending:
                commands.Add(new CommandContextItem(new StartStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_Start, Icon = Icons.StartStopwatch, RequestedShortcut = Chords.Start
                });
                break;

            case StopwatchState.Stopped:
                commands.Add(
                    new CommandContextItem(
                        new CopyTextCommand(LabelFormatter.FormatStopwatchTime(this._stopwatch.Elapsed))
                        {
                            Name = Strings.Action_CopyElapsedTime
                        }) { Title = Strings.Action_CopyElapsedTime });
                commands.Add(new CommandContextItem(new ResetStopwatchCommand(this._stopwatch.Id, this._scheduler))
                {
                    Title = Strings.Action_Reset, Icon = Icons.ResetStopwatch, RequestedShortcut = Chords.Reset
                });
                break;
        }

        var deleteCommand = new DeleteStopwatchCommand(this._stopwatch.Id, this._scheduler);
        var confirmedDeleteCommand = new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteStopwatch_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteStopwatch_Description,
                LabelFormatter.FormatStopwatchName(this._stopwatch.Name)));

        commands.Add(new CommandContextItem(confirmedDeleteCommand)
        {
            Title = Strings.Action_Delete, Icon = Icons.Delete, RequestedShortcut = Chords.Delete
        });

        return [.. commands];
    }

    private static IconInfo GetIconForState(StopwatchState state) => state switch
    {
        StopwatchState.Running => Icons.StopwatchRunning,
        StopwatchState.Paused => Icons.StopwatchPaused,
        StopwatchState.Stopped => Icons.StopwatchStopped,
        _ => Icons.Stopwatch
    };

    private static Tag CreateStateTag(StopwatchState state) => state switch
    {
        StopwatchState.Pending => new Tag(Strings.State_Ready),
        StopwatchState.Running => StateTags.CreateRunning(),
        StopwatchState.Paused => StateTags.CreatePaused(),
        StopwatchState.Stopped => StateTags.CreateFinished(),
        _ => new Tag()
    };

    public void Dispose()
    {
        this._stopwatch.PropertyChanged -= this.Stopwatch_PropertyChanged;
        this._uiUpdatesEnabled = false;
    }
}