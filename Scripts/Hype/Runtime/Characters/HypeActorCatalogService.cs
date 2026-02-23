using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HypeReborn.Hype.Runtime.Parsing;

namespace HypeReborn.Hype.Runtime.Characters;

public enum HypeActorCategory
{
    Hero,
    Npc,
    Enemy,
    LevelActor
}

public sealed class HypeActorRecord
{
    public required string Key { get; init; }
    public required string ModelSignature { get; init; }
    public required string DisplayName { get; init; }
    public required string SourceLevelName { get; init; }
    public required string SourceActorId { get; init; }
    public required HypeActorCategory Category { get; init; }
    public required bool IsMainActor { get; init; }
    public required bool IsHumanoid { get; init; }
    public required bool IsSectorCharacterListMember { get; init; }
    public required bool IsTargettable { get; init; }
    public required bool IsPlayerSelectable { get; init; }
    public required uint CustomBits { get; init; }
    public required int ChannelCount { get; init; }
    public required int FrameCount { get; init; }
    public required int ObjectCount { get; init; }
}

public sealed class HypeActorCatalog
{
    public required string GameRoot { get; init; }
    public required IReadOnlyList<HypeActorRecord> Actors { get; init; }
    public required IReadOnlyList<HypeActorRecord> HumanoidActors { get; init; }
    public required IReadOnlyList<HypeActorRecord> PlayableActors { get; init; }
}

