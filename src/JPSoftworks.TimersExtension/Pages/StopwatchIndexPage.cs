using Windows.Foundation;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class StopwatchIndexPage : ListPage, INotifyItemsChanged
{
    private readonly ListItem _createItem;
    private readonly Dictionary<Guid, StopwatchInstanceListItem> _stopwatchItems = [];
    private readonly StopwatchScheduler _stopwatchScheduler;
    private int _itemsChangedSubscriberCount;
    private bool _uiUpdatesEnabled;
    private int _lastStopwatchCount;

    public StopwatchIndexPage(StopwatchScheduler stopwatchScheduler)
    {
        _stopwatchScheduler = stopwatchScheduler;
        Id = "com.jpsoftworks.timers.stopwatchesIndexPage";
        Icon = Icons.StopwatchIndex;
        Title = "Stopwatches";
        Name = "Open";

        _createItem = new ListItem(new CreateStopwatchCommand(_stopwatchScheduler))
        {
            Title = "Create new stopwatch",
            Icon = Icons.NewStopwatch
        };

        _stopwatchScheduler.StopwatchesChanged += StopwatchScheduler_StopwatchesChanged;

        RaiseItemsChanged();
    }

    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            _itemsChangedSubscriberCount++;
            ItemsChanged += value;
            EnableUIUpdates(_itemsChangedSubscriberCount > 0);
        }
        remove
        {
            ItemsChanged -= value;
            _itemsChangedSubscriberCount = Math.Max(0, _itemsChangedSubscriberCount - 1);
            EnableUIUpdates(_itemsChangedSubscriberCount > 0);
        }
    }

    private void EnableUIUpdates(bool enableUpdates)
    {
        if (enableUpdates == _uiUpdatesEnabled)
        {
            return;
        }

        _uiUpdatesEnabled = enableUpdates;

        foreach (var item in _stopwatchItems.Values)
        {
            item.SetUIUpdatesEnabled(enableUpdates);
        }

        if (enableUpdates)
        {
            RaiseItemsChanged();
        }
    }

    private void StopwatchScheduler_StopwatchesChanged(object? sender, EventArgs e)
    {
        var currentCount = _stopwatchScheduler.GetStopwatchesSnapshot().Count;
        if (currentCount != _lastStopwatchCount)
        {
            _lastStopwatchCount = currentCount;
            RaiseItemsChanged();
        }
    }

    public override IListItem[] GetItems()
    {
        var stopwatches = _stopwatchScheduler.GetStopwatchesSnapshot();
        _lastStopwatchCount = stopwatches.Count;

        var currentIds = new HashSet<Guid>();
        foreach (var stopwatch in stopwatches)
        {
            currentIds.Add(stopwatch.Id);
            if (!_stopwatchItems.ContainsKey(stopwatch.Id))
            {
                var item = new StopwatchInstanceListItem(stopwatch, _stopwatchScheduler);
                item.SetUIUpdatesEnabled(_uiUpdatesEnabled);
                _stopwatchItems[stopwatch.Id] = item;
            }
        }

        var staleIds = new List<Guid>();
        foreach (var id in _stopwatchItems.Keys)
        {
            if (!currentIds.Contains(id))
            {
                staleIds.Add(id);
            }
        }
        foreach (var id in staleIds)
        {
            _stopwatchItems.Remove(id);
        }

        var totalItems = 1;
        if (stopwatches.Count > 0)
        {
            totalItems += 1 + stopwatches.Count;
        }

        var items = new IListItem[totalItems];
        var index = 0;

        items[index++] = _createItem;

        if (stopwatches.Count > 0)
        {
            items[index++] = new GroupHeaderListItem("Stopwatches");
            foreach (var stopwatch in stopwatches)
            {
                items[index++] = _stopwatchItems[stopwatch.Id];
            }
        }

        return items;
    }
}