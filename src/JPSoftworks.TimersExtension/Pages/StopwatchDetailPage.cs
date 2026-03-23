using System.ComponentModel;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;
using Microsoft.CommandPalette.Extensions;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed class StopwatchDetailPage : ListPage, INotifyItemsChanged
{
    private readonly StopwatchInstance _stopwatch;
    private readonly StopwatchScheduler _scheduler;
    private readonly ListItem _elapsedItem;
    private readonly ListItem _lapTimeItem;
    private int _lastLapCount;
    private StopwatchState _lastState;
    private bool _uiUpdatesEnabled;

    public StopwatchDetailPage(StopwatchInstance stopwatch, StopwatchScheduler scheduler)
    {
        _stopwatch = stopwatch;
        _scheduler = scheduler;
        _lastLapCount = stopwatch.Laps.Count;
        _lastState = stopwatch.State;

        Icon = Icons.Stopwatch;
        Title = string.IsNullOrWhiteSpace(stopwatch.Name) ? "Stopwatch" : stopwatch.Name;
        Name = "Details";

        _elapsedItem = new ListItem
        {
            Title = "Elapsed",
            Subtitle = FormatTime(_stopwatch.Elapsed),
            Icon = Icons.Stopwatch
        };

        _lapTimeItem = new ListItem
        {
            Title = "Current Lap",
            Subtitle = FormatTime(_stopwatch.CurrentLapTime ?? TimeSpan.Zero),
            Icon = Icons.Lap
        };
    }

    public void SetUIUpdatesEnabled(bool enabled)
    {
        if (_uiUpdatesEnabled == enabled)
        {
            return;
        }

        _uiUpdatesEnabled = enabled;

        if (enabled)
        {
            _stopwatch.PropertyChanged += Stopwatch_PropertyChanged;
            UpdateTimeDisplays();
            if (_lastLapCount != _stopwatch.Laps.Count || _lastState != _stopwatch.State)
            {
                _lastLapCount = _stopwatch.Laps.Count;
                _lastState = _stopwatch.State;
                RaiseItemsChanged();
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
            case nameof(StopwatchInstance.CurrentLapTime):
                UpdateTimeDisplays();
                break;

            case nameof(StopwatchInstance.Laps):
                if (_stopwatch.Laps.Count != _lastLapCount)
                {
                    _lastLapCount = _stopwatch.Laps.Count;
                    RaiseItemsChanged();
                }
                break;

            case nameof(StopwatchInstance.State):
                if (_stopwatch.State != _lastState)
                {
                    _lastState = _stopwatch.State;
                    RaiseItemsChanged();
                }
                break;
        }
    }

    private void UpdateTimeDisplays()
    {
        _elapsedItem.Subtitle = FormatTime(_stopwatch.Elapsed);
        _lapTimeItem.Subtitle = FormatTime(_stopwatch.CurrentLapTime ?? TimeSpan.Zero);
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>
        {
            _elapsedItem,
            _lapTimeItem
        };

        // Actions section
        items.Add(new GroupHeaderListItem("Actions"));

        switch (_stopwatch.State)
        {
            case StopwatchState.Pending:
                items.Add(new ListItem(new StartStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Start",
                    Icon = Icons.StartStopwatch
                });
                break;

            case StopwatchState.Running:
                items.Add(new ListItem(new LapStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Lap",
                    Icon = Icons.Lap
                });
                items.Add(new ListItem(new PauseStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Pause",
                    Icon = Icons.PauseStopwatch
                });
                items.Add(new ListItem(new StopStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Stop",
                    Icon = Icons.StopStopwatch
                });
                break;

            case StopwatchState.Paused:
                items.Add(new ListItem(new ResumeStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Resume",
                    Icon = Icons.StartStopwatch
                });
                items.Add(new ListItem(new StopStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Stop",
                    Icon = Icons.StopStopwatch
                });
                items.Add(new ListItem(new ResetStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Reset",
                    Icon = Icons.ResetStopwatch
                });
                break;

            case StopwatchState.Stopped:
                items.Add(new ListItem(new ResetStopwatchCommand(_stopwatch.Id, _scheduler))
                {
                    Title = "Reset",
                    Icon = Icons.ResetStopwatch
                });
                break;
        }

        items.Add(new ListItem(new DeleteStopwatchCommand(_stopwatch.Id, _scheduler))
        {
            Title = "Delete",
            Icon = Icons.Delete
        });

        // Laps section
        var laps = _stopwatch.Laps;
        if (laps.Count > 0)
        {
            items.Add(new GroupHeaderListItem("Laps"));

            int fastestIndex = -1;
            int slowestIndex = -1;

            if (laps.Count >= 2)
            {
                var fastestTime = TimeSpan.MaxValue;
                var slowestTime = TimeSpan.MinValue;

                for (var i = 0; i < laps.Count; i++)
                {
                    var lapTime = laps[i].LapTime;
                    if (lapTime < fastestTime)
                    {
                        fastestTime = lapTime;
                        fastestIndex = i;
                    }
                    if (lapTime > slowestTime)
                    {
                        slowestTime = lapTime;
                        slowestIndex = i;
                    }
                }

                if (fastestTime == slowestTime)
                {
                    fastestIndex = -1;
                    slowestIndex = -1;
                }
            }

            for (var i = laps.Count - 1; i >= 0; i--)
            {
                var lap = laps[i];
                var tags = new List<Tag>();

                if (i == fastestIndex)
                {
                    tags.Add(new Tag("Fastest"));
                }
                if (i == slowestIndex)
                {
                    tags.Add(new Tag("Slowest"));
                }

                items.Add(new ListItem
                {
                    Title = $"Lap {lap.Number}",
                    Subtitle = $"{FormatTime(lap.LapTime)} (Total: {FormatTime(lap.TotalTime)})",
                    Icon = Icons.Lap,
                    Tags = [.. tags]
                });
            }
        }

        return [.. items];
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
        }
        return $"{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
    }
}