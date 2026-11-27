// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Linq;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class TimerCreationQueryParserTests
{
    [Theory]
    [InlineData("4m", 240, null)]
    [InlineData("tea 4m", 240, "tea")]
    [InlineData("4m tea", 240, "tea")]
    [InlineData("focus 1h 30m", 5400, "focus")]
    [InlineData("05:00 laundry", 300, "laundry")]
    [InlineData("focus 1.01:00:00", 90000, "focus")]
    [InlineData("00:01:30.5 tea", 90.5, "tea")]
    [InlineData("vitamin 2 5m", 300, "vitamin 2")]
    [InlineData("room 2300 5m", 300, "room 2300")]
    [InlineData("2300m", 138000, null)]
    [InlineData("pizza 120", 7200, "pizza")]
    [InlineData("tea 175", 10500, "tea")]
    [InlineData("bake 360", 21600, "bake")]
    [InlineData("999", 59940, null)]
    [InlineData("930", 55800, null)]
    [InlineData("2300", 138000, null)]
    [InlineData("tea ٢٣٠٠", 138000, "tea")]
    [InlineData("0230", 13800, null)]
    [InlineData("15", 900, null)]
    [InlineData("chat 05:00", 300, "chat")]
    [InlineData("heat 05:00", 300, "heat")]
    [InlineData("tea at work 4m", 240, "tea at work")]
    [InlineData("tea at work 05:00", 300, "tea at work")]
    [InlineData("focus\t1h\t30m", 5400, "focus")]
    [InlineData("focus 1 h30 m", 5400, "focus")]
    [InlineData("focus 1 : 30 : 05", 5405, "focus")]
    [InlineData("pause 0h 45m", 2700, "pause")]
    public void TryParseExtractsDurationAndOptionalName(string input, double seconds, string? expectedName)
    {
        var parsed = TimerCreationQueryParser.TryParse(input, out var query);

        Assert.True(parsed);
        Assert.Equal(seconds, query.Duration.TotalSeconds);
        Assert.Equal(expectedName, query.Name);
        Assert.Null(query.EndTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tea")]
    [InlineData("tea time")]
    [InlineData("999999999:00:00")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("100000000h")]
    [InlineData("until 25:00")]
    [InlineData("tea at 99:00")]
    [InlineData("until 2360")]
    [InlineData("at 2400")]
    [InlineData("at 24:00")]
    [InlineData("tea until 175")]
    public void TryParseRejectsQueriesWithoutACompleteDuration(string input)
    {
        Assert.False(TimerCreationQueryParser.TryParse(input, out _));
    }

    [Fact]
    public void PastedLabelsAndLongUnitRunsUseBoundedAllocations()
    {
        var label = string.Join(' ', Enumerable.Repeat("focus", 500));
        var units = string.Join(' ', Enumerable.Repeat("1s", 500));
        Assert.True(TimerCreationQueryParser.TryParse("focus 1s 1s", out _));
        var before = GC.GetAllocatedBytesForCurrentThread();

        var parsed = TimerCreationQueryParser.TryParse($"{label} {units}", out var query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromSeconds(500), query.Duration);
        Assert.Equal(label, query.Name);
        Assert.InRange(allocated, 0, 12 * 1024 * 1024);
    }

    [Theory]
    [InlineData("until 02:30")]
    [InlineData("until 0230")]
    public void InvalidSpringForwardEndTimeCannotBecomeADuration(string input)
    {
        var now = new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.FromHours(1));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

        Assert.False(TimerCreationQueryParser.TryParse(input, now, zone, out _));
    }

    [Theory]
    [InlineData("cs-CZ", "čaj do 25:00", @"^(?:(?<name>.*?)\s+)?do\s+(?<time>.+)$")]
    [InlineData("fi-FI", "tee asti 25.00", @"^(?:(?<name>.*?)\s+)?asti\s+(?<time>.+)$")]
    [InlineData("ar-EG", "شاي حتى ٢٥:٠٠", @"^(?:(?<name>.*?)\s+)?حتى\s+(?<time>.+)$")]
    [InlineData("ko-KR", "tea until 오후 25:00", @"^(?:(?<name>.*?)\s+)?until\s+(?<time>.+)$")]
    public void InvalidLocalizedEndTimesCannotBecomeDurations(string cultureName, string input, string pattern)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var resources = TimerParsingResources.Invariant with { EndTimePattern = pattern };
            var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

            Assert.False(TimerCreationQueryParser.TryParse(input, now, TimeZoneInfo.Utc, out _, resources));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("until 15:30", null)]
    [InlineData("tea until 3:30 PM", "tea")]
    [InlineData("tea until 1530", "tea")]
    public void TryParseSupportsExplicitEndTimes(string input, string? expectedName)
    {
        var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

        var parsed = TimerCreationQueryParser.TryParse(input, now, TimeZoneInfo.Utc, out var query);

        Assert.True(parsed);
        Assert.Equal(expectedName, query.Name);
        Assert.Equal(TimeSpan.FromMinutes(90), query.Duration);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 15, 30, 0, TimeSpan.Zero), query.EndTime);
    }

    [Theory]
    [InlineData("until 2300", 26, 23, 0, null)]
    [InlineData("at 930", 27, 9, 30, null)]
    [InlineData("until 0000", 27, 0, 0, null)]
    [InlineData("until 00:00", 27, 0, 0, null)]
    [InlineData("tea at 2300", 26, 23, 0, "tea")]
    [InlineData("tea until ٢٣٠٠", 26, 23, 0, "tea")]
    public void ClockInputWithAKeywordUsesTheNextLocalOccurrence(
        string input,
        int day,
        int hour,
        int minute,
        string? expectedName)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        var offset = TimeSpan.FromHours(2);
        var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, offset).ToUniversalTime();
        var expected = new DateTimeOffset(2026, 7, day, hour, minute, 0, offset);

        Assert.True(TimerCreationQueryParser.TryParse(input, now, zone, out var query));
        Assert.Equal(expectedName, query.Name);
        Assert.Equal(expected, query.EndTime);
        Assert.Equal(offset, query.EndTime?.Offset);
        Assert.Equal(expected - now, query.Duration);
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("fi-FI")]
    [InlineData("ko-KR")]
    [InlineData("ar-EG")]
    public void TryParseAcceptsTheCulturesClockFormat(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);
            var localTime = new TimeOnly(15, 30).ToString("t", culture);

            Assert.True(TimerCreationQueryParser.TryParse($"tea until {localTime}", now, TimeZoneInfo.Utc,
                out var query));
            Assert.Equal(TimeSpan.FromMinutes(90), query.Duration);
            Assert.Equal("tea", query.Name);
            Assert.NotNull(query.EndTime);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Theory]
    [InlineData("cs-CZ", "čaj do 15:30", @"^(?:(?<name>.*?)\s+)?(?:do|v)\s+(?<time>.+)$", "čaj")]
    [InlineData("cs-CZ", "čaj do 1530", @"^(?:(?<name>.*?)\s+)?(?:do|v)\s+(?<time>.+)$", "čaj")]
    [InlineData("ja-JP", "お茶15:30まで", @"^(?<name>.*?)(?<time>\p{Nd}{1,2}:\p{Nd}{2})まで$", "お茶")]
    [InlineData("ar-EG", "شاي حتى ١٥:٣٠", @"^(?:(?<name>.*?)\s+)?حتى\s+(?<time>.+)$", "شاي")]
    [InlineData("tr-TR", "çay BİTİR 15:30", @"^(?:(?<name>.*?)\s+)?bitir\s+(?<time>.+)$", "çay")]
    public void TryParseSupportsLocalizedKeywordsAndWordOrder(
        string cultureName,
        string input,
        string pattern,
        string name)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);
            var resources = TimerParsingResources.Invariant with { EndTimePattern = pattern };

            Assert.True(TimerCreationQueryParser.TryParse(input, now, TimeZoneInfo.Utc, out var query, resources));
            Assert.Equal(TimeSpan.FromMinutes(90), query.Duration);
            Assert.Equal(name, query.Name);
            Assert.NotNull(query.EndTime);
            Assert.True(TimerCreationQueryParser.TryParse("tea UNTIL 15:30", now, TimeZoneInfo.Utc, out query,
                resources));
            Assert.Equal(TimeSpan.FromMinutes(90), query.Duration);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void LocalizedUnitsTakePriorityOverNumbersInTheName()
    {
        var resources = TimerParsingResources.Invariant with { MinuteAliases = "分" };
        var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

        Assert.True(TimerCreationQueryParser.TryParse("task 12345678 5分", now, TimeZoneInfo.Utc, out var query,
            resources));
        Assert.Equal(TimeSpan.FromMinutes(5), query.Duration);
        Assert.Equal("task 12345678", query.Name);
    }

    [Theory]
    [InlineData("lancer un minuteur thé 5m", "thé")]
    [InlineData("lancer thé 5m", "thé")]
    [InlineData("start timer thé 5m", "thé")]
    [InlineData("lancerie 5m", "lancerie")]
    public void TimerQueryAcceptsLocalizedAndEnglishPrefixes(string input, string name)
    {
        var resources = TimerParsingResources.Invariant with { TimerPrefixes = "lancer un minuteur|lancer" };
        var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

        Assert.True(TimerCreationQueryParser.TryParse(input, now, TimeZoneInfo.Utc, out var query, resources));
        Assert.Equal(name, query.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), query.Duration);
    }

    [Fact]
    public void PartialTranslationUsesTheLongestTimerPrefixAndRegionalClock()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            var region = CultureInfo.GetCultureInfo("fi-FI");
            CultureInfo.CurrentCulture = region;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-SC");
            var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);
            var clock = new TimeOnly(15, 30).ToString("t", region);

            Assert.True(TimerCreationQueryParser.TryParse($"de\u0301marrer le minuteur thé jusqu’à {clock}",
                now, TimeZoneInfo.Utc, out var query));
            Assert.Equal("thé", query.Name);
            Assert.Equal(TimeSpan.FromMinutes(90), query.Duration);
            Assert.Equal(now.AddMinutes(90), query.EndTime);

            Assert.True(TimerCreationQueryParser.TryParse("start timer tea 1,5 mn", now, TimeZoneInfo.Utc, out query));
            Assert.Equal("tea", query.Name);
            Assert.Equal(TimeSpan.FromSeconds(90), query.Duration);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}