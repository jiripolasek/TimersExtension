// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Pages;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class IndexSearchMatcherTests
{
    [Theory]
    [InlineData("Focus", "paused", "", true)]
    [InlineData("Focus", "paused", " \t\n ", true)]
    [InlineData("Focus", "paused", "fcs", true)]
    [InlineData("Focus", "paused", "paus FCS", true)]
    [InlineData("Focus", "paused", "\tpaus\nfcs\u3000", true)]
    [InlineData("Focus", "paused", "fcs cooking", false)]
    [InlineData("Focus", "paused", "focuz", true)]
    [InlineData("Focus", "paused", "foccus", true)]
    [InlineData("Focus", "paused", "focsu", true)]
    [InlineData("Focus", "paused", "foucs", true)]
    [InlineData("Focus", "paused", "focs", true)]
    [InlineData("Focus", "paused", "pauesd", true)]
    [InlineData("Focus-timer", "", "focsu", true)]
    [InlineData("Focus", "paused", "fokuz", false)]
    [InlineData("Tea", "03:00 left", "sea", false)]
    [InlineData("Tea", "12:00 left", "12:01", false)]
    [InlineData("Tea", "12:00 left", "12:00", true)]
    [InlineData("Tea", "12:00 left", "workout", false)]
    [InlineData("", "", "focus", false)]
    public void MatchesSupportsPartialWordsAndLimitedTypos(string title, string subtitle, string query, bool expected)
    {
        var item = new ListItem { Title = title, Subtitle = subtitle };

        Assert.Equal(expected, IndexSearchMatcher.Matches(item, query));
    }

    [Theory]
    [InlineData("tr-TR", "İZMİR", "izmir")]
    [InlineData("tr-TR", "IŞIK", "ışık")]
    [InlineData("en-US", "İZMİR", "izmir")]
    [InlineData("cs-CZ", "Čaj", "caj")]
    [InlineData("fr-FR", "Crème brûlée", "creme brulee")]
    [InlineData("fr-FR", "Café", "cafe\u0301")]
    [InlineData("fr-FR", "Cafe\u0301", "café")]
    [InlineData("fr-FR", "Café", "cafez")]
    [InlineData("fr-FR", "Café", "cafe\u0301z")]
    [InlineData("fr-FR", "Cafe\u0301", "cafez")]
    [InlineData("fr-FR", "Café", "cae\u0301f")]
    [InlineData("el-GR", "Καφές", "καφες")]
    [InlineData("ru-RU", "Кофе", "кофе")]
    [InlineData("ja-JP", "コーヒータイマー", "ｺｰﾋｰ")]
    [InlineData("ja-JP", "コーヒータイマー", "こーひー")]
    [InlineData("ko-KR", "집중 타이머", "집타")]
    [InlineData("ar-EG", "مؤقت القراءة", "قراءة")]
    public void MatchesHandlesLocalizedText(string cultureName, string title, string query)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.True(IndexSearchMatcher.Matches(new ListItem { Title = title }, query));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}