using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JPSoftworks.TimersExtension.Models;

public sealed class StopwatchInstance : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private TimeSpan _elapsedBeforePause;
    private StopwatchState _state = StopwatchState.Pending;
    private List<LapRecord> _laps = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        set
        {
            if (SetProperty(ref _startedAt, value))
            {
                OnPropertyChanged(nameof(Elapsed));
            }
        }
    }

    public DateTimeOffset? StoppedAt
    {
        get => _stoppedAt;
        set => SetProperty(ref _stoppedAt, value);
    }

    public TimeSpan ElapsedBeforePause
    {
        get => _elapsedBeforePause;
        set
        {
            if (SetProperty(ref _elapsedBeforePause, value))
            {
                OnPropertyChanged(nameof(Elapsed));
            }
        }
    }

    public StopwatchState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(Elapsed));
            }
        }
    }

    public IReadOnlyList<LapRecord> Laps => _laps;

    public TimeSpan Elapsed
    {
        get
        {
            if (State == StopwatchState.Pending)
            {
                return TimeSpan.Zero;
            }

            if (State is StopwatchState.Paused or StopwatchState.Stopped)
            {
                return _elapsedBeforePause;
            }

            if (State == StopwatchState.Running && _startedAt is { } started)
            {
                return (DateTimeOffset.UtcNow - started) + _elapsedBeforePause;
            }

            return _elapsedBeforePause;
        }
    }

    public TimeSpan? CurrentLapTime
    {
        get
        {
            if (State != StopwatchState.Running)
            {
                return null;
            }

            var lastLapTotal = _laps.Count > 0 ? _laps[^1].TotalTime : TimeSpan.Zero;
            return Elapsed - lastLapTotal;
        }
    }

    public void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(CurrentLapTime));
    }

    public void TransitionToRunning(DateTimeOffset startedAt)
    {
        _state = StopwatchState.Running;
        _startedAt = startedAt;
        _stoppedAt = null;
        NotifyStateChanged();
    }

    public void TransitionToPaused(DateTimeOffset pausedAt, TimeSpan elapsed)
    {
        _state = StopwatchState.Paused;
        _stoppedAt = pausedAt;
        _elapsedBeforePause = elapsed;
        NotifyStateChanged();
    }

    public void TransitionToStopped(DateTimeOffset stoppedAt, TimeSpan elapsed)
    {
        _state = StopwatchState.Stopped;
        _stoppedAt = stoppedAt;
        _elapsedBeforePause = elapsed;
        NotifyStateChanged();
    }

    public void TransitionToReset()
    {
        _state = StopwatchState.Pending;
        _startedAt = null;
        _stoppedAt = null;
        _elapsedBeforePause = TimeSpan.Zero;
        _laps = [];
        NotifyStateChanged();
        OnPropertyChanged(nameof(Laps));
        OnPropertyChanged(nameof(CurrentLapTime));
    }

    public LapRecord RecordLap(DateTimeOffset recordedAt)
    {
        var totalTime = Elapsed;
        var lastLapTotal = _laps.Count > 0 ? _laps[^1].TotalTime : TimeSpan.Zero;
        var lapTime = totalTime - lastLapTotal;

        var lap = new LapRecord
        {
            Number = _laps.Count + 1,
            LapTime = lapTime,
            TotalTime = totalTime,
            RecordedAt = recordedAt
        };

        _laps.Add(lap);
        OnPropertyChanged(nameof(Laps));
        OnPropertyChanged(nameof(CurrentLapTime));
        return lap;
    }

    internal void SetLaps(List<LapRecord> laps)
    {
        _laps = laps;
        OnPropertyChanged(nameof(Laps));
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StartedAt));
        OnPropertyChanged(nameof(StoppedAt));
        OnPropertyChanged(nameof(ElapsedBeforePause));
        OnPropertyChanged(nameof(Elapsed));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName!);
        return true;
    }
}