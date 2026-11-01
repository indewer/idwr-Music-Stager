using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace MusicStagerWidget;

public class AppSettings
{
    public bool AlwaysOnTop { get; set; } = true;
    public double? WindowLeft { get; set; } = null;
    public double? WindowTop { get; set; } = null;
    public string? SavedScreenDeviceName { get; set; } = null;
    public bool? IsPrimaryScreen { get; set; } = null;

    public bool StartWithWindows { get; set; } = false;

    public string ItunesImportFolder { get; set; } = @"Z:\Заземление 2026 - Музыка\iTunes\iTunes Media\Automatically Add to iTunes";
    public string ItunesMusicFolder { get; set; } = @"Z:\Заземление 2026 - Музыка\iTunes\iTunes Media\Music";
    public string FfmpegPath { get; set; } = @"C:\Program Files\ffmpeg-8.1.1-full_build\bin\ffmpeg.exe";
    public string TagScannerPath { get; set; } = @"C:\Program Files\TagScanner\Tagscan.exe";

    public int MaxArtworkDimension { get; set; } = 600;
    public int JpegQuality { get; set; } = 95;
    public bool DeleteSourceAfterImport { get; set; } = false;

    // Mapping: Base Artist -> Album Artist (with suffix, e.g. "The Plot In You" -> "The Plot In You (USA)")
    public Dictionary<string, string> KnownArtists { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Genre usage frequency counter: Genre -> Count (e.g. "Alternative" -> 104)
    public Dictionary<string, int> GenreUsage { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicStagerWidget",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    settings.KnownArtists = new Dictionary<string, string>(settings.KnownArtists ?? new(), StringComparer.OrdinalIgnoreCase);
                    settings.GenreUsage = new Dictionary<string, int>(settings.GenreUsage ?? new(), StringComparer.OrdinalIgnoreCase);
                    if (settings.GenreUsage.Count == 0)
                    {
                        settings.ScanExistingItunesGenres();
                        settings.Save();
                    }
                    return settings;
                }
            }
        }
        catch { }

        var def = new AppSettings();
        def.ScanExistingItunesLibrary();
        def.ScanExistingItunesGenres();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch { }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string appName = "MusicStagerWidget";
            using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(appName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Scans iTunes Music folder to learn existing artist suffixes: (USA), (RUS), [BAND], etc.
    /// </summary>
    public void ScanExistingItunesLibrary()
    {
        try
        {
            if (!Directory.Exists(ItunesMusicFolder)) return;

            var dirs = Directory.GetDirectories(ItunesMusicFolder);
            foreach (var dir in dirs)
            {
                string folderName = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(folderName) || folderName.Equals("Compilations", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract base artist name if folder has suffix like "(USA)", "(RUS)", "[BAND]"
                string baseName = folderName;
                int parenIdx = folderName.IndexOf('(');
                int bracketIdx = folderName.IndexOf('[');
                int cutIdx = -1;

                if (parenIdx > 0 && bracketIdx > 0)
                    cutIdx = Math.Min(parenIdx, bracketIdx);
                else if (parenIdx > 0)
                    cutIdx = parenIdx;
                else if (bracketIdx > 0)
                    cutIdx = bracketIdx;

                if (cutIdx > 0)
                {
                    baseName = folderName.Substring(0, cutIdx).Trim();
                }

                if (!string.IsNullOrWhiteSpace(baseName) && !KnownArtists.ContainsKey(baseName))
                {
                    KnownArtists[baseName] = folderName;
                }
            }
        }
        catch { }
    }

    public string GetAlbumArtist(string baseArtist)
    {
        if (string.IsNullOrWhiteSpace(baseArtist)) return "";
        if (KnownArtists.TryGetValue(baseArtist.Trim(), out var mapped))
            return mapped;
        return baseArtist.Trim();
    }

    public void RegisterArtist(string baseArtist, string albumArtist)
    {
        if (string.IsNullOrWhiteSpace(baseArtist)) return;
        KnownArtists[baseArtist.Trim()] = albumArtist.Trim();
        Save();
    }

    /// <summary>
    /// Scans iTunes Music Library.xml to extract existing genre usage frequencies.
    /// </summary>
    public void ScanExistingItunesGenres()
    {
        try
        {
            var candidates = new List<string>();

            // Try relative to iTunes import folder
            if (!string.IsNullOrEmpty(ItunesImportFolder))
            {
                var dir1 = Path.GetDirectoryName(ItunesImportFolder); // ...\iTunes Media
                if (dir1 != null)
                {
                    var itunesDir = Path.GetDirectoryName(dir1); // ...\iTunes
                    if (itunesDir != null)
                    {
                        candidates.Add(Path.Combine(itunesDir, "iTunes Music Library.xml"));
                        candidates.Add(Path.Combine(itunesDir, "iTunes Library.xml"));
                    }
                }
            }

            // Fallback locations
            candidates.Add(@"Z:\Заземление 2026 - Музыка\iTunes\iTunes Music Library.xml");
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "iTunes", "iTunes Music Library.xml"));

            string? xmlPath = candidates.FirstOrDefault(File.Exists);
            if (xmlPath == null) return;

            string content = File.ReadAllText(xmlPath);
            var matches = System.Text.RegularExpressions.Regex.Matches(content, @"<key>Genre</key><string>([^<]+)</string>");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string rawGenre = match.Groups[1].Value;
                // Unescape XML entities like &#38; -> &
                rawGenre = System.Net.WebUtility.HtmlDecode(rawGenre);

                var parts = rawGenre.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    string g = p.Trim();
                    if (!string.IsNullOrEmpty(g))
                    {
                        if (GenreUsage.ContainsKey(g))
                            GenreUsage[g]++;
                        else
                            GenreUsage[g] = 1;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Increments usage counter for each genre in a pipe-separated string and saves settings.
    /// </summary>
    public void RegisterGenreUsage(string genreString)
    {
        if (string.IsNullOrWhiteSpace(genreString)) return;

        var parts = genreString.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        bool modified = false;

        foreach (var p in parts)
        {
            string g = p.Trim();
            if (!string.IsNullOrEmpty(g))
            {
                if (GenreUsage.ContainsKey(g))
                    GenreUsage[g]++;
                else
                    GenreUsage[g] = 1;

                modified = true;
            }
        }

        if (modified)
        {
            Save();
        }
    }

    public List<KeyValuePair<string, int>> GetTopGenres()
    {
        if (GenreUsage == null || GenreUsage.Count == 0)
        {
            // Seed defaults if nothing yet
            return new List<KeyValuePair<string, int>>
            {
                new("Alternative", 10),
                new("Indie Rock", 8),
                new("Metalcore", 7),
                new("Rock", 6),
                new("Electronic", 5),
                new("Post-Hardcore", 4),
                new("Hip-Hop", 3),
                new("Pop-Rock", 2)
            };
        }

        return GenreUsage
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .ToList();
    }
}
