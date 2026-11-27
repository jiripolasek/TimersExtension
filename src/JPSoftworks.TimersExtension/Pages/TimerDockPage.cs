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

internal sealed partial class TimerDockPage : ListPage, INotifyItemsChanged, IDisposable
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

    private readonly TimerScheduler _scheduler;
    private readonly Timer _refreshTimer;
    private readonly Lock _gate = new();
    private readonly NotificationQueue _notifications;
    private readonly ListItem _createTimerItem;

    private readonly DockItemCache<TimerInstance, (TimerState State, string Name, TimeSpan Duration, ICommandContextItem
        [] StartCommands)> _items;

    private readonly Guid? _pinnedTimerId;
    private readonly bool _showActive;
    private IReadOnlyList<TimerDefinition>? _startMenuTemplates;
    private ICommandContextItem[] _startCommands = [];
    private int _subscriberCount;
    private bool _updatesEnabled;
    private bool _refreshing;
    private bool _disposed;

    public TimerDockPage(TimerScheduler scheduler, TimerInstance? timer = null, bool showActive = false)
    {
        this._scheduler = scheduler;
        this._pinnedTimerId = timer?.Id;
        this._showActive = showActive;
        var nameInTitle = showActive || timer is not null;
        this._notifications = new NotificationQueue(this._gate);
        this._items
            = new DockItemCache<TimerInstance, (TimerState State, string Name, TimeSpan Duration, ICommandContextItem[]
                StartCommands)>(this._gate, this._notifications,
                static model => model.Id,
                model => (model.State, model.Name, model.Duration, this._startCommands),
                (model, state) => DockItemFactory.CreateTimerOverviewItem(
                    model, state.State, this._scheduler, nameInTitle, state.StartCommands),
                (item, model, state, updateCommands) => DockItemFactory.UpdateTimerOverviewItem(
                    item, model, state.State, this._scheduler, updateCommands, nameInTitle, state.StartCommands));
        this._refreshTimer = new Timer(this.RefreshCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        this._createTimerItem = DockItemFactory.CreateCreateTimerItem(this._scheduler);
        if (timer is not null)
        {
            this.Id = CommandIds.TimerDetail(timer.Id);
            this.Title = LabelFormatter.FormatTimerName(timer.Name, timer.Duration);
        }
        else
        {
            this.Id = showActive ? CommandIds.ActiveTimersDockBand : CommandIds.TimerDockBand;
            this.Title = showActive ? Strings.Dock_ActiveTimers_Title : Strings.Dock_RecentTimer_Title;
        }

        this.Name = this.Title;
        this.Icon = Icons.GenericTimer;
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
            this._scheduler.TimersChanged += this.Scheduler_Changed;
            this._scheduler.TemplatesChanged += this.Scheduler_TemplatesChanged;
            TimerServices.Settings.TimerPresentationChanged += this.Settings_TimerPresentationChanged;
            this.SyncItems(true);
        }
        else
        {
            this._scheduler.TimersChanged -= this.Scheduler_Changed;
            this._scheduler.TemplatesChanged -= this.Scheduler_TemplatesChanged;
            TimerServices.Settings.TimerPresentationChanged -= this.Settings_TimerPresentationChanged;
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

            this.SyncItems(!this._updatesEnabled);
            items = this._items.Items.Length == 0 && this._pinnedTimerId is null
                ? [this._createTimerItem]
                : this._items.Items;
        }

        this._notifications.Drain();
        return items;
    }

    private void Scheduler_Changed(object? sender, EventArgs e) => this.RefreshItems();

    private void Scheduler_TemplatesChanged(object? sender, EventArgs e) => this.RefreshItems(templatesChanged: true);

    private void Settings_TimerPresentationChanged(object? sender, EventArgs e) => this.RefreshItems(true);

    private void RefreshCallback(object? state) =>
        NotificationQueue.InvokeSafely(() => this.RefreshItems(fromTimer: true));

    private void RefreshItems(bool presentationChanged = false, bool fromTimer = false, bool templatesChanged = false)
    {
        lock (this._gate)
        {
            if (!this._updatesEnabled || (fromTimer && this._notifications.HasPending))
            {
                return;
            }

            if (this.SyncItems(templatesChanged) || presentationChanged)
            {
                this._notifications.Enqueue(() => this.RaiseItemsChanged());
            }
        }

        this._notifications.Drain();
    }

    private bool SyncItems(bool refreshStartCommands)
    {
        if (refreshStartCommands)
        {
            var templates = this._scheduler.GetTemplatesSnapshot();
            if (this._startMenuTemplates is null || !this._startMenuTemplates.SequenceEqual(templates))
            {
                this._startCommands = DockItemFactory.CreateTimerStartCommands(this._scheduler, templates);
                this._startMenuTemplates = templates;
            }
        }

        var timers = this.GetVisibleTimers();
        var changed = this._items.Sync(timers);
        this.UpdateRefreshLoop(this._updatesEnabled && timers.Any(static timer => timer.State == TimerState.Running));
        return changed;
    }

    private TimerInstance[] GetVisibleTimers()
    {
        if (this._pinnedTimerId is { } timerId)
        {
            return this._scheduler.TryGetTimer(timerId) is { } timer ? [timer] : [];
        }

        var active = this._scheduler.GetTimersSnapshot()
            .Where(static timer => timer.State is TimerState.Running or TimerState.Paused);
        return this._showActive
            ? active.OrderBy(static timer => timer.CreatedAt).ThenBy(static timer => timer.Id).ToArray()
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