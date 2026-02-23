using System;
using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime.Parsing;

internal static class HypeWorldRootReader
{
    public static IReadOnlyList<HypeAddress> ReadWorldRoots(
        HypeRelocatedAddressSpace space,
        HypeAddress levelGptAddress,
        List<string> diagnostics)
    {
        var roots = new List<HypeAddress>(3);
        try
        {
            var reader = space.CreateReader(levelGptAddress);
            _ = reader.ReadPointer();
            _ = reader.ReadPointer();
            _ = reader.ReadPointer();
            _ = reader.ReadUInt32();

            AddUnique(roots, reader.ReadPointer());
            AddUnique(roots, reader.ReadPointer());
            AddUnique(roots, reader.ReadPointer());
            _ = reader.ReadUInt32();
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to read GPT world roots: {ex.Message}");
        }

        return roots;
    }

    private static void AddUnique(List<HypeAddress> list, HypeAddress? address)
    {
        if (address.HasValue && !list.Contains(address.Value))
        {
            list.Add(address.Value);
        }
    }
}
