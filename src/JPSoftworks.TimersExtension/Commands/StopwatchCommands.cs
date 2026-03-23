using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed class CreateStopwatchCommand : InvokableCommand
{
    private readonly StopwatchScheduler _scheduler;

    public CreateStopwatchCommand(StopwatchScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.CreateAndStart();
        return CommandResult.KeepOpen();
    }
}

internal sealed class StartStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public StartStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.Start(_stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed class PauseStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public PauseStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.Pause(_stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed class ResumeStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public ResumeStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.Resume(_stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed class StopStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public StopStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.Stop(_stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed class ResetStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public ResetStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.Reset(_stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed class LapStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public LapStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.RecordLap(_stopwatchId);
        return CommandResult.KeepOpen();
    }
}

internal sealed class DeleteStopwatchCommand : InvokableCommand
{
    private readonly Guid _stopwatchId;
    private readonly StopwatchScheduler _scheduler;

    public DeleteStopwatchCommand(Guid stopwatchId, StopwatchScheduler scheduler)
    {
        _stopwatchId = stopwatchId;
        _scheduler = scheduler;
    }

    public override CommandResult Invoke()
    {
        _scheduler.Remove(_stopwatchId);
        return CommandResult.GoBack();
    }
}