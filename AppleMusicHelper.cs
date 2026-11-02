using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStagerWidget;

public static class AppleMusicHelper
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ConcurrentDictionary<string, uint?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up the official Apple Music / iTunes Store Artist ID (atID) for an artist.
    /// Returns null if not found or on error.
    /// </summary>
    public static async Task<uint?> LookupArtistIdAsync(string artistName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return null;

        // Strip country and custom brackets like (USA), (RUS), (UK), [&], [BAND], etc.
        string clean = Regex.Replace(artistName, @"\s*\((?:USA|RUS|UK|ENG|GER|FRA|JPN|CAN|AUS|[A-Z]{2,3})\)", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*\[(?:BAND|&|MNQN|ЯАVЬ|[^\]]+)\]", "", RegexOptions.IgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(clean))
            clean = artistName.Trim();

        if (Cache.TryGetValue(clean, out var cachedId))
            return cachedId;

        try
        {
            string url = $"https://itunes.apple.com/search?term={WebUtility.UrlEncode(clean)}&entity=musicArtist&limit=5";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Cache[clean] = null;
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                Cache[clean] = null;
                return null;
            }

            uint? matchedId = null;

            // First pass: exact case-insensitive match
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("artistName", out var nameProp) &&
                    string.Equals(nameProp.GetString(), clean, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.TryGetProperty("artistId", out var idProp))
                    {
                        matchedId = (uint)idProp.GetInt64();
                        break;
                    }
                }
            }

            // Second pass: fallback to first result
            if (matchedId == null && results.GetArrayLength() > 0)
            {
                var first = results[0];
                if (first.TryGetProperty("artistId", out var idProp))
                {
                    matchedId = (uint)idProp.GetInt64();
                }
            }

            Cache[clean] = matchedId;
            return matchedId;
        }
        catch
        {
            Cache[clean] = null;
            return null;
        }
    }

    /// <summary>
    /// Writes the atID atom (Apple Store Artist ID) into an MP4/M4A container in-place.
    /// If atID exists, updates the value. If not, inserts the 28-byte atID atom into ilst,
    /// absorbing the space into the free box so mdat and chunk offsets do not shift.
    /// </summary>
    public static bool TryWriteMp4ArtistId(string filePath, uint artistId)
    {
        try
        {
            if (!File.Exists(filePath) || !filePath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                return false;

            byte[] data = File.ReadAllBytes(filePath);

            int ilstPos = FindSequence(data, "ilst"u8.ToArray());
            if (ilstPos == -1) return false;

            int ilstStart = ilstPos - 4;
            int ilstSize = ReadBigEndianInt32(data, ilstStart);

            // Check if atID already exists in ilst
            int atidPos = FindSequence(data, "atID"u8.ToArray(), ilstStart, ilstSize);
            if (atidPos != -1)
            {
                // Update 4-byte payload at atidPos + 20
                int valOffset = atidPos + 20;
                WriteBigEndianUInt32(data, valOffset, artistId);
                File.WriteAllBytes(filePath, data);
                return true;
            }

            // Construct 28-byte atID box
            byte[] atidBox = new byte[28];
            WriteBigEndianInt32(atidBox, 0, 28);               // Box size = 28
            "atID"u8.CopyTo(atidBox.AsSpan(4, 4));              // 'atID'
            WriteBigEndianInt32(atidBox, 8, 20);               // data box size = 20
            "data"u8.CopyTo(atidBox.AsSpan(12, 4));             // 'data'
            WriteBigEndianInt32(atidBox, 16, 21);              // type = 21 (BE uint32)
            WriteBigEndianInt32(atidBox, 20, 0);               // flags/locale = 0
            WriteBigEndianUInt32(atidBox, 24, artistId);       // payload artistId

            int metaPos = FindSequenceReverse(data, "meta"u8.ToArray(), 0, ilstStart);
            if (metaPos == -1) return false;
            int metaStart = metaPos - 4;

            int udtaPos = FindSequenceReverse(data, "udta"u8.ToArray(), 0, metaStart);
            if (udtaPos == -1) return false;
            int udtaStart = udtaPos - 4;

            int moovPos = FindSequenceReverse(data, "moov"u8.ToArray(), 0, udtaStart);
            if (moovPos == -1) return false;
            int moovStart = moovPos - 4;

            int moovSize = ReadBigEndianInt32(data, moovStart);
            int afterMoov = moovStart + moovSize;

            if (afterMoov + 8 > data.Length) return false;

            int freeSize = ReadBigEndianInt32(data, afterMoov);
            bool isFree = data[afterMoov + 4] == (byte)'f' &&
                         data[afterMoov + 5] == (byte)'r' &&
                         data[afterMoov + 6] == (byte)'e' &&
                         data[afterMoov + 7] == (byte)'e';

            if (!isFree || freeSize < 28 + 8)
            {
                return false;
            }

            // In-place absorption into free box:
            // 1. Insert atidBox at end of ilst
            int insertPos = ilstStart + ilstSize;
            byte[] newData = new byte[data.Length]; // total length stays same after absorption

            // Copy before insert
            Array.Copy(data, 0, newData, 0, insertPos);
            // Copy atID box
            Array.Copy(atidBox, 0, newData, insertPos, 28);
            // Copy between insert and free payload
            int midLen = afterMoov - insertPos;
            Array.Copy(data, insertPos, newData, insertPos + 28, midLen);

            // Update box headers (+28) in newData
            WriteBigEndianInt32(newData, ilstStart, ilstSize + 28);
            WriteBigEndianInt32(newData, metaStart, ReadBigEndianInt32(data, metaStart) + 28);
            WriteBigEndianInt32(newData, udtaStart, ReadBigEndianInt32(data, udtaStart) + 28);
            int newMoovSize = moovSize + 28;
            WriteBigEndianInt32(newData, moovStart, newMoovSize);

            // Free box shifts by +28, and its size decreases by 28
            int newFreePos = moovStart + newMoovSize;
            int newFreeSize = freeSize - 28;
            WriteBigEndianInt32(newData, newFreePos, newFreeSize);
            newData[newFreePos + 4] = (byte)'f';
            newData[newFreePos + 5] = (byte)'r';
            newData[newFreePos + 6] = (byte)'e';
            newData[newFreePos + 7] = (byte)'e';

            // Copy remaining free payload and mdat
            int oldFreePayloadAfter28 = afterMoov + 8 + 28;
            int remLen = data.Length - oldFreePayloadAfter28;
            Array.Copy(data, oldFreePayloadAfter28, newData, newFreePos + 8, remLen);

            File.WriteAllBytes(filePath, newData);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static void WriteBigEndianInt32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteBigEndianUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static int FindSequence(byte[] data, byte[] pattern, int start = 0, int length = -1)
    {
        int limit = (length == -1 ? data.Length : Math.Min(data.Length, start + length)) - pattern.Length;
        for (int i = start; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static int FindSequenceReverse(byte[] data, byte[] pattern, int start, int end)
    {
        int limit = Math.Min(data.Length, end) - pattern.Length;
        for (int i = limit; i >= start; i--)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
