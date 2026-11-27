# Changelog

All notable changes to Timers for Command Palette are documented in this file.

## [0.2.0]

First public release of Timers for Command Palette, with MSIX packages for x64 and ARM64.

### Features

- Run named countdown timers and stopwatches side by side. Timers, reusable templates, stopwatches, and recorded laps are saved across restarts.
- Start a timer from Command Palette Home or the Timers page by entering a duration such as `Focus 25m` or a local finish time such as `until 15:30`. Results preview the finish time or countdown duration.
- Start a saved timer template or a named stopwatch from Home. Use **Repeat last timer**, **Stop next timer**, or **Dismiss ringing timer** for quick control.
- Search timers and stopwatches by name, filter by state, and expand older finished results. Use **Clear finished timers…** to clear finished and stopped countdowns from the overview.
- Record stopwatch laps with split and total times, see the fastest and slowest laps, and copy elapsed time.
- Choose an alarm sound per timer or template, preview the default sound and volume in settings, or use silent notifications. Snooze a finished timer for five minutes.
- Add one or five minutes to a countdown from its list row, details, or Dock menu. Use **Restart** to start the countdown at its original duration.
- Pin individual timers, stopwatches, and saved templates to the Dock. Add bands for all active items, the latest active item, or quick access to each list. Live buttons show remaining or elapsed time in Compact mode.
- Choose when countdowns show seconds, enable animated timer icons, and configure deletion confirmations.

### First Dock shortcut

1. In **Timers**, choose **New timer**, enter `Tea` and `4m`, and enable **Save as template** before starting the timer.
2. Under **Saved templates**, right-click **Tea** (**4 min**) and choose **Pin to Dock**.
3. Click the pinned template. The timer starts and Command Palette shows **Started Tea · 4 min**.

You can choose which templates to pin and arrange them using the Dock's controls. [Command Palette Dock](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/dock#pin-commands-to-the-dock)
