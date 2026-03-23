using System.Threading;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Services;

public sealed class StopwatchScheduler : IDisposable
{
    private readonly StopwatchStorage _storage;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, StopwatchInstance> _stopwatches = [];
    private readonly Timer _uiUpdateTimer;
    private bool _initialized;

    public event EventHandler? StopwatchesChanged;
    public event EventHandler<LapRecordedEventArgs>? LapRecorded;

    public StopwatchScheduler(StopwatchStorage storage)
    {
        _storage = storage;

        // Update at 1s interval to avoid overwhelming COM RPC in CmdPal extension
        // Internal precision remains high (DateTimeOffset), only UI notifications are throttled
        _uiUpdateTimer = new Timer(UIUpdateCallback, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void UIUpdateCallback(object? state)
    {
        lock (_gate)
        {
            foreach (var stopwatch in _stopwatches.Values)
            {
                if (stopwatch.State == StopwatchState.Running)
                {
                    stopwatch.NotifyTimeChanged();
                }
            }
        }
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        var loaded = _storage.Load();

        lock (_gate)
        {
            foreach (var stopwatch in loaded)
            {
                _stopwatches[stopwatch.Id] = stopwatch;
            }
            _initialized = true;
        }

        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<StopwatchInstance> GetStopwatchesSnapshot()
    {
        lock (_gate)
        {
            List<StopwatchInstance> list = [.. _stopwatches.Values];
            list.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
            return list;
        }
    }

    public StopwatchInstance? TryGetStopwatch(Guid stopwatchId)
    {
        lock (_gate)
        {
            return _stopwatches.GetValueOrDefault(stopwatchId);
        }
    }

    public StopwatchInstance Create(string? name = null)
    {
        EnsureInitialized();

        var instance = new StopwatchInstance
        {
            Id = Guid.NewGuid(),
            Name = name ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            State = StopwatchState.Pending
        };

        lock (_gate)
        {
            _stopwatches[instance.Id] = instance;
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
        return instance;
    }

    public StopwatchInstance CreateAndStart(string? name = null)
    {
        EnsureInitialized();

        var now = DateTimeOffset.UtcNow;
        var instance = new StopwatchInstance
        {
            Id = Guid.NewGuid(),
            Name = name ?? string.Empty,
            CreatedAt = now
        };
        instance.TransitionToRunning(now);

        lock (_gate)
        {
            _stopwatches[instance.Id] = instance;
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
        return instance;
    }

    public void Start(Guid stopwatchId)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            if (stopwatch.State is not (StopwatchState.Pending or StopwatchState.Paused))
            {
                return;
            }

            stopwatch.TransitionToRunning(now);
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause(Guid stopwatchId)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            if (stopwatch.State != StopwatchState.Running)
            {
                return;
            }

            var elapsed = stopwatch.Elapsed;
            stopwatch.TransitionToPaused(now, elapsed);
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume(Guid stopwatchId)
    {
        EnsureInitialized();
        Start(stopwatchId); // Same logic as Start
    }

    public void Stop(Guid stopwatchId)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            if (stopwatch.State is StopwatchState.Pending or StopwatchState.Stopped)
            {
                return;
            }

            var elapsed = stopwatch.Elapsed;
            stopwatch.TransitionToStopped(now, elapsed);
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(Guid stopwatchId)
    {
        EnsureInitialized();

        lock (_gate)
        {
            if (!_stopwatches.TryGetValue(stopwatchId, out var stopwatch))
            {
                return;
            }

            stopwatch.TransitionToReset();
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    public LapRecord? RecordLap(Guid stopwatchId)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        LapRecord? lap = null;
        StopwatchInstance? stopwatch = null;

        lock (_gate)
        {
            if (!_stopwatches.TryGetValue(stopwatchId, out stopwatch))
            {
                return null;
            }

            if (stopwatch.State != StopwatchState.Running)
            {
                return null;
            }

            lap = stopwatch.RecordLap(now);
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);

        if (lap is not null && stopwatch is not null)
        {
            LapRecorded?.Invoke(this, new LapRecordedEventArgs(stopwatch, lap));
        }

        return lap;
    }

    public void Remove(Guid stopwatchId)
    {
        EnsureInitialized();

        lock (_gate)
        {
            if (!_stopwatches.Remove(stopwatchId))
            {
                return;
            }
        }

        Persist();
        StopwatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Persist()
    {
        List<StopwatchInstance> stopwatches;
        lock (_gate)
        {
            stopwatches = [.. _stopwatches.Values];
        }

        _storage.Save(stopwatches);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    public void Dispose()
    {
        _uiUpdateTimer.Dispose();
    }
}

public sealed class LapRecordedEventArgs : EventArgs
{
    public StopwatchInstance Stopwatch { get; }
    public LapRecord Lap { get; }

    public LapRecordedEventArgs(StopwatchInstance stopwatch, LapRecord lap)
    {
        Stopwatch = stopwatch;
        Lap = lap;
    }
}