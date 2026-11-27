// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerIndexFilters : Filters
{
    internal const string All = "all";
    internal const string Running = "running";
    internal const string Paused = "paused";
    internal const string Finished = "finished";
    internal const string Stopped = "stopped";
    internal const string SavedTemplates = "savedTemplates";

    public bool IncludesTemplates => this.CurrentFilterId is All or SavedTemplates;

    public TimerIndexFilters()
    {
        this.CurrentFilterId = All;
    }

    public bool IncludesTimer(TimerState state) =>
        this.CurrentFilterId switch
        {
            Running => state == TimerState.Running,
            Paused => state == TimerState.Paused,
            Finished => state == TimerState.Finished,
            Stopped => state == TimerState.Cancelled,
            SavedTemplates => false,
            _ => true
        };

    public override IFilterItem[] GetFilters() =>
    [
        new Filter { Id = All, Name = Strings.Filter_All, Icon = Icons.Filter },
        new Separator(),
        new Filter { Id = Running, Name = Strings.State_Running, Icon = Icons.RunningTimer },
        new Filter { Id = Paused, Name = Strings.State_Paused, Icon = Icons.PausedTimer },
        new Filter { Id = Finished, Name = Strings.State_Finished, Icon = Icons.FinishedTimer },
        new Filter { Id = Stopped, Name = Strings.State_Stopped, Icon = Icons.CancelledTimer },
        new Separator(),
        new Filter { Id = SavedTemplates, Name = Strings.Section_SavedTemplates, Icon = Icons.GenericTimer }
    ];
}

internal sealed partial class StopwatchIndexFilters : Filters
{
    internal const string All = "all";
    internal const string Running = "running";
    internal const string Paused = "paused";
    internal const string Stopped = "stopped";
    internal const string Ready = "ready";

    public bool IsAllSelected => this.CurrentFilterId == All;

    public StopwatchIndexFilters()
    {
        this.CurrentFilterId = All;
    }

    public bool Includes(StopwatchState state) =>
        this.CurrentFilterId switch
        {
            Running => state == StopwatchState.Running,
            Paused => state == StopwatchState.Paused,
            Stopped => state == StopwatchState.Stopped,
            Ready => state == StopwatchState.Pending,
            _ => true
        };

    public override IFilterItem[] GetFilters() =>
    [
        new Filter { Id = All, Name = Strings.Filter_All, Icon = Icons.Filter },
        new Separator(),
        new Filter { Id = Running, Name = Strings.State_Running, Icon = Icons.StopwatchRunning },
        new Filter { Id = Paused, Name = Strings.State_Paused, Icon = Icons.StopwatchPaused },
        new Filter { Id = Stopped, Name = Strings.State_Finished, Icon = Icons.StopwatchStopped },
        new Filter { Id = Ready, Name = Strings.State_Ready, Icon = Icons.Stopwatch }
    ];
}