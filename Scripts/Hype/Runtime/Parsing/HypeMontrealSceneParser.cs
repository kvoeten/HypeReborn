using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace HypeReborn.Hype.Runtime.Parsing;

public static partial class HypeMontrealSceneParser
{
    private const uint MainActorBit = 0x80000000u;
    private const uint TargettableCustomBit = 1u << 0;
    private const uint VisualMaterialFlagBackfaceCulling = 1u << 10;
    private const int MaxChildChain = 20000;
    private const int MaxLinkedListTraversal = 20000;

    public static IReadOnlyList<HypeResolvedEntity> ParseLevel(
        string gameRoot,
        HypeLevelRecord level,
        IReadOnlyList<HypeAnimationRecord> animations)
    {
        var diagnostics = new List<string>();
        var entities = new List<HypeResolvedEntity>();

        if (!HypeParseContextBuilder.TryBuild(level, out var context, diagnostics))
        {
            EmitDiagnostics(level.LevelName, diagnostics);
            AddAnimationAnchors(entities, animations);
            return entities;
        }

        try
        {
            var roots = HypeWorldRootReader.ReadWorldRoots(context.Space, context.LevelGptAddress, diagnostics);
            var decoder = new HypeSceneDecoder(context.Space, diagnostics);
            entities.AddRange(decoder.Decode(roots));
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Scene parse failed: {ex.Message}");
        }

        AddAnimationAnchors(entities, animations);
        EmitDiagnostics(level.LevelName, diagnostics);
        return entities;
    }

    private static void AddAnimationAnchors(
        List<HypeResolvedEntity> entities,
        IReadOnlyList<HypeAnimationRecord> animations)
    {
        var animationOffset = 0;
        foreach (var animation in animations)
        {
            entities.Add(new HypeResolvedEntity
            {
                Id = animation.Id,
                Name = Path.GetFileNameWithoutExtension(animation.SourceFile),
                Kind = HypeResolvedEntityKind.AnimationAnchor,
                Transform = new Transform3D(
                    Basis.Identity,
                    new Vector3((animationOffset % 12) * 1.5f, 2f, (animationOffset / 12) * 1.5f)),
                SourceFile = animation.SourceFile
            });
            animationOffset++;
        }
    }

    private static void EmitDiagnostics(string levelName, IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        foreach (var line in diagnostics)
        {
            GD.PrintErr($"[HypeSceneParser:{levelName}] {line}");
        }
    }
}
