// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Services;

public sealed partial class TimerScheduler : IDisposable
{
    public event EventHandler? TimersChanged;
    public event EventHandler? TemplatesChanged;
    public event EventHandler<TimerFinishedEventArgs>? TimerFinished;
    internal event EventHandler? TimersRemoved;
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(1);
    private static readonly TimeSpan UIUpdateInterval = TimeSpan.FromSeconds(1);

    private readonly TimerStorage _storage;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Lock _initializationGate = new();
    private readonly Lock _gate = new();
    private readonly NotificationQueue _notifications;
    private readonly Dictionary<Guid, TimerInstance> _timers = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _runningTokens = [];
    private readonly SchedulerRefreshTimer _uiUpdateTimer;
    private List<TimerDefinition> _templates = [];
    private volatile bool _initialized;
    private bool _disposed;

    public TimerScheduler(TimerStorage storage)
        : this(storage, Task.Delay)
    {
    }

    internal TimerScheduler(
        TimerStorage storage,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeProvider? timeProvider = null)
    {
        this._storage = storage;
        this._delay = delay;
        this._notifications = new NotificationQueue(this._gate);
        this._uiUpdateTimer = new SchedulerRefreshTimer(timeProvider ?? TimeProvider.System, UIUpdateInterval,
            this.RefreshRunningTimers);
    }

    private void RefreshRunningTimers()
    {
        TimerInstance[] running;
        List<TimerFinishedEventArgs>? overdue = null;
        lock (this._gate)
        {
            if (this._disposed)
            {
                return;
            }

            running = this._timers.Values.Where(static timer => timer.State == TimerState.Running).ToArray();
            var detectedAt = DateTimeOffset.UtcNow;
            foreach (var timer in running)
            {
                // A relative wait may still be pending after the wall-clock deadline passes during sleep.
                if (this._runningTokens.ContainsKey(timer.Id) &&
                    CalculateElapsed(timer, detectedAt) >= timer.CountdownDuration)
                {
                    (overdue ??= []).Add(this.CompleteScheduledTimer(timer, detectedAt));
                }
            }

            if (overdue is not null)
            {
                this.PersistTimers();
            }
        }

        if (overdue is not null)
        {
            this.PublishCompletions(overdue);
        }

        foreach (var timer in running)
        {
            if (timer.State == TimerState.Running)
            {
                timer.NotifyTimeChanged();
            }
        }
    }

    public void Initialize()
    {
        List<TimerFinishedEventArgs> expired = [];

        lock (this._initializationGate)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            if (this._initialized)
            {
                return;
            }

            var loaded = this._storage.Load();
            var timersAreReadOnly = this._storage.TimersAreReadOnly;
            var restoredAt = DateTimeOffset.UtcNow;
            lock (this._gate)
            {
                var templatesChanged = false;
                this._templates = [];
                foreach (var template in loaded.templates)
                {
                    var normalizedTemplate = NormalizeTemplate(template);
                    templatesChanged |= normalizedTemplate != template;
                    this._templates.Add(normalizedTemplate);
                }

                foreach (var timer in loaded.timers)
                {
                    this._timers[timer.Id] = timer;
                    if (timer.State != TimerState.Running)
                    {
                        continue;
                    }

                    var elapsed = CalculateElapsed(timer, restoredAt);
                    if (timer.StartedAt is not null && elapsed >= timer.CountdownDuration)
                    {
                        var completion = MarkFinished(timer, restoredAt);
                        if (!timersAreReadOnly)
                        {
                            expired.Add(completion);
                        }
                    }
                    else if (timersAreReadOnly)
                    {
                        // The primary file may contain a newer state for this timer.
                        timer.TransitionToPaused(restoredAt, elapsed, false);
                    }
                    else
                    {
                        this.Schedule(timer);
                    }
                }

                if (expired.Count > 0)
                {
                    this.PersistTimers();
                }

                if (templatesChanged)
                {
                    this.PersistTemplates();
                }

                this._initialized = true;
            }
        }

