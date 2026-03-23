using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JPSoftworks.TimersExtension.Models;

public sealed class TimerInstance : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private TimeSpan _duration;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedAt;
    private TimeSpan _elapsedBeforePause;
    private TimerState _state = TimerState.Pending;
    private bool _isTemplate;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(Remaining));
            }
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        set
        {
            if (SetProperty(ref _startedAt, value))
            {
                OnPropertyChanged(nameof(ExpectedEnd));
                OnPropertyChanged(nameof(Remaining));
            }
        }
    }

    public DateTimeOffset? PausedAt
    {
        get => _pausedAt;
        set => SetProperty(ref _pausedAt, value);
    }

    public TimeSpan ElapsedBeforePause
    {
        get => _elapsedBeforePause;
        set
        {
            if (SetProperty(ref _elapsedBeforePause, value))
            {
                OnPropertyChanged(nameof(ExpectedEnd));
                OnPropertyChanged(nameof(Remaining));
            }
        }
    }

    public TimerState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(ExpectedEnd));
                OnPropertyChanged(nameof(Remaining));
            }
        }
    }

    public bool IsTemplate
    {
        get => _isTemplate;
        set => SetProperty(ref _isTemplate, value);
    }

    public DateTimeOffset? ExpectedEnd => State == TimerState.Running && StartedAt is { } start
        ? start + Duration - ElapsedBeforePause
        : null;

    public TimeSpan Remaining
    {
        get
        {
            if (State == TimerState.Finished)
            {
                return TimeSpan.Zero;
            }

            if (State == TimerState.Pending)
            {
                return Duration;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = State switch
            {
                TimerState.Running when StartedAt is { } started => (now - started) + ElapsedBeforePause,
                _ => ElapsedBeforePause
            };

            var remaining = Duration - elapsed;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    // Helper to trigger logic dependent on time passing
    public void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(Remaining));
    }

    public void TransitionToPaused(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause)
    {
        _state = TimerState.Paused;
        _pausedAt = pausedAt;
        _elapsedBeforePause = elapsedBeforePause;
        NotifyStateChanged();
    }

    public void TransitionToRunning(DateTimeOffset startedAt)
    {
        _state = TimerState.Running;
        _startedAt = startedAt;
        _pausedAt = null;
        NotifyStateChanged();
    }

    public void TransitionToRestarted(DateTimeOffset startedAt)
    {
        _state = TimerState.Running;
        _startedAt = startedAt;
        _pausedAt = null;
        _elapsedBeforePause = TimeSpan.Zero;
        NotifyStateChanged();
    }

    public void TransitionToCancelled(DateTimeOffset pausedAt)
    {
        _state = TimerState.Cancelled;
        _pausedAt = pausedAt;
        NotifyStateChanged();
    }

    public void TransitionToFinished(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause)
    {
        _state = TimerState.Finished;
        _pausedAt = pausedAt;
        _elapsedBeforePause = elapsedBeforePause;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StartedAt));
        OnPropertyChanged(nameof(PausedAt));
        OnPropertyChanged(nameof(ElapsedBeforePause));
        OnPropertyChanged(nameof(ExpectedEnd));
        OnPropertyChanged(nameof(Remaining));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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