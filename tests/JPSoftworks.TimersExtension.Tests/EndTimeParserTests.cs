// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class EndTimeParserTests
{
    [Theory]
    [InlineData(2, 30, 2, "02:45", 15, 2)]
    [InlineData(2, 50, 2, "02:45", 55, 1)]
    [InlineData(2, 30, 1, "02:45", 15, 1)]
    [InlineData(2, 45, 2, "02:45", 60, 1)]
    public void RollbackSelectsTheEarliestFutureOccurrence(
        int hour,
        int minute,
        int offset,
        string target,
        int minutes,
        int expectedOffset)
    {
        var now = new DateTimeOffset(2026, 10, 25, hour, minute, 0, TimeSpan.FromHours(offset));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

        Assert.True(EndTimeParser.TryGetDuration(target, now, zone, out var duration, out var end));
        Assert.Equal(TimeSpan.FromMinutes(minutes), duration);
        Assert.Equal(TimeSpan.FromHours(expectedOffset), end.Offset);
    }

    [Fact]
    public void SpringForwardRejectsANonexistentUpcomingTime()
    {
        var now = new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.FromHours(1));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

        Assert.False(EndTimeParser.TryGetDuration("02:30", now, zone, out _, out _));
    }

    [Fact]
    public void SpringForwardUsesTomorrowOnceTheSkippedTimeHasPassed()
    {
        var now = new DateTimeOffset(2026, 3, 29, 4, 0, 0, TimeSpan.FromHours(2));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

        Assert.True(EndTimeParser.TryGetDuration("02:30", now, zone, out var duration, out var end));
        Assert.Equal(TimeSpan.FromHours(22.5), duration);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 2, 30, 0, TimeSpan.FromHours(2)), end);
    }

    [Fact]
    public void UnrepresentableTomorrowIsRejected()
    {
        Assert.False(EndTimeParser.TryGetDuration("00:00", DateTimeOffset.MaxValue, TimeZoneInfo.Utc, out _, out _));
    }

    [Fact]
    public void TryGetDurationUsesTodayForAFutureTime()
    {
        var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

        var parsed = EndTimeParser.TryGetDuration("15:30", now, TimeZoneInfo.Utc, out var duration, out var endTime);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromMinutes(90), duration);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 15, 30, 0, TimeSpan.Zero), endTime);
    }

    [Fact]
    public void TryGetDurationUsesTomorrowForAPastTime()
    {
        var now = new DateTimeOffset(2026, 7, 26, 16, 0, 0, TimeSpan.Zero);

        var parsed = EndTimeParser.TryGetDuration("15:30", now, TimeZoneInfo.Utc, out var duration, out var endTime);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromHours(23.5), duration);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 15, 30, 0, TimeSpan.Zero), endTime);
    }
}