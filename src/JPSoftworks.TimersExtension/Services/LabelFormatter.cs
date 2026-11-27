// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

internal static class LabelFormatter
{
    public static string FormatTimerName(string? name, TimeSpan duration)
    {
        return string.IsNullOrWhiteSpace(name)
            ? ResourceFormatter.Format(Strings.Label_DefaultTimerNameFormat, DurationParser.FormatCompact(duration))
            : name.Trim();
    }

    public static string FormatTemplateName(TimerDefinition template) =>
        FormatTimerName(template.Name, template.Duration);

    public static string FormatTemplateSubtitle(TimerDefinition template)
    {
        return string.IsNullOrWhiteSpace(template.Name)
            ? Strings.Label_SavedTemplate
            : FormatTemplateDuration(template.Duration);
    }

    public static string FormatTemplateDuration(TimeSpan duration) =>
        duration >= TimeSpan.FromMinutes(1) && duration < TimeSpan.FromHours(1) &&
        duration.Ticks % TimeSpan.TicksPerMinute == 0
            ? ResourceFormatter.Format(Strings.Duration_MinutesLabelFormat, duration.Minutes)
            : DurationParser.FormatCompact(duration);

    public static string FormatTemplateTitle(string? name, TimeSpan duration)
    {
        var title = FormatTimerName(name, duration);
        return string.IsNullOrWhiteSpace(name)
            ? title
            : ResourceFormatter.Format(Strings.Label_NameAndTimeFormat, title, FormatTemplateDuration(duration));
    }

    public static string FormatTimerTargetSubtitle(TimerInstance? timer) =>
        timer is null ? string.Empty : FormatTimerTargetSubtitle(timer.Name, timer.Duration);

    public static string FormatTimerTargetSubtitle(string? name, TimeSpan duration)
    {
        var defaultName = FormatTimerName(null, duration);
        return string.Equals(name, defaultName, StringComparison.Ordinal)
            ? defaultName
            : FormatTemplateTitle(name, duration);
    }

