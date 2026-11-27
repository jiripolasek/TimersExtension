// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace JPSoftworks.TimersExtension.Services;

internal static class EndTimeParser
{
    public static bool TryGetDuration(
        string input,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        out TimeSpan duration,
        out DateTimeOffset endTime)
    {
        duration = TimeSpan.Zero;
        endTime = default;
        input = DurationParser.NormalizeDigits(input.Trim());

        TimeOnly time;
        if (LooksLikeCompactTime(input))
        {
            if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var compactTime)
                || compactTime / 100 >= 24 || compactTime % 100 >= 60)
            {
                return false;
            }

            time = new TimeOnly(compactTime / 100, compactTime % 100);
        }
        else if (!TryParseClockTime(input, CultureInfo.CurrentCulture, out time)
                 && !TryParseClockTime(input, CultureInfo.InvariantCulture, out time))
        {
            return false;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localEnd = DateTime.SpecifyKind(localNow.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        for (var day = 0; day < 2; day++)
        {
            if (!timeZone.IsInvalidTime(localEnd))
            {
                var offsets = timeZone.IsAmbiguousTime(localEnd)
                    ? timeZone.GetAmbiguousTimeOffsets(localEnd)
                    : new[] { timeZone.GetUtcOffset(localEnd) };
                foreach (var offset in offsets)
                {
                    var utcTicks = localEnd.Ticks - offset.Ticks;
                    if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
                    {
                        continue;
                    }

                    var candidate = new DateTimeOffset(localEnd, offset);
                    if (candidate > now && (endTime == default || candidate < endTime))
                    {
                        endTime = candidate;
                    }
                }

                if (endTime != default)
                {
                    duration = endTime - now;
                    return true;
                }
            }
            else if (localEnd > localNow.DateTime)
            {
                return false;
            }

            if (localEnd.Date == DateTime.MaxValue.Date)
            {
                return false;
            }

            localEnd = localEnd.AddDays(1);
        }

        return false;
    }

    internal static bool LooksLikeClockTime(string input)
    {
        input = DurationParser.NormalizeDigits(input.Trim());
        if (LooksLikeCompactTime(input)
            || TimeOnly.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out _)
            || TimeOnly.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return true;
        }

        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        var designators = string.Join('|', new[] { format.AMDesignator, format.PMDesignator, "AM", "PM" }
            .Where(static value => !string.IsNullOrWhiteSpace(value)).Select(Regex.Escape));
        var separator = $"(?:[:.]|{Regex.Escape(format.TimeSeparator)})";
        var clock = $@"\d+\s*{separator}\s*\d+(?:\s*{separator}\s*\d+)?";
        return Regex.IsMatch(input, $@"^(?:(?:{designators})\s*)?{clock}(?:\s*(?:{designators}))?$",
            RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    }

    private static bool TryParseClockTime(string input, CultureInfo culture, out TimeOnly time)
    {
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;

        // Parsing with a date distinguishes midnight from a 24:00 rollover.
        return TimeOnly.TryParse(input, culture, styles, out time)
               && (time != TimeOnly.MinValue
                   || !DateTime.TryParse(input, culture, styles | DateTimeStyles.NoCurrentDateDefault, out var date)
                   || date.Date == DateTime.MinValue);
    }

    private static bool LooksLikeCompactTime(string input) => input.Length is 3 or 4 && input.All(char.IsDigit);
}