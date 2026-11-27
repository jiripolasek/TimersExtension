// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Pages;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension;

public partial class TimersExtensionCommandsProvider : CommandProvider
{
    // Use the property name directly until the extension SDK exposes DockCommandId.
    private const string DockCommandIdProperty = "Microsoft.CommandPalette.DockCommandId";

    private readonly ICommandItem[] _homeCommandsBeforeTemplates;
    private readonly ICommandItem[] _homeCommandsAfterTemplates;
    private readonly ICommandItem[] _dockBands;
    private readonly IFallbackCommandItem[] _fallbackCommands;
    private readonly Dictionary<string, ICommandItem> _staticCommandItems;
    private readonly SettingsManager _settingsManager = TimerServices.Settings;
    private readonly TimerScheduler _timerScheduler;
    private readonly StopwatchScheduler _stopwatchScheduler;
    private readonly AlarmService _alarmService;
    private readonly TimersIndexPage _timersPage;
    private readonly StopwatchIndexPage _stopwatchesPage;
    private readonly TimerDockPage _timerDockPage;
    private readonly StopwatchDockPage _stopwatchDockPage;
    private readonly TimerDockPage _activeTimersDockPage;
    private readonly StopwatchDockPage _activeStopwatchesDockPage;
    private readonly DockAccessPage _allTimersDockPage;
    private readonly DockAccessPage _allStopwatchesDockPage;
    private readonly Lock _notificationGate = new();
    private readonly NotificationQueue _notifications;
    private readonly RepeatLastTimerCommand _repeatLastTimerCommand;
    private readonly CommandItem _repeatLastTimerItem;
    private readonly CommandItem _stopNextTimerItem;
    private readonly CommandItem _dismissRingingTimerItem;
    private bool _disposed;

    public TimersExtensionCommandsProvider()
        : this(TimerServices.Scheduler, TimerServices.StopwatchScheduler, TimerServices.AlarmService)
    {
    }

