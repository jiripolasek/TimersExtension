// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;

namespace JPSoftworks.TimersExtension.Services;

internal sealed record TimerCreationQuery(
    TimeSpan Duration,
    string? Name,
    string TimeInput,
    DateTimeOffset? EndTime = null);

internal static class TimerCreationQueryParser
{
    public static bool TryParse(string input, out TimerCreationQuery query)
    {
        return TryParse(input, DateTimeOffset.Now, TimeZoneInfo.Local, out query);
    }

    internal static bool TryParse(
        string input,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        out TimerCreationQuery query,
        TimerParsingResources? resources = null)
    {
        query = new TimerCreationQuery(TimeSpan.Zero, null, string.Empty);
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        input = input.Trim();
        resources ??= TimerParsingResources.Current;
        var invariant = TimerParsingResources.Invariant;
        var uiCulture = TimerParsingResources.UICulture;
        input = TimerParsingResources.TrimCommandPrefix(input, resources.TimerPrefixes, invariant.TimerPrefixes, uiCulture)
                ?? input;

        var patterns = new[]
        {
            (resources.EndTimePattern, uiCulture),
            (invariant.EndTimePattern, CultureInfo.InvariantCulture)
        };
        var invalidEndTime = false;
        foreach (var (pattern, culture) in patterns)
        {
            var endTimeMatch = MatchEndTime(input, pattern, culture);
            if (endTimeMatch.Success)
            {
                var timeInput = endTimeMatch.Groups["time"].Value.Trim();
                if (!EndTimeParser.TryGetDuration(timeInput, now, timeZone, out var duration, out var endTime))
                {
                    invalidEndTime |= EndTimeParser.LooksLikeClockTime(timeInput);
                    continue;
                }

                var endTimeName = endTimeMatch.Groups["name"].Value.Trim();
                query = new TimerCreationQuery(duration, string.IsNullOrWhiteSpace(endTimeName) ? null : endTimeName,
                    timeInput, endTime);
                return true;
            }
        }

        if (invalidEndTime)
        {
            return false;
        }

        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (bestStart, bestLength, bestDuration) = FindDurationInput(tokens, resources);
        if (bestStart < 0)
        {
            return false;
        }

        var nameTokens = new List<string>(tokens.Length - bestLength);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i < bestStart || i >= bestStart + bestLength)
            {
                nameTokens.Add(tokens[i]);
            }
        }

        var name = string.Join(' ', nameTokens).Trim();
        query = new TimerCreationQuery(bestDuration, string.IsNullOrWhiteSpace(name) ? null : name,
            string.Join(" ", tokens, bestStart, bestLength));
        return true;
    }

    private static (int Start, int Length, TimeSpan Duration) FindDurationInput(
        string[] tokens,
        TimerParsingResources resources)
    {
        var text = DurationParser.NormalizeDigits(string.Join(' ', tokens));
        var offsets = new int[tokens.Length + 1];
        for (var i = 0; i < tokens.Length; i++)
        {
            offsets[i + 1] = offsets[i] + tokens[i].Length + 1;
        }

        var best = (Start: -1, Length: 0, Duration: TimeSpan.Zero);
        var bestScore = -1L;
        var hasClock = text.Contains(':');
        var maxTicks = DateTime.MaxValue.Ticks - DateTimeOffset.UtcNow.UtcTicks;
        var unitChunks = new Queue<(int Start, long Ticks)>();
        var unitEnd = 0;
        var unitTicks = 0L;

        for (var start = 0; start < tokens.Length; start++)
        {
            if (DurationParser.TryParse(tokens[start], resources, out var single, out var explicitUnit))
            {
                Score(start, start + 1, single, explicitUnit);
            }

            // A clock has at most three fields and two colons, even with spaces around each colon.
            for (var end = start + 2; hasClock && end <= tokens.Length && end <= start + 5; end++)
            {
                var clock = text.AsSpan(offsets[start], offsets[end] - offsets[start] - 1);
                if (clock.Contains(':') &&
                    DurationParser.TryParse(clock.ToString(), resources, out var duration, out _))
                {
                    Score(start, end, duration, true);
                }
            }

            if (start < unitEnd)
            {
                continue;
            }

            var offset = offsets[start];
            if (!char.IsAsciiDigit(text[offset]) || !DurationParser.TryReadUnitChunk(text, offset, resources,
                                                     out var nextOffset, out var part)
                                                 || part.Ticks > maxTicks)
            {
                unitChunks.Clear();
                unitTicks = 0;
                continue;
            }

            unitEnd = start + 1;
            while (offsets[unitEnd] < nextOffset)
            {
                unitEnd++;
            }

            // The longest legal unit run ending here also has the highest score for this endpoint.
            while (unitTicks > maxTicks - part.Ticks)
            {
                unitTicks -= unitChunks.Dequeue().Ticks;
            }

            unitTicks += part.Ticks;
            unitChunks.Enqueue((start, part.Ticks));
            if (unitTicks > 0)
            {
                Score(unitChunks.Peek().Start, unitEnd, TimeSpan.FromTicks(unitTicks), true);
            }
        }

        return best;

        void Score(int start, int end, TimeSpan duration, bool explicitUnit)
        {
            var length = end - start;
            var score = (explicitUnit ? 1000 : 0) + (10L * length) + offsets[end] - offsets[start] - 1;
            if (score > bestScore || (score == bestScore && start < best.Start))
            {
                best = (start, length, duration);
                bestScore = score;
            }
        }
    }

    private static Match MatchEndTime(string input, string pattern, CultureInfo culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // Regex uses the language's casing rules; clock parsing keeps the user's date/time culture.
            CultureInfo.CurrentCulture = culture;
            return Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}