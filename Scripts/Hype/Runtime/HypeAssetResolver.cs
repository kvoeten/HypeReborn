using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HypeReborn.Hype.Config;
using HypeReborn.Hype.Maps;
using HypeReborn.Hype.Runtime.Characters;
using HypeReborn.Hype.Runtime.Parsing;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.Runtime;

public sealed class HypeResolvedLevel
{
    public required string GameRoot { get; init; }
    public required string Language { get; init; }
    public required HypeLevelRecord Level { get; init; }
    public required IReadOnlyList<HypeAnimationRecord> Animations { get; init; }
    public required IReadOnlyList<HypeScriptRecord> Scripts { get; init; }
    public required IReadOnlyList<HypeResolvedEntity> Entities { get; init; }
}

public static class HypeAssetResolver
{
    public static string ResolveConfiguredGameRoot(string? overrideRoot = null)
    {
        var candidate = string.IsNullOrWhiteSpace(overrideRoot)
            ? HypeProjectSettings.GetExternalGameRoot()
            : overrideRoot.Trim();

        if (!HypeInstallProbe.TryResolveGameRoot(candidate, out var resolvedRoot))
        {
            throw new InvalidOperationException("No valid Hype game install configured. Set 'hype/external_game_root' first.");
        }

        return resolvedRoot;
    }

    public static HypeResolvedLevel ResolveLevel(HypeMapDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.LevelName))
        {
            throw new InvalidOperationException("Map definition is missing LevelName.");
        }

        var gameRoot = ResolveConfiguredGameRoot(definition.ExternalGameRootOverride);
        var language = HypeProjectSettings.GetDefaultLanguage();
        var index = HypeAssetIndexer.Build(gameRoot, language);

        var level = index.Levels.FirstOrDefault(x =>
            x.LevelName.Equals(definition.LevelName, StringComparison.OrdinalIgnoreCase));

        if (level == null)
        {
            throw new InvalidOperationException($"Level '{definition.LevelName}' was not found under '{gameRoot}'.");
        }

        var parsedLevel = index.ParsedLevels.FirstOrDefault(x =>
            x.LevelName.Equals(level.LevelName, StringComparison.OrdinalIgnoreCase));
        if (parsedLevel != null && !parsedLevel.Succeeded)
        {
            var firstError = parsedLevel.Diagnostics
                .FirstOrDefault(x => x.Severity == HypeParseSeverity.Error);
            var reason = firstError != null ? $"{firstError.Phase}: {firstError.Message}" : "Unknown parser error.";
            throw new InvalidOperationException(
                $"Parser failed for '{level.LevelName}'. Resolve parser diagnostics before map load. {reason}");
        }

        var animations = index.Animations
            .Where(x => x.SourceLevel.Equals(level.LevelName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var scripts = index.Scripts
            .Where(x => x.SourceLevel.Equals(level.LevelName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var entities = HypeParserFacade.ExtractResolvedEntities(gameRoot, level, animations);

        return new HypeResolvedLevel
        {
            GameRoot = gameRoot,
            Language = language,
            Level = level,
            Animations = animations,
            Scripts = scripts,
            Entities = entities
        };
    }

    public static Task<HypeResolvedLevel> ResolveLevelAsync(
        HypeMapDefinition definition,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ResolveLevel(definition), cancellationToken);
    }

    public static HypeAssetIndex BuildIndex(
        string? overrideRoot = null,
        string? overrideLanguage = null,
        bool includeResolvedMapAssets = false,
        bool forceRefresh = false)
    {
        var root = ResolveConfiguredGameRoot(overrideRoot);
        var language = string.IsNullOrWhiteSpace(overrideLanguage)
            ? HypeProjectSettings.GetDefaultLanguage()
            : overrideLanguage.Trim();

        return HypeAssetIndexer.Build(
            root,
            language,
            includeResolvedMapAssets,
            forceRefresh);
    }

    public static async Task<HypeAssetIndex> BuildIndexAsync(
        string? overrideRoot = null,
        string? overrideLanguage = null,
        bool includeResolvedMapAssets = false,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveConfiguredGameRoot(overrideRoot);
        var language = string.IsNullOrWhiteSpace(overrideLanguage)
            ? HypeProjectSettings.GetDefaultLanguage()
            : overrideLanguage.Trim();

        return await HypeAssetIndexer.BuildAsync(
            root,
            language,
            includeResolvedMapAssets,
            forceRefresh,
            cancellationToken);
    }

    public static HypeActorCatalog BuildActorCatalog(string? overrideRoot = null)
    {
        return HypeActorCatalogService.BuildCatalog(overrideRoot);
    }

    public static void InvalidateCaches(string? overrideRoot = null, string? overrideLanguage = null)
    {
        HypeAssetIndexer.InvalidateCache(overrideRoot, overrideLanguage);
        HypeActorCatalogService.InvalidateCache(overrideRoot);
        HypeMontrealCharacterParser.InvalidateCache();
        HypeTextureLookupService.InvalidateCache(overrideRoot);
    }
}
