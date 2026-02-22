using System;
using System.Collections.Generic;
using Godot;
using HypeReborn.Hype.Runtime.Parsing;

namespace HypeReborn.Hype.Runtime;

public static class HypeParserFacade
{
    public static void ValidateFutureParserDependencies()
    {
        GD.Print("[HypeParser] Parser status: relocation + SNA parsing + Montreal map geometry/material extraction available; pending modules: animation decode, script AST decode, full light/particle semantic decode.");
    }

    public static bool IsAnimationPreviewSupported => false;

    public static bool IsScriptDecodeSupported => false;

    public static IReadOnlyList<HypeResolvedEntity> ExtractResolvedEntities(
        string gameRoot,
        HypeLevelRecord level,
        IReadOnlyList<HypeAnimationRecord> animations)
    {
        return HypeMontrealSceneParser.ParseLevel(gameRoot, level, animations);
    }

    public static void ThrowNotImplemented(string module)
    {
        throw new NotImplementedException($"{module} parsing is planned but not implemented yet.");
    }
}
