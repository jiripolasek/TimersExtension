// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class RepeatLastTimerCommand : InvokableCommand
{
    private readonly TimerScheduler _scheduler;
    private readonly Guid? _timerId;

    public RepeatLastTimerCommand(TimerScheduler scheduler, Guid? timerId = null)
    {
        this._scheduler = scheduler;
        this._timerId = timerId;
        if (!timerId.HasValue)
        {
            this.Id = CommandIds.TimerRepeatLast;
        }

        this.Name = Strings.Action_RepeatLastTimer;
        this.Icon = Icons.RepeatTimer;
    }

    private TimerInstance? GetTarget()
    {
        if (this._timerId is { } id)
        {
            return this._scheduler.TryGetTimer(id);
        }

        return this._scheduler.GetLastStartedTimer();
    }

    internal string GetTargetSubtitle() => LabelFormatter.FormatTimerTargetSubtitle(this.GetTarget());

    public override CommandResult Invoke()
    {
        if (this.GetTarget() is not { } target || this._scheduler.Restart(target.Id) is not { } timer)
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Toast_NoTimerToRepeat, Result = CommandResult.KeepOpen()
            });
        }

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = ResourceFormatter.Format(Strings.Toast_RepeatedTimerFormat,
                LabelFormatter.FormatTimerName(timer.Name, timer.Duration)),
            Result = CommandResult.KeepOpen()
        });
    }
}