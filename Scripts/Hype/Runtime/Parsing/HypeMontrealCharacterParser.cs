using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace HypeReborn.Hype.Runtime.Parsing;

public sealed class HypeCharacterObjectVisual
{
    public required int ObjectIndex { get; init; }
    public required Vector3 ScaleMultiplier { get; init; }
    public HypeResolvedMesh? Mesh { get; init; }
}

public readonly struct HypeCharacterChannelSample
{
    public HypeCharacterChannelSample(int objectIndex, Transform3D localTransform)
    {
        ObjectIndex = objectIndex;
        LocalTransform = localTransform;
    }

    public int ObjectIndex { get; }
    public Transform3D LocalTransform { get; }
}

public sealed class HypeCharacterFrameAsset
{
    public required HypeCharacterChannelSample[] ChannelSamples { get; init; }
    public required int[] ParentChannelIndices { get; init; }
}

public sealed class HypeCharacterActorAsset
{
    public required string LevelName { get; init; }
    public required string ActorId { get; init; }
    public required bool IsMainActor { get; init; }
    public required bool IsSectorCharacterListMember { get; init; }
    public required bool IsTargettable { get; init; }
    public required uint CustomBits { get; init; }
    public required float FramesPerSecond { get; init; }
    public required int ChannelCount { get; init; }
    public required IReadOnlyList<HypeCharacterObjectVisual> Objects { get; init; }
    public required IReadOnlyList<HypeCharacterFrameAsset> Frames { get; init; }
}

public static partial class HypeMontrealCharacterParser
{
    private const uint MainActorBit = 0x80000000u;
    private const uint TargettableCustomBit = 1u << 0;
    private const uint VisualMaterialFlagBackfaceCulling = 1u << 10;
    private const int MaxChildChain = 20000;
    private const int MaxLinkedListTraversal = 20000;
    private static readonly Dictionary<string, CachedLevelActors> ParsedActorsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ParsedActorsCacheLock = new();

    private sealed class CachedLevelActors
    {
        public required bool Succeeded { get; init; }
        public required HypeCharacterActorAsset[] Actors { get; init; }
        public required Dictionary<string, HypeCharacterActorAsset> ActorsById { get; init; }
        public required HypeCharacterActorAsset? MainActor { get; init; }
        public required string[] Diagnostics { get; init; }
    }

    public static bool TryParseMainActor(
        HypeLevelRecord level,
        out HypeCharacterActorAsset? actor,
        out IReadOnlyList<string> diagnostics)
    {
        var parsed = GetOrParseLevelActors(level);
        diagnostics = parsed.Diagnostics;
        actor = parsed.MainActor;
        if (!parsed.Succeeded)
        {
            return false;
        }

        return actor != null;
    }

    public static bool TryParseActors(
        HypeLevelRecord level,
        out IReadOnlyList<HypeCharacterActorAsset> actors,
        out IReadOnlyList<string> diagnostics)
    {
        var parsed = GetOrParseLevelActors(level);
        actors = parsed.Actors;
        diagnostics = parsed.Diagnostics;
        return parsed.Succeeded;
    }

    public static bool TryParseActor(
        HypeLevelRecord level,
        string actorId,
        out HypeCharacterActorAsset? actor,
        out IReadOnlyList<string> diagnostics)
    {
        actor = null;
        var parsed = GetOrParseLevelActors(level);
        diagnostics = parsed.Diagnostics;
        if (!parsed.Succeeded)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(actorId))
        {
            return false;
        }

        parsed.ActorsById.TryGetValue(actorId.Trim(), out actor);
        if (actor != null)
        {
            return true;
        }

        var mutableDiagnostics = diagnostics is List<string> list
            ? list
            : new List<string>(diagnostics);
        mutableDiagnostics.Add($"Requested actor '{actorId}' was not found in level '{level.LevelName}'.");
        diagnostics = mutableDiagnostics;
        return false;
    }

    public static void InvalidateCache()
    {
        lock (ParsedActorsCacheLock)
        {
            ParsedActorsCache.Clear();
        }
    }

    private static CachedLevelActors ParseActorsUncached(HypeLevelRecord level)
    {
        var actors = Array.Empty<HypeCharacterActorAsset>();
        var diagnostics = new List<string>();

        if (!HypeParseContextBuilder.TryBuild(level, out var context, diagnostics))
        {
            return new CachedLevelActors
            {
                Succeeded = false,
                Actors = actors,
                ActorsById = new Dictionary<string, HypeCharacterActorAsset>(StringComparer.OrdinalIgnoreCase),
                MainActor = null,
                Diagnostics = diagnostics.ToArray()
            };
        }

        try
        {
            var roots = HypeWorldRootReader.ReadWorldRoots(context.Space, context.LevelGptAddress, diagnostics);
            var decoder = new HypeCharacterDecoder(context.Space, diagnostics);
            actors = decoder.ParseActors(roots, level.LevelName)
                .Select(actor => new HypeCharacterActorAsset
                {
                    LevelName = level.LevelName,
                    ActorId = actor.ActorId,
                    IsMainActor = actor.IsMainActor,
                    IsSectorCharacterListMember = actor.IsSectorCharacterListMember,
                    IsTargettable = actor.IsTargettable,
                    CustomBits = actor.CustomBits,
                    FramesPerSecond = actor.FramesPerSecond,
                    ChannelCount = actor.ChannelCount,
                    Objects = actor.Objects,
                    Frames = actor.Frames
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Character parse failed: {ex.Message}");
        }

        var actorsById = new Dictionary<string, HypeCharacterActorAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in actors)
        {
            if (!actorsById.ContainsKey(item.ActorId))
            {
                actorsById[item.ActorId] = item;
            }
        }

        return new CachedLevelActors
        {
            Succeeded = actors.Length > 0,
            Actors = actors,
            ActorsById = actorsById,
            MainActor = actors.FirstOrDefault(),
            Diagnostics = diagnostics.ToArray()
        };
    }

    private static string BuildCacheKey(HypeLevelRecord level)
    {
        return $"{level.LevelDirectoryPath}::{level.LevelName}";
    }

    private static CachedLevelActors GetOrParseLevelActors(HypeLevelRecord level)
    {
        var cacheKey = BuildCacheKey(level);
        lock (ParsedActorsCacheLock)
        {
            if (ParsedActorsCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var parsed = ParseActorsUncached(level);
        lock (ParsedActorsCacheLock)
        {
            ParsedActorsCache[cacheKey] = parsed;
        }

        return parsed;
    }
}
