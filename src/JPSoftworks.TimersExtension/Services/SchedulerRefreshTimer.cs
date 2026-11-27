// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Threading;

namespace JPSoftworks.TimersExtension.Services;

internal sealed partial class SchedulerRefreshTimer : IDisposable
{
    private readonly Lock _gate = new();
    private readonly ITimer _timer;
    private readonly TimeSpan _interval;
    private readonly Action _refresh;
    private bool _enabled;
    private bool _refreshing;
    private bool _disposed;

    public SchedulerRefreshTimer(TimeProvider timeProvider, TimeSpan interval, Action refresh)
    {
        this._interval = interval;
        this._refresh = refresh;
        this._timer = timeProvider.CreateTimer(this.Tick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void SetEnabled(bool enabled)
    {
        lock (this._gate)
        {
            if (this._disposed || this._enabled == enabled)
            {
                return;
            }

            this._enabled = enabled;
            if (!this._refreshing)
            {
                this._timer.Change(enabled ? this._interval : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Tick(object? state)
    {
        lock (this._gate)
        {
            if (!this._enabled || this._refreshing)
            {
                return;
            }

            this._refreshing = true;
        }

        try
        {
            this._refresh();
        }
        finally
        {
            lock (this._gate)
            {
                this._refreshing = false;
                if (this._enabled)
                {
                    this._timer.Change(this._interval, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (this._gate)
        {
            this._disposed = true;
            this._enabled = false;
            this._timer.Dispose();
        }
    }
}