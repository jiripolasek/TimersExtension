// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension;

internal static class Icons
{
    // Timers

    public static IconInfo TimersIndex { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Index.light.png",
        "Assets/Icons/Timer_Index.dark.png");

    public static IconInfo NewTimer { get; }
        = IconHelpers.FromRelativePaths("Assets/Icons/Timer_New.light.png", "Assets/Icons/Timer_New.dark.png");

    public static IconInfo DismissTimer { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Dismiss.light.png",
        "Assets/Icons/Timer_Dismiss.dark.png");

    public static IconInfo RepeatTimer { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Repeat.light.png",
        "Assets/Icons/Timer_Repeat.dark.png");

    public static IconInfo StopTimer { get; }
        = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Stop.light.png", "Assets/Icons/Timer_Stop.dark.png");

    public static IconInfo PausedTimer { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Paused.light.png",
        "Assets/Icons/Timer_Paused.dark.png");

    public static IconInfo RunningTimer { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Running.light.png",
        "Assets/Icons/Timer_Running.dark.png");

    public static IconInfo FinishedTimer { get; }
        = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Finished.light.png",
            "Assets/Icons/Timer_Finished.dark.png");

    public static IconInfo CancelledTimer { get; }
        = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Stopped.light.png", "Assets/Icons/Timer_Stopped.dark.png");

    public static IconInfo GenericTimer { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Timer_Generic.light.png",
        "Assets/Icons/Timer_Generic.dark.png");

    // Stopwatch icons

    public static IconInfo StopwatchIndex { get; }
        = IconHelpers.FromRelativePaths("Assets/Icons/Stopwatch_Index.light.png",
            "Assets/Icons/Stopwatch_Index.dark.png");

    public static IconInfo StopwatchRunning { get; }
        = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Running.png");

    public static IconInfo StopwatchPaused { get; } = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Paused.png");

    public static IconInfo StopwatchStopped { get; }
        = IconHelpers.FromRelativePath("Assets/Icons/Stopwatch_Stopped.png");

    public static IconInfo Stopwatch { get; } = new("\uE916");

    public static IconInfo NewStopwatch { get; } = IconHelpers.FromRelativePaths("Assets/Icons/Stopwatch_New.light.png",
        "Assets/Icons/Stopwatch_New.png");

    public static IconInfo ResetStopwatch { get; } = new("\uE72C");

    public static IconInfo StopStopwatch { get; } = new("\uE71A");

    public static IconInfo StartStopwatch { get; } = new("\uE768");

    public static IconInfo PauseStopwatch { get; } = new("\uE769");

    public static IconInfo Lap { get; } = new("\uE7C1");

    // Generic

    public static IconInfo ChevronDown { get; } = new("\uE70D");

    public static IconInfo ChevronUp { get; } = new("\uE70E");

    public static IconInfo Filter { get; } = new("\uE71C");

    public static IconInfo Delete { get; } = new("\uE74D");

    public static IconInfo AlarmSound { get; } = new("\uE767");
}