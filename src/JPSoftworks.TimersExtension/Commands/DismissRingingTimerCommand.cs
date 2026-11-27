// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class DismissRingingTimerCommand : InvokableCommand
{
    private readonly AlarmService _alarmService;

    public DismissRingingTimerCommand(AlarmService alarmService)
    {
        this._alarmService = alarmService;
        this.Id = CommandIds.DismissRingingTimer;
        this.Name = Strings.Action_DismissRingingTimer;
        this.Icon = Icons.DismissTimer;
    }

    public override CommandResult Invoke()
    {
        var dismissed = this._alarmService.TryDismissRingingAlarm();
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = dismissed ? Strings.Toast_TimerAlarmDismissed : Strings.Toast_NoRingingTimer,
            Result = dismissed ? CommandResult.Dismiss() : CommandResult.KeepOpen()
        });
    }
}