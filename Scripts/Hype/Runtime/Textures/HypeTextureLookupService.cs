using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace HypeReborn.Hype.Runtime.Textures;

public static class HypeTextureLookupService
{
    private const uint TextureFlagColorKeyMask = 0x902u;
    private static readonly Dictionary<string, HypeCntFile> CntCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HypeTextureLookupResult> TextureCache = new(StringComparer.OrdinalIgnoreCase);

    public static Texture2D? TryGetTextureByTgaName(
        string gameRoot,
        string tgaName,
        uint textureFlags = 0,
        uint textureAlphaMask = 0)
    {
        return TryGetTextureByTgaNameDetailed(gameRoot, tgaName, textureFlags, textureAlphaMask).Texture;
    }

    public static HypeTextureLookupResult TryGetTextureByTgaNameDetailed(
        string gameRoot,
        string tgaName,
        uint textureFlags = 0,
        uint textureAlphaMask = 0)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(tgaName))
        {
            return HypeTextureLookupResult.Empty;
        }

        var candidateNames = BuildCandidateNames(tgaName);
        if (candidateNames.Count == 0)
        {
            return HypeTextureLookupResult.Empty;
        }

        var cacheKey = $"{gameRoot}::{candidateNames[0]}::{textureFlags:X8}:{textureAlphaMask:X8}";
        if (TextureCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var result = HypeTextureLookupResult.Empty;
        foreach (var containerPath in GetCandidateContainers(gameRoot))
        {
            var cnt = TryGetCnt(containerPath);
            if (cnt == null)
            {
                continue;
            }

            HypeCntFile.Entry? entry = null;
            foreach (var candidate in candidateNames)
            {
                entry = cnt.FindByTgaName(candidate) ?? FindBySuffix(cnt, candidate);
                if (entry != null)
                {
                    break;
                }
            }

            entry ??= FindByUniqueFileName(cnt, Path.GetFileName(candidateNames[0]));
            if (entry == null)
            {
                continue;
            }

            try
            {
                var bytes = cnt.ReadEntryBytes(entry);
                var image = HypeGfDecoder.Decode(bytes);
                if (ShouldApplyColorKey(textureFlags))
                {
                    ApplyColorKeyAlpha(image, textureAlphaMask);
                }

                AnalyzeImageAlpha(image, out var hasAnyTransparency, out var hasPartialTransparency);
                result = new HypeTextureLookupResult(
                    ImageTexture.CreateFromImage(image),
                    hasAnyTransparency,
                    hasPartialTransparency);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[HypeTexture] Failed to decode '{entry.FullName}' from '{containerPath}': {ex.Message}");
            }

            break;
        }

        TextureCache[cacheKey] = result;
        return result;
    }

    private static bool ShouldApplyColorKey(uint textureFlags)
    {
        return (textureFlags & TextureFlagColorKeyMask) != 0;
    }

    private static void ApplyColorKeyAlpha(Image image, uint alphaMask)
    {
        var maskBytes = BitConverter.GetBytes(alphaMask);
        var maskColor = Color.Color8(maskBytes[0], maskBytes[1], maskBytes[2], 255);

        var width = image.GetWidth();
        var height = image.GetHeight();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R8 == maskColor.R8 &&
                    pixel.G8 == maskColor.G8 &&
                    pixel.B8 == maskColor.B8)
                {
                    image.SetPixel(x, y, new Color(pixel.R, pixel.G, pixel.B, 0f));
                }
            }
        }
    }

    private static void AnalyzeImageAlpha(Image image, out bool hasAnyTransparency, out bool hasPartialTransparency)
    {
        hasAnyTransparency = false;
        hasPartialTransparency = false;

        var bytes = image.GetData();
        for (var i = 3; i < bytes.Length; i += 4)
        {
            var alpha = bytes[i];
            if (alpha >= 255)
            {
                continue;
            }

            hasAnyTransparency = true;
            if (alpha > 0)
            {
                hasPartialTransparency = true;
                return;
            }
        }
    }

    public readonly struct HypeTextureLookupResult
    {
        public HypeTextureLookupResult(Texture2D? texture, bool hasAnyTransparency, bool hasPartialTransparency)
        {
            Texture = texture;
            HasAnyTransparency = hasAnyTransparency;
            HasPartialTransparency = hasPartialTransparency;
        }

        public Texture2D? Texture { get; }
        public bool HasAnyTransparency { get; }
        public bool HasPartialTransparency { get; }

        public static HypeTextureLookupResult Empty => new(null, false, false);
    }

    private static HypeCntFile.Entry? FindBySuffix(HypeCntFile cnt, string normalizedTgaName)
    {
        var matches = cnt.Entries
            .Where(x => x.TgaName.EndsWith(normalizedTgaName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static HypeCntFile.Entry? FindByUniqueFileName(HypeCntFile cnt, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var matches = cnt.Entries
            .Where(x => Path.GetFileName(x.TgaName).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static IEnumerable<string> GetCandidateContainers(string gameRoot)
    {
        var candidates = new[]
        {
            Path.Combine(gameRoot, "Gamedata", "Textures.cnt"),
            Path.Combine(gameRoot, "Gamedata", "Vignette.cnt"),
            Path.Combine(gameRoot, "Gamedata", "World", "Levels", "fix.cnt")
        };

        return candidates.Where(File.Exists);
    }

    private static HypeCntFile? TryGetCnt(string path)
    {
        if (CntCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var cnt = new HypeCntFile(path);
            CntCache[path] = cnt;
            return cnt;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HypeTexture] Failed to read CNT '{path}': {ex.Message}");
            return null;
        }
    }

    private static List<string> BuildCandidateNames(string tgaName)
    {
        var candidates = new List<string>();

        void AddCandidate(string value)
        {
            var normalized = NormalizeTgaName(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }

        var baseName = NormalizeTgaName(tgaName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return candidates;
        }

        AddCandidate(baseName);

        if (baseName.StartsWith("game\\", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(baseName["game\\".Length..]);
        }

        if (baseName.StartsWith("gamedata\\", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(baseName["gamedata\\".Length..]);
        }

        if (baseName.StartsWith("game\\gamedata\\", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(baseName["game\\gamedata\\".Length..]);
        }

        var marker = "\\world\\graphics\\textures\\";
        var markerIndex = baseName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            AddCandidate(baseName[(markerIndex + 1)..]);
        }

        return candidates;
    }

    private static string NormalizeTgaName(string tgaName)
    {
        if (string.IsNullOrWhiteSpace(tgaName))
        {
            return string.Empty;
        }

        var normalized = tgaName.Replace('/', '\\').Trim();
        normalized = normalized.TrimStart('\\');
        if (normalized.EndsWith(".gf", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3] + ".tga";
        }

        return normalized;
    }
}
