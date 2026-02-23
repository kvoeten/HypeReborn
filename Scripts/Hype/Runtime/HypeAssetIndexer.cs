using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HypeReborn.Hype.Runtime.Parsing;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.Runtime;

public static class HypeAssetIndexer
{
    private static readonly string[] CoreLevelExtensions = { ".sna", ".gpt", ".ptx", ".rtb", ".rtp", ".rtt", ".sda" };
    private static readonly string[] ScriptExtensions = { ".gpt", ".dlg", ".lng", ".rtd", ".rtg", ".rts", ".snd" };
    private static readonly Dictionary<string, HypeAssetIndex> IndexCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static HypeAssetIndex Build(
        string gameRoot,
        string language,
        bool includeResolvedMapAssets = false,
        bool forceRefresh = false)
    {
        if (!HypeInstallProbe.TryResolveGameRoot(gameRoot, out var resolvedRoot))
        {
            throw new DirectoryNotFoundException($"Could not resolve a valid Hype game root from '{gameRoot}'.");
        }

        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "Dutch" : language.Trim();
        var cacheKey = BuildCacheKey(resolvedRoot, normalizedLanguage, includeResolvedMapAssets);
        if (!forceRefresh)
        {
            lock (CacheLock)
            {
                if (IndexCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }
        }

        var levelsRoot = Path.Combine(resolvedRoot, "Gamedata", "World", "Levels");
        var languageLevelsRoot = Path.Combine(resolvedRoot, "LangData", normalizedLanguage, "world", "levels");

        var levelRecords = new List<HypeLevelRecord>();
        var animationRecords = new List<HypeAnimationRecord>();
        var scriptRecords = new List<HypeScriptRecord>();

        foreach (var levelDir in Directory.EnumerateDirectories(levelsRoot).OrderBy(Path.GetFileName))
        {
            var levelName = Path.GetFileName(levelDir);
            if (string.IsNullOrWhiteSpace(levelName))
            {
                continue;
            }

            var coreFiles = IndexByFileName(levelDir, CoreLevelExtensions);
            var levelLanguageDir = Path.Combine(languageLevelsRoot, levelName);
            var langFiles = Directory.Exists(levelLanguageDir)
                ? IndexByFileName(levelLanguageDir, ScriptExtensions)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            levelRecords.Add(new HypeLevelRecord
            {
                LevelName = levelName,
                LevelDirectoryPath = levelDir,
                CoreFiles = coreFiles,
                LanguageFiles = langFiles
            });

            // Current implementation exposes animation sources at bank granularity.
            // Later, this can be expanded to decoded per-state clips.
            if (coreFiles.TryGetValue($"{levelName}.sna", out var snaPath))
            {
                animationRecords.Add(new HypeAnimationRecord
                {
                    Id = $"animbank:{levelName}",
                    SourceLevel = levelName,
                    SourceFile = snaPath,
                    VirtualPath = $"Animations/{levelName}/StateBank"
                });
            }

            foreach (var kvp in coreFiles)
            {
                if (HasExtension(kvp.Key, ScriptExtensions))
                {
                    scriptRecords.Add(new HypeScriptRecord
                    {
                        Id = $"script:{levelName}:{Path.GetFileNameWithoutExtension(kvp.Key)}",
                        SourceLevel = levelName,
                        SourceFile = kvp.Value,
                        ScriptKind = Path.GetExtension(kvp.Key).Trim('.').ToLowerInvariant(),
                        VirtualPath = $"Scripts/{levelName}/{kvp.Key}"
                    });
                }
            }

            foreach (var kvp in langFiles)
            {
                if (HasExtension(kvp.Key, ScriptExtensions))
                {
                    scriptRecords.Add(new HypeScriptRecord
                    {
                        Id = $"script:{levelName}:lang:{Path.GetFileNameWithoutExtension(kvp.Key)}",
                        SourceLevel = levelName,
                        SourceFile = kvp.Value,
                        ScriptKind = $"lang-{Path.GetExtension(kvp.Key).Trim('.').ToLowerInvariant()}",
                        VirtualPath = $"Scripts/{levelName}/Lang/{kvp.Key}"
                    });
                }
            }
        }

        var textureContainers = new List<HypeTextureContainerRecord>();
        var textureEntries = new List<HypeTextureEntryRecord>();
        var textureCandidates = new[]
        {
            Path.Combine(resolvedRoot, "Gamedata", "Textures.cnt"),
            Path.Combine(resolvedRoot, "Gamedata", "Vignette.cnt"),
            Path.Combine(resolvedRoot, "Gamedata", "World", "Levels", "fix.cnt")
        };
        foreach (var candidate in textureCandidates)
        {
            if (File.Exists(candidate))
            {
                var containerName = Path.GetFileName(candidate);
                var containerId = $"texture:{Path.GetFileNameWithoutExtension(candidate).ToLowerInvariant()}";

                textureContainers.Add(new HypeTextureContainerRecord
                {
                    Id = containerId,
                    Name = containerName,
                    SourceFile = candidate,
                    VirtualPath = $"TextureContainers/{containerName}"
                });

                try
                {
                    using var cnt = new HypeCntFile(candidate);
                    foreach (var entry in cnt.Entries.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
                    {
                        var normalizedFullName = entry.FullName.Replace('\\', '/');
                        textureEntries.Add(new HypeTextureEntryRecord
                        {
                            Id = $"{containerId}:{normalizedFullName.ToLowerInvariant()}",
                            ContainerId = containerId,
                            ContainerSourceFile = candidate,
                            EntryName = entry.Name,
                            EntryFullName = entry.FullName,
                            SizeBytes = entry.Size,
                            VirtualPath = $"TextureContainers/{containerName}/{normalizedFullName}"
                        });
                    }
                }
                catch
                {
                    // Keep indexing resilient even if one container cannot be parsed.
                }
            }
        }

        var parsedLevels = HypeLevelParser.ParseLevels(resolvedRoot, levelRecords);
        var parsedMapAssets = includeResolvedMapAssets
            ? BuildParsedMapAssets(resolvedRoot, levelRecords, animationRecords, parsedLevels)
            : Array.Empty<HypeParsedMapAssetRecord>();

        var index = new HypeAssetIndex
        {
            GameRoot = resolvedRoot,
            Language = normalizedLanguage,
            Levels = levelRecords,
            Animations = animationRecords,
            Scripts = scriptRecords,
            TextureContainers = textureContainers,
            TextureEntries = textureEntries,
            ParsedLevels = parsedLevels,
            ParsedMapAssets = parsedMapAssets
        };

        lock (CacheLock)
        {
            IndexCache[cacheKey] = index;
        }

        return index;
    }

    public static Task<HypeAssetIndex> BuildAsync(
        string gameRoot,
        string language,
        bool includeResolvedMapAssets = false,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Build(gameRoot, language, includeResolvedMapAssets, forceRefresh),
            cancellationToken);
    }

    public static void InvalidateCache(string? gameRoot = null, string? language = null)
    {
        lock (CacheLock)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) && string.IsNullOrWhiteSpace(language))
            {
                IndexCache.Clear();
                return;
            }

