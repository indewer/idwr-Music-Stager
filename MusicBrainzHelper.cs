using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStagerWidget;

public record ReleaseGroupInfo(
    string Title,
    string? FirstReleaseDate,
    string? PrimaryType,
    List<string> SecondaryTypes
);

public class MusicBrainzResult
{
    public int? AlbumNumber { get; set; }
    public string ReleaseType { get; set; } = "Album"; // "Album", "EP", "Single"
    public string SuggestedComment { get; set; } = "-A-01-";
    public string MatchedTitle { get; set; } = "";
    public int TotalStudioAlbums { get; set; }
}

public static class MusicBrainzHelper
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    static MusicBrainzHelper()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MusicStagerWidget/0.1 (contact: indewer@ya.ru)");
    }

    public static async Task<MusicBrainzResult?> LookupDiscographyAsync(string artist, string albumTitle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(albumTitle))
            return null;

        try
        {
            // Escape query params
            string cleanArtist = artist.Trim().Replace("\"", "\\\"");
            string url = $"https://musicbrainz.org/ws/2/release-group?query=artist:%22{Uri.EscapeDataString(cleanArtist)}%22&fmt=json&limit=100";

            using var response = await HttpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("release-groups", out var releaseGroupsElem))
                return null;

            var items = new List<ReleaseGroupInfo>();
            foreach (var elem in releaseGroupsElem.EnumerateArray())
            {
                string title = elem.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                string? date = elem.TryGetProperty("first-release-date", out var d) ? d.GetString() : null;
                string? primaryType = elem.TryGetProperty("primary-type", out var pt) ? pt.GetString() : null;

                var secTypes = new List<string>();
                if (elem.TryGetProperty("secondary-types", out var stElem))
                {
                    foreach (var s in stElem.EnumerateArray())
                    {
                        var str = s.GetString();
                        if (!string.IsNullOrEmpty(str)) secTypes.Add(str);
                    }
                }

                items.Add(new ReleaseGroupInfo(title, date, primaryType, secTypes));
            }

            // Exclude live, compilation, remix, soundtrack for studio numbering
            bool IsStudioAlbum(ReleaseGroupInfo r)
            {
                if (r.PrimaryType != "Album") return false;
                foreach (var st in r.SecondaryTypes)
                {
                    if (st.Equals("Live", StringComparison.OrdinalIgnoreCase) ||
                        st.Equals("Compilation", StringComparison.OrdinalIgnoreCase) ||
                        st.Equals("Remix", StringComparison.OrdinalIgnoreCase) ||
                        st.Equals("Soundtrack", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                return true;
            }

            // 1. Check if albumTitle is a Single
            var singleMatch = items.FirstOrDefault(i => i.PrimaryType == "Single" && TitlesMatch(i.Title, albumTitle));
            if (singleMatch != null)
            {
                return new MusicBrainzResult
                {
                    ReleaseType = "Single",
                    SuggestedComment = "-S-",
                    MatchedTitle = singleMatch.Title
                };
            }

            // 2. Check if albumTitle is an EP
            var epMatch = items.FirstOrDefault(i => i.PrimaryType == "EP" && TitlesMatch(i.Title, albumTitle));
            if (epMatch != null)
            {
                return new MusicBrainzResult
                {
                    ReleaseType = "EP",
                    SuggestedComment = "-EP-",
                    MatchedTitle = epMatch.Title
                };
            }

            // 3. Studio Albums chronological list
            var studioAlbums = items
                .Where(IsStudioAlbum)
                .Where(i => !string.IsNullOrEmpty(i.FirstReleaseDate))
                .OrderBy(i => i.FirstReleaseDate)
                .ToList();

            int matchIndex = -1;
            for (int i = 0; i < studioAlbums.Count; i++)
            {
                if (TitlesMatch(studioAlbums[i].Title, albumTitle))
                {
                    matchIndex = i;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                int albumNum = matchIndex + 1;
                return new MusicBrainzResult
                {
                    AlbumNumber = albumNum,
                    ReleaseType = "Album",
                    SuggestedComment = $"-A-{albumNum:D2}-",
                    MatchedTitle = studioAlbums[matchIndex].Title,
                    TotalStudioAlbums = studioAlbums.Count
                };
            }

            // If album matched without studio filter or partial match
            var anyAlbumMatch = items.FirstOrDefault(i => i.PrimaryType == "Album" && TitlesMatch(i.Title, albumTitle));
            if (anyAlbumMatch != null)
            {
                return new MusicBrainzResult
                {
                    AlbumNumber = 1,
                    ReleaseType = "Album",
                    SuggestedComment = "-A-01-",
                    MatchedTitle = anyAlbumMatch.Title,
                    TotalStudioAlbums = studioAlbums.Count
                };
            }
        }
        catch { }

        return null;
    }

    private static bool TitlesMatch(string a, string b)
    {
        string normA = NormalizeTitle(a);
        string normB = NormalizeTitle(b);

        if (string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase)) return true;
        if (normA.Contains(normB, StringComparison.OrdinalIgnoreCase) || normB.Contains(normA, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizeTitle(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var chars = s.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return new string(chars).Trim().ToLowerInvariant();
    }
}
