// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Resources;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

internal sealed record TimerParsingResources(
    string HourAliases,
    string MinuteAliases,
    string SecondAliases,
    string EndTimePattern,
    string TimerPrefixes,
    string StopwatchPrefixes)
{
    private static readonly ResourceSet NeutralResources =
        Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;

    public static CultureInfo UICulture => Strings.Culture ?? CultureInfo.CurrentUICulture;

    public static TimerParsingResources Current => ForCulture(UICulture);

    public static TimerParsingResources Invariant { get; } = ForCulture(CultureInfo.InvariantCulture);

    private static TimerParsingResources ForCulture(CultureInfo culture) => new(
        GetAliases("Parsing.HourAliases", culture),
        GetAliases("Parsing.MinuteAliases", culture),
        GetAliases("Parsing.SecondAliases", culture),
        Strings.ResourceManager.GetString("Parsing.EndTimePattern", culture)!,
        Strings.ResourceManager.GetString("Parsing.TimerPrefixes", culture)!,
        Strings.ResourceManager.GetString("Parsing.StopwatchPrefixes", culture)!);

    private static string GetAliases(string key, CultureInfo culture)
    {
        if (culture.Equals(CultureInfo.InvariantCulture))
        {
            return NeutralResources.GetString(key)!;
        }

        // Keep neutral aliases out of the localized pass, including cached resource sets.
        for (var candidate = culture; !candidate.Equals(CultureInfo.InvariantCulture); candidate = candidate.Parent)
        {
            var resources = Strings.ResourceManager.GetResourceSet(candidate, true, false);
            if (!ReferenceEquals(resources, NeutralResources) && resources?.GetString(key) is { } aliases)
            {
                return aliases;
            }
        }

        return string.Empty;
    }

    public int? GetUnitSeconds(string unit, CultureInfo culture)
    {
        if (MatchesAlias(unit, this.HourAliases, culture))
        {
            return 3600;
        }

        if (MatchesAlias(unit, this.MinuteAliases, culture))
        {
            return 60;
        }

        return MatchesAlias(unit, this.SecondAliases, culture) ? 1 : null;
    }

    public static string? TrimCommandPrefix(
        string query,
        string localizedPrefixes,
        string invariantPrefixes,
        CultureInfo culture)
    {
        var length = Math.Max(
            GetLongestPrefixLength(query, localizedPrefixes, culture.CompareInfo),
            GetLongestPrefixLength(query, invariantPrefixes, CultureInfo.InvariantCulture.CompareInfo));
        return length == 0 || length == query.Length ? null : query[length..].TrimStart();
    }

    private static int GetLongestPrefixLength(string query, string prefixes, CompareInfo comparison)
    {
        var longest = 0;
        foreach (var prefix in prefixes.Split('|',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (comparison.IsPrefix(query.AsSpan(), prefix.AsSpan(), CompareOptions.IgnoreCase, out var length)
                && length > longest && (length == query.Length || char.IsWhiteSpace(query[length])))
            {
                longest = length;
            }
        }

        return longest;
    }

    private static bool MatchesAlias(string value, string aliases, CultureInfo culture)
    {
        foreach (var alias in aliases.Split('|',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (culture.CompareInfo.Compare(value, alias, CompareOptions.IgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }
}