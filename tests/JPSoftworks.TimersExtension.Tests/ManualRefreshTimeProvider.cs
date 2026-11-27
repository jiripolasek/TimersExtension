// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace JPSoftworks.TimersExtension.Tests;

internal sealed class ManualRefreshTimeProvider : TimeProvider, ITimer
{
    private TimerCallback? _callback;
    private object? _state;
    public TimeSpan DueTime { get; private set; } = Timeout.InfiniteTimeSpan;
    public bool Disposed { get; private set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Assert.Null(this._callback);
        this._callback = callback;
        this._state = state;
        this.Change(dueTime, period);
        return this;
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        Assert.False(this.Disposed);
        Assert.Equal(Timeout.InfiniteTimeSpan, period);
        this.DueTime = dueTime;
        return true;
    }

    public bool Fire()
    {
        if (this.DueTime == Timeout.InfiniteTimeSpan)
        {
            return false;
        }

        this.DueTime = Timeout.InfiniteTimeSpan;
        this._callback!(this._state);
        return true;
    }

    public void Dispose()
    {
        this.DueTime = Timeout.InfiniteTimeSpan;
        this.Disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        this.Dispose();
        return ValueTask.CompletedTask;
    }
}