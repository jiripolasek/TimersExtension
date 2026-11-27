// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class AlarmServiceTests
{
    [Theory]
    [InlineData("action=snooze", AlarmClosedKind.Snooze)]
    [InlineData("action=restart", AlarmClosedKind.Restart)]
    [InlineData("action=stop", AlarmClosedKind.Stopped)]
    [InlineData("", AlarmClosedKind.Stopped)]
    public void GetActivationKindMapsToastActions(string arguments, AlarmClosedKind expected)
    {
        Assert.Equal(expected, AlarmService.GetActivationKind(arguments));
    }

    [Fact]
    public void SnoozeDurationIsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), AlarmService.SnoozeDuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissedAlarmUsesAPassiveToast(bool isMissed)
    {
        var request = new AlarmRequest
        {
            TimerId = Guid.NewGuid(),
            Title = "Tea",
            Message = "Finished",
            OriginalDuration = TimeSpan.FromMinutes(4),
            IsMissed = isMissed
        };

        var toast = AlarmService.CreateAlarmToast(request);

        Assert.Equal(!isMissed, toast.Content.GetXml().Contains("scenario=\"alarm\"", StringComparison.Ordinal));
        Assert.Equal(isMissed ? 0u : 3u, toast.Content.GetElementsByTagName("action").Length);
    }
}