    internal TimersExtensionCommandsProvider(
        TimerScheduler timerScheduler,
        StopwatchScheduler stopwatchScheduler,
        AlarmService alarmService)
    {
        this._timerScheduler = timerScheduler;
        this._stopwatchScheduler = stopwatchScheduler;
        this._alarmService = alarmService;
        this._notifications = new NotificationQueue(this._notificationGate);
        this.Id = CommandIds.Provider;
        this.DisplayName = Strings.Provider_DisplayName;
        this.Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        this.Settings = this._settingsManager.Settings;

        this._repeatLastTimerCommand = new RepeatLastTimerCommand(this._timerScheduler);
        this._timersPage = new TimersIndexPage(this._timerScheduler, this._alarmService, this._repeatLastTimerCommand);
        this._stopwatchesPage = new StopwatchIndexPage(this._stopwatchScheduler);
        this._timerDockPage = new TimerDockPage(this._timerScheduler);
        this._stopwatchDockPage = new StopwatchDockPage(this._stopwatchScheduler);
        this._activeTimersDockPage = new TimerDockPage(this._timerScheduler, showActive: true);
        this._activeStopwatchesDockPage = new StopwatchDockPage(this._stopwatchScheduler, showActive: true);
        this._allTimersDockPage = new DockAccessPage(
            CommandIds.AllTimersDockBand,
            Strings.Dock_AllTimers_Title,
            Icons.TimersIndex, this._timersPage);
        this._allStopwatchesDockPage = new DockAccessPage(
            CommandIds.AllStopwatchesDockBand,
            Strings.Dock_AllStopwatches_Title,
            Icons.StopwatchIndex, this._stopwatchesPage);
        this._allStopwatchesDockPage.UpdateMoreCommands([
            DockItemFactory.CreateStartStopwatchContextItem(this._stopwatchScheduler)
        ]);

        var timersItem = new ListItem(this._timersPage)
        {
            Title = this.DisplayName, MoreCommands = [new CommandContextItem(this.Settings.SettingsPage!)]
        };
        timersItem.GetProperties()[DockCommandIdProperty] = CommandIds.AllTimersDockBand;
        var stopwatchesItem = new ListItem(this._stopwatchesPage)
        {
            Title = Strings.Page_Stopwatches_Title,
            MoreCommands = [new CommandContextItem(this.Settings.SettingsPage!)]
        };
        stopwatchesItem.GetProperties()[DockCommandIdProperty] = CommandIds.AllStopwatchesDockBand;
        this._repeatLastTimerItem = new CommandItem(this._repeatLastTimerCommand)
        {
            Title = Strings.Action_RepeatLastTimer, Icon = this._repeatLastTimerCommand.Icon
        };
        var startCustomTimerItem = new CommandItem(new EditTimerPage(this._timerScheduler))
        {
            Title = Strings.Action_StartCustomTimer, Icon = Icons.NewTimer
        };
        var startStopwatchItem = DockItemFactory.CreateCreateStopwatchItem(this._stopwatchScheduler);
        var stopNextTimer = new StopNextTimerCommand(this._timerScheduler);
        this._stopNextTimerItem = new CommandItem(stopNextTimer)
        {
            Title = stopNextTimer.Name, Icon = stopNextTimer.Icon
        };
        var dismissRingingTimer = new DismissRingingTimerCommand(this._alarmService);
        this._dismissRingingTimerItem = new CommandItem(dismissRingingTimer)
        {
            Title = dismissRingingTimer.Name, Icon = dismissRingingTimer.Icon
        };

        this._homeCommandsBeforeTemplates =
            [timersItem, stopwatchesItem, this._repeatLastTimerItem, startCustomTimerItem];
        this._homeCommandsAfterTemplates = [startStopwatchItem, this._stopNextTimerItem, this._dismissRingingTimerItem];
        this._dockBands =
        [
            new CommandItem(this._activeTimersDockPage)
            {
                Title = Strings.Dock_ActiveTimers_Title, Icon = Icons.GenericTimer
            },
            new CommandItem(this._timerDockPage) { Title = Strings.Dock_RecentTimer_Title, Icon = Icons.GenericTimer },
            new CommandItem(this._allTimersDockPage) { Title = Strings.Dock_AllTimers_Title, Icon = Icons.TimersIndex },
            new CommandItem(this._activeStopwatchesDockPage)
            {
                Title = Strings.Dock_ActiveStopwatches_Title, Icon = Icons.Stopwatch
            },
            new CommandItem(this._stopwatchDockPage)
            {
                Title = Strings.Dock_RecentStopwatch_Title, Icon = Icons.Stopwatch
            },
            new CommandItem(this._allStopwatchesDockPage)
            {
                Title = Strings.Dock_AllStopwatches_Title, Icon = Icons.StopwatchIndex
            }
        ];
        this._fallbackCommands =
        [
            new TimerFallbackCommandItem(this._timerScheduler),
            new StopwatchFallbackCommandItem(this._stopwatchScheduler, true)
        ];

        this._staticCommandItems = new Dictionary<string, ICommandItem>(StringComparer.OrdinalIgnoreCase)
        {
            [TimersIndexPage.PageId] = timersItem,
            [StopwatchIndexPage.PageId] = stopwatchesItem,
            [CommandIds.TimerCreatePage] = startCustomTimerItem,
            [CommandIds.StopNextTimer] = this._stopNextTimerItem,
            [CommandIds.DismissRingingTimer] = this._dismissRingingTimerItem,
            [CommandIds.StopwatchCreate] = startStopwatchItem,
            [CommandIds.TimerFallback] = this._fallbackCommands[0],
            [CommandIds.StopwatchFallback] = this._fallbackCommands[1],
            [CommandIds.TimerRepeatLast] = this._repeatLastTimerItem
        };

        foreach (var band in this._dockBands)
        {
            this._staticCommandItems.Add(band.Command!.Id, band);
        }

        this._timerScheduler.TimersChanged += this.Scheduler_TimersChanged;
        this._timerScheduler.TimersRemoved += this.Scheduler_ItemsChanged;
        this._timerScheduler.TemplatesChanged += this.Scheduler_TemplatesChanged;
        this._stopwatchScheduler.StopwatchesRemoved += this.Scheduler_ItemsChanged;
        this._alarmService.RingingAlarmChanged += this.Alarm_RingingAlarmChanged;
        this.UpdateAllTimersDockMenu();
        this.UpdateTimerCommandSubtitles();
        this.UpdateRingingAlarmSubtitle();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return
        [
            .. this._homeCommandsBeforeTemplates,
            .. this._timerScheduler.GetTemplatesSnapshot().Select(this.CreateHomeTemplateItem),
            .. this._homeCommandsAfterTemplates
        ];
    }

    public override IFallbackCommandItem[] FallbackCommands()
    {
        return this._fallbackCommands;
    }

    public override ICommandItem[]? GetDockBands()
    {
        return this._dockBands;
    }

    public override ICommand? GetCommand(string id)
    {
        if (string.Equals(id, TimersIndexPage.PageId, StringComparison.OrdinalIgnoreCase))
        {
            return this._timersPage;
        }

        if (string.Equals(id, StopwatchIndexPage.PageId, StringComparison.OrdinalIgnoreCase))
        {
            return this._stopwatchesPage;
        }

        if (string.Equals(id, EditTimerPage.PageId, StringComparison.OrdinalIgnoreCase))
        {
            return new EditTimerPage(this._timerScheduler);
        }

        if (string.Equals(id, CommandIds.DismissRingingTimer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, CommandIds.StopNextTimer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, CommandIds.StopwatchCreate, StringComparison.OrdinalIgnoreCase)
            || CommandIds.TryGetHomeTimerTemplateId(id, out _))
        {
            return this.GetCommandItem(id)?.Command;
        }

        if (CommandIds.TryGetTimerDetailId(id, out var timerId))
        {
            var timer = this._timerScheduler.TryGetTimer(timerId);
            if (timer is not null)
            {
                return new TimerDetailPage(timer, this._timerScheduler);
            }
        }

        if (CommandIds.TryGetStopwatchDetailId(id, out var stopwatchId))
        {
            var stopwatch = this._stopwatchScheduler.TryGetStopwatch(stopwatchId);
            if (stopwatch is not null)
            {
                return new StopwatchDetailPage(stopwatch, this._stopwatchScheduler);
            }
        }

        return null;
    }

