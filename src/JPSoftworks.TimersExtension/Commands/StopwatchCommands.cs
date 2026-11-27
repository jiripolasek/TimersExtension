// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class CreateStopwatchCommand : InvokableCommand
{
    private readonly StopwatchScheduler _scheduler;

    public CreateStopwatchCommand(StopwatchScheduler scheduler)
    {
        this._scheduler = scheduler;
        this.Id = CommandIds.StopwatchCreate;
        this.Name = Strings.Action_StartStopwatch;
        this.Icon = Icons.NewStopwatch;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.CreateAndStart();
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = Strings.Fallback_StartedStopwatch, Result = CommandResult.KeepOpen()
        });
    }
}

internal sealed partial class StartStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public StartStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Start;
        this.Icon = Icons.StartStopwatch;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.Start(this._stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class PauseStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public PauseStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Pause;
        this.Icon = Icons.PauseStopwatch;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.Pause(this._stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class ResumeStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public ResumeStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Resume;
        this.Icon = Icons.StartStopwatch;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.Resume(this._stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class StopStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public StopStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_FinishStopwatch;
        this.Icon = Icons.StopStopwatch;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.Stop(this._stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class ResetStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public ResetStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Reset;
        this.Icon = Icons.ResetStopwatch;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.Reset(this._stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class LapStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public LapStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Lap;
        this.Icon = Icons.Lap;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.RecordLap(this._stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class DeleteStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;
    private readonly string? _pageId;
    private readonly NavigationMode _navigationMode = NavigationMode.Push;

    public DeleteStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        this._stopwatchId = stopwatchId;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Delete;
        this.Icon = Icons.Delete;
    }

    public DeleteStopwatchCommand(
        Guid stopwatchId,
        StopwatchScheduler scheduler,
        string pageId,
        NavigationMode navigationMode)
        : this(stopwatchId, scheduler)
    {
        this._pageId = pageId;
        this._navigationMode = navigationMode;
    }

    public override CommandResult Invoke()
    {
        this._scheduler.Remove(this._stopwatchId);
        if (!string.IsNullOrWhiteSpace(this._pageId) && this._navigationMode == NavigationMode.GoBack)
        {
            return CommandResult.GoBack();
        }

        return CommandResult.KeepOpen();
    }
}