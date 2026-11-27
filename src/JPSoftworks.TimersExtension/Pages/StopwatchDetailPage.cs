// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Threading;
using Windows.Foundation;
using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class StopwatchDetailPage : ListPage, INotifyItemsChanged, IDisposable
{
    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            lock (this._gate)
            {
                this._itemsChangedSubscriberCount++;
                this.ItemsChanged += value;
                this.EnableUIUpdates(true);
            }

            this._refreshes.Drain();
        }
        remove
        {
            lock (this._gate)
            {
                this.ItemsChanged -= value;
                this._itemsChangedSubscriberCount = Math.Max(0, this._itemsChangedSubscriberCount - 1);
                this.EnableUIUpdates(this._itemsChangedSubscriberCount > 0);
            }
        }
    }

    private const int SoftRefreshItemCount = -2;

    private readonly StopwatchInstance _stopwatch;
    private readonly StopwatchScheduler _scheduler;
    private readonly LapStopwatchCommand _lapCommand;
    private readonly ElapsedCommand _elapsedCommand;
    private readonly ListItem _elapsedItem;
    private readonly ListItem _lapTimeItem;
    private readonly Lock _gate = new();
    private readonly NotificationQueue _refreshes;
    private ListItem? _pauseResumeItem;
    private CommandContextItem? _pauseResumeShortcutItem;
    private volatile IListItem[] _items = [];
    private int _itemsChangedSubscriberCount;
    private IReadOnlyList<LapRecord> _lastLaps = [];
    private StopwatchState _lastState;
    private volatile bool _uiUpdatesEnabled;

    public StopwatchDetailPage(StopwatchInstance stopwatch, StopwatchScheduler scheduler)
    {
        this._stopwatch = stopwatch;
        this._scheduler = scheduler;
        this._refreshes = new NotificationQueue(this._gate);
        this._lapCommand = new LapStopwatchCommand(stopwatch.Id, scheduler);
        this._elapsedCommand = new ElapsedCommand { Action = this._lapCommand };

        this.Id = CommandIds.StopwatchDetail(stopwatch.Id);
        this.Icon = Icons.Stopwatch;
        this.Title = LabelFormatter.FormatStopwatchName(stopwatch.Name);
        this.Name = Strings.Page_Details;

        this._elapsedItem = new ListItem(this._elapsedCommand)
        {
            Title = Strings.Label_Elapsed,
            Subtitle = LabelFormatter.FormatStopwatchTime(this._stopwatch.Elapsed),
            Icon = Icons.Stopwatch
        };

        this._lapTimeItem = new ListItem
        {
            Title = Strings.Label_CurrentLap,
            Subtitle = LabelFormatter.FormatStopwatchTime(this._stopwatch.CurrentLapTime ?? TimeSpan.Zero),
            Icon = Icons.Lap
        };

        this.RebuildItems(stopwatch.State, stopwatch.Laps);
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
            this._stopwatch.PropertyChanged += this.Stopwatch_PropertyChanged;
            this._refreshes.Enqueue(() => this.RefreshItems(true));
        }
        else
        {
            this._stopwatch.PropertyChanged -= this.Stopwatch_PropertyChanged;
        }
    }

    private void Stopwatch_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StopwatchInstance.Elapsed):
                this.UpdateTimeDisplays();
                break;

            case nameof(StopwatchInstance.Laps):
            case nameof(StopwatchInstance.State):
                lock (this._gate)
                {
                    if (!this._uiUpdatesEnabled)
                    {
                        return;
                    }

                    this._refreshes.Enqueue(() => this.RefreshItems(false));
                }

                this._refreshes.Drain();
                break;
        }
    }

    private void RefreshItems(bool opening)
    {
        if (!this._uiUpdatesEnabled)
        {
            return;
        }

        if (opening)
        {
            this.UpdateTimeDisplays();
        }

        // Read the model when the queued refresh runs, including after a newer state change.
        var state = this._stopwatch.State;
        var laps = this._stopwatch.Laps;
        var contentChanged = this._lastLaps != laps || this._lastState != state;
        if (opening || contentChanged)
        {
            this.RebuildItems(state, laps);
        }

        if (contentChanged)
        {
            this.RaiseItemsChanged(opening ? -1 : SoftRefreshItemCount);
        }
    }

    private void UpdateTimeDisplays()
    {
        this._elapsedItem.Subtitle = LabelFormatter.FormatStopwatchTime(this._stopwatch.Elapsed);
        this._lapTimeItem.Subtitle
            = LabelFormatter.FormatStopwatchTime(this._stopwatch.CurrentLapTime ?? TimeSpan.Zero);
    }

    public override IListItem[] GetItems() => this._items;

    private void RebuildItems(StopwatchState state, IReadOnlyList<LapRecord> laps)
    {
        var items = new List<IListItem> { this._elapsedItem };
        if (laps.Count > 0 && state is StopwatchState.Running or StopwatchState.Paused)
        {
            items.Add(this._lapTimeItem);
        }

        // Actions section
        items.Add(new GroupHeaderListItem(Strings.Page_Actions));
        var shortcutCommands = new List<CommandContextItem>();

        switch (state)
        {
            case StopwatchState.Pending:
                var startCommand = new StartStopwatchCommand(this._stopwatch.Id, this._scheduler);
                this._elapsedCommand.Action = startCommand;
                items.Add(new ListItem(startCommand) { Icon = startCommand.Icon });
                shortcutCommands.Add(new CommandContextItem(this.CreateCopyElapsedTimeCommand()));
                break;

            case StopwatchState.Running:
                this._elapsedCommand.Action = this._lapCommand;
                this.AddPauseResumeAction(items, shortcutCommands, false);
                AddAction(items, shortcutCommands, this._lapCommand, Strings.Action_Lap, Icons.Lap, Chords.Lap);
                AddAction(items, shortcutCommands, new StopStopwatchCommand(this._stopwatch.Id, this._scheduler),
                    Strings.Action_FinishStopwatch, Icons.StopStopwatch, Chords.Stop);
                break;

            case StopwatchState.Paused:
                shortcutCommands.Add(new CommandContextItem(this.CreateCopyElapsedTimeCommand()));
                this._elapsedCommand.Action = this.AddPauseResumeAction(items, shortcutCommands, true);
                AddAction(items, shortcutCommands, new ResetStopwatchCommand(this._stopwatch.Id, this._scheduler),
                    Strings.Action_Reset, Icons.ResetStopwatch, Chords.Reset);
                AddAction(items, shortcutCommands, new StopStopwatchCommand(this._stopwatch.Id, this._scheduler),
                    Strings.Action_FinishStopwatch, Icons.StopStopwatch, Chords.Stop);
                break;

            case StopwatchState.Stopped:
                this._elapsedCommand.Action = this.CreateCopyElapsedTimeCommand();
                AddAction(items, shortcutCommands, new ResetStopwatchCommand(this._stopwatch.Id, this._scheduler),
                    Strings.Action_Reset, Icons.ResetStopwatch, Chords.Reset);
                break;
        }

        var deleteCommand = new DeleteStopwatchCommand(this._stopwatch.Id, this._scheduler, StopwatchIndexPage.PageId,
            NavigationMode.GoBack);
        var confirmedDeleteCommand = new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteStopwatch_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteStopwatch_Description,
                LabelFormatter.FormatStopwatchName(this._stopwatch.Name)));

        AddAction(items, shortcutCommands, confirmedDeleteCommand, Strings.Action_Delete, Icons.Delete, Chords.Delete,
            true);
        this._elapsedItem.MoreCommands = [.. shortcutCommands];

        // Laps section
        var lapCount = laps.Count;
        if (lapCount > 0)
        {
            items.Add(new GroupHeaderListItem(Strings.Label_Laps));

            int fastestIndex = -1;
            int slowestIndex = -1;

            if (lapCount >= 2)
            {
                var fastestTime = TimeSpan.MaxValue;
                var slowestTime = TimeSpan.MinValue;

                for (var i = 0; i < lapCount; i++)
                {
                    var lapTime = laps[i].LapTime;
                    if (lapTime < fastestTime)
                    {
                        fastestTime = lapTime;
                        fastestIndex = i;
                    }

                    if (lapTime > slowestTime)
                    {
                        slowestTime = lapTime;
                        slowestIndex = i;
                    }
                }

                if (fastestTime == slowestTime)
                {
                    fastestIndex = -1;
                    slowestIndex = -1;
                }
            }

            for (var i = lapCount - 1; i >= 0; i--)
            {
                var lap = laps[i];
                var tags = new List<Tag>();

                if (i == fastestIndex)
                {
                    tags.Add(new Tag(Strings.Stopwatch_Fastest));
                }

                if (i == slowestIndex)
                {
                    tags.Add(new Tag(Strings.Stopwatch_Slowest));
                }

                items.Add(new ListItem
                {
                    Title = ResourceFormatter.Format(Strings.Stopwatch_LapTitleFormat, lap.Number),
                    Subtitle = ResourceFormatter.Format(Strings.Stopwatch_LapSubtitleFormat,
                        LabelFormatter.FormatStopwatchTime(lap.LapTime),
                        LabelFormatter.FormatStopwatchTime(lap.TotalTime)),
                    Icon = Icons.Lap,
                    Tags = [.. tags]
                });
            }
        }

        this._items = [.. items];
        this._lastState = state;
        this._lastLaps = laps;
    }

    private CopyTextCommand CreateCopyElapsedTimeCommand() =>
        new(LabelFormatter.FormatStopwatchTime(this._stopwatch.Elapsed)) { Name = Strings.Action_CopyElapsedTime };

    private InvokableCommand AddPauseResumeAction(
        List<IListItem> items,
        List<CommandContextItem> shortcutCommands,
        bool resume)
    {
        InvokableCommand command = resume
            ? new ResumeStopwatchCommand(this._stopwatch.Id, this._scheduler)
            : new PauseStopwatchCommand(this._stopwatch.Id, this._scheduler);
        var title = resume ? Strings.Action_Resume : Strings.Action_Pause;
        var icon = resume ? Icons.StartStopwatch : Icons.PauseStopwatch;
        var shortcut = resume ? Chords.Resume : Chords.Pause;

        if (this._pauseResumeItem is null)
        {
            this._pauseResumeItem = new ListItem(command);
        }
        else
        {
            this._pauseResumeItem.Command = command;
        }

        this._pauseResumeItem.Title = title;
        this._pauseResumeItem.Icon = icon;
        items.Add(this._pauseResumeItem);

        if (this._pauseResumeShortcutItem is null)
        {
            this._pauseResumeShortcutItem = new CommandContextItem(command);
        }
        else
        {
            this._pauseResumeShortcutItem.Command = command;
        }

        this._pauseResumeShortcutItem.Title = title;
        this._pauseResumeShortcutItem.Icon = icon;
        this._pauseResumeShortcutItem.RequestedShortcut = shortcut;
        shortcutCommands.Add(this._pauseResumeShortcutItem);
        return command;
    }

    private static void AddAction(
        List<IListItem> items,
        List<CommandContextItem> shortcutCommands,
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

    public void Dispose()
    {
        lock (this._gate)
        {
            this.EnableUIUpdates(false);
        }
    }

    // Retail CmdPal displays the row title in More when a list item's command is replaced.
    private sealed partial class ElapsedCommand : InvokableCommand
    {
        public required InvokableCommand Action
        {
            get;
            set
            {
                field = value;
                this.Icon = value.Icon;
                this.Name = value.Name;
            }
        }

        public override ICommandResult Invoke() => this.Action.Invoke();
    }
}