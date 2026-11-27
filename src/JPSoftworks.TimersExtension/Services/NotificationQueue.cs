// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace JPSoftworks.TimersExtension.Services;

internal sealed class NotificationQueue(Lock gate)
{
    private readonly Queue<Action> _pending = new();
    private bool _draining;

    public bool HasPending => this._draining || this._pending.Count > 0;

    public void Enqueue(Action notification)
    {
        Debug.Assert(gate.IsHeldByCurrentThread);
        this._pending.Enqueue(notification);
    }

    public void Drain()
    {
        lock (gate)
        {
            if (this._draining)
            {
                return;
            }

            this._draining = true;
        }

        // Serialize notifications without holding the state lock across host callbacks.
        while (true)
        {
            Action? notification;
            lock (gate)
            {
                if (!this._pending.TryDequeue(out notification))
                {
                    this._draining = false;
                    return;
                }
            }

            InvokeSafely(notification);
        }
    }

    public static void InvokeSafely(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Failed to deliver a timer or stopwatch notification: {0}", ex);
        }
    }

    public static void Raise(EventHandler? handlers, object sender)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(handlers))
        {
            InvokeSafely(() => handler(sender, EventArgs.Empty));
        }
    }

    public static void RaisePropertyChanged(PropertyChangedEventHandler? handlers, object sender, string propertyName)
    {
        if (handlers is null)
        {
            return;
        }

        var args = new PropertyChangedEventArgs(propertyName);
        foreach (var handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Failed to deliver a change notification for '{0}': {1}", propertyName, ex);
            }
        }
    }
}