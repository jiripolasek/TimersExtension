// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using System.Threading;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Services;

public sealed partial class StopwatchScheduler : IDisposable
{
    public event EventHandler? StopwatchesChanged;
    public event EventHandler<LapRecordedEventArgs>? LapRecorded;
    internal event EventHandler? StopwatchesRemoved;
    private static readonly TimeSpan UIUpdateInterval = TimeSpan.FromMilliseconds(50);

    private readonly StopwatchStorage _storage;
    private readonly Lock _initializationGate = new();
    private readonly Lock _gate = new();
    private readonly NotificationQueue _notifications;
    private readonly Dictionary<Guid, StopwatchInstance> _stopwatches = [];
    private readonly SchedulerRefreshTimer _uiUpdateTimer;
    private volatile bool _initialized;
    private bool _disposed;

    public StopwatchScheduler(StopwatchStorage storage)
        : this(storage, TimeProvider.System)
    {
    }

    internal StopwatchScheduler(StopwatchStorage storage, TimeProvider timeProvider)
    {
        this._storage = storage;
        this._notifications = new NotificationQueue(this._gate);

        // Keep the centisecond display fluid without attempting a wasteful 100 Hz COM update rate.
        // Internal precision remains based on DateTimeOffset.
        this._uiUpdateTimer = new SchedulerRefreshTimer(timeProvider, UIUpdateInterval, this.RefreshRunningStopwatches);
    }

    private void RefreshRunningStopwatches()
    {
        StopwatchInstance[] running;
        lock (this._gate)
        {
            if (this._disposed)
            {
                return;
            }

            running = this._stopwatches.Values.Where(static stopwatch => stopwatch.State == StopwatchState.Running)
                .ToArray();
        }

        foreach (var stopwatch in running)
        {
            stopwatch.NotifyTimeChanged();
        }
    }

    private void UpdateRefreshTimer() =>
        this._uiUpdateTimer.SetEnabled(this._stopwatches.Values.Any(static stopwatch =>
            stopwatch.State == StopwatchState.Running));

    public void Initialize()
    {
        lock (this._initializationGate)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            if (this._initialized)
            {
                return;
            }

            var loaded = this._storage.Load();
            var isReadOnly = this._storage.IsReadOnly;
            var restoredAt = DateTimeOffset.UtcNow;
            lock (this._gate)
            {
                var restoredStateChanged = false;
                foreach (var stopwatch in loaded)
                {
                    restoredStateChanged |= stopwatch.NormalizeAfterRestore();
                    if (isReadOnly && stopwatch.State == StopwatchState.Running)
                    {
                        stopwatch.TransitionToPaused(restoredAt, stopwatch.Elapsed, false);
                    }

                    this._stopwatches[stopwatch.Id] = stopwatch;
                }

                if (restoredStateChanged)
                {
                    this.Persist();
                }

                this.UpdateRefreshTimer();
                this._initialized = true;
            }
        }

