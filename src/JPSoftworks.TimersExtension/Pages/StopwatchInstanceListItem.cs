using System.ComponentModel;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;
using Microsoft.CommandPalette.Extensions;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed class StopwatchInstanceListItem : ListItem
{
    private readonly StopwatchInstance _stopwatch;
    private readonly StopwatchScheduler _scheduler;
    private readonly StopwatchDetailPage _detailPage;
    private bool _uiUpdatesEnabled;
    private StopwatchState _lastState;
    private string _lastName = string.Empty;

    public StopwatchInstanceListItem(StopwatchInstance stopwatch, StopwatchScheduler scheduler)
        : base()
    {
        _stopwatch = stopwatch;
        _scheduler = scheduler;
        _detailPage = new StopwatchDetailPage(stopwatch, scheduler);
        _lastState = stopwatch.State;
        _lastName = stopwatch.Name;

        Command = _detailPage;
        Title = FormatTitle(stopwatch.Name);
        Subtitle = FormatElapsed(stopwatch.Elapsed);
        Icon = GetIconForState(stopwatch.State);
        Tags = [new Tag(GetStateText(stopwatch.State))];
        MoreCommands = BuildMoreCommands(stopwatch.State);
    }

    public void SetUIUpdatesEnabled(bool enabled)
    {
        if (_uiUpdatesEnabled == enabled)
        {
            return;
        }

        _uiUpdatesEnabled = enabled;
        _detailPage.SetUIUpdatesEnabled(enabled);

        if (enabled)
        {
            _stopwatch.PropertyChanged += Stopwatch_PropertyChanged;
            UpdateElapsed();
            if (_lastState != _stopwatch.State)
            {
                UpdateStateRelatedProperties();
            }
            if (_lastName != _stopwatch.Name)
            {
                UpdateTitle();
            }
        }
        else
        {
            _stopwatch.PropertyChanged -= Stopwatch_PropertyChanged;
        }
    }

    private void Stopwatch_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StopwatchInstance.Elapsed):
                UpdateElapsed();
                break;

            case nameof(StopwatchInstance.State):
                UpdateStateRelatedProperties();
                break;

            case nameof(StopwatchInstance.Name):
                UpdateTitle();
                break;
        }
    }

    private void UpdateElapsed()
    {
        Subtitle = FormatElapsed(_stopwatch.Elapsed);
    }

    private void UpdateTitle()
    {
        _lastName = _stopwatch.Name;
        Title = FormatTitle(_stopwatch.Name);
    }

    private void UpdateStateRelatedProperties()
    {
        _lastState = _stopwatch.State;
        Icon = GetIconForState(_stopwatch.State);
        Tags = [new Tag(GetStateText(_stopwatch.State))];
        MoreCommands = BuildMoreCommands(_stopwatch.State);
    }

    private ICommandContextItem[] BuildMoreCommands(StopwatchState state)
    {
        var commands = new List<ICommandContextItem>();

        switch (state)
        {
            case StopwatchState.Running:
                commands.Add(new CommandContextItem(new LapStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Lap",
                    Icon = Icons.Lap
                });
                commands.Add(new CommandContextItem(new PauseStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Pause",
                    Icon = Icons.PauseStopwatch
                });
                commands.Add(new CommandContextItem(new StopStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Stop",
                    Icon = Icons.StopStopwatch
                });
                break;

            case StopwatchState.Paused:
                commands.Add(new CommandContextItem(new ResumeStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Resume",
                    Icon = Icons.StartStopwatch
                });
                commands.Add(new CommandContextItem(new StopStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Stop",
                    Icon = Icons.StopStopwatch
                });
                commands.Add(new CommandContextItem(new ResetStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Reset",
                    Icon = Icons.ResetStopwatch
                });
                break;

            case StopwatchState.Pending:
                commands.Add(new CommandContextItem(new StartStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Start",
                    Icon = Icons.StartStopwatch
                });
                break;

            case StopwatchState.Stopped:
                commands.Add(new CommandContextItem(new ResetStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Reset",
                    Icon = Icons.ResetStopwatch
                });
                break;
        }

        commands.Add(new CommandContextItem(new DeleteStopwatchCommand(_stopwatch.Id, _scheduler))
        {
            Title = "Delete",
            Icon = Icons.Delete
        });

        return [.. commands];
    }

    private static string FormatTitle(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "Stopwatch" : name;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
        return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private static IconInfo GetIconForState(StopwatchState state) => state switch
    {
        StopwatchState.Running => Icons.StopwatchRunning,
        StopwatchState.Paused => Icons.StopwatchPaused,
        StopwatchState.Stopped => Icons.StopwatchStopped,
        _ => Icons.Stopwatch
    };

    private static string GetStateText(StopwatchState state) => state switch
    {
        StopwatchState.Pending => "Ready",
        StopwatchState.Running => "Running",
        StopwatchState.Paused => "Paused",
        StopwatchState.Stopped => "Stopped",
        _ => ""
    };
}