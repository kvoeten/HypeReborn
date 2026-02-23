namespace HypeReborn.Hype.Runtime;

public sealed class HypeAssetIndexProvider : IHypeAssetIndexProvider
{
    public HypeAssetIndex Build(
        string gameRoot,
        string language,
        bool includeResolvedMapAssets = false,
        bool forceRefresh = false)
    {
        return HypeAssetIndexer.Build(gameRoot, language, includeResolvedMapAssets, forceRefresh);
    }

    public void InvalidateCache(string? gameRoot = null, string? language = null)
    {
        HypeAssetIndexer.InvalidateCache(gameRoot, language);
    }
}