        NotificationQueue.Raise(this.TimersChanged, this);
        NotificationQueue.Raise(this.TemplatesChanged, this);
        foreach (var completion in expired)
        {
            this.NotifyTimerFinished(completion);
        }
    }

    public IReadOnlyList<TimerInstance> GetTimersSnapshot()
    {
        lock (this._gate)
        {
            List<TimerInstance> list = [.. this._timers.Values];
            list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return list;
        }
    }

    internal TimerInstance? GetLastStartedTimer()
    {
        lock (this._gate)
        {
            return this._timers.Values.MaxBy(static timer => (timer.StartedAt ?? timer.CreatedAt, timer.CreatedAt));
        }
    }

    public IReadOnlyList<TimerDefinition> GetTemplatesSnapshot()
    {
        lock (this._gate)
        {
            return new List<TimerDefinition>(this._templates);
        }
    }

    internal TimerDefinition? TryGetTemplate(Guid templateId)
    {
        lock (this._gate)
        {
            return this._templates.Find(template => template.Id == templateId);
        }
    }

    public TimerInstance? TryGetTimer(Guid timerId)
    {
        lock (this._gate)
        {
            return this._timers.GetValueOrDefault(timerId);
        }
    }

    public TimerInstance StartTimer(TimerDefinition definition, bool saveAsTemplate = false) =>
        this.StartTimer(definition, saveAsTemplate, null);

    private TimerInstance StartTimer(TimerDefinition definition, bool saveAsTemplate, TimeSpan? snoozeDuration)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        if (!TimerInstance.IsValidDuration(definition.Duration, now))
        {
            throw new ArgumentOutOfRangeException(nameof(definition),
                "The duration must be positive and produce a representable end time.");
        }

        var instance = new TimerInstance
        {
            Id = Guid.NewGuid(),
            Name = definition.Name?.Trim() ?? string.Empty,
            Duration = definition.Duration,
            SnoozeDuration = snoozeDuration,
            AlarmSoundId = AlarmSoundCatalog.Find(definition.AlarmSoundId)?.Id,
            CreatedAt = now,
            StartedAt = now,
            State = TimerState.Running,
            IsTemplate = definition.IsTemplate || saveAsTemplate
        };

        lock (this._gate)
        {
            this._timers[instance.Id] = instance;
            if (instance.IsTemplate)
            {
                this._templates.Add(NormalizeTemplate(new TimerDefinition(instance.Name, instance.Duration, true)
                {
                    AlarmSoundId = instance.AlarmSoundId
                }));
            }

            this.Schedule(instance);
            this.Persist();
        }

        NotificationQueue.Raise(this.TimersChanged, this);
        if (instance.IsTemplate)
        {
            NotificationQueue.Raise(this.TemplatesChanged, this);
        }

        return instance;
    }

    public TimerInstance StartTimer(TimeSpan duration, string? name = null)
    {
        return this.StartTimer(new TimerDefinition(name ?? string.Empty, duration));
    }

    internal void SetAlarmSound(Guid timerId, string? soundId)
    {
        this.EnsureInitialized();
        var selectedSoundId = AlarmSoundCatalog.Find(soundId)?.Id;
        lock (this._gate)
        {
            if (!this._timers.TryGetValue(timerId, out var timer) || timer.AlarmSoundId == selectedSoundId)
            {
                return;
            }

            timer.AlarmSoundId = selectedSoundId;
            this.PersistTimers();
            this._notifications.Enqueue(timer.NotifyAlarmSoundChanged);
        }

        this._notifications.Drain();
    }

    public void Pause(Guid timerId)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        lock (this._gate)
        {
            if (!this._timers.TryGetValue(timerId, out var timer) || timer.State != TimerState.Running)
            {
                return;
            }

            this.CancelScheduled(timerId);
            var elapsed = CalculateElapsed(timer, now);
            timer.TransitionToPaused(now, elapsed, false);
            this.Persist();
            this._notifications.Enqueue(timer.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
    }

    public void Resume(Guid timerId)
    {
        this.EnsureInitialized();
        var now = DateTimeOffset.UtcNow;
        lock (this._gate)
        {
            if (!this._timers.TryGetValue(timerId, out var timer) || timer.State != TimerState.Paused)
            {
                return;
            }

            timer.TransitionToRunning(now, false);
            this.Schedule(timer);
            this.Persist();
            this._notifications.Enqueue(timer.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
    }

    public void Stop(Guid timerId)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            if (!this._timers.TryGetValue(timerId, out var timer) ||
                timer.State is not (TimerState.Running or TimerState.Paused))
            {
                return;
            }

            this.CancelCountdown(timer, DateTimeOffset.UtcNow);
            this.Persist();
            this._notifications.Enqueue(timer.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
    }

    internal TimerInstance? GetNextRunningTimer()
    {
        lock (this._gate)
        {
            return this.FindNextRunningTimer(DateTimeOffset.UtcNow);
        }
    }

    internal TimerInstance? StopNextRunningTimer()
    {
        this.EnsureInitialized();
        TimerInstance? next;
        lock (this._gate)
        {
            var now = DateTimeOffset.UtcNow;
            next = this.FindNextRunningTimer(now);
            if (next is null)
            {
                return null;
            }

            this.CancelCountdown(next, now);
            this.PersistTimers();
            this._notifications.Enqueue(next.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
        return next;
    }

    private TimerInstance? FindNextRunningTimer(DateTimeOffset now)
    {
        // Leave elapsed timers for the completion path so their alarms can still fire.
        return this._timers.Values.Where(timer => timer.ExpectedEnd > now).MinBy(static timer => timer.ExpectedEnd);
    }

    public void StopAll()
    {
        this.EnsureInitialized();
        var changed = false;
        var now = DateTimeOffset.UtcNow;

        lock (this._gate)
        {
            foreach (var timer in this._timers.Values)
            {
                if (timer.State is not (TimerState.Running or TimerState.Paused))
                {
                    continue;
                }

                this.CancelCountdown(timer, now);
                this._notifications.Enqueue(timer.NotifyStateChanged);
                changed = true;
            }

            if (changed)
            {
                this.Persist();
            }
        }

        if (changed)
        {
            this._notifications.Drain();
            NotificationQueue.Raise(this.TimersChanged, this);
        }
    }

    public TimerInstance? Restart(Guid timerId) => this.RestartCountdown(timerId, null);

    internal static bool CanExtend(TimerState state) =>
        state is TimerState.Running or TimerState.Paused or TimerState.Finished;

    internal bool TryExtend(Guid timerId, TimeSpan extra)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            if (extra <= TimeSpan.Zero || !this._timers.TryGetValue(timerId, out var timer) || !CanExtend(timer.State))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var expired = timer.State == TimerState.Finished || timer.ExpectedEnd <= now;
            TimeSpan duration;
            try
            {
                var elapsed = expired ? TimeSpan.Zero : CalculateElapsed(timer, now);
                var current = expired ? TimeSpan.Zero : timer.CountdownDuration;
                duration = (current > elapsed ? current : elapsed) + extra;
                if (!TimerInstance.IsValidDuration(duration - elapsed, now))
                {
                    return false;
                }
            }
            catch (OverflowException)
            {
                return false;
            }

            if (expired)
            {
                timer.TransitionToRestarted(now, false);
            }

            timer.RunDuration = duration;
            if (timer.State == TimerState.Running)
            {
                this.Schedule(timer);
            }

            this.PersistTimers();
            this._notifications.Enqueue(expired ? timer.NotifyStateChanged : timer.NotifyCountdownDurationChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
        return true;
    }

    private TimerInstance? RestartCountdown(Guid timerId, TimeSpan? snoozeDuration, DateTimeOffset? finishedAt = null)
    {
        this.EnsureInitialized();
        TimerInstance? updated;
        var now = DateTimeOffset.UtcNow;
        lock (this._gate)
        {
            if (!this._timers.TryGetValue(timerId, out var timer))
            {
                return null;
            }

            if (((snoozeDuration.HasValue || finishedAt.HasValue) && timer.State != TimerState.Finished)
                || (finishedAt.HasValue && timer.PausedAt != finishedAt))
            {
                return timer;
            }

            this.CancelScheduled(timerId);
            timer.TransitionToRestarted(now, false, snoozeDuration);
            updated = timer;
            this.Schedule(updated);
            this.Persist();
            this._notifications.Enqueue(timer.NotifyStateChanged);
        }

        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
        return updated;
    }

    public TimerInstance RestartOrCreate(Guid timerId, TimeSpan fallbackDuration, string? fallbackName = null)
    {
        this.EnsureInitialized();
        return this.Restart(timerId) ?? this.StartTimer(fallbackDuration, fallbackName);
    }

    internal void HandleAlarmClosed(AlarmResult result)
    {
        if (result.TimerId == Guid.Empty || result.Kind is not (AlarmClosedKind.Restart or AlarmClosedKind.Snooze))
        {
            return;
        }

        TimeSpan? snooze = result.Kind == AlarmClosedKind.Snooze ? AlarmService.SnoozeDuration : null;
        if (this.RestartCountdown(result.TimerId, snooze, result.FinishedAt) is null && result.FinishedAt is null)
        {
            var definition = new TimerDefinition(result.TimerName ?? string.Empty, result.OriginalDuration)
            {
                AlarmSoundId = result.AlarmSoundId
            };
            this.StartTimer(definition, false, snooze);
        }
    }

    internal bool IsCurrentCompletion(Guid timerId, DateTimeOffset? finishedAt)
    {
        lock (this._gate)
        {
            return this.IsCurrentCompletionCore(timerId, finishedAt);
        }
    }

    private bool IsCurrentCompletionCore(Guid timerId, DateTimeOffset? finishedAt) =>
        this._timers.TryGetValue(timerId, out var timer)
        && timer.State == TimerState.Finished && timer.PausedAt == finishedAt;

    public void AddTemplate(TimerDefinition definition)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            this._templates.Add(NormalizeTemplate(definition));
            this.PersistTemplates();
        }

        NotificationQueue.Raise(this.TemplatesChanged, this);
    }

    internal bool TryUpdateTemplate(TimerDefinition definition)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            var index = this._templates.FindIndex(template => template.Id == definition.Id);
            if (index < 0)
            {
                return false;
            }

            this._templates[index] = NormalizeTemplate(definition);
            this.PersistTemplates();
        }

        NotificationQueue.Raise(this.TemplatesChanged, this);
        return true;
    }

    public void RemoveTemplate(Guid templateId)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            var filtered = new List<TimerDefinition>(this._templates.Count);
            foreach (var template in this._templates)
            {
                if (template.Id != templateId)
                {
                    filtered.Add(template);
                }
            }

            this._templates = filtered;
            this.PersistTemplates();
        }

        NotificationQueue.Raise(this.TemplatesChanged, this);
    }

    public void RemoveTimer(Guid timerId)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            if (!this._timers.ContainsKey(timerId))
            {
                return;
            }

            this.CancelScheduled(timerId);
            this._timers.Remove(timerId);
            this.Persist();
        }

        NotificationQueue.Raise(this.TimersChanged, this);
        NotificationQueue.Raise(this.TimersRemoved, this);
    }

    public void ClearAll()
    {
        this.EnsureInitialized();

        lock (this._gate)
        {
            if (this._timers.Count == 0)
            {
                return;
            }

            foreach (var timerId in this._timers.Keys)
            {
                this.CancelScheduled(timerId);
            }

            this._timers.Clear();
            this.Persist();
        }

        NotificationQueue.Raise(this.TimersChanged, this);
        NotificationQueue.Raise(this.TimersRemoved, this);
    }

    internal void ClearTimerHistory(IReadOnlyList<(Guid Id, TimerState State, DateTimeOffset? EndedAt)> entries)
    {
        this.EnsureInitialized();
        lock (this._gate)
        {
            var removed = false;
            foreach (var (id, state, endedAt) in entries)
            {
                if (state is TimerState.Finished or TimerState.Cancelled
                    && this._timers.TryGetValue(id, out var timer) && timer.State == state && timer.PausedAt == endedAt)
                {
                    this.CancelScheduled(id);
                    this._timers.Remove(id);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            this.PersistTimers();
        }

        NotificationQueue.Raise(this.TimersChanged, this);
        NotificationQueue.Raise(this.TimersRemoved, this);
    }

    private static TimerDefinition NormalizeTemplate(TimerDefinition template)
    {
        var normalizedId = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id;
        var normalizedName = template.Name?.Trim() ?? string.Empty;
        if (normalizedId != template.Id || !template.IsTemplate ||
            !string.Equals(normalizedName, template.Name, StringComparison.Ordinal))
        {
            return template with { Id = normalizedId, Name = normalizedName, IsTemplate = true };
        }

        return template;
    }

    private static TimeSpan CalculateElapsed(TimerInstance timer, DateTimeOffset now)
    {
        if (timer is { State: TimerState.Running, StartedAt: { } started })
        {
            return now - started + timer.ElapsedBeforePause;
        }

        return timer.ElapsedBeforePause;
    }

    private void Schedule(TimerInstance timer)
    {
        this.CancelScheduled(timer.Id);
        if (timer.State != TimerState.Running)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        this._runningTokens[timer.Id] = cts;
        this._uiUpdateTimer.SetEnabled(true);
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TimeSpan remaining;
                TimerFinishedEventArgs? completion = null;
                lock (this._gate)
                {
                    if (this._disposed || token.IsCancellationRequested ||
                        !this._timers.TryGetValue(timer.Id, out var current))
                    {
                        return;
                    }

                    var detectedAt = DateTimeOffset.UtcNow;
                    remaining = current.CountdownDuration - CalculateElapsed(current, detectedAt);
                    if (remaining <= TimeSpan.Zero)
                    {
                        completion = this.CompleteScheduledTimer(current, detectedAt);
                        this.PersistTimers();
                    }
                }

                if (completion is not null)
                {
                    this.PublishCompletions(new[] { completion });
                    return;
                }

                try
                {
                    // Task.Delay has a finite upper limit, so recheck the timer after bounded waits.
                    await this._delay(remaining > MaximumDelay ? MaximumDelay : remaining, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
            }
        });
    }

    private static TimerFinishedEventArgs MarkFinished(TimerInstance timer, DateTimeOffset detectedAt)
    {
        var finishedAt = timer.ExpectedEnd ?? detectedAt;
        timer.TransitionToFinished(finishedAt, timer.CountdownDuration, false);
        return new TimerFinishedEventArgs(timer) { DetectedAt = detectedAt };
    }

    private TimerFinishedEventArgs CompleteScheduledTimer(TimerInstance timer, DateTimeOffset detectedAt)
    {
        var completion = MarkFinished(timer, detectedAt);
        this.CancelScheduled(timer.Id);
        this._notifications.Enqueue(timer.NotifyStateChanged);
        return completion;
    }

    private void PublishCompletions(IEnumerable<TimerFinishedEventArgs> completions)
    {
        this._notifications.Drain();
        NotificationQueue.Raise(this.TimersChanged, this);
        foreach (var completion in completions)
        {
            this.NotifyTimerFinished(completion);
        }
    }

    private void NotifyTimerFinished(TimerFinishedEventArgs args)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(this.TimerFinished))
        {
            NotificationQueue.InvokeSafely(() => handler(this, args));
        }
    }

    private void CancelCountdown(TimerInstance timer, DateTimeOffset now)
    {
        this.CancelScheduled(timer.Id);
        var elapsed = CalculateElapsed(timer, now);
        timer.TransitionToCancelled(now, elapsed, false);
    }

    private void CancelScheduled(Guid timerId)
    {
        if (this._runningTokens.Remove(timerId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            if (this._runningTokens.Count == 0)
            {
                this._uiUpdateTimer.SetEnabled(false);
            }
        }
    }

    private void Persist()
    {
        this.PersistTimers();
        this.PersistTemplates();
    }

    private void PersistTimers()
    {
        // Capture and queue each snapshot under the caller's lock so writes follow mutation order.
        var timers = new List<TimerInstance>(this._timers.Count);
        foreach (var timer in this._timers.Values)
        {
            timers.Add(timer.CreatePersistenceSnapshot());
        }

        this._storage.SaveTimers(timers);
    }

    private void PersistTemplates()
    {
        this._storage.SaveTemplates(this._templates);
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
                foreach (var cts in this._runningTokens.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                this._runningTokens.Clear();
            }
        }
    }
}