// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using System.Threading;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class StopwatchDockPage : ListPage, INotifyItemsChanged, IDisposable
{
    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            lock (this._gate)
            {
                this._subscriberCount++;
                this.ItemsChanged += value;
                this.SetUpdatesEnabled(true);
            }

            this._notifications.Drain();
        }
        remove
        {
            lock (this._gate)
            {
                this.ItemsChanged -= value;
                this._subscriberCount = Math.Max(0, this._subscriberCount - 1);
                this.SetUpdatesEnabled(this._subscriberCount > 0);
            }
        }
    }

    private readonly StopwatchScheduler _scheduler;
    private readonly Timer _refreshTimer;
    private readonly Lock _gate = new();
    private readonly NotificationQueue _notifications;
    private readonly ListItem _createStopwatchItem;
    private readonly DockItemCache<StopwatchInstance, (StopwatchState State, string Name)> _items;
    private readonly Guid? _pinnedStopwatchId;
    private readonly bool _showActive;
    private int _subscriberCount;
    private bool _updatesEnabled;
    private bool _refreshing;
    private bool _disposed;

    public StopwatchDockPage(StopwatchScheduler scheduler, StopwatchInstance? stopwatch = null, bool showActive = false)
    {
        this._scheduler = scheduler;
        this._pinnedStopwatchId = stopwatch?.Id;
        this._showActive = showActive;
        var nameInTitle = showActive || stopwatch is not null;
        this._notifications = new NotificationQueue(this._gate);
        this._items = new DockItemCache<StopwatchInstance, (StopwatchState State, string Name)>(this._gate,
            this._notifications,
            static model => model.Id,
            static model => (model.State, model.Name),
            (model, state) => DockItemFactory.CreateStopwatchItem(model, state.State, this._scheduler, nameInTitle),
            (item, model, state, updateCommands) => DockItemFactory.UpdateStopwatchItem(item, model, state.State,
                this._scheduler, updateCommands, nameInTitle));
        this._refreshTimer = new Timer(this.RefreshCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        this._createStopwatchItem = DockItemFactory.CreateCreateStopwatchItem(this._scheduler);
        if (stopwatch is not null)
        {
            this.Id = CommandIds.StopwatchDetail(stopwatch.Id);
            this.Title = LabelFormatter.FormatStopwatchName(stopwatch.Name);
        }
        else
        {
            this.Id = showActive ? CommandIds.ActiveStopwatchesDockBand : CommandIds.StopwatchDockBand;
            this.Title = showActive ? Strings.Dock_ActiveStopwatches_Title : Strings.Dock_RecentStopwatch_Title;
        }

        this.Name = this.Title;
        this.Icon = Icons.Stopwatch;
    }

    private void SetUpdatesEnabled(bool enabled)
    {
        enabled &= !this._disposed;
        if (this._updatesEnabled == enabled)
        {
            return;
        }

        this._updatesEnabled = enabled;
        if (enabled)
        {
            this._scheduler.StopwatchesChanged += this.Scheduler_Changed;
            this.SyncItems();
        }
        else
        {
            this._scheduler.StopwatchesChanged -= this.Scheduler_Changed;
            this.UpdateRefreshLoop(false);
        }
    }

    public override IListItem[] GetItems()
    {
        IListItem[] items;
        lock (this._gate)
        {
            if (this._disposed)
            {
                return [];
            }

            this.SyncItems();
            items = this._items.Items.Length == 0 && this._pinnedStopwatchId is null
                ? [this._createStopwatchItem]
                : this._items.Items;
        }

        this._notifications.Drain();
        return items;
    }

    private void Scheduler_Changed(object? sender, EventArgs e) => this.RefreshItems();

    private void RefreshCallback(object? state) => NotificationQueue.InvokeSafely(() => this.RefreshItems(true));

    private void RefreshItems(bool fromTimer = false)
    {
        lock (this._gate)
        {
            if (!this._updatesEnabled || (fromTimer && this._notifications.HasPending))
            {
                return;
            }

            if (this.SyncItems())
            {
                this._notifications.Enqueue(() => this.RaiseItemsChanged());
            }
        }

        this._notifications.Drain();
    }

    private bool SyncItems()
    {
        var stopwatches = this.GetVisibleStopwatches();
        var changed = this._items.Sync(stopwatches);
        this.UpdateRefreshLoop(this._updatesEnabled &&
                               stopwatches.Any(static stopwatch => stopwatch.State == StopwatchState.Running));
        return changed;
    }

    private StopwatchInstance[] GetVisibleStopwatches()
    {
        if (this._pinnedStopwatchId is { } stopwatchId)
        {
            return this._scheduler.TryGetStopwatch(stopwatchId) is { } stopwatch ? [stopwatch] : [];
        }

        var active = this._scheduler.GetStopwatchesSnapshot()
            .Where(static stopwatch => stopwatch.State is StopwatchState.Running or StopwatchState.Paused);
        return this._showActive
            ? active.OrderBy(static stopwatch => stopwatch.CreatedAt).ThenBy(static stopwatch => stopwatch.Id).ToArray()
            : active.Take(1).ToArray();
    }

    private void UpdateRefreshLoop(bool running)
    {
        if (this._refreshing == running)
        {
            return;
        }

        this._refreshing = running;
        var interval = running ? TimeSpan.FromSeconds(1) : Timeout.InfiniteTimeSpan;
        this._refreshTimer.Change(interval, interval);
    }

    public void Dispose()
    {
        lock (this._gate)
        {
            this._disposed = true;
            this.SetUpdatesEnabled(false);
            this._refreshTimer.Dispose();
            this._items.Clear();
        }
    }
}