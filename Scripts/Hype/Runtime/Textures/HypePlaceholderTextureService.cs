using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace HypeReborn.Hype.Runtime.Textures;

public static class HypePlaceholderTextureService
{
    private static readonly Dictionary<string, IReadOnlyList<(string Container, string Entry)>> EntryCatalogCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Texture2D> TextureCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Texture2D? TryGetGeometryTexture(string gameRoot, string seed)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return null;
        }

        var catalog = GetOrBuildCatalog(gameRoot);
        if (catalog.Count == 0)
        {
            return null;
        }

        var hash = StableHash(seed);
        var index = (int)(hash % (uint)catalog.Count);
        var candidate = catalog[index];
        var key = $"{candidate.Container}::{candidate.Entry}";

        if (TextureCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var texture = HypeVignettePreviewService.TryGetTextureByFullName(candidate.Container, candidate.Entry);
        if (texture == null)
        {
            return null;
        }

        TextureCache[key] = texture;
        return texture;
    }

    private static IReadOnlyList<(string Container, string Entry)> GetOrBuildCatalog(string gameRoot)
    {
        if (EntryCatalogCache.TryGetValue(gameRoot, out var cached))
        {
            return cached;
        }

        var result = new List<(string Container, string Entry)>();
        foreach (var container in GetCandidateContainers(gameRoot))
        {
            try
            {
                using var cnt = new HypeCntFile(container);
                foreach (var entry in cnt.Entries.Where(IsLikelyRenderableTextureEntry))
                {
                    result.Add((container, entry.FullName));
                }
            }
            catch
            {
                // Ignore invalid containers for placeholder purposes.
            }
        }

        EntryCatalogCache[gameRoot] = result;
        return result;
    }

    private static IEnumerable<string> GetCandidateContainers(string gameRoot)
    {
        var candidates = new[]
        {
            Path.Combine(gameRoot, "Gamedata", "Textures.cnt"),
            Path.Combine(gameRoot, "Gamedata", "World", "Levels", "fix.cnt"),
            Path.Combine(gameRoot, "Gamedata", "Vignette.cnt")
        };

        return candidates.Where(File.Exists);
    }

    private static bool IsLikelyRenderableTextureEntry(HypeCntFile.Entry entry)
    {
        var ext = Path.GetExtension(entry.Name);
        if (!ext.Equals(".gf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (entry.Size < 80)
        {
            return false;
        }

        var fullName = entry.FullName.ToLowerInvariant();
        if (fullName.Contains("font") || fullName.Contains("cursor"))
        {
            return false;
        }

        return true;
    }

    private static uint StableHash(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0u;
        }

        unchecked
        {
            uint hash = 2166136261;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }

            return hash;
        }
    }
}