    public static string FormatStopwatchName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? Strings.Label_DefaultStopwatchName : name.Trim();
    }

    public static string FormatTimerListSubtitle(TimerInstance timer, bool showSeconds = true)
    {
        var state = timer.State;
        var isSnooze = timer.SnoozeDuration is not null;
        if (state == TimerState.Finished)
        {
            if (timer.PausedAt is { } finishedAt)
            {
                var local = finishedAt.ToLocalTime();
                var format = local.Date == DateTime.Today ? "t" : "g";
                return ResourceFormatter.Format(
                    isSnooze ? Strings.Subtitle_TimerSnoozeFinishedAtFormat : Strings.Subtitle_TimerFinishedAtFormat,
                    local.ToString(format, CultureInfo.CurrentCulture));
            }

            return ResourceFormatter.Format(
                isSnooze ? Strings.Subtitle_TimerSnoozeFinishedFormat : Strings.Subtitle_TimerFinishedFormat,
                DurationParser.FormatCompact(timer.CountdownDuration));
        }

        var remaining = FormatTimerRemaining(timer, showSeconds);
        var subtitle = state switch
        {
            TimerState.Running when timer.ExpectedEnd is { } end => ResourceFormatter.Format(
                Strings.Subtitle_TimerRunningWithEndFormat, remaining,
                end.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
            TimerState.Paused => ResourceFormatter.Format(Strings.Subtitle_TimerPausedFormat, remaining),
            TimerState.Cancelled => ResourceFormatter.Format(Strings.Subtitle_TimerStoppedFormat, remaining),
            _ => remaining
        };
        return isSnooze ? ResourceFormatter.Format(Strings.Subtitle_TimerSnoozeFormat, subtitle) : subtitle;
    }

    public static string FormatEndTime(
        DateTimeOffset end,
        DateTimeOffset now,
        TimeZoneInfo zone,
        bool showSeconds = false)
    {
        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
        var sameDay = localEnd.Date == TimeZoneInfo.ConvertTime(now, zone).Date;
        var format = sameDay ? showSeconds ? "T" : "t" : showSeconds ? "G" : "g";
        return ResourceFormatter.Format(Strings.Preview_EndsAtFormat,
            localEnd.ToString(format, CultureInfo.CurrentCulture));
    }

    public static string FormatTimerDockSubtitle(TimerInstance timer, bool nameInTitle)
    {
        if (timer.State == TimerState.Finished)
        {
            return FormatTimerListSubtitle(timer);
        }

        var subtitle = timer.State switch
        {
            _ when !nameInTitle => FormatTimerName(timer.Name, timer.Duration),
            TimerState.Running when timer.ExpectedEnd is { } end => FormatEndTime(end, DateTimeOffset.UtcNow,
                TimeZoneInfo.Local),
            TimerState.Cancelled => Strings.State_Stopped,
            _ => FormatTemplateDuration(timer.CountdownDuration)
        };
        return timer.SnoozeDuration.HasValue
            ? ResourceFormatter.Format(Strings.Subtitle_TimerSnoozeFormat, subtitle)
            : subtitle;
    }

    private static string FormatTimerRemaining(TimerInstance timer, bool showSeconds) =>
        ResourceFormatter.Format(Strings.Subtitle_TimerLeftFormat,
            DurationParser.FormatCountdown(timer.Remaining, showSeconds));

    public static string FormatStopwatchListSubtitle(StopwatchInstance stopwatch, DateOnly? today = null)
    {
        var localDate = today ?? DateOnly.FromDateTime(DateTime.Today);
        var state = stopwatch.State;
        var elapsed = ResourceFormatter.Format(Strings.Subtitle_StopwatchElapsedFormat,
            DurationParser.FormatClock(stopwatch.Elapsed));
        var subtitle = state switch
        {
            StopwatchState.Pending => Strings.Subtitle_StopwatchReadyToStart,
            StopwatchState.Running => elapsed,
            StopwatchState.Paused => ResourceFormatter.Format(Strings.Subtitle_StopwatchPausedFormat, elapsed),
            StopwatchState.Stopped => ResourceFormatter.Format(Strings.Subtitle_StopwatchTotalFormat,
                DurationParser.FormatClock(stopwatch.Elapsed)),
            _ => elapsed
        };

        var timestamp = state switch
        {
            StopwatchState.Stopped when stopwatch.StoppedAt is { } stopped => FormatStopwatchTimestamp(
                Strings.Subtitle_StopwatchFinishedAtFormat, stopped, localDate),
            StopwatchState.Pending => null,
            _ when stopwatch.RunStartedAt is { } started => FormatStopwatchTimestamp(
                Strings.Subtitle_StopwatchStartedAtFormat, started, localDate),
            _ when stopwatch.StartedAt is { } lastStarted => FormatStopwatchTimestamp(
                Strings.Subtitle_StopwatchLastStartedAtFormat, lastStarted, localDate),
            _ => null
        };
        return timestamp is null
            ? subtitle
            : ResourceFormatter.Format(Strings.Label_NameAndTimeFormat, subtitle, timestamp);
    }

    private static string FormatStopwatchTimestamp(string format, DateTimeOffset timestamp, DateOnly today)
    {
        var localTime = timestamp.ToLocalTime();
        var daysAgo = today.DayNumber - DateOnly.FromDateTime(localTime.DateTime).DayNumber;
        var dateAndTime = daysAgo switch
        {
            0 => ResourceFormatter.Format(Strings.Label_TodayAtFormat,
                localTime.ToString("t", CultureInfo.CurrentCulture)),
            1 => ResourceFormatter.Format(Strings.Label_YesterdayAtFormat,
                localTime.ToString("t", CultureInfo.CurrentCulture)),
            _ => localTime.ToString("g", CultureInfo.CurrentCulture)
        };

        return ResourceFormatter.Format(format, dateAndTime);
    }

    public static string FormatStopwatchTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
        }

        return $"{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
    }
}