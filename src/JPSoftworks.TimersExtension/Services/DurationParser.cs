// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

internal static partial class DurationParser
{
    public static bool TryParse(string input, out TimeSpan duration)
    {
        return TryParse(input, TimerParsingResources.Current, out duration, out _);
    }

    internal static bool TryParse(
        string input,
        TimerParsingResources resources,
        out TimeSpan duration,
        out bool hasExplicitUnit)
    {
        if (TryParseDuration(input, resources, out duration, out hasExplicitUnit)
            && TimerInstance.IsValidDuration(duration, DateTimeOffset.UtcNow))
        {
            return true;
        }

        duration = TimeSpan.Zero;
        hasExplicitUnit = false;
        return false;
    }

    private static bool TryParseDuration(
        string input,
        TimerParsingResources resources,
        out TimeSpan duration,
        out bool hasExplicitUnit)
    {
        duration = TimeSpan.Zero;
        hasExplicitUnit = false;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        input = NormalizeDigits(input.Trim());
        try
        {
            if (input.Contains(':'))
            {
                hasExplicitUnit = TryParseClockDuration(input, out duration);
                return hasExplicitUnit;
            }

            if (TryParseNumber(input, out var minutes))
            {
                if (minutes <= 0)
                {
                    return false;
                }

                duration = TimeSpan.FromMinutes(minutes);
                return duration > TimeSpan.Zero;
            }

            var total = TimeSpan.Zero;
            var consumed = 0;
            foreach (Match match in UnitDurationPattern().Matches(input))
            {
                if (!TryParseUnit(match, resources, out var unitDuration))
                {
                    return false;
                }

                total += unitDuration;
                consumed += match.Length;
            }

            if (consumed == input.Length && total > TimeSpan.Zero)
            {
                duration = total;
                hasExplicitUnit = true;
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            duration = TimeSpan.Zero;
            hasExplicitUnit = false;
        }

        return false;
    }

    internal static bool TryReadUnitChunk(
        string input,
        int start,
        TimerParsingResources resources,
        out int end,
        out TimeSpan duration)
    {
        end = start;
        duration = TimeSpan.Zero;
        try
        {
            do
            {
                var match = UnitDurationPattern().Match(input, end);
                if (!match.Success || !TryParseUnit(match, resources, out var part))
                {
                    return false;
                }

                duration += part;
                end += match.Length;
            } while (end < input.Length && !char.IsWhiteSpace(input[end - 1]));

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseUnit(Match match, TimerParsingResources resources, out TimeSpan duration)
    {
        if (TryParseNumber(match.Groups["value"].Value, out var value)
            && TryGetUnitSeconds(match.Groups["unit"].Value, resources, out var unitSeconds))
        {
            duration = TimeSpan.FromSeconds(value * unitSeconds);
            return true;
        }

        duration = TimeSpan.Zero;
        return false;
    }

    private static bool TryGetUnitSeconds(string unit, TimerParsingResources resources, out double seconds)
    {
        seconds = unit switch
        {
            "h" or "H" => 3600,
            "m" or "M" => 60,
            "s" or "S" => 1,
            _ => resources.GetUnitSeconds(unit, TimerParsingResources.UICulture)
                 ?? TimerParsingResources.Invariant.GetUnitSeconds(unit, CultureInfo.InvariantCulture)
                 ?? 0
        };

        return seconds > 0;
    }

    internal static string NormalizeDigits(string input)
    {
        if (!input.Any(static character => char.IsDigit(character) && !char.IsAsciiDigit(character)))
        {
            return input;
        }

        var characters = input.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (char.IsDigit(characters[i]))
            {
                characters[i] = (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(characters[i]));
            }
        }

        return new string(characters);
    }

    public static string FormatCompact(TimeSpan duration)
    {
        if (duration.TotalHours >= 1 && duration.Minutes > 0)
        {
            return ResourceFormatter.Format(Strings.Duration_HoursMinutesFormat, (int)duration.TotalHours,
                duration.Minutes);
        }

        if (duration.TotalHours >= 1)
        {
            return ResourceFormatter.Format(Strings.Duration_HoursFormat, (int)Math.Round(duration.TotalHours));
        }

        if (duration.TotalMinutes >= 1)
        {
            return ResourceFormatter.Format(Strings.Duration_MinutesFormat, (int)Math.Round(duration.TotalMinutes));
        }

        return ResourceFormatter.Format(Strings.Duration_SecondsFormat,
            Math.Max(1, (int)Math.Round(duration.TotalSeconds)));
    }

    public static string FormatClock(TimeSpan duration)
    {
        var hours = Math.Abs(duration.Ticks / TimeSpan.TicksPerHour);
        var minutes = Math.Abs(duration.Minutes);
        var seconds = Math.Abs(duration.Seconds);
        var clock = hours > 0
            ? ResourceFormatter.Format(Strings.Duration_ClockHoursMinutesSecondsFormat,
                hours, minutes, seconds)
            : ResourceFormatter.Format(Strings.Duration_ClockMinutesSecondsFormat, minutes, seconds);
        return duration < TimeSpan.Zero ? CultureInfo.CurrentCulture.NumberFormat.NegativeSign + clock : clock;
    }

    public static string FormatCountdown(TimeSpan duration, bool showSeconds)
    {
        if (showSeconds)
        {
            return FormatClock(duration);
        }

        var totalMinutes = Math.Max(0, (long)Math.Ceiling(duration.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours > 0 && minutes > 0)
        {
            return ResourceFormatter.Format(Strings.Duration_HoursMinutesFormat, hours, minutes);
        }

        return hours > 0
            ? ResourceFormatter.Format(Strings.Duration_HoursFormat, hours)
            : ResourceFormatter.Format(Strings.Duration_MinutesFormat, totalMinutes);
    }

    private static bool TryParseNumber(string input, out double value)
    {
        return (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
               && double.IsFinite(value);
    }

    private static bool TryParseClockDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        var parts = input.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        // The editor uses the lossless TimeSpan format; two-part input still means minutes and seconds.
        if (parts.Length == 3
            && TimeSpan.TryParseExact(input, "c", CultureInfo.InvariantCulture, out var formattedDuration)
            && formattedDuration > TimeSpan.Zero)
        {
            duration = formattedDuration;
            return true;
        }

        if (!parts.All(static part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            var minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var seconds = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (seconds >= 60)
            {
                return false;
            }

            duration = new TimeSpan(0, minutes, seconds);
            return duration > TimeSpan.Zero;
        }

        var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutesPart = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var secondsPart = int.Parse(parts[2], CultureInfo.InvariantCulture);
        if (minutesPart >= 60 || secondsPart >= 60)
        {
            return false;
        }

        duration = new TimeSpan(hours, minutesPart, secondsPart);
        return duration > TimeSpan.Zero;
    }

    [GeneratedRegex(
        @"\G\s*(?<value>[0-9]+(?:[^\p{L}\p{M}\p{Nd}\s:]+[0-9]+)?)\s*(?<unit>[\p{L}\p{M}.]+)\s*",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnitDurationPattern();
}