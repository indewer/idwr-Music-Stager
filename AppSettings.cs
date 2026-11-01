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
                    return settings;
                }
            }
        }
        catch { }

        var def = new AppSettings();
        def.ScanExistingItunesLibrary();
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
}
