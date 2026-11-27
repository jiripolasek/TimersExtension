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
using AlarmRequest = JPSoftworks.TimersExtension.Models.AlarmRequest;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimersIndexPage : DynamicListPage, INotifyItemsChanged, IDisposable
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

    internal const string PageId = "com.jpsoftworks.timers.timersIndexPage";

    private readonly Dictionary<Guid, TimerTemplateListItem> _templateItems = [];
    private readonly Dictionary<Guid, TimerInstanceListItem> _timerItems = [];

    private readonly TimerScheduler _timerScheduler;
    private readonly AlarmService _alarmService;
    private readonly ListItem _createTimerItem;
    private readonly ListItem _startFiveMinutesItem;
    private readonly TimerFallbackCommandItem _queryTimerCommandItem;
    private readonly ListItem _queryTimerItem;
    private readonly GroupHeaderListItem _timersHeaderItem;
    private readonly GroupHeaderListItem _activeHeaderItem = new(Strings.Section_Active);
    private readonly GroupHeaderListItem _finishedHeaderItem = new(Strings.State_Finished);
    private readonly GroupHeaderListItem _templatesHeaderItem;
    private readonly TimerIndexFilters _filters;
    private int _itemsChangedSubscriberCount;
    private bool _uiUpdatesEnabled;
    private bool _showOlderResults;

    public TimersIndexPage(
        TimerScheduler timerScheduler,
        AlarmService alarmService,
        InvokableCommand repeatLastTimerCommand)
    {
        this._timerScheduler = timerScheduler;
        this._alarmService = alarmService;
        this._createTimerItem = new ListItem(new EditTimerPage(this._timerScheduler))
        {
            Title = Strings.Action_NewTimer, Icon = Icons.NewTimer
        };
        var fiveMinutes = TimeSpan.FromMinutes(5);
        this._startFiveMinutesItem
            = new ListItem(new StartTimerCommand(new TimerDefinition(string.Empty, fiveMinutes), this._timerScheduler))
            {
                Title = ResourceFormatter.Format(Strings.Action_StartDurationFormat,
                    LabelFormatter.FormatTemplateDuration(fiveMinutes)),
                Icon = Icons.NewTimer
            };
        this._queryTimerCommandItem = new TimerFallbackCommandItem(this._timerScheduler, this.ClearSubmittedQuery);
        this._queryTimerItem = new ListItem(this._queryTimerCommandItem) { Icon = Icons.NewTimer };
        var stopAllCommand = new AnonymousCommand(() =>
        {
            this._alarmService.StopAlarm();
            this._timerScheduler.StopAll();
        }) { Name = Strings.Action_StopAllTimers, Icon = Icons.StopStopwatch, Result = CommandResult.KeepOpen() };
        var clearAllCommand = new AnonymousCommand(this._timerScheduler.ClearAll)
        {
            Name = Strings.Action_ClearAllTimers, Icon = Icons.Delete, Result = CommandResult.KeepOpen()
        };
        var confirmedClearAllCommand = new DeletionConfirmationCommand(
            clearAllCommand,
            Strings.Confirm_ClearAllTimers_Title,
            Strings.Confirm_ClearAllTimers_Description,
            () => TimerServices.Settings.ConfirmClearAll);
        this._createTimerItem.MoreCommands =
        [
            new CommandContextItem(repeatLastTimerCommand)
            {
                Title = Strings.Action_RepeatLastTimer,
                Icon = repeatLastTimerCommand.Icon,
                RequestedShortcut = Chords.RepeatLast
            },
            new CommandContextItem(stopAllCommand)
            {
                Title = Strings.Action_StopAllTimers, Icon = Icons.StopStopwatch, RequestedShortcut = Chords.StopAll
            },
            new CommandContextItem(confirmedClearAllCommand)
            {
                Title = Strings.Action_ClearAllTimers,
                Icon = Icons.Delete,
                IsCritical = true,
                RequestedShortcut = Chords.ClearAll
            }
        ];
        this._timersHeaderItem = new GroupHeaderListItem(Strings.Page_Timers_Title);
        this._templatesHeaderItem = new GroupHeaderListItem(Strings.Section_SavedTemplates);
        this._filters = new TimerIndexFilters();
        this._filters.PropChanged += this.Filters_PropChanged;
        this.Filters = this._filters;
        this.Id = PageId;
        this.Icon = Icons.TimersIndex;
        this.Title = Strings.Page_Timers_Title;
        this.Name = Strings.Page_Open;
        this.PlaceholderText = Strings.Page_Timers_SearchPlaceholder;
        this.EmptyContent
            = new CommandItem(new NoOpCommand()) { Title = Strings.Page_Timers_NoMatches, Icon = this.Icon };

        this._timerScheduler.TimersChanged += this.TimerScheduler_TimersChanged;
        this._timerScheduler.TemplatesChanged += this.TimerScheduler_TemplatesChanged;
        this._timerScheduler.TimerFinished += this.TimerSchedulerOnTimerFinished;

        this._alarmService.AlarmClosed += this._timerScheduler.HandleAlarmClosed;
    }

    private void Filters_PropChanged(object sender, IPropChangedEventArgs args)
    {
        if (args.PropertyName == nameof(this.Filters.CurrentFilterId))
        {
            this.RaiseItemsChanged();
        }
    }

    private void TimerSchedulerOnTimerFinished(object? sender, TimerFinishedEventArgs e)
    {
        if (!this._timerScheduler.IsCurrentCompletion(e.Timer.Id, e.FinishedAt))
        {
            return;
        }

        var request = AlarmRequest.FromTimer(
            e,
            TimerServices.Settings.AlarmSoundUri,
            TimerServices.Settings.AlarmVolume,
            TimerServices.Settings.IsAlarmSilent);
        try
        {
            this._alarmService.ShowAlarm(request);
        }
        finally
        {
            // The countdown can change while Windows is displaying the notification.
            if (!this._timerScheduler.IsCurrentCompletion(e.Timer.Id, e.FinishedAt))
            {
                this._alarmService.DismissAlarm(request.AlarmId);
            }
        }
    }

    private void EnableUIUpdates(bool enableUpdates)
    {
        if (enableUpdates == this._uiUpdatesEnabled)
        {
            return;
        }

        this._uiUpdatesEnabled = enableUpdates;

        foreach (var item in this._timerItems.Values)
        {
            item.SetUIUpdatesEnabled(enableUpdates);
        }

        if (enableUpdates)
            // Force refresh to catch up with any changes that happened while updates were disabled.
        {
            this.RaiseItemsChanged();
        }
    }

    private void TimerScheduler_TemplatesChanged(object? sender, EventArgs e)
    {
        this.RaiseItemsChanged();
    }

    private void TimerScheduler_TimersChanged(object? sender, EventArgs e)
    {
        this._alarmService.DismissStaleAlarms(this._timerScheduler);
        this.RaiseItemsChanged();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        this._queryTimerCommandItem.UpdateQuery(newSearch);
        this._queryTimerItem.Title = this._queryTimerCommandItem.Title;
        this._queryTimerItem.Subtitle = this._queryTimerCommandItem.Subtitle;
        this.RaiseItemsChanged();
    }

    private void ClearSubmittedQuery(string submittedQuery)
    {
        if (string.Equals(this.SearchText, submittedQuery, StringComparison.Ordinal))
        {
            this.SearchText = string.Empty;
            this.OnPropertyChanged(nameof(this.SearchText));
        }
    }

    public override IListItem[] GetItems()
    {
        var timers = this._timerScheduler.GetTimersSnapshot();
        var templates = this._timerScheduler.GetTemplatesSnapshot();
        var query = this.SearchText.Trim();

        // Sync _timerItems
        var currentIds = new HashSet<Guid>();
        foreach (var timer in timers)
        {
            currentIds.Add(timer.Id);
            this.GetOrCreateTimerItem(timer);
        }

        // Remove stale timers
        foreach (var id in this._timerItems.Keys.Where(k => !currentIds.Contains(k)).ToList())
        {
            this._timerItems[id].Dispose();
            this._timerItems.Remove(id);
        }

        // Sync _templateItems
        var currentTemplateIds = new HashSet<Guid>();
        foreach (var template in templates)
        {
            currentTemplateIds.Add(template.Id);
            this.GetOrCreateTemplateItem(template);
        }

        // Remove stale templates
        foreach (var id in this._templateItems.Keys.Where(k => !currentTemplateIds.Contains(k)).ToList())
        {
            this._templateItems.Remove(id);
        }

        var items = new List<IListItem>();
        if (query.Length == 0 && this._filters.CurrentFilterId == TimerIndexFilters.All)
        {
            this.AddOverviewItems(timers, templates, items);
            return [.. items];
        }

        var visibleTimers = timers
            .Where(timer => this._filters.IncludesTimer(timer.State))
            .Where(timer => IndexSearchMatcher.Matches(this._timerItems[timer.Id], query))
            .ToList();
        if (this._queryTimerCommandItem.HasCreationQuery)
        {
            items.Add(this._queryTimerItem);
        }
        else if (query.Length > 0 && IndexSearchMatcher.Matches(this._createTimerItem, query))
        {
            items.Add(this._createTimerItem);
        }

        if (visibleTimers.Count > 0)
        {
            if (this._filters.CurrentFilterId == TimerIndexFilters.Finished)
            {
                items.Add(this._finishedHeaderItem);
                items.AddRange(visibleTimers.OrderByDescending(timer => timer.PausedAt ?? timer.CreatedAt)
                    .Select(timer => this._timerItems[timer.Id]));
                if (query.Length == 0)
                {
                    items.Add(this.CreateClearFinishedItem(visibleTimers));
                }
            }
            else
            {
                items.Add(this._timersHeaderItem);
                items.AddRange(visibleTimers.Select(timer => this._timerItems[timer.Id]));
            }
        }

        if (this._filters.IncludesTemplates)
        {
            this.AddTemplateItems(
                templates.Where(template => IndexSearchMatcher.Matches(this._templateItems[template.Id], query))
                    .ToArray(), items);
        }

        return [.. items];
    }

    private void AddOverviewItems(
        IReadOnlyList<TimerInstance> timers,
        IReadOnlyList<TimerDefinition> templates,
        List<IListItem> items)
    {
        var active = timers.Where(timer => timer.State is TimerState.Running or TimerState.Paused).ToList();
        var finished = timers.Where(timer => timer.State is TimerState.Finished or TimerState.Cancelled)
            .OrderByDescending(timer => timer.PausedAt ?? timer.CreatedAt)
            .ToList();
        var latest = finished.Count > 0 ? finished[0] : null;
        var lastStarted = this._timerScheduler.GetLastStartedTimer();

        if (active.Count == 0)
        {
            if (lastStarted is null)
            {
                items.Add(this._startFiveMinutesItem);
            }
            else if (lastStarted != latest)
            {
                var repeat = new RepeatLastTimerCommand(this._timerScheduler, lastStarted.Id);
                items.Add(new ListItem(repeat)
                {
                    Title = ResourceFormatter.Format(Strings.Action_RepeatTimerFormat,
                        LabelFormatter.FormatTemplateTitle(lastStarted.Name, lastStarted.Duration)),
                    Icon = repeat.Icon
                });
            }
        }

        items.Add(this._createTimerItem);
        if (active.Count > 0)
        {
            items.Add(this._activeHeaderItem);
            items.AddRange(active.Select(timer => this._timerItems[timer.Id]));
        }

        if (latest is not null)
        {
            items.Add(this._finishedHeaderItem);
            items.Add(this._timerItems[latest.Id]);
        }

        if (finished.Count > 1)
        {
            if (this._showOlderResults)
            {
                items.AddRange(finished.Skip(1).Select(timer => this._timerItems[timer.Id]));
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
            if (this._showOlderResults)
            {
                items.Add(this.CreateClearFinishedItem(finished));
            }
        }

        this.AddTemplateItems(templates, items);
    }

    private void AddTemplateItems(IReadOnlyList<TimerDefinition> templates, List<IListItem> items)
    {
        if (templates.Count > 0)
        {
            items.Add(this._templatesHeaderItem);
            items.AddRange(templates.Select(template => this._templateItems[template.Id]));
        }
    }

    private ListItem CreateClearFinishedItem(List<TimerInstance> timers)
    {
        var history = timers.Select(timer => (timer.Id, timer.State, timer.PausedAt)).ToArray();
        var clear = new AnonymousCommand(() =>
        {
            this._showOlderResults = false;
            this._timerScheduler.ClearTimerHistory(history);
            this.RaiseItemsChanged();
        }) { Name = Strings.Action_ClearFinishedTimers, Icon = Icons.Delete, Result = CommandResult.KeepOpen() };
        var confirmation = new DeletionConfirmationCommand(
            clear,
            Strings.Action_ClearFinishedTimers,
            ResourceFormatter.Format(Strings.Confirm_ClearFinishedTimersFormat, history.Length),
            () => true);
        return new ListItem(confirmation) { Title = clear.Name, Icon = clear.Icon };
    }

    internal ICommandItem? GetTimerDockItem(Guid timerId)
    {
        var timer = this._timerScheduler.TryGetTimer(timerId);
        if (timer is null)
        {
            return null;
        }

        return new CommandItem(new TimerDockPage(this._timerScheduler, timer));
    }

    internal ICommandItem? GetTemplateDockItem(Guid templateId)
    {
        var template = this._timerScheduler.TryGetTemplate(templateId);

        if (template is null)
        {
            return null;
        }

        var item = DockItemFactory.CreateTimerTemplateItem(template, this._timerScheduler);
        return new WrappedDockItem([item], CommandIds.TimerTemplate(template.Id), item.Title) { Icon = item.Icon };
    }

    private TimerInstanceListItem GetOrCreateTimerItem(TimerInstance timer)
    {
        if (this._timerItems.TryGetValue(timer.Id, out var item))
        {
            return item;
        }

        item = new TimerInstanceListItem(timer, this._timerScheduler);
        item.SetUIUpdatesEnabled(this._uiUpdatesEnabled);
        this._timerItems[timer.Id] = item;
        return item;
    }

    private TimerTemplateListItem GetOrCreateTemplateItem(TimerDefinition template)
    {
        if (this._templateItems.TryGetValue(template.Id, out var item) && item.Definition == template)
        {
            return item;
        }

        item = new TimerTemplateListItem(template, this._timerScheduler);
        this._templateItems[template.Id] = item;
        return item;
    }

    public void Dispose()
    {
        this._timerScheduler.TimersChanged -= this.TimerScheduler_TimersChanged;
        this._timerScheduler.TemplatesChanged -= this.TimerScheduler_TemplatesChanged;
        this._timerScheduler.TimerFinished -= this.TimerSchedulerOnTimerFinished;
        this._alarmService.AlarmClosed -= this._timerScheduler.HandleAlarmClosed;
        this._filters.PropChanged -= this.Filters_PropChanged;

        foreach (var item in this._timerItems.Values)
        {
            item.Dispose();
        }

        this._timerItems.Clear();
        this._templateItems.Clear();
    }
}