using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime.Parsing;

public interface IHypeSceneParser
{
    IReadOnlyList<HypeResolvedEntity> ParseLevel(
        string gameRoot,
        HypeLevelRecord level,
        IReadOnlyList<HypeAnimationRecord> animations);
}
