using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace HypeReborn.Hype.Runtime.Textures;

public static class HypeVignettePreviewService
{
    private static readonly Dictionary<string, HypeCntFile> CntCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);

    public static Texture2D? TryGetMapPreview(string gameRoot, string levelName)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(levelName))
        {
            return null;
        }

        var cnt = TryGetVignetteCnt(gameRoot);
        if (cnt == null)
        {
            return null;
        }

        var candidate = FindBestMapEntry(cnt, levelName);
        return candidate == null ? null : TryDecodeEntry(cnt, candidate);
    }

    public static Texture2D? TryGetTextureByFullName(string containerPath, string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(containerPath) || string.IsNullOrWhiteSpace(entryFullName))
        {
            return null;
        }

        var cnt = TryGetCnt(containerPath);
        var entry = cnt?.FindByFullName(entryFullName);
        return entry == null || cnt == null ? null : TryDecodeEntry(cnt, entry);
    }

    private static HypeCntFile? TryGetVignetteCnt(string gameRoot)
    {
        var path = Path.Combine(gameRoot, "Gamedata", "Vignette.cnt");
        return File.Exists(path) ? TryGetCnt(path) : null;
    }

    private static HypeCntFile? TryGetCnt(string path)
    {
        if (CntCache.TryGetValue(path, out var existing))
        {
            return existing;
        }

        try
        {
            var cnt = new HypeCntFile(path);
            CntCache[path] = cnt;
            return cnt;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HypeVignette] Failed to parse CNT '{path}': {ex.Message}");
            return null;
        }
    }

    private static HypeCntFile.Entry? FindBestMapEntry(HypeCntFile cnt, string levelName)
    {
        var normalized = Normalize(levelName);
        var stripped = StripTrailingDigits(normalized);

        var candidates = cnt.Entries;

        HypeCntFile.Entry? best = null;
        var bestScore = int.MinValue;

        foreach (var entry in candidates)
        {
            var baseName = Normalize(Path.GetFileNameWithoutExtension(entry.Name));
            var score = Score(baseName, normalized, stripped);
            if (score > bestScore)
            {
                best = entry;
                bestScore = score;
            }
        }

        return bestScore >= 10 ? best : null;
    }

    private static int Score(string baseName, string normalized, string stripped)
    {
        if (baseName == normalized)
        {
            return 100;
        }

        if (baseName == stripped)
        {
            return 90;
        }

        if (normalized.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
        {
            return 70;
        }

        if (baseName.StartsWith(stripped, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (baseName.Contains(stripped, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return 0;
    }

    private static Texture2D? TryDecodeEntry(HypeCntFile cnt, HypeCntFile.Entry entry)
    {
        var cacheKey = $"{cnt.Path}::{entry.FullName}";
        if (TextureCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var bytes = cnt.ReadEntryBytes(entry);
            var image = HypeGfDecoder.Decode(bytes);
            var texture = ImageTexture.CreateFromImage(image);
            TextureCache[cacheKey] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HypeVignette] Failed to decode '{entry.FullName}': {ex.Message}");
            return null;
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string StripTrailingDigits(string value)
    {
        var i = value.Length;
        while (i > 0 && char.IsDigit(value[i - 1]))
        {
            i--;
        }

        return i > 0 ? value[..i] : value;
    }
}
