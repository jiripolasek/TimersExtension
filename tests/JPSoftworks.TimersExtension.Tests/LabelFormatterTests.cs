// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class LabelFormatterTests
{
    [Fact]
    public void FinishedTimerShowsItsStateAndDurationInTheDock()
    {
        var timer = new TimerInstance { State = TimerState.Finished, Duration = TimeSpan.FromMinutes(5) };

        Assert.Equal("Finished • 5m", LabelFormatter.FormatTimerDockSubtitle(timer, true));
    }

    [Fact]
    public void RunningSnoozeIsLabeledInTheListAndDock()
    {
        var timer = new TimerInstance
        {
            State = TimerState.Running,
            Duration = TimeSpan.FromMinutes(30),
            SnoozeDuration = TimeSpan.FromMinutes(5),
            StartedAt = DateTimeOffset.UtcNow
        };

        Assert.StartsWith("Snooze • ", LabelFormatter.FormatTimerListSubtitle(timer));
        Assert.StartsWith("Snooze • ", LabelFormatter.FormatTimerDockSubtitle(timer, false));
    }

    [Fact]
    public void FinishedSnoozeShowsItsIntervalInTheListAndDock()
    {
        var timer = new TimerInstance
        {
            State = TimerState.Finished,
            Duration = TimeSpan.FromMinutes(30),
            SnoozeDuration = TimeSpan.FromMinutes(5)
        };

        Assert.Equal("Snooze finished • 5m", LabelFormatter.FormatTimerListSubtitle(timer));
        Assert.Equal("Snooze finished • 5m", LabelFormatter.FormatTimerDockSubtitle(timer, true));
    }

    [Fact]
    public void FormatTimerNameUsesDurationForEmptyName()
    {
        Assert.Equal("Timer 5m", LabelFormatter.FormatTimerName("  ", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void FormatTimerNameTrimsNamedTimer()
    {
        Assert.Equal("Tea", LabelFormatter.FormatTimerName("  Tea  ", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void FormatTemplateNameUsesDurationForEmptyName()
    {
        var template = new TimerDefinition { Name = string.Empty, Duration = TimeSpan.FromMinutes(25) };

        Assert.Equal("Timer 25m", LabelFormatter.FormatTemplateName(template));
    }

    [Fact]
    public void FormatStopwatchNameUsesDefaultForEmptyName()
    {
        Assert.Equal("Stopwatch", LabelFormatter.FormatStopwatchName(null));
    }

    [Theory]
    [InlineData("en-US", 61.129, "01:01.12")]
    [InlineData("fr-FR", 3661.129, "1:01:01,12")]
    [InlineData("fr-FR", -3661.129, "-1:01:01,12")]
    public void StopwatchTimeUsesTheRegionalDecimalSeparator(string cultureName, double seconds, string expected)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal(expected, LabelFormatter.FormatStopwatchTime(TimeSpan.FromSeconds(seconds)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void StopwatchListShowsOriginalStartThroughResumeAndThenTheFinish()
    {
        var started = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var resumed = started.AddDays(1);
        var finished = resumed.AddMinutes(1);
        var stopwatch = new StopwatchInstance();
        var localStart = started.ToLocalTime();
        var localFinish = finished.ToLocalTime();
        var today = DateOnly.FromDateTime(localFinish.DateTime);
        var startTime = localStart.ToString("t", CultureInfo.CurrentCulture);
        var finishTime = localFinish.ToString("t", CultureInfo.CurrentCulture);

        stopwatch.TransitionToRunning(started);
        stopwatch.TransitionToPaused(started.AddMinutes(2), TimeSpan.FromMinutes(2));
        Assert.EndsWith($"Started yesterday at {startTime}",
            LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today));

        stopwatch.TransitionToRunning(resumed);
        Assert.EndsWith($"Started yesterday at {startTime}",
            LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today));

        stopwatch.TransitionToStopped(finished, TimeSpan.FromMinutes(3));
        Assert.Equal($"03:00 total · Finished today at {finishTime}",
            LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today));
        Assert.Equal($"03:00 total · Finished yesterday at {finishTime}",
            LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today.AddDays(1)));
        var finishDateAndTime = localFinish.ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal($"03:00 total · Finished {finishDateAndTime}",
            LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today.AddDays(2)));

        stopwatch.TransitionToReset();
        Assert.Equal("Ready to start", LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today));
        stopwatch.TransitionToRunning(finished);
        Assert.EndsWith($"Started today at {finishTime}", LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today));
    }

    [Fact]
    public void LegacyStopwatchUsesItsLastRecordedStart()
    {
        var started = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var stopwatch = new StopwatchInstance { State = StopwatchState.Paused, StartedAt = started };
        var localStart = started.ToLocalTime();
        var today = DateOnly.FromDateTime(localStart.DateTime).AddDays(2);

        Assert.EndsWith($"Last started {localStart.ToString("g", CultureInfo.CurrentCulture)}",
            LabelFormatter.FormatStopwatchListSubtitle(stopwatch, today));
    }
}