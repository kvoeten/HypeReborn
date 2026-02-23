using Godot;

namespace HypeReborn.Hype.Runtime.Textures;

public sealed class HypeTextureLookupAdapter : IHypeTextureLookup
{
    public Texture2D? TryGetTextureByTgaName(
        string gameRoot,
        string tgaName,
        uint textureFlags = 0,
        uint textureAlphaMask = 0,
        bool forceColorKey = false)
    {
        return HypeTextureLookupService.TryGetTextureByTgaName(
            gameRoot,
            tgaName,
            textureFlags,
            textureAlphaMask,
            forceColorKey);
    }

    public HypeTextureLookupService.HypeTextureLookupResult TryGetTextureByTgaNameDetailed(
        string gameRoot,
        string tgaName,
        uint textureFlags = 0,
        uint textureAlphaMask = 0,
        bool forceColorKey = false)
    {
        return HypeTextureLookupService.TryGetTextureByTgaNameDetailed(
            gameRoot,
            tgaName,
            textureFlags,
            textureAlphaMask,
            forceColorKey);
    }
}