public static class HypeActorCatalogService
{
    private static readonly Dictionary<string, HypeActorCatalog> CatalogCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static HypeActorCatalog BuildCatalog(string? overrideRoot = null)
    {
        var root = HypeAssetResolver.ResolveConfiguredGameRoot(overrideRoot);
        lock (CacheLock)
        {
            if (CatalogCache.TryGetValue(root, out var cached))
            {
                return cached;
            }
        }

        var index = HypeAssetResolver.BuildIndex(root);
        var records = new List<HypeActorRecord>();

        foreach (var level in index.Levels)
        {
            if (!HypeMontrealCharacterParser.TryParseActors(level, out var actors, out _))
            {
                continue;
            }

            var fallbackMainActorId = ResolveFallbackMainActorId(level, actors);

            foreach (var actor in actors)
            {
                var key = BuildActorKey(level.LevelName, actor.ActorId);
                var isHumanoid = IsHumanoidCandidate(actor);
                var isMainActor = IsMainActor(actor, fallbackMainActorId);
                var category = ClassifyActor(actor, isMainActor);
                var isPlayerSelectable = IsPlayerSelectable(category, isHumanoid);
                records.Add(new HypeActorRecord
                {
                    Key = key,
                    ModelSignature = BuildModelSignature(actor),
                    DisplayName = BuildDisplayName(category, isMainActor),
                    SourceLevelName = level.LevelName,
                    SourceActorId = actor.ActorId,
                    Category = category,
                    IsMainActor = isMainActor,
                    IsHumanoid = isHumanoid,
                    IsSectorCharacterListMember = actor.IsSectorCharacterListMember,
                    IsTargettable = actor.IsTargettable,
                    IsPlayerSelectable = isPlayerSelectable,
                    CustomBits = actor.CustomBits,
                    ChannelCount = actor.ChannelCount,
                    FrameCount = actor.Frames.Count,
                    ObjectCount = actor.Objects.Count
                });
            }
        }

        var sorted = records
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.IsMainActor)
                .ThenByDescending(x => x.IsSectorCharacterListMember)
                .ThenByDescending(x => x.ChannelCount)
                .ThenByDescending(x => x.FrameCount)
                .ThenByDescending(x => x.ObjectCount)
                .First())
            .OrderByDescending(x => x.IsMainActor)
            .ThenByDescending(x => x.IsPlayerSelectable)
            .ThenByDescending(x => x.IsHumanoid)
            .ThenBy(x => x.SourceLevelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceActorId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var humanoids = sorted.Where(x => x.IsHumanoid).ToArray();
        var playable = sorted.Where(x => x.IsPlayerSelectable).ToArray();

        var catalog = new HypeActorCatalog
        {
            GameRoot = root,
            Actors = sorted,
            HumanoidActors = humanoids,
            PlayableActors = playable
        };

        lock (CacheLock)
        {
            CatalogCache[root] = catalog;
        }

        return catalog;
    }

    public static bool TryBuildCatalog(out HypeActorCatalog catalog, out string failureReason, string? overrideRoot = null)
    {
        try
        {
            catalog = BuildCatalog(overrideRoot);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            catalog = null!;
            failureReason = ex.Message;
            return false;
        }
    }

    public static string BuildActorKey(string levelName, string actorId)
    {
        var level = (levelName ?? string.Empty).Trim();
        var actor = (actorId ?? string.Empty).Trim();
        return $"{level}::{actor}";
    }

    public static bool TryParseActorKey(string actorKey, out string levelName, out string actorId)
    {
        levelName = string.Empty;
        actorId = string.Empty;

        if (string.IsNullOrWhiteSpace(actorKey))
        {
            return false;
        }

        var split = actorKey.Split(new[] { "::" }, 2, StringSplitOptions.None);
        if (split.Length != 2)
        {
            return false;
        }

        levelName = split[0].Trim();
        actorId = split[1].Trim();
        return !string.IsNullOrWhiteSpace(levelName) && !string.IsNullOrWhiteSpace(actorId);
    }

    public static bool TryFindLevel(HypeAssetIndex index, string levelName, out HypeLevelRecord level)
    {
        level = index.Levels.FirstOrDefault(x =>
            x.LevelName.Equals(levelName, StringComparison.OrdinalIgnoreCase))!;
        return level != null;
    }

    public static bool IsHumanoidCandidate(HypeCharacterActorAsset actor)
    {
        if (actor == null)
        {
            return false;
        }

        if (actor.ChannelCount < 8)
        {
            return false;
        }

        if (actor.Objects.Count < 3)
        {
            return false;
        }

        return actor.Frames.Count >= 2;
    }

    public static string? ChooseDefaultHeroKey(HypeActorCatalog catalog)
    {
        var astrolabeMain = catalog.PlayableActors.FirstOrDefault(x =>
            x.IsMainActor && x.SourceLevelName.Equals("astrolabe", StringComparison.OrdinalIgnoreCase));
        if (astrolabeMain != null)
        {
            return astrolabeMain.Key;
        }

        var main = catalog.PlayableActors.FirstOrDefault(x => x.IsMainActor);
        if (main != null)
        {
            return main.Key;
        }

        var firstPlayable = catalog.PlayableActors.FirstOrDefault();
        return firstPlayable?.Key ?? catalog.HumanoidActors.FirstOrDefault()?.Key ?? catalog.Actors.FirstOrDefault()?.Key;
    }

    public static IReadOnlyList<HypeActorRecord> BuildPlayableModelRepresentatives(HypeActorCatalog catalog)
    {
        var deduped = catalog.PlayableActors
            .GroupBy(x => x.ModelSignature, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.IsMainActor)
                .ThenByDescending(x => x.SourceLevelName.Equals("astrolabe", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.ChannelCount)
                .ThenByDescending(x => x.FrameCount)
                .ThenByDescending(x => x.ObjectCount)
                .First())
            .OrderByDescending(x => x.IsMainActor)
            .ThenByDescending(x => x.SourceLevelName.Equals("astrolabe", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.SourceLevelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceActorId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var heroCounter = 0;
        var npcCounter = 0;
        var actorCounter = 0;
        var output = new List<HypeActorRecord>(deduped.Length);

        foreach (var item in deduped)
        {
            var label = item.Category switch
            {
                HypeActorCategory.Hero => heroCounter++ == 0 ? "Hero" : $"Hero {heroCounter:00}",
                HypeActorCategory.Npc => $"NPC {++npcCounter:00}",
                _ => $"Actor {++actorCounter:00}"
            };

            output.Add(new HypeActorRecord
            {
                Key = item.Key,
                ModelSignature = item.ModelSignature,
                DisplayName = label,
                SourceLevelName = item.SourceLevelName,
                SourceActorId = item.SourceActorId,
                Category = item.Category,
                IsMainActor = item.IsMainActor,
                IsHumanoid = item.IsHumanoid,
                IsSectorCharacterListMember = item.IsSectorCharacterListMember,
                IsTargettable = item.IsTargettable,
                IsPlayerSelectable = item.IsPlayerSelectable,
                CustomBits = item.CustomBits,
                ChannelCount = item.ChannelCount,
                FrameCount = item.FrameCount,
                ObjectCount = item.ObjectCount
            });
        }

        return output;
    }

    public static string ResolvePlayableRepresentativeKey(HypeActorCatalog catalog, string actorKey)
    {
        if (string.IsNullOrWhiteSpace(actorKey))
        {
            return string.Empty;
        }

        var source = catalog.PlayableActors.FirstOrDefault(x =>
            x.Key.Equals(actorKey, StringComparison.OrdinalIgnoreCase));
        if (source == null)
        {
            return actorKey;
        }

        var representative = BuildPlayableModelRepresentatives(catalog).FirstOrDefault(x =>
            x.ModelSignature.Equals(source.ModelSignature, StringComparison.OrdinalIgnoreCase));

        return representative?.Key ?? source.Key;
    }

    public static void InvalidateCache(string? overrideRoot = null)
    {
        HypeAssetIndexer.InvalidateCache(overrideRoot);

        if (string.IsNullOrWhiteSpace(overrideRoot))
        {
            lock (CacheLock)
            {
                CatalogCache.Clear();
            }

            return;
        }

        var root = HypeAssetResolver.ResolveConfiguredGameRoot(overrideRoot);
        lock (CacheLock)
        {
            CatalogCache.Remove(root);
        }
    }

    private static string ResolveFallbackMainActorId(
        HypeLevelRecord level,
        IReadOnlyList<HypeCharacterActorAsset> actors)
    {
        if (actors.Any(x => x.IsMainActor))
        {
            return string.Empty;
        }

        if (!HypeMontrealCharacterParser.TryParseMainActor(level, out var bestActor, out _))
        {
            return string.Empty;
        }

        return bestActor?.ActorId ?? string.Empty;
    }

    private static bool IsMainActor(HypeCharacterActorAsset actor, string fallbackMainActorId)
    {
        if (actor.IsMainActor)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(fallbackMainActorId) &&
               actor.ActorId.Equals(fallbackMainActorId, StringComparison.OrdinalIgnoreCase);
    }

    private static HypeActorCategory ClassifyActor(HypeCharacterActorAsset actor, bool isMainActor)
    {
        if (isMainActor)
        {
            return HypeActorCategory.Hero;
        }

        if (!actor.IsSectorCharacterListMember)
        {
            return HypeActorCategory.LevelActor;
        }

        if (actor.IsTargettable)
        {
            return HypeActorCategory.Enemy;
        }

        return HypeActorCategory.Npc;
    }

    private static bool IsPlayerSelectable(HypeActorCategory category, bool isHumanoid)
    {
        if (category == HypeActorCategory.Hero)
        {
            return true;
        }

        return isHumanoid && category == HypeActorCategory.Npc;
    }

    private static string BuildDisplayName(HypeActorCategory category, bool isMainActor)
    {
        if (isMainActor || category == HypeActorCategory.Hero)
        {
            return "Hero";
        }

        return category switch
        {
            HypeActorCategory.Npc => "NPC",
            HypeActorCategory.Enemy => "Enemy",
            HypeActorCategory.LevelActor => "LevelActor",
            _ => "Actor"
        };
    }

    private static string BuildModelSignature(HypeCharacterActorAsset actor)
    {
        var builder = new StringBuilder(4096);
        builder.Append("ch=").Append(actor.ChannelCount)
            .Append(";obj=").Append(actor.Objects.Count);

        foreach (var obj in actor.Objects.OrderBy(x => x.ObjectIndex))
        {
            builder.Append("|o=").Append(obj.ObjectIndex)
                .Append(",sx=").Append(Quantize(obj.ScaleMultiplier.X))
                .Append(",sy=").Append(Quantize(obj.ScaleMultiplier.Y))
                .Append(",sz=").Append(Quantize(obj.ScaleMultiplier.Z));

            if (obj.Mesh == null)
            {
                builder.Append(",mesh=none");
                continue;
            }

            builder.Append(",surf=").Append(obj.Mesh.Surfaces.Count);
            foreach (var surface in obj.Mesh.Surfaces)
            {
                builder.Append(";v=").Append(surface.Vertices.Length)
                    .Append(",i=").Append(surface.Indices.Length)
                    .Append(",uv=").Append(surface.Uvs.Length)
                    .Append(",ds=").Append(surface.DoubleSided ? 1 : 0)
                    .Append(",tf=").Append(surface.TextureFlags)
                    .Append(",am=").Append(surface.TextureAlphaMask)
                    .Append(",tex=").Append(surface.TextureTgaName?.ToLowerInvariant() ?? string.Empty);

                AppendBoundsSignature(builder, surface.Vertices);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).Substring(0, 16);
    }

    private static void AppendBoundsSignature(StringBuilder builder, Godot.Vector3[] vertices)
    {
        if (vertices.Length == 0)
        {
            builder.Append(",b=none");
            return;
        }

        var minX = vertices[0].X;
        var minY = vertices[0].Y;
        var minZ = vertices[0].Z;
        var maxX = vertices[0].X;
        var maxY = vertices[0].Y;
        var maxZ = vertices[0].Z;

        for (var i = 1; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.Z < minZ) minZ = v.Z;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
            if (v.Z > maxZ) maxZ = v.Z;
        }

        builder.Append(",b=")
            .Append(Quantize(minX)).Append(':').Append(Quantize(minY)).Append(':').Append(Quantize(minZ))
            .Append('-')
            .Append(Quantize(maxX)).Append(':').Append(Quantize(maxY)).Append(':').Append(Quantize(maxZ));
    }

    private static int Quantize(float value)
    {
        return (int)MathF.Round(value * 1000f);
    }
}
