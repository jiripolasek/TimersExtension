using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class StartTimerCommand : InvokableCommand
{
    private readonly TimerDefinition _timerDefinition;
    private readonly TimerScheduler _timerScheduler;

    public StartTimerCommand(TimerDefinition timerDefinition, TimerScheduler timerScheduler)
    {
        _timerDefinition = timerDefinition;
        _timerScheduler = timerScheduler;
        Name = "Start Timer";
    }

    public override ICommandResult Invoke()
    {
        var timerInstance = _timerScheduler.StartTimer(_timerDefinition);
        return CommandResult.ShowToast(new ToastArgs()
        {
            Message = $"Timer {timerInstance.Name} started",
            Result = CommandResult.KeepOpen(),
        });
    }
}