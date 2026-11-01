using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MusicStagerWidget;

public static class WordSegmenter
{
    private static readonly Dictionary<string, double> WordCosts = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized = false;
    private static readonly object LockObj = new();

    public static void Initialize(string gzFilePath)
    {
        lock (LockObj)
        {
            if (_initialized) return;

            if (File.Exists(gzFilePath))
            {
                using var fileStream = File.OpenRead(gzFilePath);
                using var gzStream = new GZipStream(fileStream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzStream);

                var words = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(line))
                        words.Add(line);
                }

                int n = words.Count;
                double logN = Math.Log(n > 0 ? n : 1);
                for (int i = 0; i < words.Count; i++)
                {
                    WordCosts[words[i]] = Math.Log((i + 1) * logN);
                }
            }

            _initialized = true;
        }
    }

    public static List<string> Segment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        string lower = text.ToLowerInvariant();
        int n = lower.Length;
        if (n <= 1) return new List<string> { text };

        double[] costs = new double[n + 1];
        int[] prevs = new int[n + 1];

        for (int i = 1; i <= n; i++)
        {
            costs[i] = double.PositiveInfinity;
            int start = Math.Max(0, i - 20);
            for (int j = start; j < i; j++)
            {
                string sub = lower.Substring(j, i - j);
                double c;
                if (WordCosts.TryGetValue(sub, out double wCost))
                {
                    // Penalize 1-letter words except 'a' and 'i'
                    if (sub.Length == 1 && sub != "a" && sub != "i")
                        wCost += 15.0;
                    c = costs[j] + wCost;
                }
                else
                {
                    c = costs[j] + 25.0 + sub.Length * 2.0;
                }

                if (c < costs[i])
                {
                    costs[i] = c;
                    prevs[i] = j;
                }
            }
        }

        var result = new List<string>();
        int idx = n;
        while (idx > 0)
        {
            int p = prevs[idx];
            result.Add(lower.Substring(p, idx - p));
            idx = p;
        }

        result.Reverse();
        return result;
    }
}
