using System;
using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime.Parsing;

internal sealed class HypeParsedVisualMaterial
{
    public required uint Flags { get; init; }
    public required string? TextureTgaName { get; init; }
    public required uint TextureFlags { get; init; }
    public required byte TextureFlagsByte { get; init; }
    public required uint TextureAlphaMask { get; init; }
}

internal sealed class HypeParsedTextureInfo
{
    public required uint Flags { get; init; }
    public required byte FlagsByte { get; init; }
    public required uint AlphaMask { get; init; }
    public required string? Name { get; init; }
}

internal sealed class HypeParsedGameMaterial
{
    public required uint VisualFlags { get; init; }
    public required string? TextureTgaName { get; init; }
    public required uint TextureFlags { get; init; }
    public required byte TextureFlagsByte { get; init; }
    public required uint TextureAlphaMask { get; init; }
}

internal static class HypeMaterialDecoder
{
    public static HypeParsedGameMaterial? ParseGameMaterial(
        HypeRelocatedAddressSpace space,
        HypeAddress address,
        Dictionary<HypeAddress, HypeParsedGameMaterial> gameMaterials,
        Dictionary<HypeAddress, HypeParsedVisualMaterial> visualMaterials,
        Dictionary<HypeAddress, HypeParsedTextureInfo> textureInfos,
        List<string> diagnostics)
    {
        if (gameMaterials.TryGetValue(address, out var cached))
        {
            return cached;
        }

        try
        {
            var reader = space.CreateReader(address);
            var offVisualMaterial = reader.ReadPointer();
            _ = reader.ReadPointer();
            _ = reader.ReadUInt32();
            _ = reader.ReadPointer();

            var visualMaterial = offVisualMaterial.HasValue
                ? ParseVisualMaterial(space, offVisualMaterial.Value, visualMaterials, textureInfos, diagnostics)
                : null;
            var parsed = new HypeParsedGameMaterial
            {
                VisualFlags = visualMaterial?.Flags ?? 0,
                TextureTgaName = visualMaterial?.TextureTgaName,
                TextureFlags = visualMaterial?.TextureFlags ?? 0,
                TextureFlagsByte = visualMaterial?.TextureFlagsByte ?? 0,
                TextureAlphaMask = visualMaterial?.TextureAlphaMask ?? 0
            };

            gameMaterials[address] = parsed;
            return parsed;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to parse GameMaterial {address}: {ex.Message}");
            return null;
        }
    }

    public static HypeParsedVisualMaterial? ParseVisualMaterial(
        HypeRelocatedAddressSpace space,
        HypeAddress address,
        Dictionary<HypeAddress, HypeParsedVisualMaterial> visualMaterials,
        Dictionary<HypeAddress, HypeParsedTextureInfo> textureInfos,
        List<string> diagnostics)
    {
        if (visualMaterials.TryGetValue(address, out var cached))
        {
            return cached;
        }

        try
        {
            var reader = space.CreateReader(address);
            var flags = reader.ReadUInt32();
            for (var i = 0; i < 16; i++)
            {
                _ = reader.ReadSingle();
            }

            _ = reader.ReadUInt32();
            var offTexture = reader.ReadPointer();
            var textureInfo = offTexture.HasValue
                ? ParseTextureInfo(space, offTexture.Value, textureInfos, diagnostics)
                : null;

            var parsed = new HypeParsedVisualMaterial
            {
                Flags = flags,
                TextureTgaName = textureInfo?.Name,
                TextureFlags = textureInfo?.Flags ?? 0,
                TextureFlagsByte = textureInfo?.FlagsByte ?? 0,
                TextureAlphaMask = textureInfo?.AlphaMask ?? 0
            };

            visualMaterials[address] = parsed;
            return parsed;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to parse VisualMaterial {address}: {ex.Message}");
            return null;
        }
    }

    private static HypeParsedTextureInfo? ParseTextureInfo(
        HypeRelocatedAddressSpace space,
        HypeAddress address,
        Dictionary<HypeAddress, HypeParsedTextureInfo> textureInfos,
        List<string> diagnostics)
    {
        if (textureInfos.TryGetValue(address, out var cached))
        {
            return cached;
        }

        try
        {
            var reader = space.CreateReader(address);
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var flags = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var alphaMask = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            for (var i = 0; i < 11; i++)
            {
                _ = reader.ReadUInt32();
            }

            var name = reader.ReadFixedString(0x50);
            _ = reader.ReadByte();
            var flagsByte = reader.ReadByte();

            var parsed = new HypeParsedTextureInfo
            {
                Flags = flags,
                FlagsByte = flagsByte,
                AlphaMask = alphaMask,
                Name = HypePathUtils.NormalizeTextureName(name)
            };

            textureInfos[address] = parsed;
            return parsed;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to parse TextureInfo {address}: {ex.Message}");
            return null;
        }
    }
}
