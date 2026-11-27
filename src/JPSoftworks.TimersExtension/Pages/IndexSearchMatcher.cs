// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Pages;

internal static partial class IndexSearchMatcher
{
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace
                                                                           | CompareOptions.IgnoreWidth |
                                                                           CompareOptions.IgnoreKanaType;

    private static readonly CompareInfo Comparison = CultureInfo.InvariantCulture.CompareInfo;

    public static bool Matches(IListItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        // Fold casing in the UI language, then ignore accents even where they distinguish letters.
        var culture = Strings.Culture ?? CultureInfo.CurrentUICulture;
        var candidate = $"{item.Title} {item.Subtitle}".ToUpper(culture);
        foreach (var term in query.ToUpper(culture).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Comparison.IndexOf(candidate, term, SearchOptions) < 0
                && !IsSubsequence(term, candidate)
                && !MatchesTypo(term, candidate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSubsequence(string query, string candidate)
    {
        var start = 0;
        var elements = StringInfo.GetTextElementEnumerator(query);
        while (elements.MoveNext())
        {
            var index = Comparison.IndexOf(candidate, elements.GetTextElement(), start, SearchOptions);
            if (index < 0)
            {
                return false;
            }

            start = index + StringInfo.GetNextTextElementLength(candidate, index);
        }

        return true;
    }

    private static bool MatchesTypo(string query, string candidate)
    {
        // A typo in a short token would match too many unrelated names.
        if (query.Length < 4 || WordPattern().Match(query).Length != query.Length)
        {
            return false;
        }

        foreach (Match word in WordPattern().Matches(candidate))
        {
            if (word.Length >= 4 && IsWithinOneEdit(query, word.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinOneEdit(ReadOnlySpan<char> query, ReadOnlySpan<char> word)
    {
        var queryIndex = 0;
        var wordIndex = 0;
        while (queryIndex < query.Length && wordIndex < word.Length)
        {
            var queryLength = StringInfo.GetNextTextElementLength(query[queryIndex..]);
            var wordLength = StringInfo.GetNextTextElementLength(word[wordIndex..]);
            if (Comparison.Compare(query.Slice(queryIndex, queryLength), word.Slice(wordIndex, wordLength),
                    SearchOptions) != 0)
            {
                break;
            }

            queryIndex += queryLength;
            wordIndex += wordLength;
        }

        var nextQuery = queryIndex + StringInfo.GetNextTextElementLength(query[queryIndex..]);
        var nextWord = wordIndex + StringInfo.GetNextTextElementLength(word[wordIndex..]);
        if (Comparison.Compare(query[nextQuery..], word[wordIndex..], SearchOptions) == 0
            || Comparison.Compare(query[queryIndex..], word[nextWord..], SearchOptions) == 0
            || Comparison.Compare(query[nextQuery..], word[nextWord..], SearchOptions) == 0)
        {
            return true;
        }

        var afterQuery = nextQuery + StringInfo.GetNextTextElementLength(query[nextQuery..]);
        var afterWord = nextWord + StringInfo.GetNextTextElementLength(word[nextWord..]);
        return nextQuery < query.Length && nextWord < word.Length
                                        && Comparison.Compare(query[queryIndex..nextQuery], word[nextWord..afterWord],
                                            SearchOptions) == 0
                                        && Comparison.Compare(query[nextQuery..afterQuery], word[wordIndex..nextWord],
                                            SearchOptions) == 0
                                        && Comparison.Compare(query[afterQuery..], word[afterWord..], SearchOptions) ==
                                        0;
    }

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}