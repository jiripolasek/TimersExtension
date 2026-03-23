using System.Threading;
using System.Threading.Tasks;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Services;

public sealed class TimerScheduler : IDisposable
{
    private readonly TimerStorage _storage;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, TimerInstance> _timers = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _runningTokens = [];
    private readonly Timer _uiUpdateTimer;
    private List<TimerDefinition> _templates = [];
    private bool _initialized;

    public event EventHandler? TimersChanged;
    public event EventHandler? TemplatesChanged;
    public event EventHandler<TimerFinishedEventArgs>? TimerFinished;

    public TimerScheduler(TimerStorage storage)
    {
        _storage = storage;
        _uiUpdateTimer = new Timer(UIUpdateCallback, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void UIUpdateCallback(object? state)
    {
        lock (_gate)
        {
            foreach (var timer in _timers.Values)
            {
                if (timer.State == TimerState.Running)
                {
                    timer.NotifyTimeChanged();
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
            _templates = [.. loaded.templates];
            foreach (var timer in loaded.timers)
            {
                Normalize(timer);
                _timers[timer.Id] = timer;
                if (timer.State == TimerState.Running)
                {
                    Schedule(timer);
                }
            }
            _initialized = true;
        }

        TimersChanged?.Invoke(this, EventArgs.Empty);
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<TimerInstance> GetTimersSnapshot()
    {
        lock (_gate)
        {
            List<TimerInstance> list = [.. _timers.Values];
            list.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
            return list;
        }
    }

    public IReadOnlyList<TimerDefinition> GetTemplatesSnapshot()
    {
        lock (_gate)
        {
            return [.. _templates];
        }
    }

    public TimerInstance? TryGetTimer(Guid timerId)
    {
        lock (_gate)
        {
            return _timers.GetValueOrDefault(timerId);
        }
    }

    public TimerInstance StartTimer(TimerDefinition definition, bool saveAsTemplate = false)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        var instance = new TimerInstance
        {
            Id = Guid.NewGuid(),
            Name = definition.Name,
            Duration = definition.Duration,
            CreatedAt = now,
            StartedAt = now,
            State = TimerState.Running,
            IsTemplate = definition.IsTemplate || saveAsTemplate
        };

        lock (_gate)
        {
            _timers[instance.Id] = instance;
            if (instance.IsTemplate)
            {
                _templates.Add(new TimerDefinition(instance.Name, instance.Duration, true));
            }
            Schedule(instance);
        }

        Persist();
        TimersChanged?.Invoke(this, EventArgs.Empty);
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
        return instance;
    }

    public TimerInstance StartTimer(TimeSpan duration, string? name = null)
    {
        return StartTimer(new TimerDefinition(name ?? string.Empty, duration));
    }

    public void Pause(Guid timerId)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_timers.TryGetValue(timerId, out var timer) || timer.State != TimerState.Running)
            {
                return;
            }

            CancelScheduled(timerId);
            var elapsed = CalculateElapsed(timer, now);
            timer.TransitionToPaused(now, elapsed);
        }

        Persist();
        TimersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume(Guid timerId)
    {
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_timers.TryGetValue(timerId, out var timer) || timer.State != TimerState.Paused)
            {
                return;
            }

            timer.TransitionToRunning(now);
            Schedule(timer);
        }

        Persist();
        TimersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop(Guid timerId)
    {
        EnsureInitialized();
        lock (_gate)
        {
            if (!_timers.TryGetValue(timerId, out var timer))
            {
                return;
            }

            CancelScheduled(timerId);
            timer.TransitionToCancelled(DateTimeOffset.UtcNow);
        }

        Persist();
        TimersChanged?.Invoke(this, EventArgs.Empty);
    }

    public TimerInstance? Restart(Guid timerId)
    {
        EnsureInitialized();
        TimerInstance? updated;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_timers.TryGetValue(timerId, out var timer))
            {
                return null;
            }

            CancelScheduled(timerId);
            timer.TransitionToRestarted(now);
            updated = timer;
            Schedule(updated);
        }

        Persist();
        TimersChanged?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    public TimerInstance RestartOrCreate(Guid timerId, TimeSpan fallbackDuration, string? fallbackName = null)
    {
        EnsureInitialized();
        return Restart(timerId) ?? StartTimer(fallbackDuration, fallbackName);
    }

    public void AddTemplate(TimerDefinition definition)
    {
        EnsureInitialized();
        lock (_gate)
        {
            _templates.Add(definition with { IsTemplate = true });
        }

        PersistTemplates();
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveTemplate(string name)
    {
        EnsureInitialized();
        lock (_gate)
        {
            var filtered = new List<TimerDefinition>(_templates.Count);
            foreach (var template in _templates)
            {
                if (!string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(template);
                }
            }
            _templates = filtered;
        }

        PersistTemplates();
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveTimer(Guid timerId)
    {
        EnsureInitialized();
        lock (_gate)
        {
            if (!_timers.ContainsKey(timerId))
            {
                return;
            }

            CancelScheduled(timerId);
            _timers.Remove(timerId);
        }

        Persist();
        TimersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Normalize(TimerInstance timer)
    {
        if (timer is { State: TimerState.Running, StartedAt: not null } && timer.Remaining <= TimeSpan.Zero)
        {
            timer.TransitionToFinished(DateTimeOffset.UtcNow, timer.Duration);
        }
    }

    private static TimeSpan CalculateElapsed(TimerInstance timer, DateTimeOffset now)
    {
        if (timer is { State: TimerState.Running, StartedAt: { } started })
        {
            return (now - started) + timer.ElapsedBeforePause;
        }
        return timer.ElapsedBeforePause;
    }

    private void Schedule(TimerInstance timer)
    {
        CancelScheduled(timer.Id);
        if (timer.State != TimerState.Running)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _runningTokens[timer.Id] = cts;
        var token = cts.Token;
        var remaining = timer.Remaining;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(remaining, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            TimerInstance? finished;
            lock (_gate)
            {
                if (!_timers.TryGetValue(timer.Id, out var current) || token.IsCancellationRequested)
                {
                    return;
                }

                current.TransitionToFinished(DateTimeOffset.UtcNow, current.Duration);
                _runningTokens.Remove(timer.Id);
                finished = current;
            }

            Persist();
            TimersChanged?.Invoke(this, EventArgs.Empty);
            TimerFinished?.Invoke(this, new TimerFinishedEventArgs(finished));
        });
    }

    private void CancelScheduled(Guid timerId)
    {
        if (_runningTokens.Remove(timerId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void Persist()
    {
        List<TimerInstance> timers;
        List<TimerDefinition> templates;
        lock (_gate)
        {
            timers = [.. _timers.Values];
            templates = [.. _templates];
        }

        _storage.SaveTimers(timers);
        _storage.SaveTemplates(templates);
    }

    private void PersistTemplates()
    {
        List<TimerDefinition> templates;
        lock (_gate)
        {
            templates = [.. _templates];
        }

        _storage.SaveTemplates(templates);
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
        lock (_gate)
        {
            foreach (var cts in _runningTokens.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _runningTokens.Clear();
        }
    }
}