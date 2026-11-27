// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class StopwatchIndexPage : DynamicListPage, INotifyItemsChanged, IDisposable
{
    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            this._itemsChangedSubscriberCount++;
            this.ItemsChanged += value;
            this.EnableUIUpdates(this._itemsChangedSubscriberCount > 0);
        }
        remove
        {
            this.ItemsChanged -= value;
            this._itemsChangedSubscriberCount = Math.Max(0, this._itemsChangedSubscriberCount - 1);
            this.EnableUIUpdates(this._itemsChangedSubscriberCount > 0);
        }
    }

    internal const string PageId = "com.jpsoftworks.timers.stopwatchesIndexPage";

    private readonly Dictionary<Guid, StopwatchInstanceListItem> _stopwatchItems = [];
    private readonly StopwatchScheduler _stopwatchScheduler;
    private readonly SchedulerRefreshTimer _dateRefreshTimer;
    private readonly ListItem _createStopwatchItem;
    private readonly StopwatchFallbackCommandItem _queryStopwatchCommandItem;
    private readonly ListItem _queryStopwatchItem;
    private readonly GroupHeaderListItem _stopwatchesHeaderItem;
    private readonly GroupHeaderListItem _activeHeaderItem = new(Strings.Section_Active);
    private readonly GroupHeaderListItem _readyHeaderItem = new(Strings.State_Ready);
    private readonly GroupHeaderListItem _finishedHeaderItem = new(Strings.State_Finished);
    private readonly StopwatchIndexFilters _filters;
    private int _itemsChangedSubscriberCount;
    private bool _uiUpdatesEnabled;
    private bool _showOlderResults;
    private DateOnly _subtitleDate = DateOnly.FromDateTime(DateTime.Today);

    public StopwatchIndexPage(StopwatchScheduler stopwatchScheduler)
    {
        this._stopwatchScheduler = stopwatchScheduler;
        this._dateRefreshTimer
            = new SchedulerRefreshTimer(TimeProvider.System, TimeSpan.FromMinutes(1), this.RefreshDates);
        this._createStopwatchItem = new ListItem(new CreateStopwatchCommand(this._stopwatchScheduler))
        {
            Title = Strings.Action_StartStopwatch, Icon = Icons.NewStopwatch
        };
        this._queryStopwatchCommandItem = new StopwatchFallbackCommandItem(this._stopwatchScheduler);
        this._queryStopwatchItem = new ListItem(this._queryStopwatchCommandItem) { Icon = Icons.NewStopwatch };
        var stopAllCommand = new AnonymousCommand(this._stopwatchScheduler.StopAll)
        {
            Name = Strings.Action_StopAllStopwatches, Icon = Icons.StopStopwatch, Result = CommandResult.KeepOpen()
        };
        var clearAllCommand = new AnonymousCommand(this._stopwatchScheduler.ClearAll)
        {
            Name = Strings.Action_ClearAllStopwatches, Icon = Icons.Delete, Result = CommandResult.KeepOpen()
        };
        var confirmedClearAllCommand = new DeletionConfirmationCommand(
            clearAllCommand,
            Strings.Confirm_ClearAllStopwatches_Title,
            Strings.Confirm_ClearAllStopwatches_Description,
            () => TimerServices.Settings.ConfirmClearAll);
        this._createStopwatchItem.MoreCommands =
        [
            new CommandContextItem(stopAllCommand)
            {
                Title = Strings.Action_StopAllStopwatches,
                Icon = Icons.StopStopwatch,
                RequestedShortcut = Chords.StopAll
            },
            new CommandContextItem(confirmedClearAllCommand)
            {
                Title = Strings.Action_ClearAllStopwatches,
                Icon = Icons.Delete,
                IsCritical = true,
                RequestedShortcut = Chords.ClearAll
            }
        ];
        this._stopwatchesHeaderItem = new GroupHeaderListItem(Strings.Page_Stopwatches_Title);
        this._filters = new StopwatchIndexFilters();
        this._filters.PropChanged += this.Filters_PropChanged;
        this.Filters = this._filters;
        this.Id = PageId;
        this.Icon = Icons.StopwatchIndex;
        this.Title = Strings.Page_Stopwatches_Title;
        this.Name = Strings.Page_Open;
        this.PlaceholderText = Strings.Page_Stopwatches_SearchPlaceholder;

        this._stopwatchScheduler.StopwatchesChanged += this.StopwatchScheduler_StopwatchesChanged;

        this.RaiseItemsChanged();
    }

    private void Filters_PropChanged(object sender, IPropChangedEventArgs args)
    {
        if (args.PropertyName == nameof(this.Filters.CurrentFilterId))
        {
            this.RaiseItemsChanged();
        }
    }

    private void EnableUIUpdates(bool enableUpdates)
    {
        if (enableUpdates == this._uiUpdatesEnabled)
        {
            return;
        }

        this._uiUpdatesEnabled = enableUpdates;
        this._dateRefreshTimer.SetEnabled(enableUpdates);

        foreach (var item in this._stopwatchItems.Values)
        {
            item.SetUIUpdatesEnabled(enableUpdates);
        }

        if (enableUpdates)
        {
            this.RaiseItemsChanged();
        }
    }

    private void RefreshDates()
    {
        if (DateOnly.FromDateTime(DateTime.Today) != this._subtitleDate)
        {
            NotificationQueue.InvokeSafely(() => this.RaiseItemsChanged());
        }
    }

    private void StopwatchScheduler_StopwatchesChanged(object? sender, EventArgs e)
    {
        this.RaiseItemsChanged();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        this._queryStopwatchCommandItem.UpdateQuery(newSearch);
        this._queryStopwatchItem.Title = this._queryStopwatchCommandItem.Title;
        this.RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        var stopwatches = this._stopwatchScheduler.GetStopwatchesSnapshot();
        var query = this.SearchText.Trim();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dateChanged = today != this._subtitleDate;
        this._subtitleDate = today;

        var currentIds = new HashSet<Guid>();
        foreach (var stopwatch in stopwatches)
        {
            currentIds.Add(stopwatch.Id);
            var item = this.GetOrCreateStopwatchItem(stopwatch);
            if (dateChanged)
            {
                item.RefreshSubtitle(today);
            }
        }

        var staleIds = new List<Guid>();
        foreach (var id in this._stopwatchItems.Keys)
        {
            if (!currentIds.Contains(id))
            {
                staleIds.Add(id);
            }
        }

        foreach (var id in staleIds)
        {
            this._stopwatchItems[id].Dispose();
            this._stopwatchItems.Remove(id);
        }

        var items = new List<IListItem>();
        if (query.Length == 0 && this._filters.IsAllSelected)
        {
            this.AddOverviewItems(stopwatches, items);
            return [.. items];
        }

        var visibleStopwatches = stopwatches
            .Where(stopwatch => this._filters.Includes(stopwatch.State))
            .Where(stopwatch => IndexSearchMatcher.Matches(this._stopwatchItems[stopwatch.Id], query))
            .ToList();
        if (query.Length > 0)
        {
            items.Add(this._queryStopwatchItem);
        }

        if (visibleStopwatches.Count > 0)
        {
            var history = this._filters.CurrentFilterId == StopwatchIndexFilters.Stopped;
            items.Add(history ? this._finishedHeaderItem : this._stopwatchesHeaderItem);
            var ordered = history
                ? visibleStopwatches.OrderByDescending(stopwatch => stopwatch.StoppedAt ?? stopwatch.CreatedAt)
                : visibleStopwatches.OrderBy(stopwatch => stopwatch.State == StopwatchState.Stopped);
            items.AddRange(ordered.Select(stopwatch => this._stopwatchItems[stopwatch.Id]));
        }

        return [.. items];
    }

    private void AddOverviewItems(IReadOnlyList<StopwatchInstance> stopwatches, List<IListItem> items)
    {
        var active = stopwatches.Where(stopwatch => stopwatch.State is StopwatchState.Running or StopwatchState.Paused)
            .ToList();
        var ready = stopwatches.Where(stopwatch => stopwatch.State == StopwatchState.Pending).ToList();
        var finished = stopwatches.Where(stopwatch => stopwatch.State == StopwatchState.Stopped)
            .OrderByDescending(stopwatch => stopwatch.StoppedAt ?? stopwatch.CreatedAt)
            .ToList();

        items.Add(this._createStopwatchItem);
        if (active.Count > 0)
        {
            items.Add(this._activeHeaderItem);
            items.AddRange(active.Select(stopwatch => this._stopwatchItems[stopwatch.Id]));
        }

        if (ready.Count > 0)
        {
            items.Add(this._readyHeaderItem);
            items.AddRange(ready.Select(stopwatch => this._stopwatchItems[stopwatch.Id]));
        }

        if (finished.Count > 0)
        {
            items.Add(this._finishedHeaderItem);
            items.Add(this._stopwatchItems[finished[0].Id]);
        }

        if (finished.Count > 1)
        {
            if (this._showOlderResults)
            {
                items.AddRange(finished.Skip(1).Select(stopwatch => this._stopwatchItems[stopwatch.Id]));
            }

            var toggle = new AnonymousCommand(() =>
            {
                this._showOlderResults = !this._showOlderResults;
                this.RaiseItemsChanged(-2);
            })
            {
                Id = PageId + ".olderResults",
                Name = this._showOlderResults
                    ? Strings.Action_HideOlderResults
                    : ResourceFormatter.Format(Strings.Action_ShowOlderResultsFormat, finished.Count - 1),
                Icon = this._showOlderResults ? Icons.ChevronUp : Icons.ChevronDown,
                Result = CommandResult.KeepOpen()
            };
            items.Add(new ListItem(toggle) { Title = toggle.Name, Icon = toggle.Icon });
        }
    }

    internal ICommandItem? GetStopwatchDockItem(Guid stopwatchId)
    {
        var stopwatch = this._stopwatchScheduler.TryGetStopwatch(stopwatchId);
        if (stopwatch is null)
        {
            return null;
        }

        return new CommandItem(new StopwatchDockPage(this._stopwatchScheduler, stopwatch));
    }

    private StopwatchInstanceListItem GetOrCreateStopwatchItem(StopwatchInstance stopwatch)
    {
        if (this._stopwatchItems.TryGetValue(stopwatch.Id, out var item))
        {
            return item;
        }

        item = new StopwatchInstanceListItem(stopwatch, this._stopwatchScheduler);
        item.SetUIUpdatesEnabled(this._uiUpdatesEnabled);
        this._stopwatchItems[stopwatch.Id] = item;
        return item;
    }

    public void Dispose()
    {
        this._dateRefreshTimer.Dispose();
        this._stopwatchScheduler.StopwatchesChanged -= this.StopwatchScheduler_StopwatchesChanged;
        this._filters.PropChanged -= this.Filters_PropChanged;

        foreach (var item in this._stopwatchItems.Values)
        {
            item.Dispose();
        }

        this._stopwatchItems.Clear();
    }
}