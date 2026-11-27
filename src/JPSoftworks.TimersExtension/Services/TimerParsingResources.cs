// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

internal sealed record TimerParsingResources(
    string HourAliases,
    string MinuteAliases,
    string SecondAliases,
    string EndTimePattern)
{
    public static TimerParsingResources Current => ForCulture(Strings.Culture ?? CultureInfo.CurrentUICulture);

    public static TimerParsingResources Invariant { get; } = ForCulture(CultureInfo.InvariantCulture);

    private static TimerParsingResources ForCulture(CultureInfo culture) => new(
        Strings.ResourceManager.GetString("Parsing.HourAliases", culture)!,
        Strings.ResourceManager.GetString("Parsing.MinuteAliases", culture)!,
        Strings.ResourceManager.GetString("Parsing.SecondAliases", culture)!,
        Strings.ResourceManager.GetString("Parsing.EndTimePattern", culture)!);

    public static bool MatchesAlias(string value, string aliases, CultureInfo? culture = null)
    {
        var comparison = (culture ?? Strings.Culture ?? CultureInfo.CurrentUICulture).CompareInfo;
        foreach (var alias in aliases.Split('|',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (comparison.Compare(value, alias, CompareOptions.IgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }
}