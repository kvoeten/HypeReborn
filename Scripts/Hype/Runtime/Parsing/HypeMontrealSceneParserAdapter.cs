using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime.Parsing;

public sealed class HypeMontrealSceneParserAdapter : IHypeSceneParser
{
    public IReadOnlyList<HypeResolvedEntity> ParseLevel(
        string gameRoot,
        HypeLevelRecord level,
        IReadOnlyList<HypeAnimationRecord> animations)
    {
        return HypeMontrealSceneParser.ParseLevel(gameRoot, level, animations);
    }
}
