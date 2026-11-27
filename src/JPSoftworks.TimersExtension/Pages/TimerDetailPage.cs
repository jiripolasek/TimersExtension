// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerDetailPage : ListPage, INotifyItemsChanged, IDisposable
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

    private readonly TimerInstance _timer;
    private readonly TimerScheduler _scheduler;
    private readonly ListItem _summaryItem;
    private readonly ListItem _alarmSoundItem;
    private IListItem[] _items = [];
    private int _itemsChangedSubscriberCount;
    private TimerState _lastState;
    private TimeSpan _lastCountdownDuration;
    private bool _lastWasSnoozed;
    private TimerState? _pageIconState;
    private string _lastName = string.Empty;
    private bool _uiUpdatesEnabled;

    public TimerDetailPage(TimerInstance timer, TimerScheduler scheduler)
    {
        this._timer = timer;
        this._scheduler = scheduler;
        this._lastState = timer.State;
        this._lastName = timer.Name;

        this.Id = CommandIds.TimerDetail(timer.Id);
        this.Title = LabelFormatter.FormatTimerName(timer.Name, timer.Duration);
        this.Name = Strings.Page_Details;
        this.SyncPageIcon();

        this._summaryItem = new ListItem { Title = Strings.Label_Status };
        this._alarmSoundItem = new ListItem(new TimerSoundPage(timer, scheduler))
        {
            Title = Strings.Setting_AlarmSound_Title, Icon = Icons.AlarmSound
        };
        this.UpdateSummaryItem();
        this.UpdateAlarmSoundItem();
        this.RebuildItems();
    }

    private void EnableUIUpdates(bool enabled)
    {
        if (this._uiUpdatesEnabled == enabled)
        {
            return;
        }

        this._uiUpdatesEnabled = enabled;
        if (enabled)
        {
            this.SyncPageIcon();
            this._timer.PropertyChanged += this.Timer_PropertyChanged;
            TimerServices.Settings.TimerPresentationChanged += this.Settings_TimerPresentationChanged;
            this.UpdateSummaryItem();
            this.UpdateAlarmSoundItem();
            var contentChanged = this._lastState != this._timer.State || this._lastName != this._timer.Name
                                                                      || this._lastCountdownDuration !=
                                                                      this._timer.CountdownDuration ||
                                                                      this._lastWasSnoozed !=
                                                                      this._timer.SnoozeDuration.HasValue;
            this._lastState = this._timer.State;
            this._lastName = this._timer.Name;
            this.RebuildItems();
            if (contentChanged)
            {
                this.RaiseItemsChanged();
            }
        }
        else
        {
            this._timer.PropertyChanged -= this.Timer_PropertyChanged;
            TimerServices.Settings.TimerPresentationChanged -= this.Settings_TimerPresentationChanged;
        }
    }

    private void Timer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TimerInstance.Remaining):
            case nameof(TimerInstance.ExpectedEnd):
                this.UpdateSummaryItem();
                break;

            case nameof(TimerInstance.Name):
                this.Title = LabelFormatter.FormatTimerName(this._timer.Name, this._timer.Duration);
                this._lastName = this._timer.Name;
                this.RebuildItems();
                this.RaiseItemsChanged();
                break;

            case nameof(TimerInstance.State):
                this._lastState = this._timer.State;
                this.SyncPageIcon();
                this.UpdateSummaryItem();
                this.RebuildItems();
                this.RaiseItemsChanged();
                break;

            case nameof(TimerInstance.CountdownDuration)
                when this._lastCountdownDuration != this._timer.CountdownDuration:
                this.RebuildItems();
                this.RaiseItemsChanged();
                break;

            case nameof(TimerInstance.AlarmSoundId):
                this.UpdateAlarmSoundItem();
                break;
        }
    }

    private void Settings_TimerPresentationChanged(object? sender, EventArgs e)
    {
        this.UpdateSummaryItem();
        if (this._uiUpdatesEnabled)
        {
            this.RaiseItemsChanged();
        }
    }

    public override IListItem[] GetItems() => this._items;

    private void RebuildItems()
    {
        var originalDuration = DurationParser.FormatClock(this._timer.Duration);
        var snoozeDuration = this._timer.SnoozeDuration;
        var items = new List<IListItem>
        {
            this._summaryItem,
            new ListItem
            {
                Title = snoozeDuration.HasValue ? Strings.Label_Snooze : Strings.Label_Duration,
                Subtitle = snoozeDuration.HasValue || this._timer.CountdownDuration != this._timer.Duration
                    ? ResourceFormatter.Format(Strings.Subtitle_AdjustedDurationFormat,
                        DurationParser.FormatClock(this._timer.CountdownDuration), originalDuration)
                    : originalDuration,
                Icon = Icons.GenericTimer
            },
            this._alarmSoundItem
        };

        items.Add(new GroupHeaderListItem(Strings.Page_Actions));
        var shortcutCommands = new List<IContextItem>();

        switch (this._timer.State)
        {
            case TimerState.Running:
                AddAction(items, shortcutCommands, this.CreatePauseCommand(), Strings.Action_Pause,
                    Icons.PauseStopwatch, Chords.Pause);
                AddAction(items, shortcutCommands, this.CreateStopCommand(), Strings.Action_Stop, Icons.StopStopwatch,
                    Chords.Stop);
                AddAction(items, shortcutCommands, this.CreateRestartCommand(), Strings.Action_Restart,
                    new IconInfo("\uE777"), Chords.Restart);
                break;

            case TimerState.Paused:
                AddAction(items, shortcutCommands, this.CreateResumeCommand(), Strings.Action_Resume,
                    Icons.StartStopwatch, Chords.Resume);
                AddAction(items, shortcutCommands, this.CreateStopCommand(), Strings.Action_Stop, Icons.StopStopwatch,
                    Chords.Stop);
                AddAction(items, shortcutCommands, this.CreateRestartCommand(), Strings.Action_Restart,
                    new IconInfo("\uE777"), Chords.Restart);
                break;

            case TimerState.Finished:
            case TimerState.Cancelled:
                AddAction(items, shortcutCommands, this.CreateRestartCommand(), Strings.Action_StartAgain,
                    Icons.StartStopwatch, Chords.Restart);
                break;
        }

        foreach (var item in AddTimerTimeCommand.CreateMenuGroup(this._timer.Id, this._timer.State, this._scheduler))
        {
            shortcutCommands.Add(item);
            if (item is ICommandContextItem command)
            {
                items.Add(new ListItem(command));
            }
        }

        AddAction(items, shortcutCommands, this.CreateDeleteCommand(), Strings.Action_Delete, Icons.Delete,
            Chords.Delete, true);

        this._summaryItem.MoreCommands = [.. shortcutCommands];
        this._items = [.. items];
        this._lastCountdownDuration = this._timer.CountdownDuration;
        this._lastWasSnoozed = snoozeDuration.HasValue;
    }

    private static void AddAction(
        List<IListItem> items,
        List<IContextItem> shortcutCommands,
        InvokableCommand command,
        string title,
        IconInfo icon,
        KeyChord shortcut,
        bool isCritical = false)
    {
        items.Add(new ListItem(command) { Title = title, Icon = icon });
        shortcutCommands.Add(new CommandContextItem(command)
        {
            Title = title, Icon = icon, IsCritical = isCritical, RequestedShortcut = shortcut
        });
    }

    private void UpdateSummaryItem()
    {
        this._summaryItem.Subtitle = this._timer.State == TimerState.Finished && this._timer.SnoozeDuration is null
            ? Strings.State_Finished
            : LabelFormatter.FormatTimerListSubtitle(this._timer,
                TimerServices.Settings.ShouldShowTimerSeconds(this._timer.Remaining));
        this._summaryItem.Icon = this.GetIconForCurrentState();
        this.Title = LabelFormatter.FormatTimerName(this._timer.Name, this._timer.Duration);
    }

    private void UpdateAlarmSoundItem() =>
        this._alarmSoundItem.Subtitle =
            AlarmSoundCatalog.Find(this._timer.AlarmSoundId)?.DisplayName ?? Strings.AlarmSound_UseDefault;

    private AnonymousCommand CreatePauseCommand()
    {
        return new AnonymousCommand(() => this._scheduler.Pause(this._timer.Id))
        {
            Name = Strings.Action_Pause, Result = CommandResult.KeepOpen()
        };
    }

    private AnonymousCommand CreateResumeCommand()
    {
        return new AnonymousCommand(() => this._scheduler.Resume(this._timer.Id))
        {
            Name = Strings.Action_Resume, Result = CommandResult.KeepOpen()
        };
    }

    private AnonymousCommand CreateStopCommand()
    {
        return new AnonymousCommand(() => this._scheduler.Stop(this._timer.Id))
        {
            Name = Strings.Action_Stop, Result = CommandResult.KeepOpen()
        };
    }

    private AnonymousCommand CreateRestartCommand()
    {
        return new AnonymousCommand(() => this._scheduler.Restart(this._timer.Id))
        {
            Name = Strings.Action_Restart, Result = CommandResult.KeepOpen()
        };
    }

    private DeletionConfirmationCommand CreateDeleteCommand()
    {
        var deleteCommand = new AnonymousCommand(() => this._scheduler.RemoveTimer(this._timer.Id))
        {
            Name = Strings.Action_Delete, Result = CommandResult.GoBack()
        };

        return new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteTimer_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteTimer_Description,
                LabelFormatter.FormatTimerName(this._timer.Name, this._timer.Duration)));
    }

    private IconInfo GetIconForCurrentState() =>
        this._timer.State switch
        {
            TimerState.Running => TimerProgressIconHelper.GetRunningTimerIcon(this._timer.CountdownDuration,
                this._timer.Remaining,
                TimerServices.Settings.AnimatedTimerIcons),
            TimerState.Paused => Icons.PausedTimer,
            TimerState.Finished => Icons.FinishedTimer,
            TimerState.Cancelled => Icons.CancelledTimer,
            _ => Icons.GenericTimer
        };

    private void SyncPageIcon()
    {
        if (this._pageIconState == this._timer.State)
        {
            return;
        }

        this.Icon = this._timer.State switch
        {
            TimerState.Running => Icons.RunningTimer,
            TimerState.Paused => Icons.PausedTimer,
            TimerState.Finished => Icons.FinishedTimer,
            TimerState.Cancelled => Icons.CancelledTimer,
            _ => Icons.GenericTimer
        };
        this._pageIconState = this._timer.State;
    }

    public void Dispose()
    {
        this.EnableUIUpdates(false);
    }
}