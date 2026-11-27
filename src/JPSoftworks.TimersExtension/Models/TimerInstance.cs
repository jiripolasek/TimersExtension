// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Models;

public sealed partial class TimerInstance : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _name = string.Empty;
    private TimeSpan _duration;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedAt;
    private TimeSpan _elapsedBeforePause;
    private TimerState _state = TimerState.Pending;
    private bool _isTemplate;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => this._name;
        set => this.SetProperty(ref this._name, value);
    }

    public TimeSpan Duration
    {
        get => this._duration;
        set
        {
            if (this.SetProperty(ref this._duration, value))
            {
                this.OnPropertyChanged(nameof(this.Remaining));
            }
        }
    }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal TimeSpan? SnoozeDuration { get; set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal TimeSpan? RunDuration { get; set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? AlarmSoundId { get; set; }

    internal TimeSpan CountdownDuration => this.RunDuration ?? this.SnoozeDuration ?? this.Duration;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt
    {
        get => this._startedAt;
        set
        {
            if (this.SetProperty(ref this._startedAt, value))
            {
                this.OnPropertyChanged(nameof(this.ExpectedEnd));
                this.OnPropertyChanged(nameof(this.Remaining));
            }
        }
    }

    public DateTimeOffset? PausedAt
    {
        get => this._pausedAt;
        set => this.SetProperty(ref this._pausedAt, value);
    }

    public TimeSpan ElapsedBeforePause
    {
        get => this._elapsedBeforePause;
        set
        {
            if (this.SetProperty(ref this._elapsedBeforePause, value))
            {
                this.OnPropertyChanged(nameof(this.ExpectedEnd));
                this.OnPropertyChanged(nameof(this.Remaining));
            }
        }
    }

    public TimerState State
    {
        get => this._state;
        set
        {
            if (this.SetProperty(ref this._state, value))
            {
                this.OnPropertyChanged(nameof(this.ExpectedEnd));
                this.OnPropertyChanged(nameof(this.Remaining));
            }
        }
    }

    public bool IsTemplate
    {
        get => this._isTemplate;
        set => this.SetProperty(ref this._isTemplate, value);
    }

    public DateTimeOffset? ExpectedEnd
    {
        get
        {
            if (this.State != TimerState.Running || this.StartedAt is not { } start)
            {
                return null;
            }

            try
            {
                return start + (this.CountdownDuration - this.ElapsedBeforePause);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
            {
                // Keep older out-of-range timers readable and serializable.
                return null;
            }
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            if (this.State == TimerState.Finished)
            {
                return TimeSpan.Zero;
            }

            if (this.State == TimerState.Pending)
            {
                return this.Duration;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = this.State switch
            {
                TimerState.Running when this.StartedAt is { } started => now - started + this.ElapsedBeforePause,
                _ => this.ElapsedBeforePause
            };

            var remaining = this.CountdownDuration - elapsed;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    internal static bool IsValidDuration(TimeSpan duration, DateTimeOffset start)
    {
        return duration > TimeSpan.Zero
               && duration.Ticks <= DateTime.MaxValue.Ticks - Math.Max(start.Ticks, start.UtcTicks);
    }

    // Helper to trigger logic dependent on time passing
    public void NotifyTimeChanged()
    {
        this.OnPropertyChanged(nameof(this.Remaining));
    }

    public void TransitionToPaused(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause) =>
        this.TransitionToPaused(pausedAt, elapsedBeforePause, true);

    internal void TransitionToPaused(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause, bool notify)
    {
        this._state = TimerState.Paused;
        this._pausedAt = pausedAt;
        this._elapsedBeforePause = elapsedBeforePause;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToRunning(DateTimeOffset startedAt) => this.TransitionToRunning(startedAt, true);

    internal void TransitionToRunning(DateTimeOffset startedAt, bool notify)
    {
        this._state = TimerState.Running;
        this._startedAt = startedAt;
        this._pausedAt = null;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToRestarted(DateTimeOffset startedAt) => this.TransitionToRestarted(startedAt, true);

    internal void TransitionToRestarted(DateTimeOffset startedAt, bool notify, TimeSpan? snoozeDuration = null)
    {
        this._state = TimerState.Running;
        this._startedAt = startedAt;
        this._pausedAt = null;
        this._elapsedBeforePause = TimeSpan.Zero;
        this.SnoozeDuration = snoozeDuration;
        this.RunDuration = null;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToCancelled(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause) =>
        this.TransitionToCancelled(pausedAt, elapsedBeforePause, true);

    internal void TransitionToCancelled(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause, bool notify)
    {
        this._state = TimerState.Cancelled;
        this._pausedAt = pausedAt;
        this._elapsedBeforePause = elapsedBeforePause;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToFinished(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause) =>
        this.TransitionToFinished(pausedAt, elapsedBeforePause, true);

    internal void TransitionToFinished(DateTimeOffset pausedAt, TimeSpan elapsedBeforePause, bool notify)
    {
        this._state = TimerState.Finished;
        this._pausedAt = pausedAt;
        this._elapsedBeforePause = elapsedBeforePause;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    internal TimerInstance CreatePersistenceSnapshot() => new()
    {
        Id = this.Id,
        Name = this._name,
        Duration = this._duration,
        SnoozeDuration = this.SnoozeDuration,
        RunDuration = this.RunDuration,
        AlarmSoundId = this.AlarmSoundId,
        CreatedAt = this.CreatedAt,
        StartedAt = this._startedAt,
        PausedAt = this._pausedAt,
        ElapsedBeforePause = this._elapsedBeforePause,
        State = this._state,
        IsTemplate = this._isTemplate
    };

    internal void NotifyStateChanged()
    {
        this.OnPropertyChanged(nameof(this.State));
        this.OnPropertyChanged(nameof(this.StartedAt));
        this.OnPropertyChanged(nameof(this.PausedAt));
        this.OnPropertyChanged(nameof(this.ElapsedBeforePause));
        this.NotifyCountdownDurationChanged();
    }

    internal void NotifyCountdownDurationChanged()
    {
        this.OnPropertyChanged(nameof(this.CountdownDuration));
        this.OnPropertyChanged(nameof(this.ExpectedEnd));
        this.OnPropertyChanged(nameof(this.Remaining));
    }

    internal void NotifyAlarmSoundChanged() => this.OnPropertyChanged(nameof(this.AlarmSoundId));

    private void OnPropertyChanged(string propertyName) =>
        NotificationQueue.RaisePropertyChanged(this.PropertyChanged, this, propertyName);

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(propertyName!);
        return true;
    }
}