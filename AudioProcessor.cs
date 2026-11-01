using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStagerWidget;

public class TrackMetadata
{
    public string FilePath { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Album { get; set; } = "";
    public uint Year { get; set; }
    public string Genre { get; set; } = "";
    public uint TrackNumber { get; set; }
    public uint TotalTracks { get; set; }
    public uint DiscNumber { get; set; }
    public string Comment { get; set; } = "";
    public bool HasCover { get; set; }
    public byte[]? CoverData { get; set; }
    public bool IsLossless => Extension is ".flac" or ".wav" or ".ape" or ".aiff";
    public bool IsM4A => Extension is ".m4a";
    public bool IsMP3 => Extension is ".mp3";
}

public class AlbumBatchInfo
{
    public string SourceDirectory { get; set; } = "";
    public List<TrackMetadata> Tracks { get; set; } = new();
    public string DetectedArtist { get; set; } = "";
    public string DetectedAlbumArtist { get; set; } = "";
    public string DetectedAlbum { get; set; } = "";
    public uint DetectedYear { get; set; }
    public string DetectedGenre { get; set; } = "";
    public string DetectedComment { get; set; } = "";
    public byte[]? OptimizedCoverBytes { get; set; }
    public string FormatSummary { get; set; } = "";
}

public static class AudioProcessor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".wav", ".ape", ".aiff", ".m4a", ".mp3"
    };

    /// <summary>
    /// Scans a directory or dropped file list and extracts batch metadata.
    /// </summary>
    public static AlbumBatchInfo ScanInput(string[] paths, AppSettings settings)
    {
        var result = new AlbumBatchInfo();
        var filesToProcess = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                if (string.IsNullOrEmpty(result.SourceDirectory))
                    result.SourceDirectory = path;

                var foundFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                    .ToList();
                filesToProcess.AddRange(foundFiles);
            }
            else if (File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                if (string.IsNullOrEmpty(result.SourceDirectory))
                    result.SourceDirectory = Path.GetDirectoryName(path) ?? "";
                filesToProcess.Add(path);
            }
        }

        // Natural sort by filename
        filesToProcess = filesToProcess
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (filesToProcess.Count == 0) return result;

        byte[]? foundCoverBytes = null;

        // Check if there is an image file in the source directory
        if (!string.IsNullOrEmpty(result.SourceDirectory) && Directory.Exists(result.SourceDirectory))
        {
            var imgCandidates = new[] { "cover.jpg", "folder.jpg", "front.jpg", "art.jpg", "cover.png", "folder.png" };
            foreach (var cand in imgCandidates)
            {
                string fullCand = Path.Combine(result.SourceDirectory, cand);
                if (File.Exists(fullCand))
                {
                    foundCoverBytes = ImageOptimizer.OptimizeImageFile(fullCand, settings.MaxArtworkDimension, settings.JpegQuality);
                    break;
                }
            }

            if (foundCoverBytes == null)
            {
                var anyImg = Directory.GetFiles(result.SourceDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                         f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                         f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                if (anyImg != null)
                {
                    foundCoverBytes = ImageOptimizer.OptimizeImageFile(anyImg, settings.MaxArtworkDimension, settings.JpegQuality);
                }
            }
        }

        uint trackCounter = 1;
        int flacCount = 0;
        int m4aCount = 0;
        int mp3Count = 0;

        foreach (var file in filesToProcess)
        {
            var track = new TrackMetadata
            {
                FilePath = file,
                Extension = Path.GetExtension(file).ToLowerInvariant()
            };

            if (track.IsLossless) flacCount++;
            else if (track.IsM4A) m4aCount++;
            else if (track.IsMP3) mp3Count++;

            try
            {
                using var tagFile = TagLib.File.Create(file);
                var tag = tagFile.Tag;

                track.Title = tag.Title ?? Path.GetFileNameWithoutExtension(file);
                track.Artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? "";
                track.AlbumArtist = tag.FirstAlbumArtist ?? "";
                track.Album = tag.Album ?? "";
                track.Year = tag.Year;
                track.Genre = tag.FirstGenre ?? "";
                track.TrackNumber = tag.Track > 0 ? tag.Track : trackCounter;
                track.TotalTracks = tag.TrackCount > 0 ? tag.TrackCount : (uint)filesToProcess.Count;
                track.DiscNumber = tag.Disc > 0 ? tag.Disc : 1;
                track.Comment = tag.Comment ?? "";

                if (foundCoverBytes == null && tag.Pictures != null && tag.Pictures.Length > 0)
                {
                    var pic = tag.Pictures[0];
                    if (pic.Data != null && pic.Data.Data.Length > 0)
                    {
                        foundCoverBytes = ImageOptimizer.OptimizeImageBytes(pic.Data.Data, settings.MaxArtworkDimension, settings.JpegQuality);
                    }
                }
            }
            catch
            {
                track.Title = Path.GetFileNameWithoutExtension(file);
                track.TrackNumber = trackCounter;
                track.TotalTracks = (uint)filesToProcess.Count;
            }

            result.Tracks.Add(track);
            trackCounter++;
        }

        result.OptimizedCoverBytes = foundCoverBytes;

        // Determine dominant album/artist info from tracks
        result.DetectedArtist = result.Tracks.Select(t => t.Artist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "";
        result.DetectedAlbum = result.Tracks.Select(t => t.Album).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "";
        result.DetectedYear = result.Tracks.Select(t => t.Year).FirstOrDefault(y => y > 0);
        result.DetectedGenre = result.Tracks.Select(t => t.Genre).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "";
        result.DetectedComment = result.Tracks.Select(t => t.Comment).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";

        // Fallback artist/album from folder name if tags were empty
        if (string.IsNullOrWhiteSpace(result.DetectedAlbum) && !string.IsNullOrWhiteSpace(result.SourceDirectory))
        {
            result.DetectedAlbum = Path.GetFileName(result.SourceDirectory);
        }

        // Apply known artist map if available
        result.DetectedAlbumArtist = settings.GetAlbumArtist(result.DetectedArtist);

        // Format summary
        var parts = new List<string>();
        if (flacCount > 0) parts.Add($"{flacCount} Lossless ➔ ALAC .m4a");
        if (m4aCount > 0) parts.Add($"{m4aCount} M4A (без пережатия)");
        if (mp3Count > 0) parts.Add($"{mp3Count} MP3 (без пережатия)");
        result.FormatSummary = string.Join(" • ", parts);

        return result;
    }

    /// <summary>
    /// Executes the full conversion, tagging, and dispatch pipeline into iTunes.
    /// </summary>
    public static async Task ProcessBatchAsync(
        AlbumBatchInfo batch,
        string targetArtist,
        string targetAlbumArtist,
        string targetAlbum,
        uint targetYear,
        string targetGenre,
        string targetComment,
        byte[]? coverBytes,
        AppSettings settings,
        Action<int, int, string> progressCallback,
        CancellationToken ct)
    {
        string itunesAddFolder = settings.ItunesImportFolder;
        if (!Directory.Exists(itunesAddFolder))
        {
            Directory.CreateDirectory(itunesAddFolder);
        }

        string tempStagingDir = Path.Combine(Path.GetTempPath(), "MusicStager_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStagingDir);

        try
        {
            int total = batch.Tracks.Count;
            int current = 0;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var track = batch.Tracks[i];
                current++;

                string cleanTitle = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : Path.GetFileNameWithoutExtension(track.FilePath);
                string trackPrefix = track.TrackNumber > 0 ? $"{track.TrackNumber:D2} " : "";
                string safeFileName = $"{trackPrefix}{SanitizeFileName(cleanTitle)}";

                progressCallback(current, total, $"[{current}/{total}] {cleanTitle}");

                string readyFilePath;

                if (track.IsLossless)
                {
                    // Transcode Lossless -> ALAC (.m4a)
                    readyFilePath = Path.Combine(tempStagingDir, safeFileName + ".m4a");
                    await TranscodeToAlacAsync(track.FilePath, readyFilePath, settings.FfmpegPath, ct);
                }
                else if (track.IsM4A)
                {
                    // Copy M4A to temp for in-place tagging without touching source yet
                    readyFilePath = Path.Combine(tempStagingDir, safeFileName + ".m4a");
                    File.Copy(track.FilePath, readyFilePath, true);
                }
                else
                {
                    // MP3 or other lossy -> preserve original format
                    string ext = track.Extension.ToLowerInvariant();
                    readyFilePath = Path.Combine(tempStagingDir, safeFileName + ext);
                    File.Copy(track.FilePath, readyFilePath, true);
                }

                // Write tags and cover using TagLib
                ApplyTags(readyFilePath, track, targetArtist, targetAlbumArtist, targetAlbum, targetYear, targetGenre, targetComment, coverBytes);

                // Move finished file directly to iTunes "Automatically Add to iTunes"
                string destFileName = Path.GetFileName(readyFilePath);
                string itunesDestPath = Path.Combine(itunesAddFolder, destFileName);

                if (File.Exists(itunesDestPath))
                {
                    // Deduplicate if name collisions occur
                    destFileName = $"{Path.GetFileNameWithoutExtension(destFileName)}_{Guid.NewGuid().ToString("N")[..4]}{Path.GetExtension(destFileName)}";
                    itunesDestPath = Path.Combine(itunesAddFolder, destFileName);
                }

                File.Move(readyFilePath, itunesDestPath);

                // If user selected to delete source files
                if (settings.DeleteSourceAfterImport)
                {
                    try { File.Delete(track.FilePath); } catch { }
                }
            }

            // If source directory is now empty and user wanted deletion, remove it
            if (settings.DeleteSourceAfterImport && !string.IsNullOrEmpty(batch.SourceDirectory) && Directory.Exists(batch.SourceDirectory))
            {
                try
                {
                    var remaining = Directory.GetFiles(batch.SourceDirectory, "*.*", SearchOption.AllDirectories);
                    if (remaining.Length == 0)
                    {
                        Directory.Delete(batch.SourceDirectory, true);
                    }
                }
                catch { }
            }
        }
        finally
        {
            // Clean up temp staging directory
            try
            {
                if (Directory.Exists(tempStagingDir))
                {
                    Directory.Delete(tempStagingDir, true);
                }
            }
            catch { }
        }
    }

    private static async Task TranscodeToAlacAsync(string inputPath, string outputPath, string ffmpegPath, CancellationToken ct)
    {
        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException($"FFmpeg не найден по пути: {ffmpegPath}");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{inputPath}\" -c:a alac -vn \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            string error = await stderrTask;
            throw new InvalidOperationException($"Ошибка FFmpeg при конвертации: {error}");
        }
    }

    private static void ApplyTags(
        string filePath,
        TrackMetadata track,
        string artist,
        string albumArtist,
        string album,
        uint year,
        string genre,
        string comment,
        byte[]? coverBytes)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;

            if (!string.IsNullOrWhiteSpace(track.Title))
                tag.Title = track.Title.Trim();

            if (!string.IsNullOrWhiteSpace(artist))
                tag.Performers = new[] { artist.Trim() };

            if (!string.IsNullOrWhiteSpace(albumArtist))
                tag.AlbumArtists = new[] { albumArtist.Trim() };

            if (!string.IsNullOrWhiteSpace(album))
                tag.Album = album.Trim();

            if (year > 0)
                tag.Year = year;

            if (!string.IsNullOrWhiteSpace(genre))
                tag.Genres = new[] { genre.Trim() };

            if (!string.IsNullOrWhiteSpace(comment))
                tag.Comment = comment.Trim();

            if (track.TrackNumber > 0)
                tag.Track = track.TrackNumber;

            if (track.TotalTracks > 0)
                tag.TrackCount = track.TotalTracks;

            if (track.DiscNumber > 0)
                tag.Disc = track.DiscNumber;

            // Embed artwork if provided
            if (coverBytes != null && coverBytes.Length > 0)
            {
                var pic = new TagLib.Picture
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = "image/jpeg",
                    Description = "Cover",
                    Data = new TagLib.ByteVector(coverBytes)
                };
                tag.Pictures = new TagLib.IPicture[] { pic };
            }

            file.Save();
        }
        catch { }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}
