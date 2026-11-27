// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class AddTimerTimeCommand : InvokableCommand
{
    private readonly Guid _timerId;
    private readonly TimeSpan _extra;
    private readonly TimerScheduler _scheduler;

    private AddTimerTimeCommand(Guid timerId, int minutes, TimerScheduler scheduler)
    {
        this._timerId = timerId;
        this._extra = TimeSpan.FromMinutes(minutes);
        this._scheduler = scheduler;
        this.Name = ResourceFormatter.Format(Strings.Action_AddMinutesFormat, minutes);
        this.Icon = new IconInfo("\uE710");
    }

    public static IContextItem[] CreateMenuGroup(Guid id, TimerState state, TimerScheduler scheduler)
    {
        if (!TimerScheduler.CanExtend(state))
        {
            return [];
        }

        return [new Separator(), CreateItem(1), CreateItem(5), new Separator()];

        CommandContextItem CreateItem(int minutes)
        {
            var command = new AddTimerTimeCommand(id, minutes, scheduler);
            return new CommandContextItem(command) { Icon = command.Icon };
        }
    }

    public override CommandResult Invoke()
    {
        if (!this._scheduler.TryExtend(this._timerId, this._extra))
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Toast_CannotExtendTimer, Result = CommandResult.KeepOpen()
            });
        }

        return CommandResult.KeepOpen();
    }
}