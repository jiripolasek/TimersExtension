namespace JPSoftworks.TimersExtension;

internal static partial class Icons
{
    // Timers

    public static IconInfo TimersIndex { get; } = IconHelpers.FromRelativePath("Assets/Icons/Times_Index.png");

    public static IconInfo NewTimer { get; } = IconHelpers.FromRelativePath("Assets/Icons/Timer_New.png");

    public static IconInfo PausedTimer { get; } = IconHelpers.FromRelativePath("Assets/Icons/Timer_Paused.png");

    public static IconInfo RunningTimer { get; } = IconHelpers.FromRelativePath("Assets/Icons/Timer_Running.png");

    public static IconInfo FinishedTimer { get; } = IconHelpers.FromRelativePath("Assets/Icons/Timer_Finished.png");

    public static IconInfo CancelledTimer { get; } = IconHelpers.FromRelativePath("Assets/Icons/Timer_Running.png");

    public static IconInfo GenericTimer { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Generic.light.png", "Assets/Icons/Timer_Generic.dark.png");

    // Stopwatch icons

    public static IconInfo StopwatchIndex { get; } = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Index.png");

    public static IconInfo StopwatchRunning { get; } = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Running.png");

    public static IconInfo StopwatchPaused { get; } = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Paused.png");

    public static IconInfo StopwatchStopped { get; } = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Stopped.png");

    public static IconInfo Stopwatch { get; } = new IconInfo("\uE916");

    public static IconInfo NewStopwatch { get; } = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_New.png");

    public static IconInfo ResetStopwatch { get; } = new("\uE72C");

    public static IconInfo StopStopwatch { get; } = new("\uE71A");

    public static IconInfo StartStopwatch { get; } = new("\uE768");

    public static IconInfo PauseStopwatch { get; } = new("\uE769");

    public static IconInfo Lap { get; } = new("\uE7C1");

    // Generic

    public static IconInfo Delete { get; } = new("\uE74D");
}