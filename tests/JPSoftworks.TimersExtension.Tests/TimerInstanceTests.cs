// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class TimerInstanceTests
{
    [Fact]
    public void DurationValidationHonorsTheLastRepresentableTick()
    {
        var start = DateTimeOffset.MaxValue.AddSeconds(-1);

        Assert.True(TimerInstance.IsValidDuration(TimeSpan.FromSeconds(1), start));
        Assert.False(TimerInstance.IsValidDuration(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1), start));
    }

    [Fact]
    public void ExpectedEndSubtractsElapsedTimeBeforeAddingTheDuration()
    {
        var timer = new TimerInstance
        {
            State = TimerState.Running,
            StartedAt = new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero),
            Duration = TimeSpan.FromDays(3),
            ElapsedBeforePause = TimeSpan.FromDays(2)
        };

        Assert.Equal(new DateTimeOffset(9999, 12, 31, 0, 0, 0, TimeSpan.Zero), timer.ExpectedEnd);
    }

    [Fact]
    public void OlderOversizedTimerCanStillBeRenderedAndPersisted()
    {
        var timer = new TimerInstance
        {
            State = TimerState.Running, StartedAt = DateTimeOffset.UtcNow, Duration = TimeSpan.FromHours(100000000)
        };

        Assert.Null(timer.ExpectedEnd);
        Assert.NotEmpty(LabelFormatter.FormatTimerListSubtitle(timer));
        var json = JsonSerializer.Serialize(new[] { timer }, TimerJsonContext.Default.TimerInstanceArray);
        var restored = Assert.Single(JsonSerializer.Deserialize(json, TimerJsonContext.Default.TimerInstanceArray)!);
        Assert.Equal(timer.Duration, restored.Duration);
        Assert.Null(restored.ExpectedEnd);
    }
}