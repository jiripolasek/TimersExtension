using System.Linq;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;
using AlarmRequest = JPSoftworks.TimersExtension.Models.AlarmRequest;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimersIndexPage : ListPage, INotifyItemsChanged
{
    private readonly ListItem _createItem;
    private readonly Dictionary<TimerDefinition, TimerTemplateListItem> _templateItems = [];
    private readonly Dictionary<Guid, TimerInstanceListItem> _timerItems = [];

    private readonly TimerScheduler _timerScheduler;
    private readonly AlarmService _alarmService;
    private int _itemsChangedSubscriberCount;
    private bool _uiUpdatesEnabled;

    public TimersIndexPage(TimerScheduler timerScheduler, AlarmService alarmService)
    {
        _timerScheduler = timerScheduler;
        _alarmService = alarmService;
        Id = "com.jpsoftworks.timers.timersIndexPage";
        Icon = Icons.TimersIndex;
        Title = "Timers";
        Name = "Open";

        _createItem = new ListItem(new EditTimerPage(_timerScheduler, this))
        {
            Title = "Create new timer",
            Icon = Icons.NewTimer
        };

        _timerScheduler.TimersChanged += TimerScheduler_TimersChanged;
        _timerScheduler.TemplatesChanged += TimerScheduler_TemplatesChanged;
        _timerScheduler.TimerFinished += TimerSchedulerOnTimerFinished;

        _alarmService.AlarmClosed += _alarmService_AlarmClosed;
    }

    private void _alarmService_AlarmClosed(AlarmResult result)
    {
        if (result.Kind == AlarmClosedKind.Restart)
        {
            var timer = _timerScheduler.TryGetTimer(result.TimerId);
            _timerScheduler.RestartOrCreate(result.TimerId, result.OriginalDuration, timer?.Name);
        }
    }

    private void TimerSchedulerOnTimerFinished(object? sender, TimerFinishedEventArgs e)
    {
        _alarmService.ShowAlarm(AlarmRequest.FromTimer(e.Timer));
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

        foreach (var item in _timerItems.Values)
        {
            item.SetUIUpdatesEnabled(enableUpdates);
        }

        if (enableUpdates)
        // Force refresh to catch up with any changes that happened while updates were disabled.
        {
            RaiseItemsChanged();
        }
    }

    private void TimerScheduler_TemplatesChanged(object? sender, EventArgs e)
    {
        RaiseItemsChanged();
    }

    private void TimerScheduler_TimersChanged(object? sender, EventArgs e)
    {
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        var timers = _timerScheduler.GetTimersSnapshot();
        var templates = _timerScheduler.GetTemplatesSnapshot();

        // Sync _timerItems
        var currentIds = new HashSet<Guid>();
        foreach (var timer in timers)
        {
            currentIds.Add(timer.Id);
            if (!_timerItems.ContainsKey(timer.Id))
            {
                var item = new TimerInstanceListItem(timer, _timerScheduler, _alarmService);
                item.SetUIUpdatesEnabled(_uiUpdatesEnabled);
                _timerItems[timer.Id] = item;
            }
        }

        // Remove stale timers
        foreach (var id in _timerItems.Keys.Where(k => !currentIds.Contains(k)).ToList())
        {
            _timerItems.Remove(id);
        }

        // Sync _templateItems
        var currentTemplates = new HashSet<TimerDefinition>(templates);
        foreach (var template in templates)
        {
            if (!_templateItems.ContainsKey(template))
            {
                _templateItems[template] = new TimerTemplateListItem(template, _timerScheduler);
            }
        }

        // Remove stale templates
        foreach (var t in _templateItems.Keys.Where(k => !currentTemplates.Contains(k)).ToList())
        {
            _templateItems.Remove(t);
        }

        var totalItems = 1; // "Create new timer"
        if (timers.Count > 0)
        {
            totalItems += 1 + timers.Count;
        }

        // Header + items
        if (templates.Count > 0)
        {
            totalItems += 1 + templates.Count;
        }
        // Header + items

        var items = new IListItem[totalItems];
        var index = 0;

        items[index++] = _createItem;

        if (timers.Count > 0)
        {
            items[index++] = new GroupHeaderListItem("Active Timers");
            foreach (var timer in timers)
            {
                items[index++] = _timerItems[timer.Id];
            }
        }

        if (templates.Count > 0)
        {
            items[index++] = new GroupHeaderListItem("Templates");
            foreach (var template in templates)
            {
                items[index++] = _templateItems[template];
            }
        }

        return items;
    }
}