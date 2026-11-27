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

public sealed partial class StopwatchInstance : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _name = string.Empty;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private TimeSpan _elapsedBeforePause;
    private StopwatchState _state = StopwatchState.Pending;
    private volatile LapRecord[] _laps = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name
    {
        get => this._name;
        set => this.SetProperty(ref this._name, value);
    }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal DateTimeOffset? RunStartedAt { get; set; }


    public DateTimeOffset? StartedAt
    {
        get => this._startedAt;
        set
        {
            if (this.SetProperty(ref this._startedAt, value))
            {
                this.OnPropertyChanged(nameof(this.Elapsed));
            }
        }
    }

    public DateTimeOffset? StoppedAt
    {
        get => this._stoppedAt;
        set => this.SetProperty(ref this._stoppedAt, value);
    }

    public TimeSpan ElapsedBeforePause
    {
        get => this._elapsedBeforePause;
        set
        {
            if (this.SetProperty(ref this._elapsedBeforePause, value))
            {
                this.OnPropertyChanged(nameof(this.Elapsed));
            }
        }
    }

    public StopwatchState State
    {
        get => this._state;
        set
        {
            if (this.SetProperty(ref this._state, value))
            {
                this.OnPropertyChanged(nameof(this.Elapsed));
            }
        }
    }

    [JsonInclude]
    public IReadOnlyList<LapRecord> Laps
    {
        get => this._laps;
        internal set
        {
            this._laps = value is null ? [] : [.. value];
            this.OnPropertyChanged(nameof(this.Laps));
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            if (this.State == StopwatchState.Pending)
            {
                return TimeSpan.Zero;
            }

            if (this.State is StopwatchState.Paused or StopwatchState.Stopped)
            {
                return this._elapsedBeforePause;
            }

            if (this.State == StopwatchState.Running && this._startedAt is { } started)
            {
                return DateTimeOffset.UtcNow - started + this._elapsedBeforePause;
            }

            return this._elapsedBeforePause;
        }
    }

    public TimeSpan? CurrentLapTime
    {
        get
        {
            if (this.State is not (StopwatchState.Running or StopwatchState.Paused))
            {
                return null;
            }

            var laps = this._laps;
            var lastLap = laps.Length > 0 ? laps[^1] : null;
            var currentLap = this.Elapsed - (lastLap?.TotalTime ?? TimeSpan.Zero);
            return currentLap < TimeSpan.Zero ? TimeSpan.Zero : currentLap;
        }
    }

    public void NotifyTimeChanged()
    {
        this.OnPropertyChanged(nameof(this.Elapsed));
        this.OnPropertyChanged(nameof(this.CurrentLapTime));
    }

    public void TransitionToRunning(DateTimeOffset startedAt) => this.TransitionToRunning(startedAt, true);

    internal void TransitionToRunning(DateTimeOffset startedAt, bool notify)
    {
        if (this._state == StopwatchState.Pending)
        {
            this.RunStartedAt = startedAt;
        }

        this._state = StopwatchState.Running;
        this._startedAt = startedAt;
        this._stoppedAt = null;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToPaused(DateTimeOffset pausedAt, TimeSpan elapsed) =>
        this.TransitionToPaused(pausedAt, elapsed, true);

    internal void TransitionToPaused(DateTimeOffset pausedAt, TimeSpan elapsed, bool notify)
    {
        this._state = StopwatchState.Paused;
        this._stoppedAt = pausedAt;
        this._elapsedBeforePause = elapsed;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToStopped(DateTimeOffset stoppedAt, TimeSpan elapsed) =>
        this.TransitionToStopped(stoppedAt, elapsed, true);

    internal void TransitionToStopped(DateTimeOffset stoppedAt, TimeSpan elapsed, bool notify)
    {
        this._state = StopwatchState.Stopped;
        this._stoppedAt = stoppedAt;
        this._elapsedBeforePause = elapsed;
        if (notify)
        {
            this.NotifyStateChanged();
        }
    }

    public void TransitionToReset() => this.TransitionToReset(true);

    internal void TransitionToReset(bool notify)
    {
        this._state = StopwatchState.Pending;
        this.RunStartedAt = null;
        this._startedAt = null;
        this._stoppedAt = null;
        this._elapsedBeforePause = TimeSpan.Zero;
        this._laps = [];
        if (notify)
        {
            this.NotifyStateChanged();
            this.NotifyLapsChanged();
        }
    }

    public LapRecord RecordLap(DateTimeOffset recordedAt) => this.RecordLap(recordedAt, true);

    internal LapRecord RecordLap(DateTimeOffset recordedAt, bool notify)
    {
        var totalTime = this.Elapsed;
        var laps = this._laps;
        var lastLapTotal = laps.Length > 0 ? laps[^1].TotalTime : TimeSpan.Zero;
        var lapTime = totalTime - lastLapTotal;

        var lap = new LapRecord
        {
            Number = laps.Length + 1, LapTime = lapTime, TotalTime = totalTime, RecordedAt = recordedAt
        };

        // Readers and queued saves may still hold the previous lap array.
        this._laps = [.. laps, lap];
        if (notify)
        {
            this.NotifyLapsChanged();
        }

        return lap;
    }

    internal void NotifyLapsChanged()
    {
        this.OnPropertyChanged(nameof(this.Laps));
        this.OnPropertyChanged(nameof(this.CurrentLapTime));
    }

    internal bool NormalizeAfterRestore()
    {
        // A stopped transition records StoppedAt. If an asynchronous persistence
        // snapshot captured the old Running state alongside that terminal marker,
        // prefer the marker so the stopwatch cannot resume after process restart.
        if (this._state == StopwatchState.Running && this._stoppedAt is not null)
        {
            this._state = StopwatchState.Stopped;
            return true;
        }

        return false;
    }

    internal StopwatchInstance CreatePersistenceSnapshot()
    {
        return new StopwatchInstance
        {
            Id = this.Id,
            Name = this._name,
            CreatedAt = this.CreatedAt,
            RunStartedAt = this.RunStartedAt,
            StartedAt = this._startedAt,
            StoppedAt = this._stoppedAt,
            ElapsedBeforePause = this._elapsedBeforePause,
            State = this._state,
            _laps = this._laps
        };
    }

    internal void NotifyStateChanged()
    {
        this.OnPropertyChanged(nameof(this.State));
        this.OnPropertyChanged(nameof(this.StartedAt));
        this.OnPropertyChanged(nameof(this.StoppedAt));
        this.OnPropertyChanged(nameof(this.ElapsedBeforePause));
        this.OnPropertyChanged(nameof(this.Elapsed));
    }

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