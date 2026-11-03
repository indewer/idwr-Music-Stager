using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicStagerWidget;

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersionTag { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public static class UpdateHelper
{
    public const string CurrentVersion = "0.1 b9";
    public const string CurrentVersionTag = "v0.1-b9";
    private const string GitHubApiUrl = "https://api.github.com/repos/indewer/idwr-Music-Stager/releases/latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static UpdateHelper()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("idwr-MusicStager", "1.0"));
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        var result = new UpdateCheckResult();
        try
        {
            using var response = await _http.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"GitHub ответил со статусом {(int)response.StatusCode}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tag_name", out var tagElem))
            {
                result.LatestVersionTag = tagElem.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("body", out var bodyElem))
            {
                result.ReleaseNotes = bodyElem.GetString() ?? string.Empty;
            }

            // Find executable asset (idwr-Music-Stager-*.exe)
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp))
                    {
                        string name = nameProp.GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlProp))
                            {
                                result.DownloadUrl = urlProp.GetString() ?? string.Empty;
                                break;
                            }
                        }
                    }
                }
            }

            result.IsUpdateAvailable = IsNewer(result.LatestVersionTag, CurrentVersionTag);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public static async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, Action<string>? statusCallback = null)
    {
        try
        {
            statusCallback?.Invoke("Загрузка обновления...");
            byte[] exeBytes = await _http.GetByteArrayAsync(downloadUrl);

            string tempFile = Path.Combine(Path.GetTempPath(), $"idwr_stager_update_{Guid.NewGuid():N}.exe");
            await File.WriteAllBytesAsync(tempFile, exeBytes);

            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrEmpty(currentExe) || !File.Exists(tempFile))
            {
                return false;
            }

            statusCallback?.Invoke("Перезапуск...");

            string dir = Path.GetDirectoryName(currentExe)!;
            string assetName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            string targetExe = Path.Combine(dir, assetName);

            string cmdArgs;
            if (string.Equals(currentExe, targetExe, StringComparison.OrdinalIgnoreCase))
            {
                cmdArgs = $"/c timeout /t 1 /nobreak >nul & move /y \"{tempFile}\" \"{currentExe}\" & start \"\" \"{currentExe}\"";
            }
            else
            {
                cmdArgs = $"/c timeout /t 1 /nobreak >nul & move /y \"{tempFile}\" \"{targetExe}\" & del /f /q \"{currentExe}\" & start \"\" \"{targetExe}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process.Start(psi);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });

            return true;
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke($"Ошибка: {ex.Message}");
            return false;
        }
    }

    public static bool IsNewer(string latestTag, string currentTag)
    {
        if (string.IsNullOrWhiteSpace(latestTag)) return false;
        if (string.Equals(latestTag.Trim(), currentTag.Trim(), StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var (latMaj, latMin, latPatch, latBuild) = ParseVersion(latestTag);
            var (curMaj, curMin, curPatch, curBuild) = ParseVersion(currentTag);

            if (latMaj != curMaj) return latMaj > curMaj;
            if (latMin != curMin) return latMin > curMin;
            if (latPatch != curPatch) return latPatch > curPatch;
            if (latBuild != curBuild) return latBuild > curBuild;
        }
        catch { }

        return !string.Equals(latestTag, currentTag, StringComparison.OrdinalIgnoreCase);
    }

    private static (int major, int minor, int patch, int build) ParseVersion(string tag)
    {
        // Parses formats like "v0.1-b9", "0.1 b9", "v0.2.0", "v0.1-b10"
        string clean = tag.Trim().TrimStart('v', 'V');
        int build = 0;
        int bIdx = clean.IndexOf('b', StringComparison.OrdinalIgnoreCase);
        if (bIdx >= 0)
        {
            string bPart = new string(clean.Substring(bIdx + 1).TakeWhile(char.IsDigit).ToArray());
            int.TryParse(bPart, out build);
            clean = clean.Substring(0, bIdx).TrimEnd('-', ' ', '_');
        }

        var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int major = parts.Length > 0 && int.TryParse(parts[0], out var ma) ? ma : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0;
        int patch = parts.Length > 2 && int.TryParse(parts[2], out var pa) ? pa : 0;

        return (major, minor, patch, build);
    }
}