    public override ICommandItem? GetCommandItem(string id)
    {
        if (this._staticCommandItems.TryGetValue(id, out var item))
        {
            return item;
        }

        if (CommandIds.TryGetHomeTimerTemplateId(id, out var homeTemplateId))
        {
            return this._timerScheduler.TryGetTemplate(homeTemplateId) is { } template
                ? this.CreateHomeTemplateItem(template)
                : null;
        }

        if (CommandIds.TryGetTimerTemplateId(id, out var templateId))
        {
            return this._timersPage.GetTemplateDockItem(templateId);
        }

        if (CommandIds.TryGetTimerDetailId(id, out var timerId))
        {
            return this._timersPage.GetTimerDockItem(timerId);
        }

        if (CommandIds.TryGetStopwatchDetailId(id, out var stopwatchId))
        {
            return this._stopwatchesPage.GetStopwatchDockItem(stopwatchId);
        }

        return null;
    }

    private CommandItem CreateHomeTemplateItem(TimerDefinition template)
    {
        var name = string.IsNullOrWhiteSpace(template.Name)
            ? LabelFormatter.FormatTemplateDuration(template.Duration)
            : template.Name.Trim();
        var command = new StartTimerCommand(template.Id, this._timerScheduler);
        return new CommandItem(command)
        {
            Title = ResourceFormatter.Format(Strings.Action_StartNamedTimerTemplateFormat, name),
            Subtitle = LabelFormatter.FormatTemplateSubtitle(template),
            Icon = command.Icon
        };
    }

    private void Scheduler_ItemsChanged(object? sender, EventArgs e)
    {
        this.RaiseItemsChanged();
    }

    private void Scheduler_TimersChanged(object? sender, EventArgs e) => this.UpdateTimerCommandSubtitles();

    private void UpdateTimerCommandSubtitles()
    {
        lock (this._notificationGate)
        {
            this._notifications.Enqueue(() =>
                this._repeatLastTimerItem.Subtitle = this._repeatLastTimerCommand.GetTargetSubtitle());
            this._notifications.Enqueue(() => this._stopNextTimerItem.Subtitle
                = this._timerScheduler.GetNextRunningTimer() is { } timer
                    ? LabelFormatter.FormatTimerTargetSubtitle(timer)
                    : Strings.Toast_NoRunningTimer);
        }

        this._notifications.Drain();
    }

    private void Alarm_RingingAlarmChanged(object? sender, EventArgs e) => this.UpdateRingingAlarmSubtitle();

    private void UpdateRingingAlarmSubtitle()
    {
        lock (this._notificationGate)
        {
            this._notifications.Enqueue(() => this._dismissRingingTimerItem.Subtitle
                = this._alarmService.GetRingingAlarm() is { } alarm
                    ? LabelFormatter.FormatTimerTargetSubtitle(alarm.TimerName, alarm.OriginalDuration)
                    : Strings.Toast_NoRingingTimer);
        }

        this._notifications.Drain();
    }

    private void Scheduler_TemplatesChanged(object? sender, EventArgs e)
    {
        this.UpdateAllTimersDockMenu();
        this.RaiseItemsChanged();
    }

    private void UpdateAllTimersDockMenu()
    {
        lock (this._notificationGate)
        {
            this._notifications.Enqueue(() => this._allTimersDockPage.UpdateMoreCommands(
                DockItemFactory.CreateTimerStartCommands(this._timerScheduler,
                    this._timerScheduler.GetTemplatesSnapshot())));
        }

        this._notifications.Drain();
    }

    public override void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this._timerScheduler.TimersChanged -= this.Scheduler_TimersChanged;
        this._timerScheduler.TimersRemoved -= this.Scheduler_ItemsChanged;
        this._timerScheduler.TemplatesChanged -= this.Scheduler_TemplatesChanged;
        this._stopwatchScheduler.StopwatchesRemoved -= this.Scheduler_ItemsChanged;
        this._alarmService.RingingAlarmChanged -= this.Alarm_RingingAlarmChanged;
        this._timerDockPage.Dispose();
        this._stopwatchDockPage.Dispose();
        this._activeTimersDockPage.Dispose();
        this._activeStopwatchesDockPage.Dispose();
        this._timersPage.Dispose();
        this._stopwatchesPage.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}