        NotificationQueue.Raise(this.StopwatchesChanged, this);
    }

    public IReadOnlyList<StopwatchInstance> GetStopwatchesSnapshot()
    {
        lock (this._gate)
        {
            List<StopwatchInstance> list = [.. this._stopwatches.Values];
            list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return list;
        }
    }

    public StopwatchInstance? TryGetStopwatch(Guid stopwatchId)
    {
        lock (this._gate)
        {
            return this._stopwatches.GetValueOrDefault(stopwatchId);
        }
    }

    public StopwatchInstance Create(string? name = null)
    {
        this.EnsureInitialized();

        var instance = new StopwatchInstance
        {
            Id = Guid.NewGuid(),
            Name = name ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            State = StopwatchState.Pending
        };

        lock (this._gate)
        {
            this._stopwatches[instance.Id] = instance;
            this.Persist();
        }

        NotificationQueue.Raise(this.StopwatchesChanged, this);
        return instance;
    }

    public StopwatchInstance CreateAndStart(string? name = null)
    {
        this.EnsureInitialized();

        var now = DateTimeOffset.UtcNow;
        var instance = new StopwatchInstance { Id = Guid.NewGuid(), Name = name ?? string.Empty, CreatedAt = now };
        instance.TransitionToRunning(now);

        lock (this._gate)
        {
            this._stopwatches[instance.Id] = instance;
            this._uiUpdateTimer.SetEnabled(true);
            this.Persist();
        }

        NotificationQueue.Raise(this.StopwatchesChanged, this);
        return instance;
    }

    public void Start(Guid stopwatchId)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;

        lock (this._gate)
        {
            if (!this._stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            if (stopwatch.State is not (StopwatchState.Pending or StopwatchState.Paused))
            {
                return;
            }

            stopwatch.TransitionToRunning(now, false);
            this._uiUpdateTimer.SetEnabled(true);
            this.Persist();
            this._notifications.Enqueue(stopwatch.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.StopwatchesChanged, this);
    }

    public void Pause(Guid stopwatchId)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;

        lock (this._gate)
        {
            if (!this._stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            if (stopwatch.State != StopwatchState.Running)
            {
                return;
            }

            var elapsed = stopwatch.Elapsed;
            stopwatch.TransitionToPaused(now, elapsed, false);
            this.UpdateRefreshTimer();
            this.Persist();
            this._notifications.Enqueue(stopwatch.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.StopwatchesChanged, this);
    }

    public void Resume(Guid stopwatchId)
    {
        this.EnsureInitialized();
        this.Start(stopwatchId); // Same logic as Start
    }

    public void Stop(Guid stopwatchId)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;

        lock (this._gate)
        {
            if (!this._stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            if (stopwatch.State is StopwatchState.Pending or StopwatchState.Stopped)
            {
                return;
            }

            var elapsed = stopwatch.Elapsed;
            stopwatch.TransitionToStopped(now, elapsed, false);
            this.UpdateRefreshTimer();
            this.Persist();
            this._notifications.Enqueue(stopwatch.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.StopwatchesChanged, this);
    }

    public void StopAll()
    {
        this.EnsureInitialized();
        var changed = false;
        var now = DateTimeOffset.UtcNow;

        lock (this._gate)
        {
            foreach (var stopwatch in this._stopwatches.Values)
            {
                if (stopwatch.State is not (StopwatchState.Running or StopwatchState.Paused))
                {
                    continue;
                }

                var elapsed = stopwatch.Elapsed;
                stopwatch.TransitionToStopped(now, elapsed, false);
                this._notifications.Enqueue(stopwatch.NotifyStateChanged);
                changed = true;
            }

            if (changed)
            {
                this._uiUpdateTimer.SetEnabled(false);
                this.Persist();
            }
        }

        if (changed)
        {
            this._notifications.Drain();
            NotificationQueue.Raise(this.StopwatchesChanged, this);
        }
    }

    public void Reset(Guid stopwatchId)
    {
        this.EnsureInitialized();

        lock (this._gate)
        {
            if (!this._stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            stopwatch.TransitionToReset(false);
            this.UpdateRefreshTimer();
            this.Persist();
            this._notifications.Enqueue(stopwatch.NotifyStateChanged);
            this._notifications.Enqueue(stopwatch.NotifyLapsChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.StopwatchesChanged, this);
    }

    public LapRecord? RecordLap(Guid stopwatchId)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        LapRecord? lap = null;
        StopwatchInstance? stopwatch = null;

        lock (this._gate)
        {
            if (!this._stopwatches.TryGetValue(stopwatchId, out stopwatch))
            {
                return null;
            }

            if (stopwatch.State != StopwatchState.Running)
            {
                return null;
            }

            lap = stopwatch.RecordLap(now, false);
            this.Persist();
            this._notifications.Enqueue(stopwatch.NotifyLapsChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.StopwatchesChanged, this);

        if (lap is not null && stopwatch is not null)
        {
            this.LapRecorded?.Invoke(this, new LapRecordedEventArgs(stopwatch, lap));
        }

        return lap;
    }

    public void Remove(Guid stopwatchId)
    {
        this.EnsureInitialized();

        lock (this._gate)
        {
            if (!this._stopwatches.Remove(stopwatchId))
            {
                return;
            }

            this.UpdateRefreshTimer();
            this.Persist();
        }

        NotificationQueue.Raise(this.StopwatchesChanged, this);
        NotificationQueue.Raise(this.StopwatchesRemoved, this);
    }

    public void ClearAll()
    {
        this.EnsureInitialized();

        lock (this._gate)
        {
            if (this._stopwatches.Count == 0)
            {
                return;
            }

            this._stopwatches.Clear();
            this._uiUpdateTimer.SetEnabled(false);
            this.Persist();
        }

        NotificationQueue.Raise(this.StopwatchesChanged, this);
        NotificationQueue.Raise(this.StopwatchesRemoved, this);
    }

    private void Persist()
    {
        List<StopwatchInstance> stopwatches;
        lock (this._gate)
        {
            // Materialize immutable-in-practice snapshots while holding the scheduler
            // lock. JsonStorageService writes asynchronously, so passing the live
            // instances could otherwise persist fields from different state transitions.
            stopwatches = new List<StopwatchInstance>(this._stopwatches.Count);
            foreach (var stopwatch in this._stopwatches.Values)
            {
                stopwatches.Add(stopwatch.CreatePersistenceSnapshot());
            }
        }

        this._storage.Save(stopwatches);
    }

    private void EnsureInitialized()
    {
        if (!this._initialized)
        {
            this.Initialize();
        }
    }

    public void Dispose()
    {
        lock (this._initializationGate)
        {
            if (this._disposed)
            {
                return;
            }

            lock (this._gate)
            {
                this._disposed = true;
                this._uiUpdateTimer.Dispose();
            }
        }
    }
}

public sealed class LapRecordedEventArgs : EventArgs
{
    public StopwatchInstance Stopwatch { get; }
    public LapRecord Lap { get; }

    public LapRecordedEventArgs(StopwatchInstance stopwatch, LapRecord lap)
    {
        this.Stopwatch = stopwatch;
        this.Lap = lap;
    }
}