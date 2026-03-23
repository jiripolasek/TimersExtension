using System.IO;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension;

internal static class TimerServices
{
    public static TimerStorage Storage { get; }
    public static TimerScheduler Scheduler { get; }

    static TimerServices()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerToys", "CommandPalette", "Timers");
        Storage = new TimerStorage(baseDir);
        Scheduler = new TimerScheduler(Storage);
    }
}
