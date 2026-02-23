using Godot;

namespace HypeReborn.Hype.Runtime.Textures;

public interface IHypeTextureLookup
{
    Texture2D? TryGetTextureByTgaName(
        string gameRoot,
        string tgaName,
        uint textureFlags = 0,
        uint textureAlphaMask = 0,
        bool forceColorKey = false);

    HypeTextureLookupService.HypeTextureLookupResult TryGetTextureByTgaNameDetailed(
        string gameRoot,
        string tgaName,
        uint textureFlags = 0,
        uint textureAlphaMask = 0,
        bool forceColorKey = false);
}
