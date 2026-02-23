using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Characters;

namespace HypeReborn.Hype.Player;

public readonly struct HypeActorResolverSettings
{
    public required string SourceLevelName { get; init; }
    public required string SourceActorId { get; init; }
    public required string SourceActorKey { get; init; }
    public required bool AutoDetectLevelFromScene { get; init; }
    public required bool UseSaveActorSelection { get; init; }
}

public readonly struct HypeActorResolveRequest
{
    public required string GameRoot { get; init; }
    public required HypeLevelRecord Level { get; init; }
    public required string ActorId { get; init; }
}

public static class HypeActorResolver
{
    public static bool TryResolveActorRequest(
        HypeActorResolverSettings settings,
        string? scenePath,
        out HypeActorResolveRequest request,
        out string failureReason)
    {
        request = default;
        failureReason = string.Empty;

        string gameRoot;
        try
        {
            gameRoot = HypeAssetResolver.ResolveConfiguredGameRoot();
        }
        catch (Exception ex)
        {
            failureReason = $"Failed to resolve game root: {ex.Message}";
            return false;
        }

        HypeAssetIndex index;
        try
        {
            index = HypeAssetResolver.BuildIndex(gameRoot);
        }
        catch (Exception ex)
        {
            failureReason = $"Failed to build asset index: {ex.Message}";
            return false;
        }

        if (TryResolveActorFromKey(index, ResolvePreferredActorKey(settings), out var keyedLevel, out var keyedActorId))
        {
            request = new HypeActorResolveRequest
            {
                GameRoot = gameRoot,
                Level = keyedLevel,
                ActorId = keyedActorId
            };
            return true;
        }

        var explicitLevel = ResolveLevel(index.Levels, settings.SourceLevelName);
        if (explicitLevel != null)
        {
            request = new HypeActorResolveRequest
            {
                GameRoot = gameRoot,
                Level = explicitLevel,
                ActorId = settings.SourceActorId.Trim()
            };
            return true;
        }

        var levelName = ResolveLevelName(index.Levels, settings, scenePath);
        if (string.IsNullOrWhiteSpace(levelName))
        {
            failureReason = "No source level could be resolved for the current scene.";
            return false;
        }

        var level = ResolveLevel(index.Levels, levelName);
        if (level == null)
        {
            failureReason = $"Level '{levelName}' was not found in external game data.";
            return false;
        }

        request = new HypeActorResolveRequest
        {
            GameRoot = gameRoot,
            Level = level,
            ActorId = settings.SourceActorId.Trim()
        };
        return true;
    }

    private static string ResolvePreferredActorKey(HypeActorResolverSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SourceActorKey))
        {
            return settings.SourceActorKey.Trim();
        }

        if (settings.UseSaveActorSelection)
        {
            if (HypeActorCatalogService.TryBuildCatalog(out var catalog, out _))
            {
                var key = HypePlayerActorSaveState.GetSelectedActorKey();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    var selectedActor = catalog.PlayableActors.FirstOrDefault(x =>
                        x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (selectedActor != null && selectedActor.Category == HypeActorCategory.Hero)
                    {
                        return key;
                    }

                    HypePlayerActorSaveState.ClearSelectedActorKey();
                }

                HypePlayerActorSaveState.EnsureDefaultSelection(catalog);
                return HypePlayerActorSaveState.GetSelectedActorKey();
            }
        }

        return string.Empty;
    }

    private static bool TryResolveActorFromKey(
        HypeAssetIndex index,
        string actorKey,
        out HypeLevelRecord level,
        out string actorId)
    {
        level = null!;
        actorId = string.Empty;

        if (!HypeActorCatalogService.TryParseActorKey(actorKey, out var levelName, out var parsedActorId))
        {
            return false;
        }

        level = ResolveLevel(index.Levels, levelName)!;
        if (level == null)
        {
            return false;
        }

        actorId = parsedActorId;
        return true;
    }

    private static HypeLevelRecord? ResolveLevel(IReadOnlyList<HypeLevelRecord> levels, string levelName)
    {
        if (string.IsNullOrWhiteSpace(levelName))
        {
            return null;
        }

        return levels.FirstOrDefault(x =>
            x.LevelName.Equals(levelName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveLevelName(
        IReadOnlyList<HypeLevelRecord> levels,
        HypeActorResolverSettings settings,
        string? scenePath)
    {
        if (!string.IsNullOrWhiteSpace(settings.SourceLevelName))
        {
            return settings.SourceLevelName.Trim();
        }

        if (!settings.AutoDetectLevelFromScene || string.IsNullOrWhiteSpace(scenePath))
        {
            return string.Empty;
        }

        var sceneName = Path.GetFileNameWithoutExtension(scenePath);
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return string.Empty;
        }

        var match = levels.FirstOrDefault(x =>
            x.LevelName.Equals(sceneName, StringComparison.OrdinalIgnoreCase));
        return match?.LevelName ?? sceneName;
    }
}
