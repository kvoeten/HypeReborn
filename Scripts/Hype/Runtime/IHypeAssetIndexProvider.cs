namespace HypeReborn.Hype.Runtime;

public interface IHypeAssetIndexProvider
{
    HypeAssetIndex Build(
        string gameRoot,
        string language,
        bool includeResolvedMapAssets = false,
        bool forceRefresh = false);

    void InvalidateCache(string? gameRoot = null, string? language = null);
}