            if (!string.IsNullOrWhiteSpace(gameRoot) &&
                HypeInstallProbe.TryResolveGameRoot(gameRoot, out var resolvedRoot))
            {
                var rootPrefix = $"{resolvedRoot}::";
                foreach (var key in IndexCache.Keys.Where(k => k.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    if (string.IsNullOrWhiteSpace(language))
                    {
                        IndexCache.Remove(key);
                        continue;
                    }

                    if (key.Contains($"::{language.Trim()}::", StringComparison.OrdinalIgnoreCase))
                    {
                        IndexCache.Remove(key);
                    }
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(language))
            {
                foreach (var key in IndexCache.Keys.Where(k => k.Contains($"::{language.Trim()}::", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    IndexCache.Remove(key);
                }
            }
        }
    }

    private static IReadOnlyList<HypeParsedMapAssetRecord> BuildParsedMapAssets(
        string gameRoot,
        IReadOnlyList<HypeLevelRecord> levels,
        IReadOnlyList<HypeAnimationRecord> animations,
        IReadOnlyList<HypeParsedLevelRecord> parsedLevels)
    {
        var parsedByLevel = parsedLevels.ToDictionary(x => x.LevelName, StringComparer.OrdinalIgnoreCase);
        var output = new List<HypeParsedMapAssetRecord>(levels.Count);

        foreach (var level in levels)
        {
            if (parsedByLevel.TryGetValue(level.LevelName, out var parsedLevel) && !parsedLevel.Succeeded)
            {
                var firstError = parsedLevel.Diagnostics.FirstOrDefault(x => x.Severity == HypeParseSeverity.Error);
                output.Add(new HypeParsedMapAssetRecord
                {
                    LevelName = level.LevelName,
                    Succeeded = false,
                    GeometryEntityCount = 0,
                    LightEntityCount = 0,
                    ParticleSourceCount = 0,
                    AnimationAnchorCount = 0,
                    MeshSurfaceCount = 0,
                    TextureUsages = Array.Empty<HypeParsedTextureUsageRecord>(),
                    ErrorMessage = firstError != null ? $"{firstError.Phase}: {firstError.Message}" : "Parser diagnostics reported errors."
                });
                continue;
            }

            try
            {
                var levelAnimations = animations
                    .Where(x => x.SourceLevel.Equals(level.LevelName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var entities = HypeParserFacade.ExtractResolvedEntities(gameRoot, level, levelAnimations);

                var textureUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var meshSurfaceCount = 0;
                foreach (var entity in entities)
                {
                    if (entity.Kind != HypeResolvedEntityKind.Geometry || entity.Mesh == null)
                    {
                        continue;
                    }

                    foreach (var surface in entity.Mesh.Surfaces)
                    {
                        meshSurfaceCount++;
                        if (string.IsNullOrWhiteSpace(surface.TextureTgaName))
                        {
                            continue;
                        }

                        var name = surface.TextureTgaName.Trim().Replace('/', '\\');
                        if (!textureUsage.ContainsKey(name))
                        {
                            textureUsage[name] = 0;
                        }

                        textureUsage[name]++;
                    }
                }

                var usageRecords = textureUsage
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new HypeParsedTextureUsageRecord
                    {
                        TextureTgaName = x.Key,
                        SurfaceCount = x.Value
                    })
                    .ToArray();

                output.Add(new HypeParsedMapAssetRecord
                {
                    LevelName = level.LevelName,
                    Succeeded = true,
                    GeometryEntityCount = entities.Count(x => x.Kind == HypeResolvedEntityKind.Geometry),
                    LightEntityCount = entities.Count(x => x.Kind == HypeResolvedEntityKind.Light),
                    ParticleSourceCount = entities.Count(x => x.Kind == HypeResolvedEntityKind.ParticleSource),
                    AnimationAnchorCount = entities.Count(x => x.Kind == HypeResolvedEntityKind.AnimationAnchor),
                    MeshSurfaceCount = meshSurfaceCount,
                    TextureUsages = usageRecords,
                    ErrorMessage = null
                });
            }
            catch (Exception ex)
            {
                output.Add(new HypeParsedMapAssetRecord
                {
                    LevelName = level.LevelName,
                    Succeeded = false,
                    GeometryEntityCount = 0,
                    LightEntityCount = 0,
                    ParticleSourceCount = 0,
                    AnimationAnchorCount = 0,
                    MeshSurfaceCount = 0,
                    TextureUsages = Array.Empty<HypeParsedTextureUsageRecord>(),
                    ErrorMessage = ex.Message
                });
            }
        }

        return output;
    }

    private static Dictionary<string, string> IndexByFileName(string directory, IReadOnlyList<string> extensions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(directory))
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName) || !HasExtension(fileName, extensions))
            {
                continue;
            }

            result[fileName] = filePath;
        }

        return result;
    }

    private static bool HasExtension(string fileName, IReadOnlyList<string> extensions)
    {
        var ext = Path.GetExtension(fileName);
        return extensions.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCacheKey(string resolvedRoot, string normalizedLanguage, bool includeResolvedMapAssets)
    {
        return $"{resolvedRoot}::{normalizedLanguage}::{(includeResolvedMapAssets ? "maps1" : "maps0")}";
    }
}
