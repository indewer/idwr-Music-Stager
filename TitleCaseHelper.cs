using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicStagerWidget;

public static class TitleCaseHelper
{
    private static readonly HashSet<string> PreservedAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "II", "III", "IV", "VI", "VII", "VIII", "IX", "X",
        "VIP", "OST", "DJ", "EP", "LP", "EDM", "ID", "DC",
        "USA", "RUS", "UK", "FLAC", "ALAC", "MP3", "M4A"
    };

    static TitleCaseHelper()
    {
        // Try to load dictionary from embedded resource first, then from disk
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var resStream = asm.GetManifestResourceStream("MusicStagerWidget.Resources.words_freq.txt.gz");
            if (resStream != null)
            {
                // Write to temp file or initialize from stream
                string tempFile = Path.Combine(Path.GetTempPath(), "musicstager_words_freq.txt.gz");
                if (!File.Exists(tempFile))
                {
                    using var fs = File.Create(tempFile);
                    resStream.CopyTo(fs);
                }
                WordSegmenter.Initialize(tempFile);
                return;
            }
        }
        catch { }

        // Fallback to relative path on disk
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string localPath = Path.Combine(baseDir, "Resources", "words_freq.txt.gz");
        if (File.Exists(localPath))
        {
            WordSegmenter.Initialize(localPath);
        }
    }

    /// <summary>
    /// Transforms any title by segmenting compound words (like adecentcupofcoffee -> A Decent Cup Of Coffee)
    /// and capitalizing every word (Title Case) while preserving symbols, brackets, and numbers.
    /// </summary>
    public static string ToTitleCaseWithWordBreak(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        string text = input.Trim();

        // 1. Separate letters and digits (e.g. "Top10Most" -> "Top 10 Most", "Sof2047" -> "Sof 2047", "lvl70" -> "lvl 70")
        text = Regex.Replace(text, @"(?<=[a-zA-Z])(?=\d)|(?<=\d)(?=[a-zA-Z])", " ");

        // 2. Split CamelCase runs if any (e.g. "SomeTitle" -> "Some Title", "iLoveBeer" -> "i Love Beer")
        text = Regex.Replace(text, @"(?<=[a-z])(?=[A-Z])", " ");

        // 2. Tokenize by splitting letter runs and non-letter runs
        var matches = Regex.Matches(text, @"([a-zA-Z]+|[^a-zA-Z]+)");
        var sb = new StringBuilder();

        foreach (Match match in matches)
        {
            string val = match.Value;

            if (Regex.IsMatch(val, @"^[a-zA-Z]+$"))
            {
                // If it's a known acronym or Roman numeral, preserve original uppercase
                if (PreservedAcronyms.Contains(val))
                {
                    sb.Append(val.ToUpperInvariant());
                    continue;
                }

                // Check if word needs segmentation (even if TitleCased like Holycrud or Adecentcupofcoffee)
                if (val.Length >= 4)
                {
                    var subwords = WordSegmenter.Segment(val);
                    if (subwords.Count > 1)
                    {
                        for (int i = 0; i < subwords.Count; i++)
                        {
                            if (i > 0) sb.Append(' ');
                            sb.Append(CapitalizeWord(subwords[i]));
                        }
                        continue;
                    }
                }

                // Single word: capitalize first letter, lower the rest
                sb.Append(CapitalizeWord(val));
            }
            else
            {
                // Non-letter punctuation, brackets, spaces, digits
                sb.Append(val);
            }
        }

        return sb.ToString();
    }

    private static string CapitalizeWord(string w)
    {
        if (string.IsNullOrEmpty(w)) return "";
        if (PreservedAcronyms.Contains(w)) return w.ToUpperInvariant();
        if (w.Length == 1) return char.ToUpperInvariant(w[0]).ToString();

        return char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
    }
}
