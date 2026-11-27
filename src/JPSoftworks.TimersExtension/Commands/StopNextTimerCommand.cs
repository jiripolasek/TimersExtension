// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class StopNextTimerCommand : InvokableCommand
{
    private readonly TimerScheduler _scheduler;

    public StopNextTimerCommand(TimerScheduler scheduler)
    {
        this._scheduler = scheduler;
        this.Id = CommandIds.StopNextTimer;
        this.Name = Strings.Action_StopNextTimer;
        this.Icon = Icons.StopTimer;
    }

    public override CommandResult Invoke()
    {
        var stopped = this._scheduler.StopNextRunningTimer();
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = stopped is null
                ? Strings.Toast_NoRunningTimer
                : ResourceFormatter.Format(Strings.Toast_StoppedTimerFormat,
                    LabelFormatter.FormatTimerName(stopped.Name, stopped.Duration)),
            Result = stopped is null ? CommandResult.KeepOpen() : CommandResult.Dismiss()
        });
    }
}