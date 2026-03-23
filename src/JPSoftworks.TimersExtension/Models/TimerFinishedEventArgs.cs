namespace JPSoftworks.TimersExtension.Models;

public sealed class TimerFinishedEventArgs : EventArgs
{
    public TimerFinishedEventArgs(TimerInstance timer)
    {
        Timer = timer;
    }

    public TimerInstance Timer { get; }
}