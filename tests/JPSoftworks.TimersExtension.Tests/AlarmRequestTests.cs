// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class AlarmRequestTests
{
    [Theory]
    [InlineData(null, false, "aurora.wav", false)]
    [InlineData(null, true, null, true)]
    [InlineData("silent", false, null, true)]
    [InlineData("bright-bell", true, "bright-bell.wav", false)]
    [InlineData("unknown", false, "aurora.wav", false)]
    public void AlarmUsesTheTimerSoundCapturedAtCompletion(
        string? soundId,
        bool defaultSilent,
        string? expectedFile,
        bool expectedSilent)
    {
        var timer = new TimerInstance { Duration = TimeSpan.FromMinutes(5), AlarmSoundId = soundId };
        var completion = new TimerFinishedEventArgs(timer);
        timer.AlarmSoundId = "urgent-alert";
        var defaultSound = defaultSilent ? null : AlarmSoundCatalog.GetSoundUri("aurora.wav");

        var request = AlarmRequest.FromTimer(completion, defaultSound, 0.5, defaultSilent);

        Assert.Equal(expectedFile is null ? null : AlarmSoundCatalog.GetSoundUri(expectedFile), request.SoundUri);
        Assert.Equal(expectedSilent, request.IsSilent);
        Assert.Equal(0.5, request.Volume);
    }

    [Fact]
    public void FromTimerPreservesSilentAndVolumeOptions()
    {
        var timer = new TimerInstance { Name = "Tea", Duration = TimeSpan.FromMinutes(4) };

        var request = AlarmRequest.FromTimer(timer, null, 0.5, true);

        Assert.True(request.IsSilent);
        Assert.Null(request.SoundUri);
        Assert.Equal(0.5, request.Volume);
        Assert.Equal(timer.Id, request.TimerId);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(10, true)]
    public void RestoredAlarmShowsTheDateAcrossMidnight(int minutesOverdue, bool isMissed)
    {
        var restoredAt = new DateTimeOffset(new DateTime(2026, 11, 26, 0, 5, 0, DateTimeKind.Local));
        var finishedAt = restoredAt.AddMinutes(-minutesOverdue);
        var timer = new TimerInstance { Name = "Tea", Duration = TimeSpan.FromMinutes(4) };
        var completion = new TimerFinishedEventArgs(timer) { FinishedAt = finishedAt, DetectedAt = restoredAt };

        var request = AlarmRequest.FromTimer(completion);

        Assert.Equal(isMissed, request.IsMissed);
        Assert.Equal(isMissed, request.IsSilent);
        Assert.Equal(isMissed ? "Missed timer: Tea" : "Tea done", request.Title);
        Assert.Contains(finishedAt.ToString("g", CultureInfo.CurrentCulture), request.Message);
    }

    [Theory]
    [InlineData(0, "Snooze finished for Tea")]
    [InlineData(10, "Missed snooze for Tea")]
    public void AlarmIdentifiesSnoozeWhenTheDurationsMatch(int minutesLate, string title)
    {
        var finishedAt = DateTimeOffset.UtcNow;
        var timer = new TimerInstance
        {
            Name = "Tea", Duration = TimeSpan.FromMinutes(5), SnoozeDuration = TimeSpan.FromMinutes(5)
        };
        var completion = new TimerFinishedEventArgs(timer)
        {
            FinishedAt = finishedAt, DetectedAt = finishedAt.AddMinutes(minutesLate)
        };
        timer.TransitionToRestarted(finishedAt);

        Assert.Equal(title, AlarmRequest.FromTimer(completion).Title);
    }
}