// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class DurationParserTests
{
    [Theory]
    [InlineData("10m", 600)]
    [InlineData("1h 30m", 5400)]
    [InlineData("05:00", 300)]
    [InlineData("1:02:03", 3723)]
    [InlineData("2.5", 150)]
    [InlineData("1h30m", 5400)]
    [InlineData("1 hour 30 minutes", 5400)]
    [InlineData("25:00:00", 90000)]
    [InlineData("1.01:00:00", 90000)]
    [InlineData("50.00:00:00", 4320000)]
    [InlineData("00:01:30.5000000", 90.5)]
    [InlineData("0h 1s", 1)]
    public void TryParseReturnsExpectedDurationForValidInput(string input, double expectedSeconds)
    {
        var parsed = DurationParser.TryParse(input, out var duration);

        Assert.True(parsed);
        Assert.Equal(expectedSeconds, duration.TotalSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a duration")]
    [InlineData("1h garbage")]
    [InlineData("00:60")]
    [InlineData("1:60:00")]
    [InlineData("999999999:00:00")]
    [InlineData("2147483648:00")]
    [InlineData("999999999999999999h")]
    [InlineData("100000000h")]
    [InlineData("100000000:00:00")]
    [InlineData("6000000000")]
    [InlineData("1e300")]
    [InlineData("1e309")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0")]
    [InlineData("0m")]
    [InlineData("-1")]
    [InlineData("-1h")]
    [InlineData("1h 30")]
    [InlineData("-1.01:00:00")]
    [InlineData("1.24:00:00")]
    [InlineData("1.00:60:00")]
    [InlineData("00:00:00.0000000")]
    [InlineData("00:00:00.00000001")]
    [InlineData("999999999.00:00:00")]
    public void TryParseReturnsFalseForInvalidInput(string input)
    {
        Assert.False(DurationParser.TryParse(input, out var duration));
        Assert.Equal(TimeSpan.Zero, duration);
    }

    [Theory]
    [InlineData(3660, "1h 1m")]
    [InlineData(3600, "1h")]
    [InlineData(300, "5m")]
    [InlineData(30, "30s")]
    public void FormatCompactUsesLocalizedCompactUnits(double seconds, string expected)
    {
        Assert.Equal(expected, DurationParser.FormatCompact(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(330, "6m")]
    [InlineData(3599, "1h")]
    [InlineData(3601, "1h 1m")]
    [InlineData(20, "1m")]
    [InlineData(0, "0m")]
    [InlineData(0.1, "1m")]
    [InlineData(180000000000, "50000000h")]
    public void FormatCountdownRoundsUpWhenSecondsAreHidden(double seconds, string expected)
    {
        Assert.Equal(expected, DurationParser.FormatCountdown(TimeSpan.FromSeconds(seconds), false));
    }

    [Theory]
    [InlineData(30, "00:30")]
    [InlineData(3661, "1:01:01")]
    [InlineData(90000, "25:00:00")]
    [InlineData(176461, "49:01:01")]
    [InlineData(-62, "01:02")]
    [InlineData(-90061, "25:01:01")]
    public void FormatClockPreservesTotalHours(double seconds, string expected)
    {
        var sign = seconds < 0 ? CultureInfo.CurrentCulture.NumberFormat.NegativeSign : string.Empty;
        Assert.Equal(sign + expected, DurationParser.FormatClock(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("cs-CZ", "1,5hod 30min", "hod|hodina|hodiny", "min|minuta|minuty", "s|sekunda", 7200)]
    [InlineData("fr-FR", "1,5 HEURES", "h|heure|heures", "min|minute|minutes", "s|seconde", 5400)]
    [InlineData("ru-RU", "1,5 ч.", "ч|ч.", "мин|мин.", "с|сек.", 5400)]
    [InlineData("ja-JP", "1時間30分", "時間", "分", "秒", 5400)]
    [InlineData("ar-EG", "١٫٥ساعة", "ساعة|ساعات", "دقيقة|دقائق", "ثانية|ثوان", 5400)]
    [InlineData("bn-BD", "১.৫ঘণ্টা", "ঘণ্টা", "মিনিট", "সেকেন্ড", 5400)]
    [InlineData("tr-TR", "5 DAKİKA", "saat", "dakika", "saniye", 300)]
    public void TryParseUsesLocalizedUnitsAndNumbers(
        string cultureName,
        string input,
        string hours,
        string minutes,
        string seconds,
        double expectedSeconds)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            var resources = TimerParsingResources.Invariant with
            {
                HourAliases = hours, MinuteAliases = minutes, SecondAliases = seconds
            };

            Assert.True(DurationParser.TryParse(input, resources, out var duration, out var hasExplicitUnit));
            Assert.True(hasExplicitUnit);
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration);
            Assert.True(DurationParser.TryParse("1h30m", resources, out duration, out _));
            Assert.Equal(TimeSpan.FromMinutes(90), duration);
            Assert.True(DurationParser.TryParse("1 MIN", resources, out duration, out _));
            Assert.Equal(TimeSpan.FromMinutes(1), duration);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Theory]
    [InlineData("cs-CZ", "2,5", 150)]
    [InlineData("cs-CZ", "2,5m", 150)]
    [InlineData("cs-CZ", "2.5m", 150)]
    [InlineData("en-US", "2.5m", 150)]
    public void TryParsePreservesLocalAndInvariantDecimals(string cultureName, string input, double seconds)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.True(DurationParser.TryParse(input, out var duration));
            Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("2h", 7200)]
    [InlineData("2M", 120)]
    [InlineData("2s", 2)]
    [InlineData("2 hour", 120)]
    [InlineData("2 hr", 7200)]
    [InlineData("1,5 mn", 90)]
    public void PartialTranslationKeepsSymbolsStableAndPrefersTranslatedWords(string input, double expectedSeconds)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-SC");

            Assert.Equal("h|hr|hour|hours", Strings.Parsing_HourAliases);
            Assert.True(DurationParser.TryParse(input, out var duration));
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}